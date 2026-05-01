namespace Melodee.Common.Services.Caching;

/// <summary>
/// Centralized cache invalidation service that owns key namespaces and provides
/// "clear by entity type" semantics across all cache implementations.
/// </summary>
public sealed class CacheInvalidationService
{
    private readonly Dictionary<string, HashSet<ICacheInvalidatable>> _entityTypeListeners = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ICacheInvalidatable> _globalListeners = new();
    private readonly object _syncLock = new();

    /// <summary>
    /// Registers a cache listener for a specific entity type.
    /// </summary>
    /// <param name="entityTypeName">The entity type name (e.g., "Song", "Album").</param>
    /// <param name="cache">The cache to invalidate when the entity type is cleared.</param>
    public void RegisterListener(string entityTypeName, ICacheInvalidatable cache)
    {
        lock (_syncLock)
        {
            if (!_entityTypeListeners.TryGetValue(entityTypeName, out var listeners))
            {
                listeners = new HashSet<ICacheInvalidatable>();
                _entityTypeListeners[entityTypeName] = listeners;
            }
            listeners.Add(cache);
        }
    }

    /// <summary>
    /// Registers a global listener that is notified for all invalidation operations.
    /// </summary>
    /// <param name="cache">The cache to register.</param>
    public void RegisterGlobalListener(ICacheInvalidatable cache)
    {
        lock (_syncLock)
        {
            _globalListeners.Add(cache);
        }
    }

    /// <summary>
    /// Invalidates all caches for a specific entity type.
    /// </summary>
    /// <param name="entityTypeName">The entity type name.</param>
    public void InvalidateByEntityType(string entityTypeName)
    {
        ICacheInvalidatable[]? listenersToNotify;
        lock (_syncLock)
        {
            if (!_entityTypeListeners.TryGetValue(entityTypeName, out var listeners))
            {
                listenersToNotify = Array.Empty<ICacheInvalidatable>();
            }
            else
            {
                listenersToNotify = listeners.ToArray();
            }
        }

        foreach (var listener in listenersToNotify)
        {
            listener.InvalidateByEntityType(entityTypeName);
        }
    }

    /// <summary>
    /// Invalidates all caches for a specific entity type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    public void InvalidateByEntityType<TEntity>() where TEntity : class
    {
        InvalidateByEntityType(typeof(TEntity).Name);
    }

    /// <summary>
    /// Invalidates all registered caches.
    /// </summary>
    public void InvalidateAll()
    {
        ICacheInvalidatable[] allListeners;
        lock (_syncLock)
        {
            var entityListeners = _entityTypeListeners.Values.SelectMany(x => x).ToList();
            allListeners = _globalListeners.Concat(entityListeners).ToArray();
        }

        foreach (var listener in allListeners.Distinct())
        {
            listener.InvalidateAll();
        }
    }
}

/// <summary>
/// Interface for caches that support entity-type-based invalidation.
/// </summary>
public interface ICacheInvalidatable
{
    /// <summary>
    /// Invalidates all cache entries for a specific entity type.
    /// </summary>
    /// <param name="entityTypeName">The entity type name.</param>
    void InvalidateByEntityType(string entityTypeName);

    /// <summary>
    /// Invalidates all cache entries.
    /// </summary>
    void InvalidateAll();
}
