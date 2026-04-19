using System.Diagnostics;
using System.Text.Json;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Jobs;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
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
    private static string FormatNumber(int number)
    {
        return number.ToString("N0");
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
        var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
        var fileSystemService = scope.ServiceProvider.GetRequiredService<IFileSystemService>();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        var artistService = scope.ServiceProvider.GetRequiredService<ArtistService>();
        var albumService = scope.ServiceProvider.GetRequiredService<AlbumService>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var summary = new ScanStepResult();
        var errors = new List<string>();

        var steps = new (string Name, Func<Task<ScanStepResult?>> Execute)[]
        {
            ("Processing inbound files", async () =>
            {
                var job = new LibraryInboundProcessJob(logger, configFactory, libraryService, directoryProcessor, schedulerFactory);
                var jobContext = new MelodeeJobExecutionContext(cancellationToken);
                jobContext.Put(MelodeeJobExecutionContext.ForceMode, settings.ForceMode);
                jobContext.Put(MelodeeJobExecutionContext.Verbose, settings.Verbose);
                await job.Execute(jobContext);
                return jobContext.Result as ScanStepResult;
            }),
            ("Revalidating staging albums", async () =>
            {
                var job = new StagingAlbumRevalidationJob(logger, configFactory, libraryService, albumDiscoveryService, artistSearchEngineService, serializer, fileSystemService);
                var jobContext = new MelodeeJobExecutionContext(cancellationToken);
                jobContext.Put(MelodeeJobExecutionContext.ForceMode, settings.ForceMode);
                await job.Execute(jobContext);
                return jobContext.Result as ScanStepResult;
            }),
            ("Moving approved albums to storage", async () =>
            {
                var job = new StagingAutoMoveJob(logger, configFactory, libraryService, schedulerFactory);
                var jobContext = new MelodeeJobExecutionContext(cancellationToken);
                await job.Execute(jobContext);
                return jobContext.Result as ScanStepResult;
            }),
            ("Inserting albums into database", async () =>
            {
                var job = new LibraryInsertJob(logger, configFactory, libraryService, serializer, dbContextFactory, artistService, albumService, albumDiscoveryService, directoryProcessor, bus);
                var jobContext = new MelodeeJobExecutionContext(cancellationToken);
                jobContext.Put(MelodeeJobExecutionContext.ForceMode, settings.ForceMode);
                jobContext.Put(MelodeeJobExecutionContext.Verbose, settings.Verbose);
                await job.Execute(jobContext);
                return jobContext.Result as ScanStepResult;
            })
        };

        var stepResults = new Dictionary<string, (bool Success, TimeSpan Elapsed)>();

        if (isSilent)
        {
            foreach (var (name, execute) in steps)
            {
                var stepStartTime = Stopwatch.GetTimestamp();
                try
                {
                    var result = await execute();
                    if (result is not null)
                    {
                        summary = summary with
                        {
                            NewArtistsCount = summary.NewArtistsCount + result.NewArtistsCount,
                            NewAlbumsCount = summary.NewAlbumsCount + result.NewAlbumsCount,
                            NewSongsCount = summary.NewSongsCount + result.NewSongsCount,
                            AlbumsRevalidated = summary.AlbumsRevalidated + result.AlbumsRevalidated,
                            AlbumsNowValid = summary.AlbumsNowValid + result.AlbumsNowValid,
                            AlbumsMoved = summary.AlbumsMoved + result.AlbumsMoved,
                            ArtistsInserted = summary.ArtistsInserted + result.ArtistsInserted,
                            AlbumsInserted = summary.AlbumsInserted + result.AlbumsInserted,
                            SongsInserted = summary.SongsInserted + result.SongsInserted
                        };
                    }
                    stepResults[name] = (true, Stopwatch.GetElapsedTime(stepStartTime));
                }
                catch (Exception ex)
                {
                    stepResults[name] = (false, Stopwatch.GetElapsedTime(stepStartTime));
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
                            var result = await execute();
                            if (result is not null)
                            {
                                summary = summary with
                                {
                                    NewArtistsCount = summary.NewArtistsCount + result.NewArtistsCount,
                                    NewAlbumsCount = summary.NewAlbumsCount + result.NewAlbumsCount,
                                    NewSongsCount = summary.NewSongsCount + result.NewSongsCount,
                                    AlbumsRevalidated = summary.AlbumsRevalidated + result.AlbumsRevalidated,
                                    AlbumsNowValid = summary.AlbumsNowValid + result.AlbumsNowValid,
                                    AlbumsMoved = summary.AlbumsMoved + result.AlbumsMoved,
                                    ArtistsInserted = summary.ArtistsInserted + result.ArtistsInserted,
                                    AlbumsInserted = summary.AlbumsInserted + result.AlbumsInserted,
                                    SongsInserted = summary.SongsInserted + result.SongsInserted
                                };
                            }

                            var elapsed = Stopwatch.GetElapsedTime(stepStartTime);
                            stepTask.Description = $"[green]✓ {name}[/] [dim]({elapsed:mm\\:ss})[/]";
                            stepTask.Value = 100;
                            stepTask.MaxValue = 100;
                            stepTask.IsIndeterminate = false;
                            stepResults[name] = (true, elapsed);
                        }
                        catch (Exception ex)
                        {
                            var elapsed = Stopwatch.GetElapsedTime(stepStartTime);
                            stepTask.Description = $"[red]✗ {name}[/] [dim]({elapsed:mm\\:ss})[/]";
                            stepTask.Value = 100;
                            stepTask.MaxValue = 100;
                            stepTask.IsIndeterminate = false;
                            stepResults[name] = (false, elapsed);
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
                    durationSeconds = s.Value.Elapsed.TotalSeconds
                }),
                summary = new
                {
                    inboundProcessing = new
                    {
                        newArtists = summary.NewArtistsCount,
                        newAlbums = summary.NewAlbumsCount,
                        newSongs = summary.NewSongsCount
                    },
                    stagingRevalidation = new
                    {
                        albumsRevalidated = summary.AlbumsRevalidated,
                        albumsNowValid = summary.AlbumsNowValid
                    },
                    storageTransfer = new
                    {
                        albumsMoved = summary.AlbumsMoved
                    },
                    databaseInsert = new
                    {
                        artistsInserted = summary.ArtistsInserted,
                        albumsInserted = summary.AlbumsInserted,
                        songsInserted = summary.SongsInserted
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
                          summary.AlbumsRevalidated > 0 || summary.AlbumsMoved > 0 ||
                          summary.ArtistsInserted > 0 || summary.AlbumsInserted > 0 || summary.SongsInserted > 0;

        if (hasActivity)
        {
            var summaryTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Blue)
                .Title("[yellow]Scan Summary[/]");

            summaryTable.AddColumn("Category");
            summaryTable.AddColumn(new TableColumn("Count").RightAligned());

            if (summary.NewArtistsCount > 0 || summary.NewAlbumsCount > 0 || summary.NewSongsCount > 0)
            {
                summaryTable.AddRow("[bold]Inbound Processing[/]", "");
                if (summary.NewArtistsCount > 0)
                    summaryTable.AddRow("  New artists discovered", FormatNumber(summary.NewArtistsCount));
                if (summary.NewAlbumsCount > 0)
                    summaryTable.AddRow("  New albums discovered", FormatNumber(summary.NewAlbumsCount));
                if (summary.NewSongsCount > 0)
                    summaryTable.AddRow("  New songs discovered", FormatNumber(summary.NewSongsCount));
            }

            if (summary.AlbumsRevalidated > 0 || summary.AlbumsNowValid > 0)
            {
                summaryTable.AddRow("[bold]Staging Revalidation[/]", "");
                if (summary.AlbumsRevalidated > 0)
                    summaryTable.AddRow("  Albums revalidated", FormatNumber(summary.AlbumsRevalidated));
                if (summary.AlbumsNowValid > 0)
                    summaryTable.AddRow("  Albums now valid", $"[green]{FormatNumber(summary.AlbumsNowValid)}[/]");
            }

            if (summary.AlbumsMoved > 0)
            {
                summaryTable.AddRow("[bold]Storage Transfer[/]", "");
                summaryTable.AddRow("  Albums moved to storage", FormatNumber(summary.AlbumsMoved));
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
