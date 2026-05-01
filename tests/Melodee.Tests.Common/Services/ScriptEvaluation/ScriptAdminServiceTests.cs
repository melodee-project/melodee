using FluentAssertions;
using Melodee.Common.Enums;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Services;
using Melodee.Common.Services.ScriptEvaluation;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

public sealed class ScriptAdminServiceTests : ServiceTestBase
{
    [Fact]
    public async Task UpsertAsync_WhenMissing_AddsSettingAndCanRetrieve()
    {
        var eventName = $"testEvent{Guid.NewGuid():N}";
        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var adminService = new ScriptAdminService(settingService, Serializer, Logger);

        var config = new ScriptConfig
        {
            Enabled = true,
            TimeoutMs = 123,
            MaxStatements = 456,
            Default = new ScriptDefaultConfig
            {
                Body = "function check(ctx, scriptConfig) { return true; }",
                OnDeny = "skip"
            },
            Overrides = []
        };

        var upsertResult = await adminService.UpsertAsync(eventName, config);
        upsertResult.IsSuccess.Should().BeTrue();
        upsertResult.Data.Should().BeTrue();

        var getResult = await adminService.GetAsync(eventName);
        getResult.Should().NotBeNull();
        getResult!.Config.Enabled.Should().BeTrue();
        getResult.Config.TimeoutMs.Should().Be(123);
        getResult.Config.MaxStatements.Should().Be(456);
        getResult.Config.Default.Body.Should().Contain("function check");
        getResult.Config.Default.OnDeny.Should().Be("skip");

        getResult.Setting.Key.Should().Be($"script.{eventName}");
        getResult.Setting.CategoryValue.Should().Be(SettingCategory.Scripting);
    }

    [Fact]
    public async Task UpsertAsync_WhenExisting_UpdatesStoredConfig()
    {
        var eventName = $"testEvent{Guid.NewGuid():N}";
        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var adminService = new ScriptAdminService(settingService, Serializer, Logger);

        var config1 = new ScriptConfig
        {
            Enabled = true,
            Default = new ScriptDefaultConfig
            {
                Body = "function check(ctx, scriptConfig) { return true; }",
                OnDeny = "skip"
            }
        };

        var config2 = new ScriptConfig
        {
            Enabled = true,
            Default = new ScriptDefaultConfig
            {
                Body = "function check(ctx, scriptConfig) { return false; }",
                OnDeny = "delete"
            }
        };

        (await adminService.UpsertAsync(eventName, config1)).IsSuccess.Should().BeTrue();
        (await adminService.UpsertAsync(eventName, config2)).IsSuccess.Should().BeTrue();

        var getResult = await adminService.GetAsync(eventName);
        getResult.Should().NotBeNull();
        getResult!.Config.Default.Body.Should().Contain("return false");
        getResult.Config.Default.OnDeny.Should().Be("delete");
    }

    [Fact]
    public async Task DeleteAsync_WhenPresent_RemovesSetting()
    {
        var eventName = $"testEvent{Guid.NewGuid():N}";
        var settingService = new SettingService(Logger, CacheManager, MockConfigurationFactory(), MockFactory());
        var adminService = new ScriptAdminService(settingService, Serializer, Logger);

        var config = new ScriptConfig
        {
            Enabled = true,
            Default = new ScriptDefaultConfig
            {
                Body = "function check(ctx, scriptConfig) { return true; }",
                OnDeny = "skip"
            }
        };

        (await adminService.UpsertAsync(eventName, config)).IsSuccess.Should().BeTrue();

        var deleteResult = await adminService.DeleteAsync(eventName);
        deleteResult.IsSuccess.Should().BeTrue();
        deleteResult.Data.Should().BeTrue();

        var getResult = await adminService.GetAsync(eventName);
        getResult.Should().BeNull();

        await using var context = await MockFactory().CreateDbContextAsync(CancellationToken.None);
        var setting = await context.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == $"script.{eventName}", CancellationToken.None);
        setting.Should().BeNull();
    }
}

