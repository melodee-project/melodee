using Melodee.Common.Models;
using Melodee.Common.Plugins.Processor.Models;
using Melodee.Common.Services.Scanning;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IScriptedDirectoryProcessor
{
    Task<OperationResult<DirectoryProcessorResult>> ProcessDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Instant? lastProcessDate,
        int? maxAlbumsToProcess,
        CancellationToken cancellationToken = default);
}

public sealed class ScriptedDirectoryProcessor : IScriptedDirectoryProcessor
{
    private readonly DirectoryProcessorToStagingService _innerProcessor;
    private readonly ILogger _logger;

    public ScriptedDirectoryProcessor(
        DirectoryProcessorToStagingService innerProcessor,
        ILogger logger)
    {
        _innerProcessor = innerProcessor;
        _logger = logger;
    }

    public Task<OperationResult<DirectoryProcessorResult>> ProcessDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Instant? lastProcessDate,
        int? maxAlbumsToProcess,
        CancellationToken cancellationToken = default)
    {
        // Script evaluation is now handled internally by DirectoryProcessorToStagingService
        return _innerProcessor.ProcessDirectoryAsync(fileSystemDirectoryInfo, lastProcessDate, maxAlbumsToProcess, cancellationToken);
    }
}
