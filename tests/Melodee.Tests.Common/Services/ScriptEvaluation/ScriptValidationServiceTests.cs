using FluentAssertions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.ScriptEvaluation;
using Moq;
using Serilog;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

public class ScriptValidationServiceTests
{
    private static ILogger CreateLogger()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();
    }

    [Fact]
    public async Task ValidateScriptAsync_ExpressionBody_IsEvaluated()
    {
        var logger = CreateLogger();
        var serializer = new Serializer(logger);
        var cacheManager = new FakeCacheManager(logger, TimeSpan.FromMinutes(5), serializer);
        var cacheService = new ScriptCacheService(cacheManager, logger);

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScriptConfig());

        var validationService = new ScriptValidationService(
            configService.Object,
            cacheService,
            logger);

        var result = await validationService.ValidateScriptAsync(new ScriptValidationRequest
        {
            EventName = "directoryProcessingStart",
            ScriptBody = "true",
            Context = new { }
        }, CancellationToken.None);

        result.IsValid.Should().BeTrue();
        result.Result.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ValidateScriptAsync_NonBooleanReturn_IsInvalid()
    {
        var logger = CreateLogger();
        var serializer = new Serializer(logger);
        var cacheManager = new FakeCacheManager(logger, TimeSpan.FromMinutes(5), serializer);
        var cacheService = new ScriptCacheService(cacheManager, logger);

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScriptConfig());

        var validationService = new ScriptValidationService(
            configService.Object,
            cacheService,
            logger);

        var result = await validationService.ValidateScriptAsync(new ScriptValidationRequest
        {
            EventName = "directoryProcessingStart",
            ScriptBody = "function check(ctx, scriptConfig) { return 'nope'; }",
            Context = new { }
        }, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Result.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ValidateScriptAsync_SyntaxError_IsInvalid()
    {
        var logger = CreateLogger();
        var serializer = new Serializer(logger);
        var cacheManager = new FakeCacheManager(logger, TimeSpan.FromMinutes(5), serializer);
        var cacheService = new ScriptCacheService(cacheManager, logger);

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScriptConfig());

        var validationService = new ScriptValidationService(
            configService.Object,
            cacheService,
            logger);

        var result = await validationService.ValidateScriptAsync(new ScriptValidationRequest
        {
            EventName = "directoryProcessingStart",
            ScriptBody = "function check(ctx, scriptConfig) { return ;",
            Context = new { }
        }, CancellationToken.None);

        result.IsValid.Should().BeFalse();
        result.Result.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }
}

