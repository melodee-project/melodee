using System.Collections.Concurrent;
using Serilog;

namespace Melodee.Common.Services.Caching;

/// <summary>
///     Thread-safe cache with single-flight (request coalescing) behavior.
///     When multiple concurrent requests arrive for the same key, only one upstream request executes.
/// </summary>
/// <typeparam name="TKey">The type of cache key.</typeparam>
/// <typeparam name="TValue">The type of cached value.</typeparam>
public sealed class SingleFlightCache<TKey, TValue> : IDisposable, ICacheInvalidatable where TKey : notnull
{
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _inflight = new();
    private readonly Func<TKey, string> _keyNormalizer;
    private readonly int _maxSize;
    private readonly TimeSpan _positiveTtl;
    private readonly TimeSpan _negativeTtl;
    private readonly string _cacheName;
    private long _hits;
    private long _misses;
    private long _coalesced;

    private sealed class CacheEntry
    {
        public required TValue? Value { get; init; }
        public required bool IsNegative { get; init; }
        public required DateTime ExpiresAt { get; init; }
    }

    /// <summary>
    ///     Creates a new single-flight cache.
    /// </summary>
    /// <param name="keyNormalizer">Function to normalize keys for consistent lookups.</param>
    /// <param name="maxSize">Maximum number of entries (default: 1000).</param>
    /// <param name="positiveTtl">TTL for successful results (default: run lifetime/24h).</param>
    /// <param name="negativeTtl">TTL for negative/failure results (default: 2 minutes).</param>
    /// <param name="cacheName">Name for logging purposes.</param>
    public SingleFlightCache(
        Func<TKey, string> keyNormalizer,
        int maxSize = 1000,
        TimeSpan? positiveTtl = null,
        TimeSpan? negativeTtl = null,
        string cacheName = "SingleFlightCache")
    {
        _keyNormalizer = keyNormalizer ?? throw new ArgumentNullException(nameof(keyNormalizer));
        _maxSize = maxSize;
        _positiveTtl = positiveTtl ?? TimeSpan.FromHours(24);
        _negativeTtl = negativeTtl ?? TimeSpan.FromMinutes(2);
        _cacheName = cacheName;
    }

    /// <summary>
    ///     Gets or creates a value, ensuring only one upstream call for concurrent requests with the same key.
    /// </summary>
    public async Task<(TValue? Value, bool WasHit, bool WasCoalesced)> GetOrCreateAsync(
        TKey key,
        Func<TKey, CancellationToken, Task<TValue?>> factory,
        CancellationToken cancellationToken = default)
    {
        var normalizedKey = _keyNormalizer(key);

        if (TryGetCached(normalizedKey, out var cachedValue, out var isNegative))
        {
            Interlocked.Increment(ref _hits);
            Log.Debug("[{CacheName}] Cache hit for key [{Key}], negative={IsNegative}",
                _cacheName, normalizedKey, isNegative);
            return (cachedValue, true, false);
        }

        Interlocked.Increment(ref _misses);

        var semaphore = _inflight.GetOrAdd(normalizedKey, _ => new SemaphoreSlim(1, 1));
        var wasCoalesced = false;

        try
        {
            if (!await semaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                Interlocked.Increment(ref _coalesced);
                wasCoalesced = true;
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (TryGetCached(normalizedKey, out cachedValue, out isNegative))
                {
                    Log.Debug("[{CacheName}] Coalesced request found cached result for key [{Key}]",
                        _cacheName, normalizedKey);
                    return (cachedValue, true, wasCoalesced);
                }
            }

            Log.Debug("[{CacheName}] Cache miss for key [{Key}], executing upstream",
                _cacheName, normalizedKey);

            TValue? result;
            try
            {
                result = await factory(key, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                CacheNegative(normalizedKey);
                Log.Warning(ex, "[{CacheName}] Upstream call failed for key [{Key}], caching negative result",
                    _cacheName, normalizedKey);
                return (default, false, wasCoalesced);
            }

            if (result is null || IsEmpty(result))
            {
                CacheNegative(normalizedKey);
                return (result, false, wasCoalesced);
            }

            CachePositive(normalizedKey, result);
            return (result, false, wasCoalesced);
        }
        finally
        {
            semaphore.Release();

            if (_inflight.TryGetValue(normalizedKey, out var currentSem) &&
                currentSem == semaphore &&
                semaphore.CurrentCount == 1)
            {
                _inflight.TryRemove(normalizedKey, out _);
            }
        }
    }

    /// <summary>
    ///     Tries to get a cached value without executing upstream.
    /// </summary>
    public bool TryGet(TKey key, out TValue? value, out bool isNegative)
    {
        var normalizedKey = _keyNormalizer(key);
        return TryGetCached(normalizedKey, out value, out isNegative);
    }

    /// <summary>
    ///     Manually adds a positive result to the cache.
    /// </summary>
    public void Add(TKey key, TValue value)
    {
        var normalizedKey = _keyNormalizer(key);
        CachePositive(normalizedKey, value);
    }

    /// <summary>
    ///     Manually adds a negative result to the cache.
    /// </summary>
    public void AddNegative(TKey key)
    {
        var normalizedKey = _keyNormalizer(key);
        CacheNegative(normalizedKey);
    }

    /// <summary>
    ///     Clears all cached entries.
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _hits = 0;
        _misses = 0;
        _coalesced = 0;
    }

    /// <summary>
    ///     Gets cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        CleanExpired();
        var entries = _cache.Values.ToList();
        return new CacheStatistics(
            TotalEntries: entries.Count,
            PositiveEntries: entries.Count(e => !e.IsNegative),
            NegativeEntries: entries.Count(e => e.IsNegative),
            Hits: _hits,
            Misses: _misses,
            CoalescedRequests: _coalesced);
    }

    private bool TryGetCached(string normalizedKey, out TValue? value, out bool isNegative)
    {
        if (_cache.TryGetValue(normalizedKey, out var entry) && entry.ExpiresAt > DateTime.UtcNow)
        {
            value = entry.Value;
            isNegative = entry.IsNegative;
            return true;
        }

        value = default;
        isNegative = false;
        return false;
    }

    private void CachePositive(string normalizedKey, TValue value)
    {
        var entry = new CacheEntry
        {
            Value = value,
            IsNegative = false,
            ExpiresAt = DateTime.UtcNow.Add(_positiveTtl)
        };
        _cache.AddOrUpdate(normalizedKey, entry, (_, _) => entry);
        CleanIfNeeded();
    }

    private void CacheNegative(string normalizedKey)
    {
        var entry = new CacheEntry
        {
            Value = default,
            IsNegative = true,
            ExpiresAt = DateTime.UtcNow.Add(_negativeTtl)
        };
        _cache.AddOrUpdate(normalizedKey, entry, (_, _) => entry);
        CleanIfNeeded();
    }

    private void CleanIfNeeded()
    {
        if (_cache.Count <= _maxSize)
        {
            return;
        }

        CleanExpired();

        if (_cache.Count > _maxSize)
        {
            var keysToRemove = _cache
                .OrderBy(kvp => kvp.Value.ExpiresAt)
                .Take(_cache.Count - _maxSize)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.TryRemove(key, out _);
            }
        }
    }

    private void CleanExpired()
    {
        var now = DateTime.UtcNow;
        var expiredKeys = _cache
            .Where(kvp => kvp.Value.ExpiresAt <= now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _cache.TryRemove(key, out _);
        }
    }

    private static bool IsEmpty(TValue value)
    {
        return value switch
        {
            ICollection<object> collection => collection.Count == 0,
            Array array => array.Length == 0,
            _ => false
        };
    }

    /// <inheritdoc />
    public void InvalidateByEntityType(string entityTypeName)
    {
    }

    /// <inheritdoc />
    public void InvalidateAll()
    {
        Clear();
    }

    public void Dispose()
    {
        foreach (var sem in _inflight.Values)
        {
            sem.Dispose();
        }
        _inflight.Clear();
        _cache.Clear();
    }
}

/// <summary>
///     Statistics for a SingleFlightCache instance.
/// </summary>
public readonly record struct CacheStatistics(
    int TotalEntries,
    int PositiveEntries,
    int NegativeEntries,
    long Hits,
    long Misses,
    long CoalescedRequests)
{
    public double HitRate => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) : 0;
}
