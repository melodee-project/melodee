using FluentAssertions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.ScriptEvaluation;
using Serilog;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

[Collection("ScriptEvaluation")]
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

    #region Malformed and Invalid Script Tests - All Should Default to Allow (true)

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptBodyIsWhitespaceOnly()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "   \t\n   ",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("whitespace-only script should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptHasSyntaxError()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return true; ", // Missing closing brace
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("syntax error should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptHasUndefinedVariable()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return undefinedVariable; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("undefined variable should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptCallsUndefinedFunction()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return undefinedFunction(); }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("undefined function should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptHasInfiniteLoop()
    {
        // Script with infinite loop should timeout and default to allow
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { while(true) {} return false; }",
            new { },
            new { },
            new ScriptConfig { TimeoutMs = 100 }, // Short timeout
            CancellationToken.None);

        result.Result.Should().BeTrue("timeout should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptExceedsMaxStatements()
    {
        // Script that exceeds max statements
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { var x = 0; for(var i = 0; i < 100000; i++) { x++; } return false; }",
            new { },
            new { },
            new ScriptConfig { MaxStatements = 100 }, // Very low limit
            CancellationToken.None);

        result.Result.Should().BeTrue("exceeding max statements should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptReturnsNull()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return null; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("null return should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptReturnsUndefined()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return undefined; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("undefined return should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptReturnsNumber()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return 42; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("number return should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptReturnsZero()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return 0; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("zero return should default to allow (not treated as falsy)");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptReturnsEmptyString()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return ''; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("empty string return should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptReturnsArray()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return [true, false]; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("array return should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptReturnsEmptyObject()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return {}; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("empty object return should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptMissingCheckFunction()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function wrongName(ctx, scriptConfig) { return false; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("missing check function should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptHasRuntimeTypeError()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return ctx.nonExistent.property; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("runtime type error should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptHasInvalidJson()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { JSON.parse('invalid json'); return false; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("JSON parse error should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptHasDivisionByZero()
    {
        // JavaScript handles division by zero differently - returns Infinity, not an error
        // But we test to ensure no crash
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { var x = 1/0; return x === Infinity; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        // This should actually return true since x === Infinity is true in JS
        result.Result.Should().BeTrue();
        result.IsDefault.Should().BeFalse("valid boolean return");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptBodyIsNull()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            null!,
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("null script body should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().Contain("empty");
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptThrowsCustomError()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { throw 'Custom string error'; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("thrown string error should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EvaluateScriptAsync_DefaultsToAllow_WhenScriptThrowsObjectError()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { throw { code: 500, message: 'Server error' }; }",
            new { },
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("thrown object error should default to allow");
        result.IsDefault.Should().BeTrue();
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Script with Complex Logic - Should Work Correctly

    [Fact]
    public async Task EvaluateScriptAsync_HandlesComplexConditionalLogic()
    {
        var context = new DirectoryProcessingContext
        {
            Path = "/test",
            DirectoryName = "test",
            TotalFilesCount = 5,
            MediaFilesCount = 3,
            TotalDurationMinutes = 15,
            HasTrackNumberGaps = false,
            TrackNumbers = [1, 2, 3]
        };

        var result = await _evaluationService.EvaluateScriptAsync(
            @"function check(ctx, scriptConfig) { 
                if (ctx.mediaFilesCount < 4) return true;
                if (ctx.totalDurationMinutes < 10) return true;
                return false;
            }",
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("mediaFilesCount < 4 should trigger delete");
        result.IsDefault.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task EvaluateScriptAsync_CanAccessScriptConfig()
    {
        var result = await _evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return scriptConfig.threshold > 5; }",
            new { },
            new { threshold = 10 },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("scriptConfig.threshold (10) > 5");
        result.IsDefault.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
    }

    #endregion
}

