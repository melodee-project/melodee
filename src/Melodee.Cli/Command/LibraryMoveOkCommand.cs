using System.Diagnostics;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Enums;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Models;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;

namespace Melodee.Cli.Command;

public class LibraryMoveOkCommand : CommandBase<LibraryMoveOkSettings>
{
    private static string FormatBytes(long bytes)
    {
        const long megabyte = 1024 * 1024;
        return (bytes / (double)megabyte).ToString("F2");
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, LibraryMoveOkSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

            Common.Models.OperationResult<bool>? result = null;
            ProcessingEventStatistics? finalStatistics = null;
            var startTime = Stopwatch.GetTimestamp();

            // Display initial configuration
            var configGrid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(4))
                .AddColumn();

            if (settings.IsPathBasedMode)
            {
                configGrid
                    .AddRow("[b]Mode[/]", "[blue]Path-based[/] (bypassing database)")
                    .AddRow("[b]From[/]", $"{settings.FromPath!.EscapeMarkup()}")
                    .AddRow("[b]To[/]", $"{settings.ToPath!.EscapeMarkup()}");
            }
            else
            {
                configGrid
                    .AddRow("[b]Mode[/]", "[blue]Library-based[/]")
                    .AddRow("[b]From Library[/]", $"{settings.LibraryName.EscapeMarkup()}")
                    .AddRow("[b]To Library[/]", $"{settings.ToLibraryName.EscapeMarkup()}");
            }

            AnsiConsole.Write(
                new Panel(configGrid)
                    .Header("[yellow]Move 'Ok' Albums Configuration[/]")
                    .RoundedBorder()
                    .BorderColor(Color.Blue));

            AnsiConsole.WriteLine();

            var currentAlbumLine = string.Empty;
            var statsLine = string.Empty;

            await AnsiConsole.Progress()
                .AutoRefresh(true)
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                [
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn(),
                    new SpinnerColumn()
                ])
                .StartAsync(async ctx =>
                {
                    var progressTask = ctx.AddTask("[green]Initializing...[/]", maxValue: 100);

                    var totalToMove = 0;
                    var totalBytes = 0L;
                    var lastCurrentAlbum = string.Empty;

                    libraryService.OnProcessingProgressEvent += (sender, e) =>
                    {
                        switch (e.Type)
                        {
                            case ProcessingEventType.Start:
                                totalToMove = e.Max;
                                totalBytes = e.TotalBytes;

                                if (e.Max == 0)
                                {
                                    progressTask.Description = "[yellow]No albums found to move[/]";
                                    progressTask.StopTask();
                                }
                                else
                                {
                                    progressTask.MaxValue = e.Max;
                                    progressTask.Description = $"[green]Moving albums:[/] 0/{e.Max:N0} (0.0%)";
                                }
                                break;

                            case ProcessingEventType.Processing:
                                if (e.Max > 0)
                                {
                                    var percentComplete = (double)e.Current / e.Max * 100;
                                    progressTask.Value = e.Current;

                                    var elapsed = Stopwatch.GetElapsedTime(startTime);
                                    var bytesPerSecond = elapsed.TotalSeconds > 0
                                        ? e.BytesProcessed / elapsed.TotalSeconds
                                        : 0;
                                    var mbps = FormatBytes((long)bytesPerSecond);

                                    // Extract album name from message if present
                                    var albumName = e.Message;
                                    if (albumName.StartsWith("Processing [") && albumName.EndsWith("]"))
                                    {
                                        albumName = albumName[12..^1]; // Remove "Processing [" and "]"
                                        if (albumName.Length > 45)
                                        {
                                            albumName = albumName[..42] + "...";
                                        }
                                        lastCurrentAlbum = albumName;
                                    }

                                    var processedMB = FormatBytes(e.BytesProcessed);
                                    var totalMB = FormatBytes(totalBytes);
                                    var dataPercent = totalBytes > 0 ? (double)e.BytesProcessed / totalBytes * 100 : 0;

                                    // Format: Albums: 125/284 (44%) | Current: Abbey Road | Data: 12.5/25.5 GB (49%) | Speed: 95 MB/s
                                    progressTask.Description = $"[green]Albums:[/] {e.Current}/{e.Max} ([cyan]{percentComplete:F1}%[/]) | " +
                                                              $"[yellow]{Markup.Escape(lastCurrentAlbum)}[/] | " +
                                                              $"[green]Data:[/] {processedMB}/{totalMB} MB ([cyan]{dataPercent:F1}%[/]) | " +
                                                              $"[green]Speed:[/] [cyan]{mbps} MB/s[/]";
                                }
                                break;

                            case ProcessingEventType.Stop:
                                progressTask.Value = progressTask.MaxValue;
                                progressTask.StopTask();

                                finalStatistics = e.Statistics;

                                var totalElapsed = Stopwatch.GetElapsedTime(startTime);
                                var avgBytesPerSecond = totalElapsed.TotalSeconds > 0
                                    ? e.BytesProcessed / totalElapsed.TotalSeconds
                                    : 0;
                                var avgMbps = FormatBytes((long)avgBytesPerSecond);

                                progressTask.Description = $"[green]✓ Completed:[/] {e.Max:N0} albums | " +
                                                          $"{FormatBytes(e.BytesProcessed)} MB | " +
                                                          $"[cyan]Avg: {avgMbps} MB/s[/] | " +
                                                          $"[cyan]Time: {totalElapsed:hh\\:mm\\:ss}[/]";
                                break;
                        }
                    };

                    if (settings.IsPathBasedMode)
                    {
                        result = await libraryService.MoveAlbumsFromPathToPath(
                                settings.FromPath!,
                                settings.ToPath!,
                                b => b.Status == AlbumStatus.Ok,
                                settings.Verbose,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }
                    else
                    {
                        if (settings.LibraryName == settings.ToLibraryName)
                        {
                            result = new Common.Models.OperationResult<bool>("Source and destination library are the same.")
                            {
                                Data = false
                            };
                        }
                        else
                        {
                            result = await libraryService.MoveAlbumsFromLibraryToLibrary(
                                    settings.LibraryName!,
                                    settings.ToLibraryName!,
                                    b => b.Status == AlbumStatus.Ok,
                                    settings.Verbose,
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                    }
                });

            AnsiConsole.WriteLine();

            if (result == null)
            {
                AnsiConsole.MarkupLine("[red]Error: Operation did not complete[/]");
                return 1;
            }

            if (!result.IsSuccess)
            {
                AnsiConsole.Write(
                    new Panel(new JsonText(serializer.Serialize(result) ?? string.Empty))
                        .Header("[red]Operation Failed[/]")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Red));
            }
            else
            {
                if (finalStatistics != null)
                {
                    var statsTable = new Table()
                        .Border(TableBorder.Rounded)
                        .BorderColor(Color.Blue)
                        .AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned())
                        .AddColumn(new TableColumn("[bold]Count[/]").RightAligned())
                        .AddColumn(new TableColumn("[bold]Description[/]").LeftAligned());

                    statsTable.AddRow(
                        "[yellow]Total Albums Found[/]",
                        $"{finalStatistics.TotalMelodeeFilesFound:N0}",
                        "Albums with melodee.json in source library");

                    statsTable.AddRow(
                        "[green]Albums Ready to Move[/]",
                        $"{finalStatistics.AlbumsReadyToMove:N0}",
                        "Albums with 'Ok' status matching move criteria");

                    statsTable.AddRow(
                        "[green]Albums Moved[/]",
                        $"{finalStatistics.AlbumsMoved:N0}",
                        "New albums moved to destination");

                    statsTable.AddRow(
                        "[cyan]Albums Merged[/]",
                        $"{finalStatistics.AlbumsMergedWithExisting:N0}",
                        "Albums merged with existing albums in destination");

                    if (finalStatistics.AlbumsSkippedByStatus > 0)
                    {
                        statsTable.AddRow(
                            "[grey]Skipped (Status)[/]",
                            $"{finalStatistics.AlbumsSkippedByStatus:N0}",
                            "Albums skipped due to non-Ok status (Invalid/New)");
                    }

                    if (finalStatistics.AlbumsSkippedAsDuplicateDirectory > 0)
                    {
                        statsTable.AddRow(
                            "[grey]Skipped (Duplicate Dir)[/]",
                            $"{finalStatistics.AlbumsSkippedAsDuplicateDirectory:N0}",
                            "Albums in duplicate-prefixed directories");
                    }

                    if (finalStatistics.AlbumsFailedToLoad > 0)
                    {
                        statsTable.AddRow(
                            "[red]Failed to Load[/]",
                            $"{finalStatistics.AlbumsFailedToLoad:N0}",
                            "Albums that couldn't be deserialized");
                    }

                    AnsiConsole.Write(
                        new Panel(statsTable)
                            .Header("[yellow]Move Operation Summary[/]")
                            .RoundedBorder()
                            .BorderColor(Color.Yellow));

                    AnsiConsole.WriteLine();
                }

                var rule = new Rule("[green]Move operation completed successfully[/]")
                {
                    Justification = Justify.Left
                };
                AnsiConsole.Write(rule);
            }

            return result.IsSuccess ? 0 : 1;
        }
    }
}
