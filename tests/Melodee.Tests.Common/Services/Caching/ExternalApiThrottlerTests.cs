using Melodee.Common.Services.Caching;

namespace Melodee.Tests.Common.Services.Caching;

public sealed class ExternalApiThrottlerTests
{
    [Fact]
    public async Task ExecuteAsync_SingleRequest_CompletesSuccessfully()
    {
        using var throttler = new ExternalApiThrottler();

        var result = await throttler.ExecuteAsync(
            "TestProvider",
            async ct =>
            {
                await Task.Delay(10, ct);
                return "success";
            },
            CancellationToken.None);

        Assert.Equal("success", result);
    }

    [Fact]
    public async Task ExecuteAsync_ConcurrentRequests_RespectsMaxConcurrency()
    {
        using var throttler = new ExternalApiThrottler();
        var concurrentCount = 0;
        var maxConcurrent = 0;
        var lockObj = new object();

        var tasks = Enumerable.Range(0, 10).Select(_ =>
            throttler.ExecuteAsync(
                "TestProvider",
                async ct =>
                {
                    lock (lockObj)
                    {
                        concurrentCount++;
                        if (concurrentCount > maxConcurrent)
                        {
                            maxConcurrent = concurrentCount;
                        }
                    }

                    await Task.Delay(50, ct);

                    lock (lockObj)
                    {
                        concurrentCount--;
                    }

                    return "done";
                },
                CancellationToken.None,
                maxConcurrency: 2)).ToList();

        await Task.WhenAll(tasks);

        Assert.True(maxConcurrent <= 2, $"Max concurrent was {maxConcurrent}, expected <= 2");
    }

    [Fact]
    public async Task ExecuteAsync_WithRateLimit_EnforcesMinInterval()
    {
        using var throttler = new ExternalApiThrottler();
        var timestamps = new List<DateTime>();
        var lockObj = new object();

        // Execute tasks sequentially to ensure predictable timing
        for (var i = 0; i < 3; i++)
        {
            var timestamp = await throttler.ExecuteAsync(
                "RateLimitedProvider",
                async ct =>
                {
                    lock (lockObj)
                    {
                        timestamps.Add(DateTime.UtcNow);
                    }
                    return "done";
                },
                CancellationToken.None,
                maxConcurrency: 1,
                minInterval: TimeSpan.FromMilliseconds(200));

            // Small delay to allow any timer jitter to settle
            await Task.Delay(5);
        }

        Assert.Equal(3, timestamps.Count);

        for (var i = 1; i < timestamps.Count; i++)
        {
            var gap = (timestamps[i] - timestamps[i - 1]).TotalMilliseconds;
            Assert.True(gap >= 190, $"Gap between requests was {gap}ms, expected >= 190ms (with 200ms minInterval - 10ms tolerance)");
        }
    }

    [Fact]
    public void GetStatistics_ReturnsCorrectCounts()
    {
        using var throttler = new ExternalApiThrottler();

        _ = throttler.GetOrCreateThrottle("Provider1", maxConcurrency: 2);
        _ = throttler.GetOrCreateThrottle("Provider2", maxConcurrency: 1);

        var stats = throttler.GetStatistics();

        Assert.Equal(2, stats.Count);
        Assert.Contains("Provider1", stats.Keys);
        Assert.Contains("Provider2", stats.Keys);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        using var throttler = new ExternalApiThrottler();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            throttler.ExecuteAsync(
                "TestProvider",
                async ct =>
                {
                    await Task.Delay(100, ct);
                    return "done";
                },
                cts.Token));
    }

    [Fact]
    public async Task ProviderThrottle_GetStatistics_TracksRequests()
    {
        using var throttle = new ProviderThrottle("TestProvider", maxConcurrency: 2, minInterval: TimeSpan.Zero);

        await throttle.ExecuteAsync(ct => Task.FromResult("a"), CancellationToken.None);
        await throttle.ExecuteAsync(ct => Task.FromResult("b"), CancellationToken.None);
        await throttle.ExecuteAsync(ct => Task.FromResult("c"), CancellationToken.None);

        var stats = throttle.GetStatistics();

        Assert.Equal(3, stats.TotalRequests);
        Assert.Equal(2, stats.AvailableSlots);
    }
}
