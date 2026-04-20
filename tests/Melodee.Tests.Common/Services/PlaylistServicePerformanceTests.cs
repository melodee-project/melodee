using System.Diagnostics;
using Melodee.Common.Data.Models;
using NodaTime;
using Melodee.Tests.Common.Performance;

namespace Melodee.Tests.Common.Services;

public class PlaylistServicePerformanceTests : ServiceTestBase
{
    [PerformanceFact]
    public async Task GetPlaylistWithComplexIncludes_WithLargeDataset_CompletesWithinTimeLimit()
    {
        // Arrange: create one user with 1,200 playlists
        await using var context = await MockFactory().CreateDbContextAsync();
        var user = new User
        {
            UserName = "perf_user",
            UserNameNormalized = "PERF_USER",
            Email = "perf@example.com",
            EmailNormalized = "PERF@EXAMPLE.COM",
            PublicKey = "perfkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var playlists = new List<Playlist>(capacity: 1200);
        for (int i = 0; i < 1200; i++)
        {
            playlists.Add(new Playlist
            {
                ApiKey = Guid.NewGuid(),
                Name = $"Perf Playlist {i}",
                Description = "Perf test",
                IsPublic = true,
                CreatedAt = SystemClock.Instance.GetCurrentInstant(),
                UserId = user.Id
            });
        }
        context.Playlists.AddRange(playlists);
        await context.SaveChangesAsync();

        var service = GetPlaylistService();

        // Act
        var sw = Stopwatch.StartNew();
        var result = await service.GetPlaylistsForUserAsync(user.Id);
        sw.Stop();

        // Assert
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(1200, result.Data.Length);

        // Allow generous time budget for CI variability; this still guards regressions
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"Query exceeded time limit: {sw.Elapsed}");
    }
}
