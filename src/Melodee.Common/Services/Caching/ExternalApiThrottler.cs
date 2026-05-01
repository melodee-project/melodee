using System.Collections.Concurrent;
using Serilog;

namespace Melodee.Common.Services.Caching;

/// <summary>
///     Global rate limiter for external API calls.
///     Provides per-provider throttling with configurable concurrency and rate limits.
/// </summary>
public sealed class ExternalApiThrottler : IDisposable
{
    private readonly ConcurrentDictionary<string, ProviderThrottle> _throttles = new();
    private bool _disposed;

    /// <summary>
    ///     Gets or creates a throttle for the specified provider.
    /// </summary>
    public ProviderThrottle GetOrCreateThrottle(
        string providerName,
        int maxConcurrency = 1,
        TimeSpan? minInterval = null)
    {
        return _throttles.GetOrAdd(providerName, _ => new ProviderThrottle(
            providerName,
            maxConcurrency,
            minInterval ?? TimeSpan.Zero));
    }

    /// <summary>
    ///     Executes an operation with throttling for the specified provider.
    /// </summary>
    public Task<T> ExecuteAsync<T>(
        string providerName,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        int maxConcurrency = 1,
        TimeSpan? minInterval = null)
    {
        var throttle = GetOrCreateThrottle(providerName, maxConcurrency, minInterval);
        return throttle.ExecuteAsync(operation, cancellationToken);
    }

    /// <summary>
    ///     Gets statistics for all providers.
    /// </summary>
    public Dictionary<string, ThrottleStatistics> GetStatistics()
    {
        return _throttles.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.GetStatistics());
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var throttle in _throttles.Values)
        {
            throttle.Dispose();
        }
        _throttles.Clear();
    }
}

/// <summary>
///     Per-provider throttle with concurrency and rate limiting.
/// </summary>
public sealed class ProviderThrottle : IDisposable
{
    private readonly string _providerName;
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly TimeSpan _minInterval;
    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly object _rateLock = new();
    private long _totalRequests;
    private long _throttledRequests;
    private bool _disposed;

    public ProviderThrottle(string providerName, int maxConcurrency, TimeSpan minInterval)
    {
        _providerName = providerName;
        _concurrencySemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        _minInterval = minInterval;
    }

    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        await _concurrencySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnforceRateLimitAsync(cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref _totalRequests);
            Log.Debug("[{Provider}] Executing throttled request (pending: {Pending})",
                _providerName, _concurrencySemaphore.CurrentCount);

            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _concurrencySemaphore.Release();
        }
    }

    private async Task EnforceRateLimitAsync(CancellationToken cancellationToken)
    {
        if (_minInterval <= TimeSpan.Zero)
        {
            return;
        }

        TimeSpan delay;
        lock (_rateLock)
        {
            var elapsed = DateTime.UtcNow - _lastRequestTime;
            if (elapsed < _minInterval)
            {
                delay = _minInterval - elapsed;
                Interlocked.Increment(ref _throttledRequests);
            }
            else
            {
                delay = TimeSpan.Zero;
            }
        }

        if (delay > TimeSpan.Zero)
        {
            Log.Debug("[{Provider}] Rate limited, waiting {DelayMs}ms", _providerName, delay.TotalMilliseconds);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        // Update the last request time to the current time after any delay has completed
        lock (_rateLock)
        {
            var currentTime = DateTime.UtcNow;
            // Ensure we don't go backwards in time in case another thread updated it
            if (currentTime > _lastRequestTime)
            {
                _lastRequestTime = currentTime;
            }
        }
    }

    public ThrottleStatistics GetStatistics()
    {
        return new ThrottleStatistics(
            _totalRequests,
            _throttledRequests,
            _concurrencySemaphore.CurrentCount);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _concurrencySemaphore.Dispose();
    }
}

/// <summary>
///     Statistics for a provider throttle.
/// </summary>
public readonly record struct ThrottleStatistics(
    long TotalRequests,
    long ThrottledRequests,
    int AvailableSlots);
