using System.Collections.Concurrent;
using System.Linq.Expressions;
using FluentAssertions;
using Melodee.Common.Data.Models;
using Melodee.Mql;

namespace Melodee.Tests.Common.Services.Caching;

/// <summary>
/// Concurrency stress tests for cache implementations.
/// </summary>
public class CacheConcurrencyStressTests
{
    [Fact]
    public void MqlExpressionCache_ConcurrentAddsAndClears_NoExceptionsAndConsistentState()
    {
        var cache = new MqlExpressionCache(maxEntries: 100);

        var exceptions = new ConcurrentQueue<Exception>();
        var iterations = 100;
        var parallelism = 10;

        Parallel.For(0, parallelism, i =>
        {
            try
            {
                for (var j = 0; j < iterations; j++)
                {
                    var entityType = j % 2 == 0 ? "Song" : "Album";
                    var cacheKey = $"{entityType}:Query{j % 10}:User{i}";

                    Expression<Func<Song, bool>> expr = x => x.Title == $"Test{j}";
                    cache.GetOrCreate(cacheKey, () => expr, TimeSpan.FromMinutes(5));

                    if (j % 20 == 0)
                    {
                        cache.Clear<Song>();
                    }

                    if (j % 25 == 0)
                    {
                        cache.InvalidateByEntityType("Album");
                    }

                    if (j % 50 == 0)
                    {
                        cache.ClearAll();
                    }
                }
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        });

        exceptions.Should().BeEmpty($"No exceptions should occur during concurrent operations. Exceptions: {string.Join(", ", exceptions.Select(e => e.Message))}");

        var stats = cache.GetStatistics();
        stats.EntryCount.Should().BeLessThanOrEqualTo(100);
    }

    [Fact]
    public void MqlExpressionCache_ConcurrentGetOrCreate_ThreadSafe()
    {
        var cache = new MqlExpressionCache(maxEntries: 1000);
        var factoryCallCount = 0;
        var exceptions = new ConcurrentQueue<Exception>();

        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 20 };

        Parallel.For(0, 100, parallelOptions, i =>
        {
            try
            {
                for (var j = 0; j < 50; j++)
                {
                    var cacheKey = $"Song:ThreadSafeTest:{j}";

                    var result = cache.GetOrCreate(cacheKey, () =>
                    {
                        Interlocked.Increment(ref factoryCallCount);
                        Expression<Func<Song, bool>> expr = x => x.Title == $"ConcurrentTest{j}";
                        return expr;
                    }, TimeSpan.FromMinutes(10));

                    result.Should().NotBeNull();
                }
            }
            catch (Exception ex)
            {
                exceptions.Enqueue(ex);
            }
        });

        exceptions.Should().BeEmpty();

        var stats = cache.GetStatistics();
        stats.HitCount.Should().BeGreaterThan(0);
        stats.MissCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MqlExpressionCache_ClearByEntityType_OnlyClearsTarget()
    {
        var cache = new MqlExpressionCache(maxEntries: 100);

        Expression<Func<Song, bool>> songExpr = x => x.Title == "Song1";
        Expression<Func<Album, bool>> albumExpr = x => x.Name == "Album1";

        cache.GetOrCreate("Song:Test1", () => songExpr, TimeSpan.FromHours(1));
        cache.GetOrCreate("Song:Test2", () => songExpr, TimeSpan.FromHours(1));
        cache.GetOrCreate("Album:Test1", () => albumExpr, TimeSpan.FromHours(1));

        cache.Clear<Song>();

        var stats = cache.GetStatistics();
        stats.EntryCount.Should().Be(1);
        stats.EntryCount.Should().NotBe(2);
    }
}
