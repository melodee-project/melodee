using FluentAssertions;
using Melodee.Common.Data.Models;
using Melodee.Common.Services;
using Melodee.Common.Services.ScriptEvaluation;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

public class SafeDeleteServiceTests : ServiceTestBase
{
    [Fact]
    public async Task DeleteDirectoryAsync_ExistingDirectory_DeletesIt()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"melodee-safe-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDirectory);

        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var safeDeleteService = new SafeDeleteService(settingService, Logger);

        var result = await safeDeleteService.DeleteDirectoryAsync(targetDirectory, CancellationToken.None);

        result.Should().BeTrue();
        Directory.Exists(targetDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_NonExistingDirectory_ReturnsTrue()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"melodee-safe-delete-{Guid.NewGuid():N}", "nonexistent");

        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var safeDeleteService = new SafeDeleteService(settingService, Logger);

        var result = await safeDeleteService.DeleteDirectoryAsync(targetDirectory, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteDirectoryAsync_DryRun_DoesNotDelete()
    {
        var targetDirectory = Path.Combine(Path.GetTempPath(), $"melodee-safe-delete-{Guid.NewGuid():N}");
        Directory.CreateDirectory(targetDirectory);

        await UpsertSettingAsync("script.dryRun.enabled", "true", CancellationToken.None);

        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var safeDeleteService = new SafeDeleteService(settingService, Logger);

        var result = await safeDeleteService.DeleteDirectoryAsync(targetDirectory, CancellationToken.None);

        result.Should().BeTrue();
        Directory.Exists(targetDirectory).Should().BeTrue();

        // Cleanup
        Directory.Delete(targetDirectory, true);
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
