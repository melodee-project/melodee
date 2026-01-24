using FluentAssertions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.ScriptEvaluation;
using Serilog;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

public class ScriptEvaluationServiceTests
{
    [Fact]
    public async Task EvaluateScriptAsync_ContextIsExposedAsCamelCase()
    {
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();

        var serializer = new Serializer(logger);
        var cacheManager = new FakeCacheManager(logger, TimeSpan.FromMinutes(5), serializer);
        var cacheService = new ScriptCacheService(cacheManager, logger);
        var evaluationService = new ScriptEvaluationService(logger, cacheService);

        var context = new DirectoryProcessingContext
        {
            LibraryId = 123,
            RelativePath = "Incoming/Test",
            DirectoryName = "Test",
            TotalFilesCount = 0,
            TotalSizeMegabytes = 0,
            MostRecentModified = DateTime.UtcNow.ToString("O"),
            MediaFilesCount = 0,
            TotalDurationMinutes = 0,
            TrackNumbers = [],
            HasTrackNumberGaps = false
        };

        var result = await evaluationService.EvaluateScriptAsync(
            "function check(ctx, scriptConfig) { return ctx.libraryId === 123 && ctx.relativePath === 'Incoming/Test'; }",
            context,
            new { },
            new ScriptConfig(),
            CancellationToken.None);

        result.Result.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
    }
}

