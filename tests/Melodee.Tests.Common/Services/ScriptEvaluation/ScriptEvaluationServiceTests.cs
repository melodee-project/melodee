using FluentAssertions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.ScriptEvaluation;
using Serilog;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

public class ScriptEvaluationServiceTests
{
    private readonly ScriptEvaluationService _evaluationService;

    public ScriptEvaluationServiceTests()
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();

        var serializer = new Serializer(logger);
        var cacheManager = new FakeCacheManager(logger, TimeSpan.FromMinutes(5), serializer);
        var cacheService = new ScriptCacheService(cacheManager, logger);
        _evaluationService = new ScriptEvaluationService(logger, cacheService);
    }

    [Fact]
    public async Task EvaluateScriptAsync_ContextIsExposedAsCamelCase()
    {
        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/Test",
            DirectoryName = "Test",
            TotalFilesCount = 0,
            TotalSizeMegabytes = 0,
            MostRecentModified = DateTime.UtcNow.ToString("O"),
            MediaFilesCount = 0,
            TotalDurationMinutes = 0,
            TrackNumbers = [],
            HasTrackNumberGaps = false
        };

        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return ctx.path === '/mnt/incoming/Test' && ctx.directoryName === 'Test'; }",
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScriptAsync_ReturnsTrue_WhenScriptReturnsTrue()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return true; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeFalse();
        result.Message.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScriptAsync_ReturnsFalse_WhenScriptReturnsFalse()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return false; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeFalse();
        result.IsDefault.Should().BeFalse();
        result.Message.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScriptAsync_ReturnsObjectWithMessage_WhenScriptReturnsObjectWithResultAndMessage()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return { result: false, message: 'Access denied for testing' }; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeFalse();
        result.IsDefault.Should().BeFalse();
        result.Message.Should().Be("Access denied for testing");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScriptAsync_ReturnsObjectWithoutMessage_WhenScriptReturnsObjectWithResultOnly()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return { result: true }; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeFalse();
        result.Message.Should().BeNull();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScriptAsync_ReturnsObjectWithTrueAndMessage_WhenScriptReturnsObjectWithTrueResultAndMessage()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return { result: true, message: 'Welcome!' }; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeFalse();
        result.Message.Should().Be("Welcome!");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptReturnsInvalidObject()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return { invalid: 'no result property' }; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptReturnsString()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return 'not a boolean'; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptThrows()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { throw new Error('Test error'); }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("Test error");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptIsDisabled()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return false; }",
            new { },
            new { },
            new ScriptConfig { Enabled = false },
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptBodyIsEmpty()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("empty");
    }
}

