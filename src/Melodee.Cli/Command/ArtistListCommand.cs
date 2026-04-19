using Melodee.Cli.CommandSettings;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     List artists in the database.
/// </summary>
public class ArtistListCommand : CommandBase<ArtistListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ArtistListSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var artistService = scope.ServiceProvider.GetRequiredService<ArtistService>();

        var pagedRequest = new PagedRequest
        {
            PageSize = (short)settings.Limit,
            OrderBy = new Dictionary<string, string> { { "Name", "ASC" } }
        };

        var result = await artistService.ListAsync(pagedRequest, cancellationToken);

        if (!result.IsSuccess || result.Data == null || !result.Data.Any())
        {
            AnsiConsole.MarkupLine("[yellow]No artists found.[/]");
            return 0;
        }

        var artists = result.Data.ToList();

        if (settings.ReturnRaw)
        {
            var jsonOutput = artists.Select(a => new
            {
                a.Id,
                a.ApiKey,
                a.Name,
                a.NameNormalized,
                a.AlbumCount,
                a.SongCount,
                a.IsLocked,
                a.LibraryId,
                CreatedAt = a.CreatedAt.ToDateTimeUtc(),
                LastPlayedAt = a.LastPlayedAt?.ToDateTimeUtc(),
                a.PlayedCount,
                a.CalculatedRating
            });
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonOutput, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn(new TableColumn("[bold]Name[/]"));
        table.AddColumn(new TableColumn("[bold]Albums[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Songs[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Plays[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Rating[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Status[/]").Centered());

        foreach (var artist in artists)
        {
            var ratingDisplay = artist.CalculatedRating > 0
                ? $"[yellow]{artist.CalculatedRating:F1}★[/]"
                : "[grey]---[/]";

            var statusDisplay = artist.IsLocked
                ? "[red]🔒 Locked[/]"
                : "[green]✓[/]";

            table.AddRow(
                artist.Name.EscapeMarkup(),
                artist.AlbumCount.ToString(),
                artist.SongCount.ToString(),
                artist.PlayedCount.ToString(),
                ratingDisplay,
                statusDisplay
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Showing {artists.Count} of {result.TotalCount:N0} artists[/]");

        return 0;
    }
}
