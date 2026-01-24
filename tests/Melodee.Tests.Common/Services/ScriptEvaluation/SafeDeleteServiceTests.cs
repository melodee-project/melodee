using FluentAssertions;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Services;
using Melodee.Common.Services.ScriptEvaluation;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

public class SafeDeleteServiceTests : ServiceTestBase
{
    private const int SeedInboundLibraryId = 1;

    [Fact]
    public async Task DeleteDirectoryAsync_EmptyInboundPathSetting_FallsBackToLibraryPath()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"melodee-safe-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryRoot);

        var targetDirectory = Path.Combine(libraryRoot, "to-delete");
        Directory.CreateDirectory(targetDirectory);

        await UpdateLibraryPathAsync(SeedInboundLibraryId, libraryRoot, CancellationToken.None);
        await UpsertSettingAsync($"library.inboundPath.{SeedInboundLibraryId}", "", CancellationToken.None);

        var libraryService = new LibraryService(Logger, CacheManager, MockFactory(), MockConfigurationFactory(), null!, null!);
        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var safeDeleteService = new SafeDeleteService(libraryService, settingService, MockFileSystemService(), Logger);

        var result = await safeDeleteService.DeleteDirectoryAsync("to-delete", SeedInboundLibraryId, CancellationToken.None);

        result.Should().BeTrue();
        Directory.Exists(targetDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_RootedRelativePath_IsRejected()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"melodee-safe-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryRoot);

        var targetDirectory = Path.Combine(libraryRoot, "keep");
        Directory.CreateDirectory(targetDirectory);

        await UpdateLibraryPathAsync(SeedInboundLibraryId, libraryRoot, CancellationToken.None);
        await UpsertSettingAsync($"library.inboundPath.{SeedInboundLibraryId}", libraryRoot, CancellationToken.None);

        var libraryService = new LibraryService(Logger, CacheManager, MockFactory(), MockConfigurationFactory(), null!, null!);
        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var safeDeleteService = new SafeDeleteService(libraryService, settingService, MockFileSystemService(), Logger);

        var result = await safeDeleteService.DeleteDirectoryAsync(targetDirectory, SeedInboundLibraryId, CancellationToken.None);

        result.Should().BeFalse();
        Directory.Exists(targetDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_PathTraversal_IsRejected()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"melodee-safe-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryRoot);

        var targetDirectory = Path.Combine(libraryRoot, "keep");
        Directory.CreateDirectory(targetDirectory);

        await UpdateLibraryPathAsync(SeedInboundLibraryId, libraryRoot, CancellationToken.None);
        await UpsertSettingAsync($"library.inboundPath.{SeedInboundLibraryId}", libraryRoot, CancellationToken.None);

        var libraryService = new LibraryService(Logger, CacheManager, MockFactory(), MockConfigurationFactory(), null!, null!);
        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var safeDeleteService = new SafeDeleteService(libraryService, settingService, MockFileSystemService(), Logger);

        var result = await safeDeleteService.DeleteDirectoryAsync("../keep", SeedInboundLibraryId, CancellationToken.None);

        result.Should().BeFalse();
        Directory.Exists(targetDirectory).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_DryRun_DoesNotDelete()
    {
        var libraryRoot = Path.Combine(Path.GetTempPath(), $"melodee-safe-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(libraryRoot);

        var targetDirectory = Path.Combine(libraryRoot, "to-delete");
        Directory.CreateDirectory(targetDirectory);

        await UpdateLibraryPathAsync(SeedInboundLibraryId, libraryRoot, CancellationToken.None);
        await UpsertSettingAsync($"library.inboundPath.{SeedInboundLibraryId}", libraryRoot, CancellationToken.None);
        await UpsertSettingAsync("script.dryRun.enabled", "true", CancellationToken.None);

        var libraryService = new LibraryService(Logger, CacheManager, MockFactory(), MockConfigurationFactory(), null!, null!);
        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var safeDeleteService = new SafeDeleteService(libraryService, settingService, MockFileSystemService(), Logger);

        var result = await safeDeleteService.DeleteDirectoryAsync("to-delete", SeedInboundLibraryId, CancellationToken.None);

        result.Should().BeTrue();
        Directory.Exists(targetDirectory).Should().BeTrue();
    }

    private async Task UpdateLibraryPathAsync(int libraryId, string path, CancellationToken cancellationToken)
    {
        await using var context = await MockFactory().CreateDbContextAsync(cancellationToken);

        var library = await context.Libraries.FirstAsync(x => x.Id == libraryId, cancellationToken);
        library.Path = path;
        library.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task UpsertSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        await using var context = await MockFactory().CreateDbContextAsync(cancellationToken);

        var existing = await context.Settings.FirstOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (existing != null)
        {
            existing.Value = value;
            existing.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();
            await context.SaveChangesAsync(cancellationToken);
            return;
        }

        context.Settings.Add(new Setting
        {
            Key = key,
            Value = value,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });

        await context.SaveChangesAsync(cancellationToken);
    }
}
