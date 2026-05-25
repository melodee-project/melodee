using System.Diagnostics;
using System.Text.Json;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Jobs;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Models;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Services.SearchEngines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Rebus.Bus;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Performs a full library scan workflow: processes inbound files, revalidates staging albums,
///     moves approved albums to storage, and inserts them into the database.
/// </summary>
/// <remarks>
///     This command orchestrates the complete media ingestion pipeline:
///     <list type="number">
///         <item>LibraryInboundProcessJob - Process raw files from inbound → staging</item>
///         <item>StagingAlbumRevalidationJob - Re-check albums with invalid artists</item>
///         <item>StagingAutoMoveJob - Move approved albums from staging → storage</item>
///         <item>LibraryInsertJob - Insert albums from storage into database</item>
///     </list>
/// </remarks>
public class LibraryScanCommand : CommandBase<LibraryScanSettings>
{
    private sealed class ScanProgressState
    {
        private readonly object _lock = new();
        private int _current;
        private int _max;
        private string _message = "Starting...";

        public void SetMessage(string message)
        {
            lock (_lock)
            {
                _message = message;
            }
        }

        public void Update(int current, int max, string message)
        {
            lock (_lock)
            {
                _current = Math.Max(0, current);
                _max = Math.Max(0, max);
                _message = message;
            }
        }

        public (int Current, int Max, string Message) Snapshot()
        {
            lock (_lock)
            {
                return (_current, _max, _message);
            }
        }
    }

    private static string FormatNumber(long number)
    {
        return number.ToString("N0");
    }

    private static string FormatDurationMs(long milliseconds)
    {
        return TimeSpan.FromMilliseconds(milliseconds).ToString(@"hh\:mm\:ss");
    }

    private static string TrimProgressMessage(string message)
    {
        var singleLine = message
            .ReplaceLineEndings(" ")
            .Trim();

        return singleLine.Length > 90 ? singleLine[..87] + "..." : singleLine;
    }

    private static void ApplyProcessingEvent(ScanProgressState? progress, ProcessingEvent processingEvent)
    {
        progress?.Update(
            processingEvent.Current,
            processingEvent.Max,
            processingEvent.Message);
    }

    private static void UpdateProgressTask(ProgressTask stepTask, string stepName, ScanProgressState? progress)
    {
        if (progress is null)
        {
            return;
        }

        var (current, max, message) = progress.Snapshot();
        if (max > 0)
        {
            var maxValue = Math.Max(1, max);
            stepTask.IsIndeterminate = false;
            stepTask.MaxValue = maxValue;
            stepTask.Value = Math.Min(current, maxValue);
        }
        else
        {
            stepTask.IsIndeterminate = true;
        }

        stepTask.Description = $"[green]{Markup.Escape(stepName)}[/] [dim]{Markup.Escape(TrimProgressMessage(message))}[/]";
    }

    private static async Task<ScanStepResult?> ExecuteStepWithProgressAsync(
        string stepName,
        Func<ScanProgressState?, Task<ScanStepResult?>> execute,
        ProgressTask stepTask,
        CancellationToken cancellationToken)
    {
        var progress = new ScanProgressState();
        var executionTask = execute(progress);

        while (!executionTask.IsCompleted)
        {
            UpdateProgressTask(stepTask, stepName, progress);
            await Task.Delay(200, cancellationToken).ConfigureAwait(false);
        }

        UpdateProgressTask(stepTask, stepName, progress);

        return await executionTask.ConfigureAwait(false);
    }

    private static ScanStepResult AddStepResult(ScanStepResult summary, ScanStepResult result)
    {
        return summary with
        {
            NewArtistsCount = summary.NewArtistsCount + result.NewArtistsCount,
            NewAlbumsCount = summary.NewAlbumsCount + result.NewAlbumsCount,
            NewSongsCount = summary.NewSongsCount + result.NewSongsCount,
            InboundProcessingErrors = summary.InboundProcessingErrors + result.InboundProcessingErrors,
            AlbumsRevalidated = summary.AlbumsRevalidated + result.AlbumsRevalidated,
            AlbumsNowValid = summary.AlbumsNowValid + result.AlbumsNowValid,
            AlbumsSkippedRevalidation = summary.AlbumsSkippedRevalidation + result.AlbumsSkippedRevalidation,
            AlbumsDeferredRevalidation = summary.AlbumsDeferredRevalidation + result.AlbumsDeferredRevalidation,
            AlbumsReadyToMove = summary.AlbumsReadyToMove + result.AlbumsReadyToMove,
            AlbumsMoved = summary.AlbumsMoved + result.AlbumsMoved,
            AlbumsMergedWithExisting = summary.AlbumsMergedWithExisting + result.AlbumsMergedWithExisting,
            AlbumsSkippedByStatus = summary.AlbumsSkippedByStatus + result.AlbumsSkippedByStatus,
            AlbumsSkippedAsDuplicateDirectory = summary.AlbumsSkippedAsDuplicateDirectory + result.AlbumsSkippedAsDuplicateDirectory,
            AlbumsFailedToLoad = summary.AlbumsFailedToLoad + result.AlbumsFailedToLoad,
            ArtistsInserted = summary.ArtistsInserted + result.ArtistsInserted,
            AlbumsInserted = summary.AlbumsInserted + result.AlbumsInserted,
            SongsInserted = summary.SongsInserted + result.SongsInserted,
            AlbumsSkippedByReason = MergeSkippedReasonCounts(summary.AlbumsSkippedByReason, result.AlbumsSkippedByReason)
        };
    }

    private static IReadOnlyDictionary<string, int>? MergeSkippedReasonCounts(
        IReadOnlyDictionary<string, int>? existing,
        IReadOnlyDictionary<string, int>? incoming)
    {
        if ((existing?.Count ?? 0) == 0 && (incoming?.Count ?? 0) == 0)
        {
            return null;
        }

        var merged = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in existing ?? Enumerable.Empty<KeyValuePair<string, int>>())
        {
            merged[item.Key] = item.Value;
        }

        foreach (var item in incoming ?? Enumerable.Empty<KeyValuePair<string, int>>())
        {
            merged[item.Key] = merged.GetValueOrDefault(item.Key) + item.Value;
        }

        return merged;
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, LibraryScanSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var overallStartTime = Stopwatch.GetTimestamp();
        var isSilent = settings.Silent || settings.Json;

        if (!isSilent)
        {
            var configGrid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(4))
                .AddColumn();

            configGrid
                .AddRow("[b]Force Mode[/]", settings.ForceMode ? "[yellow]Yes[/]" : "[dim]No[/]")
                .AddRow("[b]Verbose[/]", settings.Verbose ? "[yellow]Yes[/]" : "[dim]No[/]");

            AnsiConsole.Write(
                new Panel(configGrid)
                    .Header("[yellow]Library Scan Configuration[/]")
                    .RoundedBorder()
                    .BorderColor(Color.Blue));

            AnsiConsole.WriteLine();
        }

        var logger = scope.ServiceProvider.GetRequiredService<ILogger>();
        var configFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();
        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var schedulerFactory = scope.ServiceProvider.GetRequiredService<ISchedulerFactory>();
        var directoryProcessor = scope.ServiceProvider.GetRequiredService<DirectoryProcessorToStagingService>();
        var albumDiscoveryService = scope.ServiceProvider.GetRequiredService<AlbumDiscoveryService>();
        var artistSearchEngineService = scope.ServiceProvider.GetRequiredService<ArtistSearchEngineService>();
        var revalidationStateStore = scope.ServiceProvider.GetRequiredService<IStagingAlbumRevalidationStateStore>();
        var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
        var fileSystemService = scope.ServiceProvider.GetRequiredService<IFileSystemService>();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        var artistService = scope.ServiceProvider.GetRequiredService<ArtistService>();
        var albumService = scope.ServiceProvider.GetRequiredService<AlbumService>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var summary = new ScanStepResult();
        var errors = new List<string>();
        var warnings = new List<string>();
        using var scanRunContext = new DirectoryRunContext();

        var steps = new (string Name, Func<ScanProgressState?, Task<ScanStepResult?>> Execute)[]
        {
            ("Processing inbound files", async progress =>
            {
                progress?.SetMessage("Discovering inbound directories...");
                var job = new LibraryInboundProcessJob(logger, configFactory, libraryService, directoryProcessor, schedulerFactory);
                var jobContext = new MelodeeJobExecutionContext(cancellationToken);
                jobContext.Put(MelodeeJobExecutionContext.ForceMode, settings.ForceMode);
                jobContext.Put(MelodeeJobExecutionContext.Verbose, settings.Verbose);
                jobContext.Put(MelodeeJobExecutionContext.DirectoryRunContext, scanRunContext);
                var totalDirectories = 0;
                var processedDirectories = 0;

                void OnProcessingStart(object? sender, int count)
                {
                    totalDirectories = count;
                    progress?.Update(0, totalDirectories, $"Found [{count:N0}] directories to process");
                }

                void OnDirectoryProcessed(object? sender, FileSystemDirectoryInfo directoryInfo)
                {
                    var current = Interlocked.Increment(ref processedDirectories);
                    progress?.Update(current, totalDirectories, $"Processed [{directoryInfo.Name}]");
                }

                void OnProcessingEvent(object? sender, string message)
                {
                    progress?.Update(processedDirectories, totalDirectories, message);
                }

                if (progress is not null)
                {
                    directoryProcessor.OnProcessingStart += OnProcessingStart;
                    directoryProcessor.OnDirectoryProcessed += OnDirectoryProcessed;
                    directoryProcessor.OnProcessingEvent += OnProcessingEvent;
                }

                try
                {
                    await job.Execute(jobContext);
                    return jobContext.Result as ScanStepResult;
                }
                finally
                {
                    if (progress is not null)
                    {
                        directoryProcessor.OnProcessingStart -= OnProcessingStart;
                        directoryProcessor.OnDirectoryProcessed -= OnDirectoryProcessed;
                        directoryProcessor.OnProcessingEvent -= OnProcessingEvent;
                    }
                }
            }),
            ("Revalidating staging albums", async progress =>
            {
                progress?.SetMessage("Discovering staged albums...");
                var job = new StagingAlbumRevalidationJob(logger, configFactory, libraryService, albumDiscoveryService, artistSearchEngineService, serializer, fileSystemService, revalidationStateStore);
                var jobContext = new MelodeeJobExecutionContext(cancellationToken);
                jobContext.Put(MelodeeJobExecutionContext.ForceMode, settings.ForceMode);
                jobContext.Put(MelodeeJobExecutionContext.DirectoryRunContext, scanRunContext);

                void OnProcessingEvent(object? sender, ProcessingEvent processingEvent)
                {
                    ApplyProcessingEvent(progress, processingEvent);
                }

                if (progress is not null)
                {
                    job.OnProcessingEvent += OnProcessingEvent;
                }

                try
                {
                    await job.Execute(jobContext);
                    return jobContext.Result as ScanStepResult;
                }
                finally
                {
                    if (progress is not null)
                    {
                        job.OnProcessingEvent -= OnProcessingEvent;
                    }
                }
            }),
            ("Moving approved albums to storage", async progress =>
            {
                progress?.SetMessage("Finding approved staging albums...");
                var job = new StagingAutoMoveJob(logger, configFactory, libraryService, schedulerFactory);
                var jobContext = new MelodeeJobExecutionContext(cancellationToken);

                void OnProcessingEvent(object? sender, ProcessingEvent processingEvent)
                {
                    ApplyProcessingEvent(progress, processingEvent);
                }

                if (progress is not null)
                {
                    libraryService.OnProcessingProgressEvent += OnProcessingEvent;
                }

                try
                {
                    await job.Execute(jobContext);
                    return jobContext.Result as ScanStepResult;
                }
                finally
                {
                    if (progress is not null)
                    {
                        libraryService.OnProcessingProgressEvent -= OnProcessingEvent;
                    }
                }
            }),
            ("Inserting albums into database", async progress =>
            {
                progress?.SetMessage("Discovering library metadata...");
                var job = new LibraryInsertJob(logger, configFactory, libraryService, serializer, dbContextFactory, artistService, albumService, albumDiscoveryService, directoryProcessor, bus);
                var jobContext = new MelodeeJobExecutionContext(cancellationToken);
                jobContext.Put(MelodeeJobExecutionContext.ForceMode, settings.ForceMode);
                jobContext.Put(MelodeeJobExecutionContext.Verbose, settings.Verbose);

                void OnProcessingEvent(object? sender, ProcessingEvent processingEvent)
                {
                    ApplyProcessingEvent(progress, processingEvent);
                }

                if (progress is not null)
                {
                    job.OnProcessingEvent += OnProcessingEvent;
                }

                try
                {
                    await job.Execute(jobContext);
                    return jobContext.Result as ScanStepResult;
                }
                finally
                {
                    if (progress is not null)
                    {
                        job.OnProcessingEvent -= OnProcessingEvent;
                    }
                }
            })
        };

        var stepResults = new Dictionary<string, (bool Success, bool HasWarnings, TimeSpan Elapsed)>();

        if (isSilent)
        {
            foreach (var (name, execute) in steps)
            {
                var stepStartTime = Stopwatch.GetTimestamp();
                try
                {
                    var result = await execute(null);
                    if (result is not null)
                    {
                        summary = AddStepResult(summary, result);
                        if (result.HasWarnings)
                        {
                            warnings.Add($"{name}: {result.NonFatalErrorCount:N0} non-fatal processing error(s); see log for details.");
                        }
                    }
                    stepResults[name] = (true, result?.HasWarnings ?? false, Stopwatch.GetElapsedTime(stepStartTime));
                }
                catch (Exception ex)
                {
                    stepResults[name] = (false, false, Stopwatch.GetElapsedTime(stepStartTime));
                    errors.Add($"{name}: {ex.Message}");
                    logger.Error(ex, "Error during {StepName}", name);
                }
            }
        }
        else
        {
            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                [
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn()
                ])
                .StartAsync(async ctx =>
                {
                    var overallTask = ctx.AddTask("[bold blue]Full Library Scan[/]", maxValue: steps.Length);

                    for (var i = 0; i < steps.Length; i++)
                    {
                        var (name, execute) = steps[i];
                        var stepTask = ctx.AddTask($"[green]{name}[/]", autoStart: false);
                        stepTask.IsIndeterminate = true;
                        stepTask.StartTask();

                        var stepStartTime = Stopwatch.GetTimestamp();

                        try
                        {
                            var result = await ExecuteStepWithProgressAsync(
                                name,
                                execute,
                                stepTask,
                                cancellationToken).ConfigureAwait(false);
                            if (result is not null)
                            {
                                summary = AddStepResult(summary, result);
                                if (result.HasWarnings)
                                {
                                    warnings.Add($"{name}: {result.NonFatalErrorCount:N0} non-fatal processing error(s); see log for details.");
                                }
                            }

                            var elapsed = Stopwatch.GetElapsedTime(stepStartTime);
                            stepTask.Description = result?.HasWarnings == true
                                ? $"[yellow]! {name}[/] [dim]({elapsed:mm\\:ss}, warnings)[/]"
                                : $"[green]✓ {name}[/] [dim]({elapsed:mm\\:ss})[/]";
                            stepTask.MaxValue = 100;
                            stepTask.Value = 100;
                            stepTask.IsIndeterminate = false;
                            stepResults[name] = (true, result?.HasWarnings ?? false, elapsed);
                        }
                        catch (Exception ex)
                        {
                            var elapsed = Stopwatch.GetElapsedTime(stepStartTime);
                            stepTask.Description = $"[red]✗ {name}[/] [dim]({elapsed:mm\\:ss})[/]";
                            stepTask.MaxValue = 100;
                            stepTask.Value = 100;
                            stepTask.IsIndeterminate = false;
                            stepResults[name] = (false, false, elapsed);
                            errors.Add($"{name}: {ex.Message}");
                            logger.Error(ex, "Error during {StepName}", name);
                        }

                        stepTask.StopTask();
                        overallTask.Increment(1);
                    }

                    overallTask.Description = "[bold green]✓ Full Library Scan Complete[/]";
                });
        }

        var totalElapsed = Stopwatch.GetElapsedTime(overallStartTime);
        var performanceSummary = scanRunContext.GetPerformanceSummary();
        if (performanceSummary.ArtistSearchReadErrors > 0)
        {
            warnings.Add($"Artist search: {performanceSummary.ArtistSearchReadErrors:N0} read error(s); see log for details.");
        }
        if (performanceSummary.ArtistSearchReadCorruptions > 0)
        {
            warnings.Add($"Artist search DecentDB corruption detected {performanceSummary.ArtistSearchReadCorruptions:N0} time(s); rebuild or replace the artist search database.");
        }

        if (settings.Json)
        {
            var jsonOutput = new
            {
                success = errors.Count == 0,
                durationSeconds = totalElapsed.TotalSeconds,
                duration = totalElapsed.ToString(@"hh\:mm\:ss"),
                steps = stepResults.Select(s => new
                {
                    name = s.Key,
                    success = s.Value.Success,
                    warnings = s.Value.HasWarnings,
                    durationSeconds = s.Value.Elapsed.TotalSeconds
                }),
                summary = new
                {
                    inboundProcessing = new
                    {
                        newArtists = summary.NewArtistsCount,
                        newAlbums = summary.NewAlbumsCount,
                        newSongs = summary.NewSongsCount,
                        processingErrors = summary.InboundProcessingErrors
                    },
                    stagingRevalidation = new
                    {
                        albumsRevalidated = summary.AlbumsRevalidated,
                        albumsNowValid = summary.AlbumsNowValid,
                        albumsSkippedRevalidation = summary.AlbumsSkippedRevalidation,
                        albumsDeferredRevalidation = summary.AlbumsDeferredRevalidation
                    },
                    storageTransfer = new
                    {
                        albumsReadyToMove = summary.AlbumsReadyToMove,
                        albumsMoved = summary.AlbumsMoved,
                        albumsMergedWithExisting = summary.AlbumsMergedWithExisting,
                        albumsHandled = summary.AlbumsHandledByStorageTransfer,
                        albumsSkippedByStatus = summary.AlbumsSkippedByStatus,
                        albumsSkippedAsDuplicateDirectory = summary.AlbumsSkippedAsDuplicateDirectory,
                        albumsFailedToLoad = summary.AlbumsFailedToLoad,
                        albumsSkippedByReason = summary.AlbumsSkippedByReason
                    },
                    databaseInsert = new
                    {
                        artistsInserted = summary.ArtistsInserted,
                        albumsInserted = summary.AlbumsInserted,
                        songsInserted = summary.SongsInserted
                    }
                },
                warnings,
                performance = new
                {
                    runtimeMs = performanceSummary.RuntimeMs,
                    directoriesProcessed = performanceSummary.DirectoriesProcessed,
                    pluginTimeMs = performanceSummary.PluginTimeMs,
                    albumProcessingTimeMs = performanceSummary.AlbumProcessingTimeMs,
                    enrichmentTimeMs = performanceSummary.EnrichmentTimeMs,
                    conversionTimeMs = performanceSummary.ConversionTimeMs,
                    conversionFilesProcessed = performanceSummary.ConversionFilesProcessed,
                    copyTimeMs = performanceSummary.CopyTimeMs,
                    artistSearchPersistenceRetries = performanceSummary.ArtistSearchPersistenceRetries,
                    artistSearchPersistenceConflicts = performanceSummary.ArtistSearchPersistenceConflicts,
                    artistSearchPersistenceCorruptions = performanceSummary.ArtistSearchPersistenceCorruptions,
                    artistSearchReadErrors = performanceSummary.ArtistSearchReadErrors,
                    artistSearchReadCorruptions = performanceSummary.ArtistSearchReadCorruptions,
                    albumsSkippedRevalidation = performanceSummary.AlbumsSkippedRevalidation,
                    albumsDeferredRevalidation = performanceSummary.AlbumsDeferredRevalidation,
                    artistSearchCache = new
                    {
                        entries = performanceSummary.ArtistSearchCache.TotalEntries,
                        hits = performanceSummary.ArtistSearchCache.Hits,
                        misses = performanceSummary.ArtistSearchCache.Misses,
                        coalesced = performanceSummary.ArtistSearchCache.CoalescedRequests,
                        hitRate = performanceSummary.ArtistSearchCache.HitRate
                    },
                    forcedArtistSearchCache = new
                    {
                        entries = performanceSummary.ForcedArtistSearchCache.TotalEntries,
                        hits = performanceSummary.ForcedArtistSearchCache.Hits,
                        misses = performanceSummary.ForcedArtistSearchCache.Misses,
                        coalesced = performanceSummary.ForcedArtistSearchCache.CoalescedRequests,
                        hitRate = performanceSummary.ForcedArtistSearchCache.HitRate
                    }
                },
                errors = errors
            };
            Console.WriteLine(JsonSerializer.Serialize(jsonOutput, new JsonSerializerOptions { WriteIndented = true }));
            return errors.Count > 0 ? 1 : 0;
        }

        if (isSilent)
        {
            return errors.Count > 0 ? 1 : 0;
        }

        AnsiConsole.WriteLine();

        var rule = new Rule($"[green]Library scan completed in {totalElapsed:hh\\:mm\\:ss}[/]")
        {
            Justification = Justify.Left
        };
        AnsiConsole.Write(rule);

        AnsiConsole.WriteLine();

        var hasActivity = summary.NewArtistsCount > 0 || summary.NewAlbumsCount > 0 || summary.NewSongsCount > 0 ||
                          summary.InboundProcessingErrors > 0 ||
                          summary.AlbumsRevalidated > 0 || summary.AlbumsSkippedRevalidation > 0 ||
                          summary.AlbumsDeferredRevalidation > 0 || summary.AlbumsReadyToMove > 0 ||
                          summary.AlbumsHandledByStorageTransfer > 0 || summary.AlbumsSkippedByStatus > 0 ||
                          summary.AlbumsSkippedAsDuplicateDirectory > 0 || summary.AlbumsFailedToLoad > 0 ||
                          summary.ArtistsInserted > 0 || summary.AlbumsInserted > 0 || summary.SongsInserted > 0;

        if (hasActivity)
        {
            var summaryTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .Title("[yellow]Scan Summary[/]");

            summaryTable.AddColumn("Category");
            summaryTable.AddColumn(new TableColumn("Count").RightAligned());

            if (summary.NewArtistsCount > 0 || summary.NewAlbumsCount > 0 ||
                summary.NewSongsCount > 0 || summary.InboundProcessingErrors > 0)
            {
                summaryTable.AddRow("[bold]Inbound Processing[/]", "");
                if (summary.NewArtistsCount > 0)
                    summaryTable.AddRow("  New artists discovered", FormatNumber(summary.NewArtistsCount));
                if (summary.NewAlbumsCount > 0)
                    summaryTable.AddRow("  New albums discovered", FormatNumber(summary.NewAlbumsCount));
                if (summary.NewSongsCount > 0)
                    summaryTable.AddRow("  New songs discovered", FormatNumber(summary.NewSongsCount));
                if (summary.InboundProcessingErrors > 0)
                    summaryTable.AddRow("  Processing errors", $"[yellow]{FormatNumber(summary.InboundProcessingErrors)}[/]");
            }

            if (summary.AlbumsRevalidated > 0 || summary.AlbumsNowValid > 0 ||
                summary.AlbumsSkippedRevalidation > 0 || summary.AlbumsDeferredRevalidation > 0)
            {
                summaryTable.AddRow("[bold]Staging Revalidation[/]", "");
                if (summary.AlbumsRevalidated > 0)
                    summaryTable.AddRow("  Albums revalidated", FormatNumber(summary.AlbumsRevalidated));
                if (summary.AlbumsNowValid > 0)
                    summaryTable.AddRow("  Albums now valid", $"[green]{FormatNumber(summary.AlbumsNowValid)}[/]");
                if (summary.AlbumsSkippedRevalidation > 0)
                    summaryTable.AddRow("  Albums skipped", $"[yellow]{FormatNumber(summary.AlbumsSkippedRevalidation)}[/]");
                if (summary.AlbumsDeferredRevalidation > 0)
                    summaryTable.AddRow("  Albums deferred", $"[yellow]{FormatNumber(summary.AlbumsDeferredRevalidation)}[/]");
            }

            if (summary.AlbumsReadyToMove > 0 ||
                summary.AlbumsHandledByStorageTransfer > 0 ||
                summary.AlbumsSkippedByStatus > 0 ||
                summary.AlbumsSkippedAsDuplicateDirectory > 0 ||
                summary.AlbumsFailedToLoad > 0)
            {
                summaryTable.AddRow("[bold]Storage Transfer[/]", "");
                if (summary.AlbumsReadyToMove > 0)
                    summaryTable.AddRow("  Albums ready to move", FormatNumber(summary.AlbumsReadyToMove));
                if (summary.AlbumsMoved > 0)
                    summaryTable.AddRow("  New albums moved to storage", FormatNumber(summary.AlbumsMoved));
                if (summary.AlbumsMergedWithExisting > 0)
                    summaryTable.AddRow("  Albums merged with existing storage", FormatNumber(summary.AlbumsMergedWithExisting));
                if (summary.AlbumsHandledByStorageTransfer > 0)
                    summaryTable.AddRow("  Albums handled by storage transfer", FormatNumber(summary.AlbumsHandledByStorageTransfer));
                if (summary.AlbumsSkippedByStatus > 0)
                    summaryTable.AddRow("  Albums left in staging", $"[yellow]{FormatNumber(summary.AlbumsSkippedByStatus)}[/]");
                if (summary.AlbumsSkippedAsDuplicateDirectory > 0)
                    summaryTable.AddRow("  Duplicate-prefixed staging dirs skipped", FormatNumber(summary.AlbumsSkippedAsDuplicateDirectory));
                if (summary.AlbumsFailedToLoad > 0)
                    summaryTable.AddRow("  Albums failed to load", $"[red]{FormatNumber(summary.AlbumsFailedToLoad)}[/]");
                foreach (var skippedReason in summary.AlbumsSkippedByReason?.OrderBy(x => x.Key) ?? Enumerable.Empty<KeyValuePair<string, int>>())
                {
                    summaryTable.AddRow($"    {Markup.Escape(skippedReason.Key)}", $"[yellow]{FormatNumber(skippedReason.Value)}[/]");
                }
            }

            if (summary.ArtistsInserted > 0 || summary.AlbumsInserted > 0 || summary.SongsInserted > 0)
            {
                summaryTable.AddRow("[bold]Database Insert[/]", "");
                if (summary.ArtistsInserted > 0)
                    summaryTable.AddRow("  Artists inserted", FormatNumber(summary.ArtistsInserted));
                if (summary.AlbumsInserted > 0)
                    summaryTable.AddRow("  Albums inserted", FormatNumber(summary.AlbumsInserted));
                if (summary.SongsInserted > 0)
                    summaryTable.AddRow("  Songs inserted", FormatNumber(summary.SongsInserted));
            }

            AnsiConsole.Write(summaryTable);
        }
        else
        {
            AnsiConsole.MarkupLine("[dim]No new content processed during this scan.[/]");
        }

        var hasPerformanceActivity = performanceSummary.ArtistSearchCache.Hits > 0 ||
                                     performanceSummary.ArtistSearchCache.Misses > 0 ||
                                     performanceSummary.ArtistSearchCache.CoalescedRequests > 0 ||
                                     performanceSummary.ForcedArtistSearchCache.Hits > 0 ||
                                     performanceSummary.ForcedArtistSearchCache.Misses > 0 ||
                                     performanceSummary.ForcedArtistSearchCache.CoalescedRequests > 0 ||
                                     performanceSummary.ConversionFilesProcessed > 0 ||
                                     performanceSummary.ArtistSearchPersistenceRetries > 0 ||
                                     performanceSummary.ArtistSearchPersistenceConflicts > 0 ||
                                     performanceSummary.ArtistSearchPersistenceCorruptions > 0 ||
                                     performanceSummary.ArtistSearchReadErrors > 0 ||
                                     performanceSummary.ArtistSearchReadCorruptions > 0 ||
                                     performanceSummary.AlbumsSkippedRevalidation > 0 ||
                                     performanceSummary.AlbumsDeferredRevalidation > 0;
        if (hasPerformanceActivity)
        {
            AnsiConsole.WriteLine();
            var performanceTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Grey)
                .Title("[yellow]Scan Performance[/]");

            performanceTable.AddColumn("Metric");
            performanceTable.AddColumn(new TableColumn("Value").RightAligned());

            performanceTable.AddRow("Directories processed", FormatNumber(performanceSummary.DirectoriesProcessed));
            performanceTable.AddRow("Artist cache hits", FormatNumber(performanceSummary.ArtistSearchCache.Hits));
            performanceTable.AddRow("Artist cache misses", FormatNumber(performanceSummary.ArtistSearchCache.Misses));
            performanceTable.AddRow("Artist cache coalesced", FormatNumber(performanceSummary.ArtistSearchCache.CoalescedRequests));
            performanceTable.AddRow("Forced artist cache hits", FormatNumber(performanceSummary.ForcedArtistSearchCache.Hits));
            performanceTable.AddRow("Forced artist cache misses", FormatNumber(performanceSummary.ForcedArtistSearchCache.Misses));
            performanceTable.AddRow("Forced artist cache coalesced", FormatNumber(performanceSummary.ForcedArtistSearchCache.CoalescedRequests));
            performanceTable.AddRow("Artist lookup time", FormatDurationMs(performanceSummary.EnrichmentTimeMs));
            performanceTable.AddRow("Conversion files", FormatNumber(performanceSummary.ConversionFilesProcessed));
            performanceTable.AddRow("Conversion time", FormatDurationMs(performanceSummary.ConversionTimeMs));
            performanceTable.AddRow("Copy time", FormatDurationMs(performanceSummary.CopyTimeMs));
            if (performanceSummary.ArtistSearchPersistenceConflicts > 0 ||
                performanceSummary.ArtistSearchPersistenceRetries > 0 ||
                performanceSummary.ArtistSearchPersistenceCorruptions > 0)
            {
                performanceTable.AddRow("DecentDB conflicts", FormatNumber(performanceSummary.ArtistSearchPersistenceConflicts));
                performanceTable.AddRow("DecentDB retries", FormatNumber(performanceSummary.ArtistSearchPersistenceRetries));
                performanceTable.AddRow("DecentDB corruptions", FormatNumber(performanceSummary.ArtistSearchPersistenceCorruptions));
            }
            if (performanceSummary.ArtistSearchReadErrors > 0 ||
                performanceSummary.ArtistSearchReadCorruptions > 0)
            {
                performanceTable.AddRow("Artist search read errors", FormatNumber(performanceSummary.ArtistSearchReadErrors));
                performanceTable.AddRow("Artist search corruptions", FormatNumber(performanceSummary.ArtistSearchReadCorruptions));
            }
            if (performanceSummary.AlbumsSkippedRevalidation > 0)
            {
                performanceTable.AddRow("Revalidation skipped", FormatNumber(performanceSummary.AlbumsSkippedRevalidation));
            }
            if (performanceSummary.AlbumsDeferredRevalidation > 0)
            {
                performanceTable.AddRow("Revalidation deferred", FormatNumber(performanceSummary.AlbumsDeferredRevalidation));
            }

            AnsiConsole.Write(performanceTable);
        }

        if (warnings.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]Warnings encountered: {warnings.Count}[/]");
            foreach (var warning in warnings)
            {
                AnsiConsole.MarkupLine($"  [yellow]• {Markup.Escape(warning)}[/]");
            }
        }

        if (errors.Count > 0)
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red]Errors encountered: {errors.Count}[/]");
            foreach (var error in errors)
            {
                AnsiConsole.MarkupLine($"  [red]• {Markup.Escape(error)}[/]");
            }
        }

        return errors.Count > 0 ? 1 : 0;
    }
}
