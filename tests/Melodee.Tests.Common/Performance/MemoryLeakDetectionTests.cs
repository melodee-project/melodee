using Melodee.Common.Models;
using Melodee.Tests.Common.Services;
using NodaTime;
using DataPlaylist = Melodee.Common.Data.Models.Playlist;
using DataUser = Melodee.Common.Data.Models.User;

namespace Melodee.Tests.Common.Performance;

public class MemoryLeakDetectionTests : ServiceTestBase
{
    [PerformanceFact]
    public async Task RepeatedLargeQueryExecution_DoesNotLeakMemory()
    {
        // Arrange: user with many playlists to iterate via paging
        await using var context = await MockFactory().CreateDbContextAsync();

        var user = new DataUser
        {
            UserName = "leak_user",
            UserNameNormalized = "LEAK_USER",
            Email = "leak@example.com",
            EmailNormalized = "LEAK@EXAMPLE.COM",
            PublicKey = "leakkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var many = Enumerable.Range(0, 1500).Select(i => new DataPlaylist
        {
            ApiKey = Guid.NewGuid(),
            Name = $"Leak Playlist {i}",
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UserId = user.Id
        });
        context.Playlists.AddRange(many);
        await context.SaveChangesAsync();

        var service = GetPlaylistService();
        var userInfo = new UserInfo(user.Id, user.ApiKey, user.UserName, user.Email, string.Empty, string.Empty);

        // Act: run the paged query multiple times and ensure memory returns near baseline
        ForceGc();
        var baseline = GC.GetTotalMemory(true);

        for (int i = 0; i < 5; i++)
        {
            var result = await service.ListAsync(userInfo, new PagedRequest { Page = i, PageSize = 200 });
            Assert.True(result.IsSuccess);
            Assert.True(result.Data.Count() <= 200);
        }

        ForceGc();
        var after = GC.GetTotalMemory(true);

        // Assert: allow small drift (< 32MB) to account for JIT and caches, but no unbounded growth
        var delta = after - baseline;
        Assert.True(delta < 32 * 1024 * 1024, $"Memory leak suspected: delta {delta} bytes");
    }

    private static void ForceGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
