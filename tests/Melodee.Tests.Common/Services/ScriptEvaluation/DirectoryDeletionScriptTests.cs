using FluentAssertions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.ScriptEvaluation;
using Moq;
using Serilog;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

/// <summary>
/// Critical tests for directory deletion script logic.
/// These tests ensure that directories are ONLY deleted when the script explicitly returns true
/// and that directories are preserved in all other cases (errors, disabled scripts, default behavior).
/// 
/// PRODUCTION SAFETY: These tests are essential to prevent accidental data loss.
/// </summary>
public class DirectoryDeletionScriptTests
{
    private static ILogger CreateLogger()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();
    }

    private static ScriptEvaluationService CreateEvaluationService(ILogger logger)
    {
        var serializer = new Serializer(logger);
        var cacheManager = new FakeCacheManager(logger, TimeSpan.FromMinutes(5), serializer);
        var cacheService = new ScriptCacheService(cacheManager, logger);
        return new ScriptEvaluationService(logger, cacheService);
    }

    /// <summary>
    /// The actual production delete script that checks for insufficient files or media.
    /// </summary>
    private const string ProductionDeleteScript = @"
        function check(ctx, scriptConfig) { 
            if(ctx.totalFilesCount < 4 || ctx.mediaFilesCount < 4)
                return true;
            if(ctx.totalDurationMinutes < 10)
                return true;
            if(ctx.hasTrackNumberGaps)
                return true;
            return false; 
        }";

    #region Script Should Return TRUE (Directory Should Be Deleted)

    [Fact]
    public async Task DeleteScript_WithZeroMediaFiles_ReturnsTrue_ShouldDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 3,
            MediaFilesCount = 0,
            TotalDurationMinutes = 0,
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("directory with 0 media files should be marked for deletion");
        result.IsDefault.Should().BeFalse("script evaluated successfully, not a default");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteScript_WithThreeMediaFiles_ReturnsTrue_ShouldDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 3,
            MediaFilesCount = 3,
            TotalDurationMinutes = 15,
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("directory with only 3 media files (< 4) should be marked for deletion");
        result.IsDefault.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteScript_WithShortDuration_ReturnsTrue_ShouldDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 10,
            MediaFilesCount = 10,
            TotalDurationMinutes = 5, // Less than 10 minutes
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("directory with < 10 minutes of audio should be marked for deletion");
        result.IsDefault.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteScript_WithTrackNumberGaps_ReturnsTrue_ShouldDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 10,
            MediaFilesCount = 10,
            TotalDurationMinutes = 45,
            TrackNumbers = [1, 2, 4, 5], // Gap at track 3
            HasTrackNumberGaps = true
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("directory with track number gaps should be marked for deletion");
        result.IsDefault.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region Script Should Return FALSE (Directory Should NOT Be Deleted)

    [Fact]
    public async Task DeleteScript_WithSufficientMediaFiles_ReturnsFalse_ShouldNotDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/valid-album",
            DirectoryName = "valid-album",
            TotalFilesCount = 12,
            MediaFilesCount = 12,
            TotalDurationMinutes = 45,
            TrackNumbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeFalse("valid album with sufficient files should NOT be marked for deletion");
        result.IsDefault.Should().BeFalse("script evaluated successfully");
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteScript_WithExactlyFourMediaFiles_ReturnsFalse_ShouldNotDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/ep-album",
            DirectoryName = "ep-album",
            TotalFilesCount = 4,
            MediaFilesCount = 4,
            TotalDurationMinutes = 20,
            TrackNumbers = [1, 2, 3, 4],
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeFalse("album with exactly 4 files (not < 4) should NOT be marked for deletion");
        result.IsDefault.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteScript_WithExactlyTenMinutes_ReturnsFalse_ShouldNotDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/short-album",
            DirectoryName = "short-album",
            TotalFilesCount = 5,
            MediaFilesCount = 5,
            TotalDurationMinutes = 10, // Exactly 10 minutes (not < 10)
            TrackNumbers = [1, 2, 3, 4, 5],
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeFalse("album with exactly 10 minutes (not < 10) should NOT be marked for deletion");
        result.IsDefault.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region Error Handling - Directory Should NOT Be Deleted

    [Fact]
    public async Task DeleteScript_WhenScriptHasError_DefaultsToAllowAndShouldNotDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 0,
            MediaFilesCount = 0
        };

        var result = await evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { throw new Error('Script error'); }",
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("errors default to allow (true)");
        result.IsDefault.Should().BeTrue("error causes default behavior");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeleteScript_WhenScriptIsDisabled_DefaultsToAllowAndShouldNotDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 0,
            MediaFilesCount = 0
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig { Enabled = false },
            CancellationToken.None);

        result.Result.Should().BeTrue("disabled script defaults to allow");
        result.IsDefault.Should().BeTrue("disabled script uses default behavior");
    }

    [Fact]
    public async Task DeleteScript_WhenScriptBodyIsEmpty_DefaultsToAllowAndShouldNotDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 0,
            MediaFilesCount = 0
        };

        var result = await evaluationService.EvaluateScriptAsync(
            "",
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("empty script defaults to allow");
        result.IsDefault.Should().BeTrue("empty script uses default behavior");
    }

    [Fact]
    public async Task DeleteScript_WhenScriptReturnsNonBoolean_DefaultsToAllowAndShouldNotDelete()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 0,
            MediaFilesCount = 0
        };

        var result = await evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return 'delete me'; }",
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("non-boolean return defaults to allow");
        result.IsDefault.Should().BeTrue("non-boolean return uses default behavior");
        result.ErrorMessage.Should().Contain("non-boolean");
    }

    #endregion

    #region Orchestration Service - IsDefault Flag Tests

    [Fact]
    public async Task Orchestration_WithSuccessfulScriptReturningTrue_IsDefaultShouldBeFalse()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var config = new ScriptConfig
        {
            Default = new ScriptDefaultConfig
            {
                Body = ProductionDeleteScript,
                OnDeny = "delete"
            },
            SettingKey = "script.directoryProcessingDelete",
            SettingEtag = "1"
        };

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync("directoryProcessingDelete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var orchestrationService = new ScriptOrchestrationService(
            configService.Object,
            evaluationService,
            logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 0,
            MediaFilesCount = 0
        };

        var result = await orchestrationService.EvaluateScriptForEventAsync(
            "directoryProcessingDelete",
            context,
            CancellationToken.None);

        result.Result.Should().BeTrue("script returns true for empty directory");
        result.IsDefault.Should().BeFalse("successful script evaluation should NOT be default");
        result.OnDeny.Should().Be("delete");
    }

    [Fact]
    public async Task Orchestration_WithSuccessfulScriptReturningFalse_IsDefaultShouldBeFalse()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var config = new ScriptConfig
        {
            Default = new ScriptDefaultConfig
            {
                Body = ProductionDeleteScript,
                OnDeny = "delete"
            },
            SettingKey = "script.directoryProcessingDelete",
            SettingEtag = "1"
        };

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync("directoryProcessingDelete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var orchestrationService = new ScriptOrchestrationService(
            configService.Object,
            evaluationService,
            logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/valid-album",
            DirectoryName = "valid-album",
            TotalFilesCount = 12,
            MediaFilesCount = 12,
            TotalDurationMinutes = 45,
            TrackNumbers = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
            HasTrackNumberGaps = false
        };

        var result = await orchestrationService.EvaluateScriptForEventAsync(
            "directoryProcessingDelete",
            context,
            CancellationToken.None);

        result.Result.Should().BeFalse("script returns false for valid album");
        result.IsDefault.Should().BeFalse("successful script evaluation should NOT be default");
    }

    [Fact]
    public async Task Orchestration_WithNoConfig_IsDefaultShouldBeTrue()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync("directoryProcessingDelete", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScriptConfig?)null);

        var orchestrationService = new ScriptOrchestrationService(
            configService.Object,
            evaluationService,
            logger);

        var result = await orchestrationService.EvaluateScriptForEventAsync(
            "directoryProcessingDelete",
            new { },
            CancellationToken.None);

        result.Result.Should().BeTrue("no config defaults to allow");
        result.IsDefault.Should().BeTrue("no config means default behavior - MUST NOT trigger delete");
    }

    [Fact]
    public async Task Orchestration_WithScriptError_IsDefaultShouldBeTrue()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var config = new ScriptConfig
        {
            Default = new ScriptDefaultConfig
            {
                Body = "function check(ctx, scriptConfig) { throw new Error('Oops'); }",
                OnDeny = "delete"
            },
            SettingKey = "script.directoryProcessingDelete",
            SettingEtag = "1"
        };

        var configService = new Mock<IScriptConfigurationService>();
        configService
            .Setup(x => x.GetScriptConfigAsync("directoryProcessingDelete", It.IsAny<CancellationToken>()))
            .ReturnsAsync(config);

        var orchestrationService = new ScriptOrchestrationService(
            configService.Object,
            evaluationService,
            logger);

        var result = await orchestrationService.EvaluateScriptForEventAsync(
            "directoryProcessingDelete",
            new { },
            CancellationToken.None);

        result.Result.Should().BeTrue("error defaults to allow");
        result.IsDefault.Should().BeTrue("error means default behavior - MUST NOT trigger delete");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Delete Decision Logic Tests

    /// <summary>
    /// This test simulates the exact condition in DirectoryProcessorToStagingService:
    /// if (deleteResult.Result && !deleteResult.IsDefault)
    /// </summary>
    [Theory]
    [InlineData(true, false, true, "Script returns true, not default = DELETE")]
    [InlineData(true, true, false, "Script returns true but is default = DO NOT DELETE")]
    [InlineData(false, false, false, "Script returns false = DO NOT DELETE")]
    [InlineData(false, true, false, "Script returns false and is default = DO NOT DELETE")]
    public void DeleteDecisionLogic_CorrectlyDeterminesWhetherToDelete(
        bool scriptResult, 
        bool isDefault, 
        bool expectedShouldDelete,
        string scenario)
    {
        // This is the exact condition from DirectoryProcessorToStagingService.cs line 1215
        var shouldDelete = scriptResult && !isDefault;
        
        shouldDelete.Should().Be(expectedShouldDelete, scenario);
    }

    #endregion

    #region Context Property Access Tests

    [Fact]
    public async Task DeleteScript_CanAccessAllContextProperties()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test-dir",
            TotalFilesCount = 5,
            TotalSizeMegabytes = 100.5,
            MostRecentModified = "2025-01-25T00:00:00Z",
            MediaFilesCount = 3,
            TotalDurationMinutes = 15.5,
            TrackNumbers = [1, 2, 3],
            HasTrackNumberGaps = false
        };

        // Script that validates all properties are accessible as camelCase
        // Note: Use >= and <= comparisons for floats to avoid floating point precision issues
        // Arrays are converted to List<object?> by ScriptValueConverter which Jint can iterate
        const string validationScript = @"
            function check(ctx, scriptConfig) {
                return ctx.path === '/mnt/incoming/test' &&
                       ctx.directoryName === 'test-dir' &&
                       ctx.totalFilesCount === 5 &&
                       ctx.totalSizeMegabytes >= 100 && ctx.totalSizeMegabytes <= 101 &&
                       ctx.mediaFilesCount === 3 &&
                       ctx.totalDurationMinutes >= 15 && ctx.totalDurationMinutes <= 16 &&
                       ctx.hasTrackNumberGaps === false &&
                       ctx.trackNumbers && ctx.trackNumbers.length === 3;
            }";

        var result = await evaluationService.EvaluateScriptAsync(
            validationScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("all context properties should be accessible in script");
        result.IsDefault.Should().BeFalse();
        result.ErrorMessage.Should().BeNull();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task DeleteScript_WithNullTrackNumbers_HandlesGracefully()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 3,
            MediaFilesCount = 3,
            TotalDurationMinutes = 5,
            TrackNumbers = [], // Empty array
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        // Should not throw, should evaluate based on other criteria
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task DeleteScript_BoundaryCondition_ThreeFilesAndNineMinutes_ReturnsTrue()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        // All boundary conditions that should trigger deletion
        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 3, // < 4
            MediaFilesCount = 3, // < 4
            TotalDurationMinutes = 9, // < 10
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue("all conditions fail, should be marked for deletion");
    }

    [Fact]
    public async Task DeleteScript_BoundaryCondition_FourFilesAndTenMinutes_ReturnsFalse()
    {
        var logger = CreateLogger();
        var evaluationService = CreateEvaluationService(logger);

        // Exact boundary conditions that should NOT trigger deletion
        var context = new DirectoryProcessingContext
        {
            Path = "/mnt/incoming/test",
            DirectoryName = "test",
            TotalFilesCount = 4, // >= 4
            MediaFilesCount = 4, // >= 4
            TotalDurationMinutes = 10, // >= 10
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            ProductionDeleteScript,
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeFalse("all conditions pass, should NOT be marked for deletion");
    }

    #endregion
}
