using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Plugins.Processor.Models;
using Melodee.Common.Services.Scanning;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IScriptedDirectoryProcessor
{
    Task<OperationResult<DirectoryProcessorResult>> ProcessDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Instant? lastProcessDate,
        int? maxAlbumsToProcess,
        int libraryId,
        CancellationToken cancellationToken = default);
}

public sealed class ScriptedDirectoryProcessor : IScriptedDirectoryProcessor
{
    private readonly DirectoryProcessorToStagingService _innerProcessor;
    private readonly IScriptOrchestrationService _scriptOrchestrationService;
    private readonly IDirectoryContextProvider _contextProvider;
    private readonly DenyActionHandlerFactory _denyActionHandlerFactory;
    private readonly LibraryService _libraryService;
    private readonly SettingService _settingService;
    private readonly ILogger _logger;

    public ScriptedDirectoryProcessor(
        DirectoryProcessorToStagingService innerProcessor,
        IScriptOrchestrationService scriptOrchestrationService,
        IDirectoryContextProvider contextProvider,
        DenyActionHandlerFactory denyActionHandlerFactory,
        LibraryService libraryService,
        SettingService settingService,
        ILogger logger)
    {
        _innerProcessor = innerProcessor;
        _scriptOrchestrationService = scriptOrchestrationService;
        _contextProvider = contextProvider;
        _denyActionHandlerFactory = denyActionHandlerFactory;
        _libraryService = libraryService;
        _settingService = settingService;
        _logger = logger;
    }

    public async Task<OperationResult<DirectoryProcessorResult>> ProcessDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Instant? lastProcessDate,
        int? maxAlbumsToProcess,
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        var featureEnabledResult = await _settingService.GetValueAsync("feature.eventScripting.enabled", false, cancellationToken);
        if (!featureEnabledResult.IsSuccess || featureEnabledResult.Data != true)
        {
            return await _innerProcessor.ProcessDirectoryAsync(fileSystemDirectoryInfo, lastProcessDate, maxAlbumsToProcess, cancellationToken);
        }

        var libraryResult = await _libraryService.GetAsync(libraryId, cancellationToken);
        if (!libraryResult.IsSuccess || libraryResult.Data == null)
        {
            _logger.Warning("Failed to get library {LibraryId}, falling back to non-scripted processing", libraryId);
            return await _innerProcessor.ProcessDirectoryAsync(fileSystemDirectoryInfo, lastProcessDate, maxAlbumsToProcess, cancellationToken);
        }

        var library = libraryResult.Data;

        var directoriesToProcess = fileSystemDirectoryInfo
            .GetFileSystemDirectoryInfosToProcess(lastProcessDate, SearchOption.AllDirectories)
            .ToList();

        var totalNewAlbums = 0;
        var totalNewArtists = 0;
        var totalNewSongs = 0;
        var totalDuration = 0.0;
        var directoriesSkipped = 0;
        var directoriesDeleted = 0;

        foreach (var directoryInfo in directoriesToProcess)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var context = _contextProvider.BuildContext(directoryInfo, library);

            var startResult = await _scriptOrchestrationService.EvaluateScriptForEventAsync(
                ScriptEventNames.DirectoryProcessingStart,
                context,
                libraryId,
                context.RelativePath,
                cancellationToken);

            if (!startResult.Result)
            {
                var onDeny = startResult.OnDeny ?? "skip";

                if (string.Equals(onDeny, "delete", StringComparison.OrdinalIgnoreCase))
                {
                    var deleteResult = await _scriptOrchestrationService.EvaluateScriptForEventAsync(
                        ScriptEventNames.DirectoryProcessingDelete,
                        context,
                        libraryId,
                        context.RelativePath,
                        cancellationToken);

                    if (deleteResult.Result)
                    {
                        var handler = _denyActionHandlerFactory.CreateHandler(onDeny);
                        var actionResult = await handler.ExecuteAsync(context.RelativePath, libraryId, cancellationToken);
                        if (actionResult)
                        {
                            directoriesDeleted++;
                            _logger.Information("Script denied processing for {Path}, action {Action} executed",
                                context.RelativePath, onDeny);
                            continue;
                        }
                    }
                }
                else if (string.Equals(onDeny, "quarantine", StringComparison.OrdinalIgnoreCase))
                {
                    var handler = _denyActionHandlerFactory.CreateHandler(onDeny);
                    var actionResult = await handler.ExecuteAsync(context.RelativePath, libraryId, cancellationToken);
                    if (actionResult)
                    {
                        directoriesSkipped++;
                        _logger.Information("Script denied processing for {Path}, action {Action} executed",
                            context.RelativePath, onDeny);
                        continue;
                    }
                }

                directoriesSkipped++;
                _logger.Debug("Script denied processing for {Path}, skipping", context.RelativePath);
                continue;
            }

            var result = await _innerProcessor.ProcessDirectoryAsync(
                directoryInfo,
                lastProcessDate,
                maxAlbumsToProcess,
                cancellationToken);

            if (result.IsSuccess && result.Data != null)
            {
                totalNewAlbums += result.Data.NewAlbumsCount;
                totalNewArtists += result.Data.NewArtistsCount;
                totalNewSongs += result.Data.NewSongsCount;
                totalDuration += result.Data.DurationInMs;
            }
        }

        return new OperationResult<DirectoryProcessorResult>
        {
            Data = new DirectoryProcessorResult
            {
                DurationInMs = totalDuration,
                NewAlbumsCount = totalNewAlbums,
                NewArtistsCount = totalNewArtists,
                NewSongsCount = totalNewSongs,
                NumberOfAlbumsProcessed = totalNewAlbums,
                NumberOfValidAlbumsProcessed = totalNewAlbums,
                NumberOfAlbumFilesProcessed = 0,
                NumberOfConversionPluginsProcessed = 0,
                NumberOfConversionPluginsProcessedFileCount = 0,
                NumberOfDirectoryPluginProcessed = 0
            }
        };
    }
}
