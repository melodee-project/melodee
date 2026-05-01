using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq.Expressions;
using Melodee.Mql.Interfaces;
using Melodee.Mql.Models;

namespace Melodee.Mql;

/// <summary>
/// Thread-safe LRU cache for compiled MQL expressions.
/// </summary>
public sealed class MqlExpressionCache : IMqlExpressionCache, Melodee.Common.Services.Caching.ICacheInvalidatable
{
    private readonly int _maxEntries;
    private readonly TimeSpan _defaultTtl;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache;
    private readonly ConcurrentDictionary<string, ImmutableList<string>> _entityTypeIndex;
    private readonly LRUCache<string, string> _lruOrder;
    private long _hitCount;
    private long _missCount;
    private int _evictionCount;
    private DateTime? _lastEvictionTime;
    private readonly SemaphoreSlim _cleanupSemaphore = new(1, 1);

    private record CacheEntry(
        Type EntityType,
        Expression Expression,
        DateTime CreatedAt,
        DateTime? ExpiresAt,
        long EstimatedSize);

    private class LRUCache<TKey, TValue> where TKey : notnull
    {
        private readonly int _capacity;
        private readonly LinkedList<TKey> _list = new();
        private readonly ConcurrentDictionary<TKey, TValue> _map = new();
        private readonly object _syncLock = new();

        public LRUCache(int capacity)
        {
            _capacity = capacity;
        }

        public int Count
        {
            get
            {
                lock (_syncLock)
                {
                    return _list.Count;
                }
            }
        }

        public TValue? Get(TKey key)
        {
            lock (_syncLock)
            {
                if (_map.TryGetValue(key, out var value))
                {
                    MoveToFront(key);
                    return value;
                }
                return default;
            }
        }

        public void Set(TKey key, TValue value)
        {
            lock (_syncLock)
            {
                if (_map.ContainsKey(key))
                {
                    MoveToFront(key);
                    _map[key] = value;
                    return;
                }

                _list.AddFirst(key);
                _map[key] = value;

                if (_list.Count > _capacity)
                {
                    var last = _list.Last!.Value;
                    _list.RemoveLast();
                    _map.TryRemove(last, out _);
                }
            }
        }

        public void Remove(TKey key)
        {
            lock (_syncLock)
            {
                if (_map.TryRemove(key, out _))
                {
                    _list.Remove(key);
                }
            }
        }

        public void Clear()
        {
            lock (_syncLock)
            {
                _list.Clear();
                _map.Clear();
            }
        }

        public IEnumerable<TKey> GetAllKeys()
        {
            lock (_syncLock)
            {
                return _list.ToList();
            }
        }

        private void MoveToFront(TKey key)
        {
            _list.Remove(key);
            _list.AddFirst(key);
        }
    }

    /// <summary>
    /// Creates a new MqlExpressionCache with default settings.
    /// </summary>
    /// <param name="maxEntries">Maximum number of entries to cache (default: 1000).</param>
    /// <param name="defaultTtl">Default time-to-live (default: 30 minutes).</param>
    public MqlExpressionCache(int maxEntries = 1000, TimeSpan? defaultTtl = null)
    {
        _maxEntries = maxEntries;
        _defaultTtl = defaultTtl ?? TimeSpan.FromMinutes(30);
        _cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        _entityTypeIndex = new ConcurrentDictionary<string, ImmutableList<string>>(StringComparer.OrdinalIgnoreCase);
        _lruOrder = new LRUCache<string, string>(maxEntries);
    }

    public Expression<Func<TEntity, bool>> GetOrCreate<TEntity>(
        string cacheKey,
        Func<Expression<Func<TEntity, bool>>> factory,
        TimeSpan? ttl = null) where TEntity : class
    {
        var entityTypeName = typeof(TEntity).Name;

        if (_cache.TryGetValue(cacheKey, out var entry))
        {
            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < DateTime.UtcNow)
            {
                if (_cache.TryRemove(cacheKey, out _))
                {
                    _lruOrder.Remove(cacheKey);
                    RemoveFromEntityTypeIndex(entityTypeName, cacheKey);
                }

                Interlocked.Increment(ref _missCount);
                var newEntry = CreateEntry(factory(), ttl ?? _defaultTtl);
                _cache[cacheKey] = newEntry;
                _lruOrder.Set(cacheKey, entityTypeName);
                AddToEntityTypeIndex(entityTypeName, cacheKey);
                return (Expression<Func<TEntity, bool>>)newEntry.Expression;
            }

            Interlocked.Increment(ref _hitCount);
            _lruOrder.Set(cacheKey, entityTypeName);
            return (Expression<Func<TEntity, bool>>)entry.Expression;
        }

        Interlocked.Increment(ref _missCount);
        var freshEntry = CreateEntry(factory(), ttl ?? _defaultTtl);
        _cache[cacheKey] = freshEntry;
        _lruOrder.Set(cacheKey, entityTypeName);
        AddToEntityTypeIndex(entityTypeName, cacheKey);

        TryCleanupIfNeeded();
        return (Expression<Func<TEntity, bool>>)freshEntry.Expression;
    }

    public void Clear<TEntity>() where TEntity : class
    {
        var entityTypeName = typeof(TEntity).Name;
        ClearByEntityType(entityTypeName);
    }

    public void ClearAll()
    {
        _cache.Clear();
        _lruOrder.Clear();
        _entityTypeIndex.Clear();
    }

    public MqlCacheStatistics GetStatistics()
    {
        var memoryBytes = 0L;
        foreach (var entry in _cache.Values)
        {
            memoryBytes += entry.EstimatedSize;
        }

        return new MqlCacheStatistics
        {
            HitCount = Interlocked.Read(ref _hitCount),
            MissCount = Interlocked.Read(ref _missCount),
            EntryCount = _cache.Count,
            MemoryEstimateBytes = memoryBytes,
            LastEvictionTime = _lastEvictionTime,
            LastEvictionCount = _evictionCount,
            MaxEntries = _maxEntries,
            DefaultTtlMinutes = (int)_defaultTtl.TotalMinutes
        };
    }

    public void InvalidateByEntityType(string entityTypeName)
    {
        ClearByEntityType(entityTypeName);
    }

    private void ClearByEntityType(string entityTypeName)
    {
        if (_entityTypeIndex.TryGetValue(entityTypeName, out var keys))
        {
            foreach (var key in keys)
            {
                _cache.TryRemove(key, out _);
                _lruOrder.Remove(key);
            }
            _entityTypeIndex.TryRemove(entityTypeName, out _);
        }
    }

    private CacheEntry CreateEntry(Expression expression, TimeSpan ttl)
    {
        var now = DateTime.UtcNow;
        return new CacheEntry(
            expression.Type.GetGenericArguments()[0],
            expression,
            now,
            now.Add(ttl),
            EstimateExpressionSize(expression));
    }

    private static long EstimateExpressionSize(Expression expression)
    {
        return expression.ToString().Length * 2L + 256L;
    }

    private void TryCleanupIfNeeded()
    {
        if (_cache.Count < _maxEntries * 0.9)
        {
            return;
        }

        // Use TryEnter pattern to avoid blocking if cleanup is already in progress
        if (!_cleanupSemaphore.Wait(0))
        {
            return; // Skip cleanup if semaphore is busy
        }

        try
        {
            if (_cache.Count < _maxEntries * 0.9)
            {
                return;
            }

            EvictExpiredEntries();
            EvictOldestEntriesIfNeeded();
        }
        finally
        {
            _cleanupSemaphore.Release();
        }
    }

    private void EvictExpiredEntries()
    {
        var now = DateTime.UtcNow;
        var keysToRemove = new List<string>();

        foreach (var (key, entry) in _cache)
        {
            if (entry.ExpiresAt.HasValue && entry.ExpiresAt.Value < now)
            {
                keysToRemove.Add(key);
            }
        }

        foreach (var key in keysToRemove)
        {
            if (_cache.TryRemove(key, out _))
            {
                _lruOrder.Remove(key);
                RemoveFromEntityTypeIndex(GetEntityTypeFromKey(key), key);
                Interlocked.Increment(ref _evictionCount);
            }
        }

        if (keysToRemove.Count > 0)
        {
            _lastEvictionTime = DateTime.UtcNow;
            _evictionCount = keysToRemove.Count;
        }
    }

    private void EvictOldestEntriesIfNeeded()
    {
        while (_cache.Count > _maxEntries)
        {
            var oldestKey = _lruOrder.GetAllKeys().FirstOrDefault();
            if (oldestKey == null)
            {
                break;
            }

            if (_cache.TryRemove(oldestKey, out _))
            {
                _lruOrder.Remove(oldestKey);
                RemoveFromEntityTypeIndex(GetEntityTypeFromKey(oldestKey), oldestKey);
                Interlocked.Increment(ref _evictionCount);
            }
        }

        if (Interlocked.Exchange(ref _evictionCount, 0) > 0)
        {
            _lastEvictionTime = DateTime.UtcNow;
        }
    }

    private static string GetEntityTypeFromKey(string key)
    {
        var parts = key.Split(':');
        return parts.Length > 0 ? parts[0] : string.Empty;
    }

    private void AddToEntityTypeIndex(string entityTypeName, string cacheKey)
    {
        _entityTypeIndex.AddOrUpdate(
            entityTypeName,
            _ => ImmutableList.Create(cacheKey),
            (_, existing) => existing.Add(cacheKey));
    }

    private void RemoveFromEntityTypeIndex(string entityTypeName, string cacheKey)
    {
        _entityTypeIndex.AddOrUpdate(
            entityTypeName,
            _ => ImmutableList<string>.Empty,
            (_, existing) => existing.Remove(cacheKey));
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        ClearAll();
    }
}
