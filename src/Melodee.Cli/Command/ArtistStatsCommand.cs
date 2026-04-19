using Melodee.Cli.CommandSettings;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Show artist statistics including missing images and potential duplicates.
/// </summary>
public class ArtistStatsCommand : CommandBase<ArtistStatsSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ArtistStatsSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var totalArtists = await dbContext.Artists.CountAsync(cancellationToken);
        var lockedArtists = await dbContext.Artists.CountAsync(a => a.IsLocked, cancellationToken);
        var artistsWithNoImages = await dbContext.Artists.CountAsync(a => a.ImageCount == 0, cancellationToken);
        var artistsWithNoAlbums = await dbContext.Artists.CountAsync(a => a.AlbumCount == 0, cancellationToken);

        var readyToProcessStatus = SafeParser.ToNumber<int>(MetaDataModelStatus.ReadyToProcess);
        var artistsReadyToProcess = await dbContext.Artists.CountAsync(a => a.MetaDataStatus == readyToProcessStatus, cancellationToken);

        var totalAlbums = await dbContext.Artists.SumAsync(a => a.AlbumCount, cancellationToken);
        var totalSongs = await dbContext.Artists.SumAsync(a => a.SongCount, cancellationToken);

        // Find potential duplicates by normalized name
        var duplicateNames = await dbContext.Artists
            .GroupBy(a => a.NameNormalized)
            .Where(g => g.Count() > 1)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(g => g.Count)
            .Take(20)
            .ToListAsync(cancellationToken);

        if (settings.ReturnRaw)
        {
            var jsonOutput = new
            {
                TotalArtists = totalArtists,
                LockedArtists = lockedArtists,
                ArtistsWithNoImages = artistsWithNoImages,
                ArtistsWithNoAlbums = artistsWithNoAlbums,
                ArtistsReadyToProcess = artistsReadyToProcess,
                TotalAlbums = totalAlbums,
                TotalSongs = totalSongs,
                PotentialDuplicates = duplicateNames.Select(d => new { d.Name, d.Count })
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonOutput, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        // Summary statistics table
        var summaryTable = new Table();
        summaryTable.Border = TableBorder.Rounded;
        summaryTable.Title = new TableTitle("[bold blue]Artist Statistics[/]");
        summaryTable.AddColumn(new TableColumn("[bold]Metric[/]"));
        summaryTable.AddColumn(new TableColumn("[bold]Count[/]").RightAligned());
        summaryTable.AddColumn(new TableColumn("[bold]%[/]").RightAligned());

        summaryTable.AddRow("Total Artists", $"[bold]{totalArtists:N0}[/]", "100%");
        summaryTable.AddRow("Total Albums", $"{totalAlbums:N0}", "---");
        summaryTable.AddRow("Total Songs", $"{totalSongs:N0}", "---");
        summaryTable.AddRow("───────────────────", "─────────", "─────");

        var noImagesPercent = totalArtists > 0 ? (double)artistsWithNoImages / totalArtists * 100 : 0;
        var noImagesColor = noImagesPercent > 20 ? "red" : noImagesPercent > 10 ? "yellow" : "green";
        summaryTable.AddRow(
            "Missing Images",
            $"[{noImagesColor}]{artistsWithNoImages:N0}[/]",
            $"[{noImagesColor}]{noImagesPercent:F1}%[/]"
        );

        var noAlbumsPercent = totalArtists > 0 ? (double)artistsWithNoAlbums / totalArtists * 100 : 0;
        var noAlbumsColor = noAlbumsPercent > 10 ? "red" : noAlbumsPercent > 5 ? "yellow" : "green";
        summaryTable.AddRow(
            "No Albums",
            $"[{noAlbumsColor}]{artistsWithNoAlbums:N0}[/]",
            $"[{noAlbumsColor}]{noAlbumsPercent:F1}%[/]"
        );

        summaryTable.AddRow(
            "Locked",
            lockedArtists > 0 ? $"[yellow]{lockedArtists:N0}[/]" : $"{lockedArtists:N0}",
            totalArtists > 0 ? $"{(double)lockedArtists / totalArtists * 100:F1}%" : "0%"
        );

        summaryTable.AddRow(
            "Ready to Process",
            artistsReadyToProcess > 0 ? $"[cyan]{artistsReadyToProcess:N0}[/]" : $"{artistsReadyToProcess:N0}",
            totalArtists > 0 ? $"{(double)artistsReadyToProcess / totalArtists * 100:F1}%" : "0%"
        );

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        // Potential duplicates table
        if (duplicateNames.Count != 0)
        {
            var duplicatesTable = new Table();
            duplicatesTable.Border = TableBorder.Rounded;
            duplicatesTable.Title = new TableTitle("[bold yellow]Potential Duplicates[/]");
            duplicatesTable.AddColumn(new TableColumn("[bold]Normalized Name[/]"));
            duplicatesTable.AddColumn(new TableColumn("[bold]Count[/]").RightAligned());

            foreach (var dup in duplicateNames)
            {
                duplicatesTable.AddRow(
                    dup.Name.EscapeMarkup(),
                    $"[yellow]{dup.Count}[/]"
                );
            }

            AnsiConsole.Write(duplicatesTable);
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[yellow]⚠ Found {duplicateNames.Count} artist names with potential duplicates[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[green]✓ No potential duplicate artists detected[/]");
        }

        return 0;
    }
}
