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
///     For the given library rebuild all of the Melodee data files from files in place (no conversion, no manipulations to
///     any files in place).
/// </summary>
public class LibraryRebuildCommand : CommandBase<LibraryRebuildSettings>
{
    private static string FormatNumber(int number)
    {
        return number.ToString("N0");
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, LibraryRebuildSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

            Common.Models.OperationResult<bool>? result = null;
            var startTime = Stopwatch.GetTimestamp();

            // Display initial configuration
            var configGrid = new Grid()
                .AddColumn(new GridColumn().NoWrap().PadRight(4))
                .AddColumn();

            configGrid
                .AddRow("[b]Library[/]", $"{settings.LibraryName.EscapeMarkup()}")
                .AddRow("[b]Mode[/]", settings.CreateOnlyMissing ? "[blue]Create Only Missing[/]" : "[blue]Full Rebuild[/]")
                .AddRow("[b]Skip Images[/]", settings.SkipImages ? "[yellow]Yes[/]" : "[green]No[/]")
                .AddRow("[b]Limit[/]", settings.Limit.HasValue ? $"[cyan]{settings.Limit.Value:N0}[/]" : "[green]Unlimited[/]");

            if (!string.IsNullOrEmpty(settings.OnlyPath))
            {
                configGrid.AddRow("[b]Path Filter[/]", $"{settings.OnlyPath.EscapeMarkup()}");
            }

            AnsiConsole.Write(
                new Panel(configGrid)
                    .Header("[yellow]Library Rebuild Configuration[/]")
                    .RoundedBorder()
                    .BorderColor(Color.Blue));

            AnsiConsole.WriteLine();

            if (!settings.CreateOnlyMissing)
            {
                var cleanResult = await libraryService.CleanLibraryAsync(settings.LibraryName!, cancellationToken);
                if (!cleanResult.IsSuccess)
                {
                    AnsiConsole.Write(
                        new Panel(new JsonText(serializer.Serialize(cleanResult) ?? string.Empty))
                            .Header("[red]Clean Failed[/]")
                            .Collapse()
                            .RoundedBorder()
                            .BorderColor(Color.Red));
                }
            }

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
                                    progressTask.Description = "[yellow]No directories found to rebuild[/]";
                                    progressTask.StopTask();
                                }
                                else
                                {
                                    progressTask.MaxValue = e.Max;
                                    progressTask.Description = $"[green]Rebuilding:[/] 0/{FormatNumber(e.Max)} (0.0%)";
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
                                progressTask.Description = $"[green]✓ Completed:[/] {FormatNumber(e.Current)} directories rebuilt | [cyan]Time: {elapsed:hh\\:mm\\:ss}[/]";
                                break;
                        }
                    };

                    result = await libraryService.Rebuild(settings.LibraryName!, settings.CreateOnlyMissing, settings.Verbose, settings.OnlyPath, settings.SkipImages, settings.Limit, cancellationToken).ConfigureAwait(false);
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
                        .Header("[red]Rebuild Failed[/]")
                        .Collapse()
                        .RoundedBorder()
                        .BorderColor(Color.Red));
            }
            else
            {
                var rule = new Rule("[green]Rebuild operation completed successfully[/]")
                {
                    Justification = Justify.Left
                };
                AnsiConsole.Write(rule);
            }

            return result.IsSuccess ? 0 : 1;
        }
    }
}
