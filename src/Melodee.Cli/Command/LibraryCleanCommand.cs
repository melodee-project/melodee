using System.Diagnostics;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using Spectre.Console.Json;

namespace Melodee.Cli.Command;

/// <summary>
///     Clean library of folders that don't add value.
/// </summary>
public class LibraryCleanCommand : CommandBase<LibraryCleanSettings>
{
    private static string FormatNumber(int number)
    {
        return number.ToString("N0");
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, LibraryCleanSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

            Common.Models.OperationResult<string[]>? result = null;
            var startTime = Stopwatch.GetTimestamp();

            // Display initial configuration
            var configGrid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(4))
                .AddColumn();

            configGrid.AddRow("[b]Library[/]", $"{settings.LibraryName.EscapeMarkup()}");

            AnsiConsole.Write(
                new Panel(configGrid)
                    .Header("[yellow]Library Clean Configuration[/]")
                    .RoundedBorder()
                    .BorderColor(Color.Blue));

            AnsiConsole.WriteLine();

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

                    libraryService.OnProcessingProgressEvent += (sender, e) =>
                    {
                        switch (e.Type)
                        {
                            case ProcessingEventType.Start:
                                if (e.Max == 0)
                                {
                                    progressTask.Description = "[yellow]No directories to clean[/]";
                                    progressTask.StopTask();
                                }
                                else
                                {
                                    progressTask.MaxValue = e.Max;
                                    progressTask.Description = $"[green]Cleaning:[/] 0/{FormatNumber(e.Max)} (0.0%)";
                                }
                                break;

                            case ProcessingEventType.Processing:
                                if (e.Max > 0)
                                {
                                    var percentComplete = (double)e.Current / e.Max * 100;
                                    progressTask.Value = e.Current;

                                    // Extract directory name from message if present
                                    var dirName = e.Message;
                                    if (dirName.StartsWith("Processing [") && dirName.EndsWith("]"))
                                    {
                                        dirName = dirName[12..^1];
                                        if (dirName.Length > 50)
                                        {
                                            dirName = dirName[..47] + "...";
                                        }
                                    }

                                    progressTask.Description = $"[green]Directories:[/] {FormatNumber(e.Current)}/{FormatNumber(e.Max)} ([cyan]{percentComplete:F1}%[/]) | [yellow]{Markup.Escape(dirName)}[/]";
                                }
                                break;

                            case ProcessingEventType.Stop:
                                progressTask.Value = progressTask.MaxValue;
                                progressTask.StopTask();

                                var elapsed = Stopwatch.GetElapsedTime(startTime);
                                progressTask.Description = $"[green]✓ Completed:[/] Library cleaned | [cyan]Time: {elapsed:hh\\:mm\\:ss}[/]";
                                break;
                        }
                    };

                    result = await libraryService.CleanLibraryAsync(settings.LibraryName!, cancellationToken);
                });

            AnsiConsole.WriteLine();

            if (result == null)
            {
                AnsiConsole.MarkupLine("[red]Error: Operation did not complete[/]");
                return 1;
            }

            if (settings.Verbose && result.Data?.Length > 0)
            {
                AnsiConsole.Write(
                    new Panel(new JsonText(serializer.Serialize(result) ?? string.Empty))
                        .Header("[green]Clean Results[/]")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Green));
            }
            else if (result.IsSuccess)
            {
                var rule = new Rule("[green]Clean operation completed successfully[/]")
                {
                    Justification = Justify.Left
                };
                AnsiConsole.Write(rule);
            }

            return result.IsSuccess ? 0 : 1;
        }
    }
}
