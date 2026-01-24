using FluentAssertions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.ScriptEvaluation;
using Moq;
using Serilog;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

public class ScriptOrchestrationServiceTests
{
    private static ILogger CreateLogger()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();
    }

    [Fact]
    public async Task EvaluateScriptForEventAsync_NoConfig_DefaultsToAllow()
    {
        var logger = CreateLogger();
        var serializer = new Serializer(logger);
        var cacheManager = new FakeCacheManager(logger, TimeSpan.FromMinutes(5), serializer);
        var cacheService = new ScriptCacheService(cacheManager, logger);
        var evaluationService = new ScriptEvaluationService(logger, cacheService);

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScriptConfig?)null);

        var orchestrationService = new ScriptOrchestrationService(
            configService.Object,
            evaluationService,
            logger);

        var result = await orchestrationService.EvaluateScriptForEventAsync(
            "directoryProcessingStart",
            new { },
            1,
            "Incoming/Test",
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateScriptForEventAsync_OverrideOnDeny_IsProvidedToScript()
    {
        var logger = CreateLogger();
        var serializer = new Serializer(logger);
        var cacheManager = new FakeCacheManager(logger, TimeSpan.FromMinutes(5), serializer);
        var cacheService = new ScriptCacheService(cacheManager, logger);
        var evaluationService = new ScriptEvaluationService(logger, cacheService);

        var config = new ScriptConfig
        {
            Default = new ScriptDefaultConfig
            {
                Body = "function check(ctx, scriptConfig) { return false; }",
                OnDeny = "skip"
            },
            Overrides =
            [
                new ScriptOverrideConfig
                {
                    Enabled = true,
                    LibraryId = 1,
                    PathPrefix = "Incoming/",
                    OnDeny = "delete",
                    Body = "function check(ctx, scriptConfig) { return scriptConfig.onDeny === 'delete'; }"
                }
            ],
            SettingKey = "script.directoryProcessingStart",
            SettingEtag = "1"
        };

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync("directoryProcessingStart", It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var orchestrationService = new ScriptOrchestrationService(
            configService.Object,
            evaluationService,
            logger);

        var result = await orchestrationService.EvaluateScriptForEventAsync(
            "directoryProcessingStart",
            new { },
            1,
            "Incoming/Test",
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeFalse();
        result.OnDeny.Should().Be("delete");
        result.ScriptKey.Should().Be("script.directoryProcessingStart");
        result.ScriptHash.Should().NotBeNullOrWhiteSpace();
        result.SelectedOverrideId.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptForEventAsync_NonBooleanReturn_DefaultsToAllow()
    {
        var logger = CreateLogger();
        var serializer = new Serializer(logger);
        var cacheManager = new FakeCacheManager(logger, TimeSpan.FromMinutes(5), serializer);
        var cacheService = new ScriptCacheService(cacheManager, logger);
        var evaluationService = new ScriptEvaluationService(logger, cacheService);

        var config = new ScriptConfig
        {
            Default = new ScriptDefaultConfig
            {
                Body = "function check(ctx, scriptConfig) { return 'nope'; }",
                OnDeny = "skip"
            },
            SettingKey = "script.directoryProcessingStart",
            SettingEtag = "1"
        };

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync("directoryProcessingStart", It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var orchestrationService = new ScriptOrchestrationService(
            configService.Object,
            evaluationService,
            logger);

        var result = await orchestrationService.EvaluateScriptForEventAsync(
            "directoryProcessingStart",
            new { },
            1,
            "Incoming/Test",
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        result.IsDefault.Should().BeTrue();
    }
}

