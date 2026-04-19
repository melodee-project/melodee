using Melodee.Cli.CommandSettings;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Generate some statistics for the given Library.
/// </summary>
public class LibraryStatsCommand : CommandBase<LibraryStatsSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, LibraryStatsSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

            OperationResult<Statistic[]?>? result = null;

            await AnsiConsole.Status()
                .StartAsync("Gathering library statistics...", async ctx =>
                {
                    result = await libraryService.Statistics(settings.LibraryName!, cancellationToken);
                });

            if (result == null || !result.IsSuccess)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] {result?.Messages?.FirstOrDefault() ?? "Failed to retrieve statistics"}");
                return 1;
            }

            var stats = result.Data ?? [];

            if (settings.ReturnRaw)
            {
                foreach (var stat in stats)
                {
                    if (!settings.ShowOnlyIssues || (settings.ShowOnlyIssues && stat.Type != StatisticType.Information))
                    {
                        Console.WriteLine($"{stat.Title}\t{stat.Data}\t{stat.Message}");
                    }
                }
                return 0;
            }

            // Get library info
            var libraries = await libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
            var library = libraries.Data.FirstOrDefault(x => x.Name.Equals(settings.LibraryName, StringComparison.OrdinalIgnoreCase));

            if (library != null)
            {
                // Display library header as a clean grid
                var grid = new Grid();
                grid.AddColumn(new GridColumn().NoWrap().PadRight(2));
                grid.AddColumn(new GridColumn().NoWrap());

                var typeColor = library.TypeValue switch
                {
                    Common.Enums.LibraryType.Inbound => "yellow",
                    Common.Enums.LibraryType.Staging => "cyan",
                    Common.Enums.LibraryType.Storage => "green",
                    _ => "grey"
                };

                var lastScanText = library.LastScanAt.HasValue
                    ? library.LastScanAt.Value.ToDateTimeUtc().ToString(Iso8601DateFormat)
                    : "[grey]Never scanned[/]";

                var statusIcon = library.IsLocked ? "🔒" : "✓";
                var statusColor = library.IsLocked ? "red" : "green";

                grid.AddRow($"[bold cyan]Library:[/]", $"[bold]{library.Name.EscapeMarkup()}[/]");
                grid.AddRow($"[grey]Type:[/]", $"[{typeColor}]{library.TypeValue}[/]");
                grid.AddRow($"[grey]Status:[/]", $"[{statusColor}]{statusIcon} {(library.IsLocked ? "Locked" : "Active")}[/]");
                grid.AddRow($"[grey]Last Scan:[/]", lastScanText);
                grid.AddRow($"[grey]Path:[/]", $"[dim]{library.Path.EscapeMarkup()}[/]");

                var panel = new Panel(grid)
                {
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(foreground: Color.Grey),
                    Padding = new Padding(1, 0, 1, 0)
                };

                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();
            }

            // Group statistics by category
            var groupedStats = stats
                .Where(s => !settings.ShowOnlyIssues || s.Type != StatisticType.Information)
                .GroupBy(s => s.Category ?? StatisticCategory.NotSet)
                .OrderBy(g => g.Key);

            foreach (var group in groupedStats)
            {
                if (group.Key == StatisticCategory.NotSet)
                {
                    continue;
                }

                var categoryTitle = group.Key switch
                {
                    StatisticCategory.CountArtist => "👤 Artists",
                    StatisticCategory.CountAlbum => "💿 Albums",
                    StatisticCategory.CountSong => "🎵 Songs",
                    StatisticCategory.CountUsers => "👥 Users",
                    _ => group.Key.ToString()
                };

                var panel = new Panel(CreateStatsTable(group.OrderBy(s => s.SortOrder ?? 999).ThenBy(s => s.Title)))
                {
                    Header = new PanelHeader($"[bold]{categoryTitle}[/]", Justify.Left),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(foreground: Color.Grey)
                };

                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();
            }

            // Display uncategorized stats
            var uncategorized = stats
                .Where(s => s.Category == StatisticCategory.NotSet)
                .Where(s => !settings.ShowOnlyIssues || s.Type != StatisticType.Information)
                .OrderBy(s => s.SortOrder ?? 999)
                .ThenBy(s => s.Title)
                .ToList();

            if (uncategorized.Any())
            {
                var panel = new Panel(CreateStatsTable(uncategorized))
                {
                    Header = new PanelHeader("[bold]ℹ️  Additional Information[/]", Justify.Left),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(foreground: Color.Grey)
                };

                AnsiConsole.Write(panel);
                AnsiConsole.WriteLine();
            }

            // Display issues/warnings
            var issues = stats.Where(s => s.Type == StatisticType.Warning || s.Type == StatisticType.Error).ToList();
            if (issues.Any())
            {
                var issuesTable = new Table();
                issuesTable.Border = TableBorder.Rounded;
                issuesTable.BorderStyle = new Style(foreground: Color.Red);
                issuesTable.AddColumn("[bold red]⚠️  Issues[/]");

                foreach (var issue in issues)
                {
                    var icon = issue.Type == StatisticType.Error ? "❌" : "⚠️";
                    var color = issue.Type == StatisticType.Error ? "red" : "yellow";
                    issuesTable.AddRow($"[{color}]{icon} {issue.Title.EscapeMarkup()}: {issue.Message?.EscapeMarkup() ?? "No details"}[/]");
                }

                AnsiConsole.Write(issuesTable);
                AnsiConsole.WriteLine();
            }

            // Display additional messages
            if (!settings.ShowOnlyIssues && result.Messages?.Any() == true)
            {
                var messagesPanel = new Panel(string.Join("\n", result.Messages.Select(m => m.EscapeMarkup())))
                {
                    Header = new PanelHeader("[bold]📝 Messages[/]", Justify.Left),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(foreground: Color.Grey)
                };

                AnsiConsole.Write(messagesPanel);
                AnsiConsole.WriteLine();
            }

            return 0;
        }
    }

    private static Table CreateStatsTable(IEnumerable<Statistic> stats)
    {
        var table = new Table();
        table.Border = TableBorder.None;
        table.HideHeaders();
        table.AddColumn(new TableColumn("Metric").Width(40));
        table.AddColumn(new TableColumn("Value").Width(20));
        table.AddColumn(new TableColumn("Details"));

        foreach (var stat in stats)
        {
            var icon = stat.Icon ?? (stat.Type switch
            {
                StatisticType.Warning => "⚠️",
                StatisticType.Error => "❌",
                StatisticType.Count => "📊",
                _ => "ℹ️"
            });

            var dataColor = stat.DisplayColor ?? "default";
            var dataValue = stat.Data?.ToString()?.EscapeMarkup() ?? "0";

            table.AddRow(
                $"{icon} {stat.Title.EscapeMarkup()}",
                $"[{dataColor}]{dataValue}[/]",
                stat.Message?.EscapeMarkup() ?? string.Empty
            );
        }

        return table;
    }
}
