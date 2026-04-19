using System.Diagnostics;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Cli.Command;

/// <summary>
///     Generate a report like summary of albums found for given library.
/// </summary>
public class LibraryAlbumStatusReportCommand : CommandBase<LibraryAlbumStatusReportSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, LibraryAlbumStatusReportSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

            MelodeeModels.OperationResult<MelodeeModels.Statistic[]?>? result = null;

            await AnsiConsole.Status()
                .StartAsync("Generating album status report...", async ctx =>
                {
                    result = await libraryService.AlbumStatusReport(settings.LibraryName!, cancellationToken);
                });

            if (result == null || !result.IsSuccess)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/]; {result?.Messages?.FirstOrDefault() ?? "Unknown error"}");
                return 1;
            }

            if (settings.ReturnRaw)
            {
                foreach (var stat in result.Data ?? [])
                {
                    Trace.WriteLine($"{stat.Title}\t{stat.Data}\t{stat.Message}");
                    Console.WriteLine($"{stat.Title}\t{stat.Data}\t{stat.Message}");
                }
                return 0;
            }

            var stats = result.Data ?? [];

            if (settings.Full)
            {
                var table = new Table();
                table.AddColumn("Summary");
                table.AddColumn("Data");
                table.AddColumn("Information");
                foreach (var stat in stats)
                {
                    table.AddRow(
                        stat.Title.EscapeMarkup(),
                        $"[{stat.DisplayColor ?? "default"}]{stat.Data?.ToString().EscapeMarkup() ?? string.Empty}[/]",
                        stat.Message?.EscapeMarkup() ?? string.Empty);
                }
                AnsiConsole.Write(table);
            }
            else
            {
                var table = new Table();
                table.AddColumn("Status");
                table.AddColumn("Count");

                // 1. Ok albums (from the summary stat)
                var okStat = stats.FirstOrDefault(x => x.Title == "Ok");
                if (okStat != null)
                {
                    table.AddRow("[green]Ok[/]", okStat.Data?.ToString() ?? "0");
                }

                // 2. Invalid Ok albums
                var invalidOkCount = stats.Count(x => x.Title.StartsWith("Album with `Ok` status"));
                if (invalidOkCount > 0)
                {
                    table.AddRow("[yellow]Ok (Invalid)[/]", invalidOkCount.ToString());
                }

                // 3. Other statuses
                // Filter out the Ok stat and the Invalid Ok stats
                var otherStats = stats.Where(x => x.Title != "Ok" && !x.Title.StartsWith("Album with `Ok` status"));

                // Group by Data (which contains the status string)
                var grouped = otherStats
                    .GroupBy(x => x.Data?.ToString() ?? "Unknown")
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count);

                foreach (var g in grouped)
                {
                    table.AddRow($"[red]{g.Status}[/]", g.Count.ToString());
                }

                AnsiConsole.Write(table);
                AnsiConsole.MarkupLine("[grey]Use --full to see detailed list.[/]");
            }

            if (result.Messages?.Any() ?? false)
            {
                AnsiConsole.WriteLine();
                foreach (var message in result.Messages)
                {
                    AnsiConsole.WriteLine(message.EscapeMarkup());
                }
                AnsiConsole.WriteLine();
            }

            return 0;
        }
    }
}
