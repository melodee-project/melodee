using System.Collections.Concurrent;
using System.Diagnostics;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Models.SpecialArtists;
using Melodee.Common.Plugins.Conversion;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.Conversion.Media;
using Melodee.Common.Plugins.MetaData.Directory;
using Melodee.Common.Plugins.MetaData.Directory.Blackbeard;
using Melodee.Common.Plugins.MetaData.Directory.Nfo;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Processor;
using Melodee.Common.Plugins.Processor.Models;
using Melodee.Common.Plugins.Scripting;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.ScriptEvaluation;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using SmartFormat;
using ImageInfo = Melodee.Common.Models.ImageInfo;

namespace Melodee.Common.Services.Scanning;

/// <summary>
///     Take a given directory and process all the directories in it putting processed files into the staging library.
/// </summary>
public sealed class DirectoryProcessorToStagingService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    ISerializer serializer,
    MediaEditService mediaEditService,
    ArtistSearchEngineService artistSearchEngineService,
    AlbumImageSearchEngineService albumImageSearchEngineService,
    IHttpClientFactory httpClientFactory,
    IFileSystemService fileSystemService,
    IScriptOrchestrationService scriptOrchestrationService,
    IDirectoryContextProvider directoryContextProvider,
    DenyActionHandlerFactory denyActionHandlerFactory,
    IImageProcessor imageProcessor)
    : ServiceBase(logger, cacheManager, contextFactory), IDisposable
{
    private const string CueSheetExtension = "CUE";
    private static readonly HashSet<string> SourceSidecarMetadataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        CueSheetExtension,
        M3UPlaylist.HandlesExtension,
        Nfo.HandlesExtension,
        SimpleFileVerification.HandlesExtension
    };
    private static readonly HashSet<string> SourceResidueTextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "txt"
    };

    /// <summary>
    ///     Rip/verification provenance artifacts left behind after the media was extracted. These are regenerable
    ///     transient reports (EAC logs, AccurateRip, cuetools TOC/checksum) and are safe to remove once a release
    ///     has been processed.
    /// </summary>
    private static readonly HashSet<string> SourceResidueProvenanceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "log",
        "accurip",
        "toc",
        "md5",
        "html",
        "htm",
        "url"
    };

    /// <summary>
    ///     Known extensionless release-note/provenance files dropped by rippers that have no usable extension to
    ///     classify them by. Matched by exact file name.
    /// </summary>
    private static readonly HashSet<string> SourceResidueKnownFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "about_album",
        "descript.ion"
    };

    private readonly SemaphoreSlim _processingThrottle = new(Environment.ProcessorCount);
    private readonly SemaphoreSlim _conversionThrottle = new(CalculateMaxConcurrentConversions(Environment.ProcessorCount));
    private bool _disposed;
    private IAlbumNamesInDirectoryPlugin _albumNamesInDirectoryPlugin = null!;
    private IAlbumValidator _albumValidator = new AlbumValidator(new MelodeeConfiguration([]));
    private IMelodeeConfiguration _configuration = new MelodeeConfiguration([]);

    /// <summary>
    ///     Extensions (without dots) configured via <see cref="SettingRegistry.ProcessingFileExtensionsToDelete" />
    ///     that are treated as source residue and deleted during processing.
    /// </summary>
    private HashSet<string> _configuredResidueExtensions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     These plugins convert media from various formats into configured formats.
    /// </summary>
    private IEnumerable<IConversionPlugin> _conversionPlugins = [];

    /// <summary>
    ///     These plugins translate various files into albums.
    /// </summary>
    private IEnumerable<IDirectoryPlugin> _directoryPlugins = [];

    private string _directoryStaging = null!;
    private int _duplicateThreshold;
    private ImageConvertor _imageConvertor = null!;
    private IImageValidator _imageValidator = null!;
    private bool _initialized;
    private int _maxAlbumProcessingCount;

    /// <summary>
    ///     These plugins create albums from media files.
    /// </summary>
    private IEnumerable<IDirectoryPlugin> _mediaAlbumCreatorPlugins = [];

    private IScriptPlugin _postDiscoveryScript = new NullScript();

    private IScriptPlugin _preDiscoveryScript = new NullScript();

    private ISongPlugin[] _songPlugins = [];

    private bool _stopProcessingTriggered;

    public void Dispose()
    {
        _disposed = true;
        _processingThrottle.Dispose();
        _conversionThrottle.Dispose();
    }

    /// <summary>
    ///     Calculates the conversion concurrency cap used to avoid saturating CPU and disk with ffmpeg processes.
    /// </summary>
    public static int CalculateMaxConcurrentConversions(int processorCount)
    {
        if (processorCount < 1)
        {
            return 1;
        }

        return Math.Max(1, Math.Min(2, processorCount / 2));
    }

    public async Task InitializeAsync(IMelodeeConfiguration? configuration = null, CancellationToken token = default)
    {
        await InitializeAsync(configuration, null, token).ConfigureAwait(false);
    }

    public async Task InitializeAsync(IMelodeeConfiguration? configuration, string? stagingPathOverride, CancellationToken token)
    {
        if (_initialized)
        {
            return;
        }

        _configuration = configuration ?? await configurationFactory.GetConfigurationAsync(token).ConfigureAwait(false);

        _maxAlbumProcessingCount = _configuration.GetValue<int>(SettingRegistry.ProcessingMaximumProcessingCount,
            value => value < 1 ? int.MaxValue : value);

        _duplicateThreshold = _configuration.GetValue<int?>(SettingRegistry.ImagingDuplicateThreshold) ??
                              MelodeeConfiguration.DefaultImagingDuplicateThreshold;

        _configuredResidueExtensions = MelodeeConfiguration
            .FromSerializedJsonArray(
                _configuration.GetValue<string>(SettingRegistry.ProcessingFileExtensionsToDelete),
                serializer)
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.TrimStart('.').ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        _directoryStaging = stagingPathOverride ?? (await libraryService.GetStagingLibraryAsync(token).ConfigureAwait(false)).Data.Path;

        _albumValidator = new AlbumValidator(_configuration);
        _imageValidator = new ImageValidator(imageProcessor, _configuration);
        _imageConvertor = new ImageConvertor(imageProcessor, _configuration);
        _songPlugins =
        [
            new AtlMetaTag(new MetaTagsProcessor(_configuration, serializer), imageProcessor, _imageConvertor, _imageValidator,
                _configuration),
            new NativeId3MetaTag(new MetaTagsProcessor(_configuration, serializer), _configuration)
        ];
        _albumNamesInDirectoryPlugin = new AtlMetaTag(new MetaTagsProcessor(_configuration, serializer),
            imageProcessor, _imageConvertor, _imageValidator, _configuration);

        _conversionPlugins =
        [
            new ImageConvertor(imageProcessor, _configuration),
            new MediaConvertor(_configuration)
        ];

        _directoryPlugins =
        [
            new CueSheet(serializer, _songPlugins, _albumValidator, _configuration)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.PluginEnabledCueSheet)
            },
            new Blackbeard(serializer, _albumValidator, _configuration)
            {
                IsEnabled = _configuration.GetValue<bool?>(SettingRegistry.PluginEnabledBlackbeard) ?? true
            },
            new SimpleFileVerification(serializer, _songPlugins, _albumValidator, _configuration)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.PluginEnabledSimpleFileVerification)
            },
            new M3UPlaylist(serializer, _songPlugins, _albumValidator, _configuration)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.PluginEnabledM3u)
            },
            new Nfo(serializer, _albumValidator, _configuration)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.PluginEnabledNfo)
            }
        ];

        _mediaAlbumCreatorPlugins =
        [
            new Mp3Files(_songPlugins, _albumValidator, serializer, Logger, _configuration)
        ];

        var preDiscoveryScript = _configuration.GetValue<string>(SettingRegistry.ScriptingPreDiscoveryScript).Nullify();
        if (preDiscoveryScript != null)
        {
            _preDiscoveryScript = new PreDiscoveryScript(_configuration);
        }

        var postDiscoveryScript =
            _configuration.GetValue<string>(SettingRegistry.ScriptingPostDiscoveryScript).Nullify();
        if (postDiscoveryScript != null)
        {
            _postDiscoveryScript = new PostDiscoveryScript(_configuration);
        }

        await mediaEditService.InitializeAsync(configuration, token).ConfigureAwait(false);
        await artistSearchEngineService.InitializeAsync(configuration, token).ConfigureAwait(false);

        _initialized = true;
    }

    private void CheckInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException("Directory processor service is not initialized.");
        }
    }

    public async Task<OperationResult<DirectoryProcessorResult>> ProcessDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo, Instant? lastProcessDate, int? maxAlbumsToProcess,
        CancellationToken cancellationToken = default)
    {
        return await ProcessDirectoryAsync(fileSystemDirectoryInfo, lastProcessDate, maxAlbumsToProcess, (int?)null, cancellationToken);
    }

    public async Task<OperationResult<DirectoryProcessorResult>> ProcessDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Instant? lastProcessDate,
        int? maxAlbumsToProcess,
        DirectoryRunContext? runContext,
        CancellationToken cancellationToken = default)
    {
        return await ProcessDirectoryAsync(fileSystemDirectoryInfo, lastProcessDate, maxAlbumsToProcess, null, runContext, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<DirectoryProcessorResult>> ProcessDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo, Instant? lastProcessDate, int? maxAlbumsToProcess,
        int? libraryId,
        CancellationToken cancellationToken = default)
    {
        return await ProcessDirectoryAsync(fileSystemDirectoryInfo, lastProcessDate, maxAlbumsToProcess, libraryId, null, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<DirectoryProcessorResult>> ProcessDirectoryAsync(
        FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Instant? lastProcessDate,
        int? maxAlbumsToProcess,
        int? libraryId,
        DirectoryRunContext? runContext,
        CancellationToken cancellationToken = default)
    {
        CheckInitialized();

        var processingMessages = new ConcurrentBag<string>();
        var processingErrors = new ConcurrentBag<Exception>();
        var numberOfAlbumJsonFilesProcessed = 0;
        var numberOfValidAlbumsProcessed = 0;
        var numberOfAlbumsProcessed = 0;

        var conversionPluginsProcessedFileCount = 0;
        var directoryPluginProcessedFileCount = 0;
        var numberOfAlbumFilesProcessed = 0;

        var artistsIdsSeen = new ConcurrentBag<long?>();
        var albumsIdsSeen = new ConcurrentBag<long?>();
        var songsIdsSeen = new ConcurrentBag<Guid>();

        var result = new DirectoryProcessorResult
        {
            DurationInMs = 0,
            NewAlbumsCount = 0,
            NewArtistsCount = 0,
            NewSongsCount = 0,
            NumberOfAlbumFilesProcessed = 0,
            NumberOfConversionPluginsProcessed = 0,
            NumberOfConversionPluginsProcessedFileCount = 0,
            NumberOfDirectoryPluginProcessed = 0,
            NumberOfValidAlbumsProcessed = 0,
            NumberOfAlbumsProcessed = 0
        };

        _maxAlbumProcessingCount = maxAlbumsToProcess ?? _maxAlbumProcessingCount;

        var startTicks = Stopwatch.GetTimestamp();

        // Standalone processing owns the run context; full library scans pass one shared context across stages.
        using var localRunContext = runContext is null ? new DirectoryRunContext() : null;
        var activeRunContext = runContext ?? localRunContext!;

        // Ensure directory to process exists
        LogAndRaiseEvent(LogEventLevel.Debug, "Ensuring processing path [{0}] exists...", null, fileSystemDirectoryInfo.Path);
        if (!fileSystemService.DirectoryExists(fileSystemDirectoryInfo.Path))
        {
            return new OperationResult<DirectoryProcessorResult>
            {
                Errors =
                [
                    new Exception($"Directory [{fileSystemDirectoryInfo}] not found.")
                ],
                Data = result
            };
        }

        // Ensure that staging directory exists
        LogAndRaiseEvent(LogEventLevel.Debug, "Ensuring staging path [{0}] exists...", null, _directoryStaging);
        if (!fileSystemService.DirectoryExists(_directoryStaging))
        {
            return new OperationResult<DirectoryProcessorResult>
            {
                Errors =
                [
                    new Exception($"Staging Directory [{_directoryStaging}] not found.")
                ],
                Data = result
            };
        }

        // Run PreDiscovery script
        if (_configuration.GetValue<bool>(SettingRegistry.ScriptingEnabled) && _preDiscoveryScript.IsEnabled)
        {
            LogAndRaiseEvent(LogEventLevel.Debug, "Executing _preDiscoveryScript [{0}]", null,
                _preDiscoveryScript.DisplayName);
            var preDiscoveryScriptResult = new OperationResult<bool>
            {
                Data = false
            };
            try
            {
                preDiscoveryScriptResult = await _preDiscoveryScript
                    .ProcessAsync(fileSystemDirectoryInfo, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                LogAndRaiseEvent(LogEventLevel.Error, "PreDiscoveryScript [{0}]", e, _preDiscoveryScript.DisplayName);
                preDiscoveryScriptResult.AddError(e);
            }

            if (!preDiscoveryScriptResult.IsSuccess)
            {
                return new OperationResult<DirectoryProcessorResult>(preDiscoveryScriptResult.Messages)
                {
                    Errors = preDiscoveryScriptResult.Errors,
                    Data = result
                };
            }
        }

        foreach (var dirInfo in fileSystemService.EnumerateDirectories(fileSystemDirectoryInfo.Path, "*", SearchOption.TopDirectoryOnly))
        {
            if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
            {
                break;
            }

            var dirName = dirInfo.FullName;
            if (Path.GetExtension(dirName).Nullify() != null)
            {
                var newDirName = dirName.Replace(".", "_");
                if (newDirName != dirName)
                {
                    fileSystemService.MoveDirectory(dirName, newDirName);
                    Logger.Debug("[{Name}] renamed directory from [{Old}] to [{New}]",
                        nameof(DirectoryProcessorToStagingService),
                        dirName,
                        newDirName);
                }
            }
        }

        var directoriesToProcess = fileSystemDirectoryInfo
            .GetFileSystemDirectoryInfosToProcess(lastProcessDate, SearchOption.AllDirectories).ToList();
        if (directoriesToProcess.Count > 0)
        {
            OnProcessingStart?.Invoke(this, directoriesToProcess.Count);
            LogAndRaiseEvent(LogEventLevel.Debug, "\u251c Found [{0}] directories to process", null,
                directoriesToProcess.Count);
        }

        // Process directories in parallel with controlled concurrency
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Environment.ProcessorCount // Limit parallel execution to CPU core count
        };

        var processedCount = 0;
        var totalDirectories = directoriesToProcess.Count;
        var nextProgressReport = 10; // Report every 10 directories

        try
        {
            await Parallel.ForEachAsync(directoriesToProcess, parallelOptions, async (directoryInfoToProcess, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                await _processingThrottle.WaitAsync(ct);
                try
                {
                    var processingResult = await ProcessSingleDirectoryAsync(
                        directoryInfoToProcess,
                        processingMessages,
                        processingErrors,
                        artistsIdsSeen,
                        albumsIdsSeen,
                        songsIdsSeen,
                        activeRunContext,
                        libraryId,
                        ct);
                    numberOfAlbumsProcessed += processingResult.Item1;
                    numberOfValidAlbumsProcessed += processingResult.Item2;
                    activeRunContext.IncrementDirectoriesProcessed();

                    var currentCount = Interlocked.Increment(ref processedCount);
                    if (currentCount >= nextProgressReport || currentCount == totalDirectories)
                    {
                        var percentComplete = (currentCount * 100) / totalDirectories;
                        LogAndRaiseEvent(LogEventLevel.Information,
                            "Progress: {0}/{1} directories ({2}%) - {3} valid albums processed",
                            null, currentCount, totalDirectories, percentComplete, numberOfValidAlbumsProcessed);
                        Interlocked.Add(ref nextProgressReport, 10);
                    }
                }
                finally
                {
                    if (!_disposed)
                    {
                        try
                        {
                            _processingThrottle.Release();
                        }
                        catch (ObjectDisposedException)
                        {
                            // Semaphore was disposed during shutdown - ignore
                        }
                    }
                    OnDirectoryProcessed?.Invoke(this, directoryInfoToProcess);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Handle cancellation gracefully - this is expected behavior
            LogAndRaiseEvent(LogEventLevel.Debug, "Processing was cancelled");
        }

        // Run PostDiscovery script
        if (_configuration.GetValue<bool>(SettingRegistry.ScriptingEnabled) && _postDiscoveryScript.IsEnabled)
        {
            var postDiscoveryScriptResult = new OperationResult<bool>
            {
                Data = false
            };
            try
            {
                postDiscoveryScriptResult = await _postDiscoveryScript
                    .ProcessAsync(fileSystemDirectoryInfo, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                LogAndRaiseEvent(LogEventLevel.Error, "PostDiscoveryScript [{0}]", e, _postDiscoveryScript.DisplayName);
                postDiscoveryScriptResult.AddError(e);
            }

            if (!postDiscoveryScriptResult.IsSuccess)
            {
                return new OperationResult<DirectoryProcessorResult>(postDiscoveryScriptResult.Messages)
                {
                    Errors = postDiscoveryScriptResult.Errors,
                    Data = result
                };
            }
        }

        // Remove residue from media-free directories once a release's media has been staged/ingested. This is gated on
        // either move mode (doDeleteOriginal) or the copy-mode residue-after-ingest flag, which defaults on so leftover
        // junk (logs, sidecars, images, failed transcodes) is cleaned even when the original media is preserved.
        if (ShouldDeleteSourceResidueAfterIngest())
        {
            DeleteSourceResidueOnlyDirectoryFiles(fileSystemDirectoryInfo, Logger, _configuredResidueExtensions);
        }

        fileSystemDirectoryInfo.DeleteAllEmptyDirectories();

        LogAndRaiseEvent(LogEventLevel.Debug, "Processing Complete!");

        return new OperationResult<DirectoryProcessorResult>(processingMessages)
        {
            Errors = processingErrors.ToArray(),
            Data = new DirectoryProcessorResult
            {
                DurationInMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds,
                NewAlbumsCount = albumsIdsSeen.Distinct().Count(),
                NewArtistsCount = artistsIdsSeen.Distinct().Count(),
                NewSongsCount = songsIdsSeen.Distinct().Count(),
                NumberOfAlbumFilesProcessed = numberOfAlbumJsonFilesProcessed,
                NumberOfConversionPluginsProcessed = numberOfAlbumFilesProcessed,
                NumberOfConversionPluginsProcessedFileCount = conversionPluginsProcessedFileCount,
                NumberOfDirectoryPluginProcessed = directoryPluginProcessedFileCount,
                NumberOfValidAlbumsProcessed = numberOfValidAlbumsProcessed,
                NumberOfAlbumsProcessed = numberOfAlbumsProcessed
            }
        };
    }

    /// <summary>
    ///     This is raised when a Log event happens to return activity to caller.
    /// </summary>
    public event EventHandler<string>? OnProcessingEvent;

    /// <summary>
    ///     This is raised when the number of directories to process is known.
    /// </summary>
    public event EventHandler<int>? OnProcessingStart;

    /// <summary>
    ///     This is raised when a new Album is processed put into the Staging directory.
    /// </summary>
    public event EventHandler<FileSystemDirectoryInfo>? OnDirectoryProcessed;

    private void LogAndRaiseEvent(LogEventLevel logLevel, string messageTemplate, Exception? exception = null,
        params object[] args)
    {
        if (exception != null)
        {
            Log.Error(exception, messageTemplate, args);
        }
        else
        {
            Log.Write(logLevel, messageTemplate, args);
        }

        OnProcessingEvent?.Invoke(this, FormatProcessingEventMessage(messageTemplate, exception, args));
    }

    public static string FormatProcessingEventMessage(string messageTemplate, Exception? exception = null,
        params object[] args)
    {
        var eventMessage = messageTemplate;
        if (args.Length > 0)
        {
            try
            {
                eventMessage = Smart.Format(eventMessage, args);
            }
            catch
            {
                eventMessage = $"{messageTemplate} [{string.Join(", ", args.Select(x => x?.ToString() ?? string.Empty))}]";
            }
        }

        return exception is null
            ? eventMessage.ReplaceLineEndings(" ")
            : $"Error: {eventMessage}: {exception.Message}".ReplaceLineEndings(" ");
    }

    private async Task<(int, int)> ProcessSingleDirectoryAsync(
        FileSystemDirectoryInfo directoryInfoToProcess,
        ConcurrentBag<string> processingMessages,
        ConcurrentBag<Exception> processingErrors,
        ConcurrentBag<long?> artistsIdsSeen,
        ConcurrentBag<long?> albumsIdsSeen,
        ConcurrentBag<Guid> songsIdsSeen,
        DirectoryRunContext runContext,
        int? libraryId,
        CancellationToken cancellationToken)
    {
        using var operation = Operation.At(LogEventLevel.Debug)
            .Time("ProcessSingleDirectoryAsync for directory [{DirectoryName}]", directoryInfoToProcess.Name);

        var numberOfValidAlbumsProcessed = 0;
        var numberOfAlbumsProcessed = 0;
        var dirStartTicks = Stopwatch.GetTimestamp();

        LogAndRaiseEvent(LogEventLevel.Debug, "DirectoryInfoToProcess: [{0}]", null, directoryInfoToProcess);
        try
        {
            var unstableSourceFile = await FindUnstableSourceFileAsync(directoryInfoToProcess, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (unstableSourceFile.Nullify() != null)
            {
                var message = $"Deferred directory [{directoryInfoToProcess.Path}] because source file [{unstableSourceFile}] is still changing.";
                processingMessages.Add(message);
                LogAndRaiseEvent(LogEventLevel.Warning, message);

                return (numberOfAlbumsProcessed, numberOfValidAlbumsProcessed);
            }

            // Script evaluation hooks can skip a directory, but ingestion must not
            // physically delete releases before they have a chance to reach staging.
            var scriptResult = await EvaluateDirectoryScriptsAsync(
                directoryInfoToProcess,
                cancellationToken);

            if (!scriptResult.ShouldContinue)
            {
                return (numberOfAlbumsProcessed, numberOfValidAlbumsProcessed);
            }

            var dontDeleteExistingMelodeeFiles = _configuration.GetValue<bool>(SettingRegistry.ProcessingDontDeleteExistingMelodeeDataFiles);

            if (!dontDeleteExistingMelodeeFiles)
            {
                // Optimized batch delete operations
                var melodeeFiles = directoryInfoToProcess.MelodeeJsonFiles().Select(f => f.FullName).ToList();
                if (melodeeFiles.Count > 0)
                {
                    var deletedCount = await OptimizedFileOperations.DeleteFilesAsync(melodeeFiles, cancellationToken)
                        .ConfigureAwait(false);
                    LogAndRaiseEvent(LogEventLevel.Debug, "Deleted [{0}] existing Melodee files", null, deletedCount);
                }
            }

            if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
            {
                return new ValueTuple<int, int>(numberOfAlbumsProcessed, numberOfValidAlbumsProcessed);
            }

            // Use optimized file enumeration for memory efficiency
            var fileCount = 0;
            await foreach (var fileInfo in OptimizedFileOperations.EnumerateFilesAsync(
                               directoryInfoToProcess.Path, "*", SearchOption.TopDirectoryOnly, cancellationToken))
            {
                fileCount++;
                if (fileCount > 10000)
                {
                    break; // Prevent counting very large directories
                }
            }

            LogAndRaiseEvent(LogEventLevel.Debug, "\u251c Processing [{0}] Number of files to process [{1}]", null,
                directoryInfoToProcess.Name, fileCount);

            // Run all enabled IDirectoryPlugins to convert MetaData files into Album json files.
            // e.g. Build Album json file for M3U or NFO or SFV, etc.
            foreach (var plugin in _directoryPlugins.Where(x => x.IsEnabled).OrderBy(x => x.SortOrder))
            {
                if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
                {
                    break;
                }

                var pluginResult = await plugin.ProcessDirectoryAsync(directoryInfoToProcess, cancellationToken)
                    .ConfigureAwait(false);
                if (!pluginResult.IsSuccess && pluginResult.Type != OperationResponseType.NotFound)
                {
                    // ConcurrentBag doesn't have AddRange, so add items individually
                    if (pluginResult.Errors != null)
                    {
                        foreach (var error in pluginResult.Errors)
                        {
                            processingErrors.Add(error);
                        }
                    }

                    if (pluginResult.Messages != null)
                    {
                        foreach (var message in pluginResult.Messages)
                        {
                            processingMessages.Add(message);
                        }
                    }

                    if (plugin.StopProcessing)
                    {
                        Logger.Debug("Received stop processing from [{PluginName}] on Directory [{DirectoryName}]",
                            plugin.DisplayName, directoryInfoToProcess);
                        break;
                    }

                    continue;
                }

                if (plugin.StopProcessing)
                {
                    Logger.Debug("Received stop processing from [{PluginName}] on Directory [{DirectoryName}]",
                        plugin.DisplayName, directoryInfoToProcess);
                    break;
                }
            }

            var convertedSourceFilesByOriginalName = new Dictionary<string, FileSystemFileInfo>(StringComparer.OrdinalIgnoreCase);

            // Run Enabled Conversion scripts on each file in directory
            // e.g. Convert FLAC to MP3, Convert non JPEG files into JPEGs, etc.
            await foreach (var fileInfo in OptimizedFileOperations.EnumerateFilesAsync(
                               directoryInfoToProcess.Path, "*", SearchOption.TopDirectoryOnly, cancellationToken))
            {
                if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
                {
                    break;
                }

                var fsi = fileInfo.ToFileSystemInfo();
                var originalFileExtension = fileInfo.Extension;
                foreach (var plugin in _conversionPlugins.Where(x => x.IsEnabled).OrderBy(x => x.SortOrder))
                {
                    if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
                    {
                        break;
                    }

                    if (plugin.DoesHandleFile(directoryInfoToProcess, fsi))
                    {
                        await _conversionThrottle.WaitAsync(cancellationToken).ConfigureAwait(false);
                        var conversionStartTicks = Stopwatch.GetTimestamp();
                        OperationResult<FileSystemFileInfo> pluginResult;
                        try
                        {
                            pluginResult = await plugin
                                .ProcessFileAsync(directoryInfoToProcess, fsi, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            runContext.AddConversionTime((long)Stopwatch.GetElapsedTime(conversionStartTicks).TotalMilliseconds);
                            if (!_disposed)
                            {
                                try
                                {
                                    _conversionThrottle.Release();
                                }
                                catch (ObjectDisposedException)
                                {
                                    // Service shutdown disposed the semaphore while processing was being cancelled.
                                }
                            }
                        }

                        if (!pluginResult.IsSuccess)
                        {
                            // ConcurrentBag doesn't have AddRange, so add items individually
                            if (pluginResult.Errors != null)
                            {
                                foreach (var error in pluginResult.Errors)
                                {
                                    processingErrors.Add(error);
                                }
                            }

                            if (pluginResult.Messages != null)
                            {
                                foreach (var message in pluginResult.Messages)
                                {
                                    processingMessages.Add(message);
                                }
                            }
                        }
                        else
                        {
                            if (!string.Equals(pluginResult.Data.Extension(directoryInfoToProcess), originalFileExtension,
                                    StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(pluginResult.Data.Name, fsi.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                convertedSourceFilesByOriginalName[fsi.Name] = pluginResult.Data;
                            }
                        }
                    }

                    if (plugin.StopProcessing)
                    {
                        break;
                    }
                }
            }

            if (convertedSourceFilesByOriginalName.Count > 0)
            {
                LogAndRaiseEvent(LogEventLevel.Debug, "Mapped [{0}] converted source files for staging", null,
                    convertedSourceFilesByOriginalName.Count);
            }

            // If no albums were created by previous plugins, create from media files
            if (!directoryInfoToProcess.MelodeeJsonFiles().Any())
            {
                foreach (var plugin in _mediaAlbumCreatorPlugins.Where(x => x.IsEnabled).OrderBy(x => x.SortOrder))
                {
                    if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
                    {
                        break;
                    }

                    await plugin.ProcessDirectoryAsync(directoryInfoToProcess, cancellationToken)
                        .ConfigureAwait(false);
                    if (plugin.StopProcessing)
                    {
                        Logger.Debug("Received stop processing from [{PluginName}] on Directory [{DirectoryName}]",
                            plugin.DisplayName, directoryInfoToProcess);
                        break;
                    }
                }
            }

            var albumsForDirectory = new List<Album>();
            foreach (var melodeeJsonFile in directoryInfoToProcess.MelodeeJsonFiles())
            {
                if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
                {
                    break;
                }

                try
                {
                    var album = await Album
                        .DeserializeAndInitializeAlbumAsync(serializer, melodeeJsonFile.FullName, cancellationToken)
                        .ConfigureAwait(false);
                    if (album != null)
                    {
                        album.MelodeeDataFileName = melodeeJsonFile.FullName;
                        albumsForDirectory.Add(album);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Error loading Album json file [{0}]", melodeeJsonFile.FullName);
                }
            }

            // Track plugin time
            var pluginTimeMs = Stopwatch.GetElapsedTime(dirStartTicks).TotalMilliseconds;
            runContext.AddPluginTime((long)pluginTimeMs);

            LogAndRaiseEvent(LogEventLevel.Debug, "Loading images for album...");
            var processingResult = await ProcessAlbumsAsync(
                directoryInfoToProcess,
                albumsForDirectory,
                processingMessages,
                artistsIdsSeen,
                albumsIdsSeen,
                songsIdsSeen,
                runContext,
                convertedSourceFilesByOriginalName,
                cancellationToken);
            numberOfAlbumsProcessed += processingResult.Item1;
            numberOfValidAlbumsProcessed += processingResult.Item2;
        }
        catch (Exception e)
        {
            LogAndRaiseEvent(LogEventLevel.Error, "Processing Directory [{0}]", e,
                directoryInfoToProcess.ToString());
            processingErrors.Add(e);
        }
        return new ValueTuple<int, int>(numberOfAlbumsProcessed, numberOfValidAlbumsProcessed);
    }

    private async Task<(int, int)> ProcessAlbumsAsync(
        FileSystemDirectoryInfo directoryInfoToProcess,
        List<Album> albumsForDirectory,
        ConcurrentBag<string> processingMessages,
        ConcurrentBag<long?> artistsIdsSeen,
        ConcurrentBag<long?> albumsIdsSeen,
        ConcurrentBag<Guid> songsIdsSeen,
        DirectoryRunContext runContext,
        IReadOnlyDictionary<string, FileSystemFileInfo> convertedSourceFilesByOriginalName,
        CancellationToken cancellationToken)
    {
        var httpClient = httpClientFactory.CreateClient();
        var numberOfValidAlbumsProcessed = 0;
        var numberOfAlbumsProcessed = 0;
        var albumStartTicks = Stopwatch.GetTimestamp();

        foreach (var album in albumsForDirectory.Take(_maxAlbumProcessingCount))
        {
            if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
            {
                break;
            }

            try
            {
                album.Images = (await album.FindImages(imageProcessor, _albumNamesInDirectoryPlugin, _duplicateThreshold,
                    _imageConvertor, _imageValidator,
                    _configuration.GetValue<bool>(SettingRegistry.ProcessingDoDeleteOriginal),
                    cancellationToken).ConfigureAwait(false)).ToArray();

                album.Artist = new Artist(album.Artist.Name,
                    album.Artist.NameNormalized,
                    album.Artist.SortName,
                    (await album.FindArtistImages(imageProcessor, _imageConvertor,
                            _imageValidator,
                            _configuration.GetValue<bool>(SettingRegistry.ProcessingDoDeleteOriginal),
                            _configuration.GetValue<bool>(SettingRegistry.ProcessingDoDeleteOriginal),
                            cancellationToken)
                        .ConfigureAwait(false)).ToArray());

                if (album.IsSoundTrackTypeAlbum() && album.Songs != null)
                {
                    // If the album has different artists and is soundtrack then ensure artist is set to special VariousArtists
                    var songsGroupedByArtist = album.Songs.GroupBy(x => x.AlbumArtist()).ToArray();
                    if (songsGroupedByArtist.Length > 1)
                    {
                        album.Artist = new VariousArtist();
                        foreach (var song in album.Songs)
                        {
                            album.SetSongTagValue(song.Id, MetaTagIdentifier.AlbumArtist, album.Artist.Name);
                        }
                    }
                }
                else if (album.IsOriginalCastTypeAlbum() && album.Songs != null)
                {
                    // If the album has different artists and is Original Cast type then ensure artist is set to special Theater
                    // NOTE: Remember Original Cast Type albums with a single composer/artist is attributed to that composer/artist (e.g. Stephen Schwartz - Wicked)
                    var songsGroupedByArtist = album.Songs.GroupBy(x => x.AlbumArtist()).ToArray();
                    if (songsGroupedByArtist.Length > 1)
                    {
                        album.Artist = new Theater();
                        foreach (var song in album.Songs)
                        {
                            album.SetSongTagValue(song.Id, MetaTagIdentifier.AlbumArtist, album.Artist.Name);
                        }
                    }
                }

                var albumDirectorySystemInfo = new FileSystemDirectoryInfo
                {
                    Path = Path.Combine(_directoryStaging, album.ToDirectoryName()),
                    Name = album.ToDirectoryName()
                };
                albumDirectorySystemInfo.EnsureExists();

                // Collect all file operations for batch processing
                var filesToCopy = new List<(string source, string destination)>();
                var deleteOriginal = _configuration.GetValue<bool>(SettingRegistry.ProcessingDoDeleteOriginal);

                // Prepare image file operations
                var albumImagesToMove = album.Images?.Where(x => x.FileInfo?.OriginalName != null) ?? [];
                var artistImageToMove = album.Artist.Images?.Where(x => x.FileInfo?.OriginalName != null) ?? [];

                foreach (var image in albumImagesToMove.Concat(artistImageToMove).OrderBy(x => x.SortOrder))
                {
                    var oldImageFileName = fileSystemService.CombinePath((image.DirectoryInfo ?? album.Directory).FullName(),
                        image.FileInfo!.OriginalName!);
                    if (!fileSystemService.FileExists(oldImageFileName))
                    {
                        Logger.Warning("Unable to find image by original name [{OriginalName}]",
                            oldImageFileName);
                        continue;
                    }

                    var imageTypeForName = image.PictureIdentifier.ToString();
                    var newImageFileName = albumDirectorySystemInfo.GetNextFileNameForType(imageTypeForName).Item1;
                    if (!string.Equals(oldImageFileName, newImageFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        filesToCopy.Add((oldImageFileName, newImageFileName));
                        image.FileInfo!.Name = fileSystemService.GetFileName(newImageFileName);
                    }
                }

                // Prepare song file operations
                if (album.Songs != null)
                {
                    var songs = album.Songs.ToArray();
                    for (var i = 0; i < songs.Length; i++)
                    {
                        if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
                        {
                            break;
                        }

                        var song = songs[i];
                        if (!TryResolveSourceFileForStaging(
                                album.OriginalDirectory,
                                song.File,
                                convertedSourceFilesByOriginalName,
                                fileSystemService,
                                out var sourceSongFile,
                                out var oldSongFilename))
                        {
                            continue;
                        }

                        if (!string.Equals(song.File.Name, sourceSongFile.Name, StringComparison.OrdinalIgnoreCase) ||
                            song.File.Size != sourceSongFile.Size)
                        {
                            song = song with
                            {
                                File = new FileSystemFileInfo
                                {
                                    Name = sourceSongFile.Name,
                                    Size = sourceSongFile.Size,
                                    OriginalName = sourceSongFile.Name
                                }
                            };
                            songs[i] = song;
                        }

                        var newSongFileName = fileSystemService.CombinePath(albumDirectorySystemInfo.FullName(),
                            song.ToSongFileName(albumDirectorySystemInfo));
                        if (!string.Equals(oldSongFilename, newSongFileName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            filesToCopy.Add((oldSongFilename, newSongFileName));
                            song.File.Name = fileSystemService.GetFileName(newSongFileName);
                        }
                    }

                    album.Songs = songs;
                }

                // Perform batch file operations with streaming and timing
                if (filesToCopy.Count > 0)
                {
                    var copyStartTicks = Stopwatch.GetTimestamp();
                    using (Operation.At(LogEventLevel.Debug)
                               .Time("Copying [{FileCount}] files for album [{AlbumName}]", filesToCopy.Count, album.AlbumTitle() ?? string.Empty))
                    {
                        var copyResult = await OptimizedFileOperations.CopyFilesAsync(
                            filesToCopy,
                            deleteOriginal,
                            OptimizedFileOperations.DefaultBufferSize,
                            cancellationToken,
                            OptimizedFileOperations.DefaultMaxConcurrentCopies).ConfigureAwait(false);

                        LogAndRaiseEvent(LogEventLevel.Debug, "Copied [{0}] files for album [{1}]", null,
                            copyResult.FilesCopied, album.AlbumTitle() ?? string.Empty);
                    }
                    runContext.AddCopyTime((long)Stopwatch.GetElapsedTime(copyStartTicks).TotalMilliseconds);
                }

                if (album.Songs != null)
                {
                    if ((album.Tags ?? []).Any(x => x.WasModified) ||
                        album.Songs!.Any(x => (x.Tags ?? []).Any(y => y.WasModified)))
                    {
                        LogAndRaiseEvent(LogEventLevel.Debug, "Running plugins on songs with modified tags...");
                        var songsWithModifiedTags = album.Songs
                            .Where(x => x.Tags?.Any(t => t.WasModified) ?? false)
                            .ToArray();
                        var songsWithExistingFiles = songsWithModifiedTags
                            .Where(x => File.Exists(x.File.FullName(albumDirectorySystemInfo)))
                            .ToArray();
                        var missingSongFiles = songsWithModifiedTags
                            .Except(songsWithExistingFiles)
                            .Select(x => x.File.Name)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray();

                        if (missingSongFiles.Length > 0)
                        {
                            Logger.Warning(
                                "[{Name}] Skipping tag updates for [{Count}] missing staged files in album [{Album}] (examples: {Samples})",
                                nameof(DirectoryProcessorToStagingService),
                                missingSongFiles.Length,
                                album.AlbumTitle(),
                                string.Join(", ", missingSongFiles.Take(3)));
                        }

                        foreach (var songPlugin in _songPlugins)
                        {
                            foreach (var song in songsWithExistingFiles)
                            {
                                if (cancellationToken.IsCancellationRequested || _stopProcessingTriggered)
                                {
                                    break;
                                }

                                using (Operation.At(LogEventLevel.Debug)
                                           .Time(
                                               "ProcessDirectoryAsync :: Updating song [{Name}] with plugin [{DisplayName}]",
                                               song.File.Name, songPlugin.DisplayName))
                                {
                                    try
                                    {
                                        await songPlugin
                                            .UpdateSongAsync(albumDirectorySystemInfo, song, cancellationToken)
                                            .ConfigureAwait(false);
                                    }
                                    catch (Exception e)
                                    {
                                        Logger.Error(e,
                                            "Error updating song [{Name}] with plugin [{DisplayName}]",
                                            song.File.Name, songPlugin.DisplayName);
                                    }
                                }
                            }
                        }
                    }
                }

                album.Directory = albumDirectorySystemInfo;

                // Artist search with run-context caching to avoid duplicate API calls
                LogAndRaiseEvent(LogEventLevel.Debug, "Querying for artist...");
                var searchRequest = album.Artist.ToArtistQuery([
                    new KeyValue((album.AlbumYear() ?? 0).ToString(),
                        album.AlbumTitle().ToNormalizedString() ?? album.AlbumTitle())
                ]);
                var artistSearchResult = await artistSearchEngineService.DoSearchAsync(
                        searchRequest,
                        1,
                        runContext,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (artistSearchResult.IsSuccess)
                {
                    var artistFromSearch =
                        artistSearchResult.Data.OrderByDescending(x => x.Rank).FirstOrDefault();
                    if (artistFromSearch != null)
                    {
                        album.Artist = album.Artist with
                        {
                            AmgId = album.Artist.AmgId ?? artistFromSearch.AmgId,
                            ArtistDbId = album.Artist.ArtistDbId ?? artistFromSearch.Id,
                            DiscogsId = album.Artist.DiscogsId ?? artistFromSearch.DiscogsId,
                            ItunesId = album.Artist.ItunesId ?? artistFromSearch.ItunesId,
                            LastFmId = album.Artist.LastFmId ?? artistFromSearch.LastFmId,
                            MusicBrainzId = album.Artist.MusicBrainzId ?? artistFromSearch.MusicBrainzId,
                            Name = album.Artist.Name.Nullify() ?? artistFromSearch.Name,
                            NameNormalized = album.Artist.NameNormalized.Nullify() ??
                                             artistFromSearch.Name.ToNormalizedString() ??
                                             artistFromSearch.Name,
                            OriginalName =
                            artistFromSearch.Name != album.Artist.Name ? album.Artist.Name : null,
                            SearchEngineResultUniqueId = album.Artist.SearchEngineResultUniqueId is null or < 1
                                ? artistFromSearch.UniqueId
                                : album.Artist.SearchEngineResultUniqueId,
                            SortName = album.Artist.SortName.Nullify() ?? artistFromSearch.SortName,
                            SpotifyId = album.Artist.SpotifyId ?? artistFromSearch.SpotifyId,
                            WikiDataId = album.Artist.WikiDataId ?? artistFromSearch.WikiDataId
                        };

                        if (artistFromSearch.Releases?.FirstOrDefault() != null)
                        {
                            var searchResultRelease = artistFromSearch.Releases.FirstOrDefault(x =>
                                x.Year == album.AlbumYear() &&
                                x.NameNormalized == album.AlbumTitle().ToNormalizedString());
                            if (searchResultRelease != null)
                            {
                                album.AlbumDbId = album.AlbumDbId ?? searchResultRelease.Id;
                                album.AlbumType = album.AlbumType == AlbumType.NotSet
                                    ? searchResultRelease.AlbumType
                                    : album.AlbumType;

                                album.MusicBrainzId = searchResultRelease.MusicBrainzId;
                                album.SpotifyId = searchResultRelease.SpotifyId;

                                if (!album.HasValidAlbumYear(_configuration.Configuration))
                                {
                                    album.SetTagValue(MetaTagIdentifier.RecordingYear,
                                        searchResultRelease.Year.ToString());
                                }
                            }
                        }

                        album.Status = AlbumStatus.Ok;

                        LogAndRaiseEvent(LogEventLevel.Debug,
                            $"[{nameof(DirectoryProcessorToStagingService)}] Using artist from search engine query [{searchRequest}] result [{artistFromSearch}]");
                    }
                    else
                    {
                        LogAndRaiseEvent(LogEventLevel.Warning,
                            $"[{nameof(DirectoryProcessorToStagingService)}] No result from search engine for artist [{searchRequest}]");
                    }
                }

                LogAndRaiseEvent(LogEventLevel.Debug, "Testing for album images...");
                // Album image search with run-context caching
                if (album.Images?.Count() == 0)
                {
                    LogAndRaiseEvent(LogEventLevel.Debug, "Querying for album image...");
                    var albumImageSearchRequest = album.ToAlbumQuery();
                    var albumImageSearchResult = await albumImageSearchEngineService.DoSearchAsync(
                            albumImageSearchRequest,
                            1,
                            runContext,
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (albumImageSearchResult.IsSuccess)
                    {
                        var imageSearchResult = albumImageSearchResult.Data.OrderByDescending(x => x.Rank)
                            .FirstOrDefault();
                        if (imageSearchResult != null)
                        {
                            album.AmgId ??= imageSearchResult.AmgId;
                            album.DiscogsId ??= imageSearchResult.DiscogsId;
                            album.ItunesId ??= imageSearchResult.ItunesId;
                            album.LastFmId ??= imageSearchResult.LastFmId;
                            album.SpotifyId ??= imageSearchResult.SpotifyId;
                            album.WikiDataId ??= imageSearchResult.WikiDataId;

                            album.Artist.AmgId ??= imageSearchResult.ArtistAmgId;
                            album.Artist.DiscogsId ??= imageSearchResult.ArtistDiscogsId;
                            album.Artist.ItunesId ??= imageSearchResult.ArtistItunesId;
                            album.Artist.LastFmId ??= imageSearchResult.ArtistLastFmId;
                            album.Artist.SpotifyId ??= imageSearchResult.ArtistSpotifyId;
                            album.Artist.WikiDataId ??= imageSearchResult.ArtistWikiDataId;

                            if (!album.HasValidAlbumYear(_configuration.Configuration) &&
                                imageSearchResult.ReleaseDate != null)
                            {
                                album.SetTagValue(MetaTagIdentifier.RecordingYear,
                                    imageSearchResult.ReleaseDate.ToString());
                            }

                            var albumImageFromSearchFileName = fileSystemService.CombinePath(albumDirectorySystemInfo.FullName(),
                                albumDirectorySystemInfo
                                    .GetNextFileNameForType(Data.Models.Album.FrontImageType).Item1);
                            if (await httpClient.DownloadFileAsync(
                                    imageSearchResult.MediaUrl,
                                    albumImageFromSearchFileName,
                                    async (_, newFileInfo, _) =>
                                        (await _imageValidator.ValidateImage(newFileInfo,
                                            PictureIdentifier.Front, cancellationToken)).Data.IsValid,
                                    cancellationToken).ConfigureAwait(false))
                            {
                                var newImageInfo = new FileInfo(albumImageFromSearchFileName);
                                var imageInfo = await imageProcessor
                                    .IdentifyAsync(albumImageFromSearchFileName, cancellationToken)
                                    .ConfigureAwait(false);
                                if (imageInfo != null)
                                {
                                    album.Images = new List<ImageInfo>
                                    {
                                        new()
                                        {
                                            FileInfo = newImageInfo.ToFileSystemInfo(),
                                            PictureIdentifier = PictureIdentifier.Front,
                                            CrcHash = Crc32.Calculate(newImageInfo),
                                            Width = imageInfo.Width,
                                            Height = imageInfo.Height,
                                            SortOrder = 1,
                                            WasEmbeddedInSong = false
                                        }
                                    };
                                    LogAndRaiseEvent(LogEventLevel.Debug,
                                        $"[{nameof(DirectoryProcessorToStagingService)}] Downloaded album image [{imageSearchResult.MediaUrl}]");
                                }
                            }
                        }
                        else
                        {
                            LogAndRaiseEvent(LogEventLevel.Warning,
                                $"[{nameof(DirectoryProcessorToStagingService)}] No result from album search engine for album [{albumImageSearchRequest}]");
                        }
                    }
                }

                album.RenumberImages();

                var isMagicEnabled = _configuration.GetValue<bool>(SettingRegistry.MagicEnabled);

                LogAndRaiseEvent(LogEventLevel.Debug, "Validating album...");
                var validationResult = _albumValidator.ValidateAlbum(album);
                album.ValidationMessages = validationResult.Data.Messages ?? [];
                album.Status = validationResult.Data.AlbumStatus;
                album.StatusReasons = validationResult.Data.AlbumStatusReasons;

                album.Modified = DateTimeOffset.UtcNow;
                var serialized = serializer.Serialize(album);
                var jsonName = album.ToMelodeeJsonName(_configuration, true);
                if (jsonName.Nullify() != null)
                {
                    var jsonFilePath = fileSystemService.CombinePath(albumDirectorySystemInfo.FullName(), jsonName);
                    await fileSystemService.WriteAllBytesAsync(jsonFilePath,
                        System.Text.Encoding.UTF8.GetBytes(serialized ?? ""), cancellationToken).ConfigureAwait(false);

                    artistsIdsSeen.Add(album.Artist.ArtistUniqueId());
                    // ConcurrentBag doesn't have AddRange, so add items individually
                    if (album.Songs?.Where(x => x.SongArtistUniqueId() != null) != null)
                    {
                        foreach (var artistId in album.Songs.Where(x => x.SongArtistUniqueId() != null).Select(x => x.SongArtistUniqueId()))
                        {
                            artistsIdsSeen.Add(artistId);
                        }
                    }

                    albumsIdsSeen.Add(album.ArtistAlbumUniqueId());
                    // ConcurrentBag doesn't have AddRange, so add items individually
                    if (album.Songs != null)
                    {
                        foreach (var songId in album.Songs.Select(x => x.Id))
                        {
                            songsIdsSeen.Add(songId);
                        }
                    }

                    var albumCouldBeMagicfied = album;
                    if (isMagicEnabled)
                    {
                        await mediaEditService.DoMagic(album, cancellationToken).ConfigureAwait(false);
                        var jsonPath = fileSystemService.CombinePath(albumDirectorySystemInfo.FullName(), jsonName);
                        albumCouldBeMagicfied = await fileSystemService.DeserializeAlbumAsync(jsonPath, cancellationToken)
                            .ConfigureAwait(false) ?? album;
                    }

                    albumCouldBeMagicfied.Modified = DateTimeOffset.UtcNow;
                    await fileSystemService.WriteAllBytesAsync(
                            jsonFilePath,
                            System.Text.Encoding.UTF8.GetBytes(serializer.Serialize(albumCouldBeMagicfied) ?? string.Empty),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (albumCouldBeMagicfied.IsValid)
                    {
                        numberOfValidAlbumsProcessed++;
                        LogAndRaiseEvent(LogEventLevel.Debug,
                            $"[{nameof(DirectoryProcessorToStagingService)}] \ud83d\udc4d Found valid album [{albumCouldBeMagicfied}]");
                        if (numberOfValidAlbumsProcessed >= _maxAlbumProcessingCount)
                        {
                            LogAndRaiseEvent(LogEventLevel.Debug,
                                $"[{nameof(DirectoryProcessorToStagingService)}] \ud83d\uded1 Stopped processing directory [{directoryInfoToProcess}], processing.maximumProcessingCount is set to [{_maxAlbumProcessingCount}]");
                            _stopProcessingTriggered = true;
                            break;
                        }
                    }
                    else
                    {
                        LogAndRaiseEvent(LogEventLevel.Debug,
                            $"[{nameof(DirectoryProcessorToStagingService)}] \ud83d\ude3f Found invalid album [{albumCouldBeMagicfied}]");
                    }

                    if (_configuration.GetValue<bool>(SettingRegistry.ProcessingDoDeleteOriginal) &&
                        album.MelodeeDataFileName != null)
                    {
                        fileSystemService.DeleteFile(album.MelodeeDataFileName);
                    }

                    if (deleteOriginal)
                    {
                        var deletedSourceMetadataFiles = DeleteSourceSidecarMetadataFiles(album.OriginalDirectory, Logger);
                        if (deletedSourceMetadataFiles > 0)
                        {
                            LogAndRaiseEvent(LogEventLevel.Debug,
                                "Deleted [{0}] source metadata sidecar files for album [{1}]",
                                null,
                                deletedSourceMetadataFiles,
                                album.AlbumTitle() ?? string.Empty);
                        }
                    }
                }
                else
                {
                    processingMessages.Add($"Unable to determine JsonName for Album [{album}]");
                }

                numberOfAlbumsProcessed++;
            }
            catch (Exception e)
            {
                LogAndRaiseEvent(LogEventLevel.Error,
                    $"[{nameof(DirectoryProcessorToStagingService)}] Error processing album in directory [{directoryInfoToProcess}]",
                    e);
            }
        }

        runContext.AddAlbumProcessingTime((long)Stopwatch.GetElapsedTime(albumStartTicks).TotalMilliseconds);
        return new ValueTuple<int, int>(numberOfAlbumsProcessed, numberOfValidAlbumsProcessed);
    }

    /// <summary>
    ///     Whether residue should be deleted from media-free directories after ingest. On in move mode
    ///     (<see cref="SettingRegistry.ProcessingDoDeleteOriginal" />) and also in copy mode when
    ///     <see cref="SettingRegistry.ProcessingDeleteSourceResidueAfterIngest" /> is enabled. The latter defaults to
    ///     enabled when unconfigured so leftovers are cleaned even while preserving the original media.
    /// </summary>
    private bool ShouldDeleteSourceResidueAfterIngest()
    {
        if (_configuration.GetValue<bool>(SettingRegistry.ProcessingDoDeleteOriginal))
        {
            return true;
        }

        return _configuration.Configuration.TryGetValue(
                SettingRegistry.ProcessingDeleteSourceResidueAfterIngest, out var flagValue)
            ? SafeParser.ToBoolean(flagValue)
            : true;
    }

    public static bool IsSourceSidecarMetadataFile(FileInfo fileInfo)
    {
        if (fileInfo.Name.DoStringsMatch(Blackbeard.HandlesFileName))
        {
            return true;
        }

        var extension = fileInfo.Extension.TrimStart('.');
        return SourceSidecarMetadataExtensions.Contains(extension);
    }

    /// <summary>
    ///     Determines whether a file is source residue: leftover junk safe to remove once a release's media has been
    ///     processed. This covers sidecar metadata, images, text reports, provenance artifacts, known extensionless
    ///     release-note files, zero-byte (failed transcode) media, and any additionally configured extensions.
    /// </summary>
    /// <param name="fileInfo">The file to evaluate.</param>
    /// <param name="additionalResidueExtensions">
    ///     Optional extra extensions (without dots, case-insensitive) sourced from
    ///     <see cref="SettingRegistry.ProcessingFileExtensionsToDelete" />; files with these extensions are also residue.
    /// </param>
    public static bool IsSourceResidueFile(FileInfo fileInfo, HashSet<string>? additionalResidueExtensions = null)
    {
        if (IsSourceSidecarMetadataFile(fileInfo))
        {
            return true;
        }

        var extension = fileInfo.Extension.TrimStart('.');

        if (FileHelper.IsFileImageType(extension) ||
            SourceResidueTextExtensions.Contains(extension) ||
            SourceResidueProvenanceExtensions.Contains(extension) ||
            (additionalResidueExtensions is not null && additionalResidueExtensions.Contains(extension)) ||
            SourceResidueKnownFileNames.Contains(fileInfo.Name))
        {
            return true;
        }

        // A zero-byte file with a media extension is a failed transcode artifact, not usable media. Treat it as
        // residue so it does not keep a directory from being recognized as residue-only and cleaned up.
        return fileInfo.Length == 0 && FileHelper.IsFileMediaType(extension);
    }

    public static async Task<string?> FindUnstableSourceFileAsync(
        FileSystemDirectoryInfo directoryInfo,
        int delayMs = 250,
        CancellationToken cancellationToken = default)
    {
        var dirInfo = directoryInfo.ToDirectoryInfo();
        if (!dirInfo.Exists)
        {
            return null;
        }

        var firstSnapshot = SnapshotSourceFiles(dirInfo);
        if (firstSnapshot.Count == 0)
        {
            return null;
        }

        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);

        var secondSnapshot = SnapshotSourceFiles(dirInfo);
        foreach (var (path, first) in firstSnapshot)
        {
            if (!secondSnapshot.TryGetValue(path, out var second) ||
                first.Length != second.Length ||
                first.LastWriteTimeUtc != second.LastWriteTimeUtc)
            {
                return path;
            }
        }

        return secondSnapshot.Keys.FirstOrDefault(path => !firstSnapshot.ContainsKey(path));
    }

    public static bool TryResolveSourceFileForStaging(
        FileSystemDirectoryInfo sourceDirectory,
        FileSystemFileInfo file,
        IReadOnlyDictionary<string, FileSystemFileInfo> convertedSourceFilesByOriginalName,
        IFileSystemService fileSystemService,
        out FileSystemFileInfo sourceFile,
        out string sourcePath)
    {
        sourceFile = file;
        sourcePath = string.Empty;

        var sourceName = file.OriginalName.Nullify() ?? file.Name;
        if (convertedSourceFilesByOriginalName.TryGetValue(sourceName, out var convertedSourceFile) &&
            TryResolveSourceFile(sourceDirectory, convertedSourceFile.Name, convertedSourceFile, fileSystemService,
                out sourceFile, out sourcePath))
        {
            return true;
        }

        if (!string.Equals(sourceName, file.Name, StringComparison.OrdinalIgnoreCase) &&
            convertedSourceFilesByOriginalName.TryGetValue(file.Name, out convertedSourceFile) &&
            TryResolveSourceFile(sourceDirectory, convertedSourceFile.Name, convertedSourceFile, fileSystemService,
                out sourceFile, out sourcePath))
        {
            return true;
        }

        if (TryResolveSourceFile(sourceDirectory, sourceName, file, fileSystemService, out sourceFile, out sourcePath))
        {
            return true;
        }

        return !string.Equals(sourceName, file.Name, StringComparison.OrdinalIgnoreCase) &&
               TryResolveSourceFile(sourceDirectory, file.Name, file, fileSystemService, out sourceFile, out sourcePath);
    }

    private static bool TryResolveSourceFile(
        FileSystemDirectoryInfo sourceDirectory,
        string sourceName,
        FileSystemFileInfo file,
        IFileSystemService fileSystemService,
        out FileSystemFileInfo sourceFile,
        out string sourcePath)
    {
        sourceFile = file;
        sourcePath = fileSystemService.CombinePath(sourceDirectory.FullName(), sourceName);
        return fileSystemService.FileExists(sourcePath);
    }

    public static bool IsSourceMetadataOnlyDirectory(FileSystemDirectoryInfo directoryInfo)
    {
        var dirInfo = directoryInfo.ToDirectoryInfo();
        if (!dirInfo.Exists || dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly).Any())
        {
            return false;
        }

        var files = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly).ToArray();
        return files.Length > 0 && files.All(IsSourceSidecarMetadataFile);
    }

    public static bool IsSourceResidueOnlyDirectory(FileSystemDirectoryInfo directoryInfo,
        HashSet<string>? additionalResidueExtensions = null)
    {
        var dirInfo = directoryInfo.ToDirectoryInfo();
        if (!dirInfo.Exists || dirInfo.EnumerateDirectories("*", SearchOption.TopDirectoryOnly).Any())
        {
            return false;
        }

        var files = dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly).ToArray();
        // A directory is residue-only when it holds files, has no usable (non-zero-byte) media, and every file is residue.
        return files.Length > 0 &&
               !files.Any(file => FileHelper.IsFileMediaType(file.Extension) && file.Length > 0) &&
               files.All(file => IsSourceResidueFile(file, additionalResidueExtensions));
    }

    public static int DeleteSourceSidecarMetadataFiles(FileSystemDirectoryInfo directoryInfo, ILogger? logger = null)
    {
        var dirInfo = directoryInfo.ToDirectoryInfo();
        if (!dirInfo.Exists)
        {
            return 0;
        }

        var deletedCount = 0;
        foreach (var fileInfo in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                     .Where(IsSourceSidecarMetadataFile))
        {
            try
            {
                fileInfo.Delete();
                deletedCount++;
            }
            catch (Exception e)
            {
                logger?.Warning(e, "Unable to delete source metadata sidecar file [{FileName}]", fileInfo.FullName);
            }
        }

        return deletedCount;
    }

    public static int DeleteSourceResidueFiles(FileSystemDirectoryInfo directoryInfo, ILogger? logger = null,
        HashSet<string>? additionalResidueExtensions = null)
    {
        var dirInfo = directoryInfo.ToDirectoryInfo();
        if (!dirInfo.Exists)
        {
            return 0;
        }

        var deletedCount = 0;
        foreach (var fileInfo in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                     .Where(file => IsSourceResidueFile(file, additionalResidueExtensions)))
        {
            try
            {
                fileInfo.Delete();
                deletedCount++;
            }
            catch (Exception e)
            {
                logger?.Warning(e, "Unable to delete source residue file [{FileName}]", fileInfo.FullName);
            }
        }

        return deletedCount;
    }

    public static int DeleteSourceResidueOnlyDirectoryFiles(FileSystemDirectoryInfo rootDirectory, ILogger logger,
        HashSet<string>? additionalResidueExtensions = null)
    {
        var rootDirectoryInfo = rootDirectory.ToDirectoryInfo();
        if (!rootDirectoryInfo.Exists)
        {
            return 0;
        }

        var deletedCount = 0;
        foreach (var directoryInfo in rootDirectoryInfo.EnumerateDirectories("*", SearchOption.AllDirectories)
                     .OrderByDescending(x => x.FullName.Length)
                     .Select(x => x.ToDirectorySystemInfo()))
        {
            if (!IsSourceResidueOnlyDirectory(directoryInfo, additionalResidueExtensions))
            {
                continue;
            }

            deletedCount += DeleteSourceResidueFiles(directoryInfo, logger, additionalResidueExtensions);
            TryDeleteDirectoryIfEmpty(directoryInfo, logger);
        }

        if (deletedCount > 0)
        {
            logger.Information(
                "[{ServiceName}] Deleted [{Count}] source residue files from media-free directories",
                nameof(DirectoryProcessorToStagingService),
                deletedCount);
        }

        return deletedCount;
    }

    private record DirectoryScriptEvaluationResult(bool ShouldContinue, string? Message = null);

    private static IReadOnlyDictionary<string, SourceFileSnapshot> SnapshotSourceFiles(DirectoryInfo dirInfo)
    {
        var snapshot = new Dictionary<string, SourceFileSnapshot>(StringComparer.OrdinalIgnoreCase);
        foreach (var fileInfo in dirInfo.EnumerateFiles("*", SearchOption.TopDirectoryOnly))
        {
            try
            {
                snapshot[fileInfo.FullName] = new SourceFileSnapshot(fileInfo.Length, fileInfo.LastWriteTimeUtc);
            }
            catch (IOException)
            {
                snapshot[fileInfo.FullName] = new SourceFileSnapshot(-1, DateTime.MinValue);
            }
            catch (UnauthorizedAccessException)
            {
                snapshot[fileInfo.FullName] = new SourceFileSnapshot(-1, DateTime.MinValue);
            }
        }

        return snapshot;
    }

    private static void TryDeleteDirectoryIfEmpty(FileSystemDirectoryInfo directoryInfo, ILogger logger)
    {
        try
        {
            var dirInfo = directoryInfo.ToDirectoryInfo();
            if (dirInfo.Exists &&
                !dirInfo.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly).Any())
            {
                dirInfo.Delete();
            }
        }
        catch (Exception e)
        {
            logger.Warning(e, "Unable to delete empty source residue directory [{Directory}]", directoryInfo.Path);
        }
    }

    private readonly record struct SourceFileSnapshot(long Length, DateTime LastWriteTimeUtc);

    private async Task<DirectoryScriptEvaluationResult> EvaluateDirectoryScriptsAsync(
        FileSystemDirectoryInfo directory,
        CancellationToken cancellationToken)
    {
        try
        {
            var context = await directoryContextProvider.BuildContextAsync(directory, _songPlugins, cancellationToken);

            Logger.Debug("Script context for [{Directory}]: TotalFilesCount={TotalFilesCount}, TotalDurationMinutes={TotalDurationMinutes}, HasTrackNumberGaps={HasTrackNumberGaps}, MediaFilesCount={MediaFilesCount}",
                directory.Path, context.TotalFilesCount, context.TotalDurationMinutes, context.HasTrackNumberGaps, context.MediaFilesCount);

            var startResult = await scriptOrchestrationService.EvaluateScriptForEventAsync(
                ScriptEventNames.DirectoryProcessingStart,
                context,
                cancellationToken);

            if (!startResult.Result && !startResult.IsDefault)
            {
                var onDeny = startResult.OnDeny?.ToLowerInvariant() ?? "skip";
                if (onDeny == "delete")
                {
                    Logger.Warning(
                        "DirectoryProcessingStart requested delete for [{Directory}], using skip instead.",
                        directory.Path);
                    onDeny = "skip";
                }

                var handler = denyActionHandlerFactory.CreateHandler(onDeny);

                await handler.ExecuteAsync(directory.Path, cancellationToken);
                LogAndRaiseEvent(
                    LogEventLevel.Information,
                    "DirectoryProcessingStart script returned false; directory [{0}] action [{1}]",
                    null,
                    directory.Path,
                    onDeny);

                return new DirectoryScriptEvaluationResult(false, startResult.Message ?? $"Skipped by script with action: {onDeny}");
            }

            return new DirectoryScriptEvaluationResult(true);
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Error evaluating directory scripts for [{Path}], continuing with processing", directory.Path);
            return new DirectoryScriptEvaluationResult(true);
        }
    }
}
