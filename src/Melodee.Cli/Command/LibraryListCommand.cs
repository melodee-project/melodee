using Melodee.Cli.CommandSettings;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     List all libraries in the database with their details.
/// </summary>
public class LibraryListCommand : CommandBase<LibraryListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, LibraryListSettings settings, CancellationToken cancellationToken)
    {
        using (var scope = CreateServiceProvider().CreateScope())
        {
            var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();

            PagedResult<Common.Data.Models.Library>? result = null;

            await AnsiConsole.Status()
                .StartAsync("Loading libraries...", async ctx =>
                {
                    result = await libraryService.ListAsync(new PagedRequest { PageSize = short.MaxValue }, cancellationToken);
                });

            if (result == null || !result.Data.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No libraries found in the database.[/]");
                return 0;
            }

            var libraries = result.Data.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToArray();

            if (settings.ReturnRaw)
            {
                foreach (var library in libraries)
                {
                    Console.WriteLine($"{library.Id}\t{library.Name}\t{library.TypeValue}\t{library.Path}\t{library.ArtistCount}\t{library.AlbumCount}\t{library.SongCount}\t{library.IsLocked}");
                }
                return 0;
            }

            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.AddColumn(new TableColumn("[bold]Name[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Type[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Path[/]"));
            table.AddColumn(new TableColumn("[bold]Artists[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Albums[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Songs[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Last Scan[/]").Centered());
            table.AddColumn(new TableColumn("[bold]Status[/]").Centered());

            foreach (var library in libraries)
            {
                var typeColor = library.TypeValue switch
                {
                    Common.Enums.LibraryType.Inbound => "yellow",
                    Common.Enums.LibraryType.Staging => "cyan",
                    Common.Enums.LibraryType.Storage => "green",
                    _ => "grey"
                };

                var statusMarkup = library.IsLocked ? "[red]🔒 Locked[/]" : "[green]✓ Active[/]";

                var lastScanText = !library.ShouldBeScanned()
                    ? "[grey]N/A[/]"
                    : library.LastScanAt.HasValue
                        ? library.LastScanAt.Value.ToDateTimeUtc().ToString(Iso8601DateFormat)
                        : "[grey]Never[/]";

                var needsScanning = library.ShouldBeScanned() && library.NeedsScanning();
                if (needsScanning && !library.IsLocked)
                {
                    lastScanText = $"[yellow]{lastScanText} ⚠[/]";
                }

                table.AddRow(
                    $"[bold]{library.Name.EscapeMarkup()}[/]",
                    $"[{typeColor}]{library.TypeValue}[/]",
                    library.Path.EscapeMarkup(),
                    (library.ArtistCount ?? 0).ToString(),
                    (library.AlbumCount ?? 0).ToString(),
                    (library.SongCount ?? 0).ToString(),
                    lastScanText,
                    statusMarkup
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[grey]Total libraries: {libraries.Length}[/]");

            var needsScan = libraries.Count(l => l.ShouldBeScanned() && l.NeedsScanning() && !l.IsLocked);
            if (needsScan > 0)
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ {needsScan} library(ies) need scanning[/]");
            }

            return 0;
        }
    }
}
