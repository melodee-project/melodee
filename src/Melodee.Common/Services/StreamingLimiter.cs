using System.Collections.Concurrent;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;

namespace Melodee.Common.Services;

/// <summary>
/// Simple in-memory limiter for concurrent streaming to prevent resource exhaustion.
/// Limits are configurable via SettingRegistry and default to unlimited when unset or &lt;= 0.
/// </summary>
public class StreamingLimiter
{
    private readonly IMelodeeConfigurationFactory _configurationFactory;
    private readonly ConcurrentDictionary<string, int> _perUser = new();
    private int _global;

    public StreamingLimiter(IMelodeeConfigurationFactory configurationFactory)
    {
        _configurationFactory = configurationFactory;
    }

    private async Task<(int globalLimit, int perUserLimit)> GetLimitsAsync()
    {
        var config = await _configurationFactory.GetConfigurationAsync().ConfigureAwait(false);
        var globalLimit = config.GetValue<int?>(SettingRegistry.StreamingMaxConcurrentStreamsGlobal) ?? 0;
        var perUserLimit = config.GetValue<int?>(SettingRegistry.StreamingMaxConcurrentStreamsPerUser) ?? 0;
        return (globalLimit, perUserLimit);
    }

    /// <summary>
    /// Try to enter the streaming gate for a given user key.
    /// Returns false if limits would be exceeded.
    /// </summary>
    public async Task<bool> TryEnterAsync(string userKey, CancellationToken cancellationToken = default)
    {
        var (globalLimit, perUserLimit) = await GetLimitsAsync().ConfigureAwait(false);

        // Unlimited if both <= 0
        if (globalLimit <= 0 && perUserLimit <= 0)
        {
            return true;
        }

        // Check global first
        if (globalLimit > 0)
        {
            // Optimistic increment then validate
            var newGlobal = Interlocked.Increment(ref _global);
            if (newGlobal > globalLimit)
            {
                Interlocked.Decrement(ref _global);
                return false;
            }
        }

        // Check per-user
        if (perUserLimit > 0)
        {
            var updated = _perUser.AddOrUpdate(userKey, 1, (_, current) => current + 1);
            if (updated > perUserLimit)
            {
                // Rollback per-user + global if we incremented it
                _perUser.AddOrUpdate(userKey, 0, (_, current) => Math.Max(0, current - 1));
                if (globalLimit > 0)
                {
                    Interlocked.Decrement(ref _global);
                }
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Leave the streaming gate for a given user key.
    /// Safe to call multiple times; counters will not go negative.
    /// </summary>
    public void Exit(string userKey)
    {
        _perUser.AddOrUpdate(userKey, 0, (_, current) => Math.Max(0, current - 1));
        Interlocked.Add(ref _global, -1);
        if (_global < 0)
        {
            _global = 0;
        }
    }
}
