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
///     Show album statistics grouped by status.
/// </summary>
public class AlbumStatsCommand : CommandBase<AlbumStatsSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, AlbumStatsSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var totalAlbums = await dbContext.Albums.CountAsync(cancellationToken);
        var lockedAlbums = await dbContext.Albums.CountAsync(a => a.IsLocked, cancellationToken);
        var albumsWithNoImages = await dbContext.Albums.CountAsync(a => a.ImageCount == null || a.ImageCount == 0, cancellationToken);
        var totalSongs = await dbContext.Albums.SumAsync(a => a.SongCount, cancellationToken);

        var statusCounts = await dbContext.Albums
            .GroupBy(a => a.AlbumStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        if (settings.ReturnRaw)
        {
            var jsonOutput = new
            {
                TotalAlbums = totalAlbums,
                LockedAlbums = lockedAlbums,
                AlbumsWithNoImages = albumsWithNoImages,
                TotalSongs = totalSongs,
                StatusCounts = statusCounts.Select(s => new
                {
                    Status = SafeParser.ToEnum<AlbumStatus>(s.Status).ToString(),
                    s.Count
                })
            };
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonOutput, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var summaryTable = new Table();
        summaryTable.Border = TableBorder.Rounded;
        summaryTable.Title = new TableTitle("[bold blue]Album Statistics[/]");
        summaryTable.AddColumn(new TableColumn("[bold]Metric[/]"));
        summaryTable.AddColumn(new TableColumn("[bold]Count[/]").RightAligned());
        summaryTable.AddColumn(new TableColumn("[bold]%[/]").RightAligned());

        summaryTable.AddRow("Total Albums", $"[bold]{totalAlbums:N0}[/]", "100%");
        summaryTable.AddRow("Total Songs", $"{totalSongs:N0}", "---");
        summaryTable.AddRow("───────────────────", "─────────", "─────");

        var noImagesPercent = totalAlbums > 0 ? (double)albumsWithNoImages / totalAlbums * 100 : 0;
        var noImagesColor = noImagesPercent > 20 ? "red" : noImagesPercent > 10 ? "yellow" : "green";
        summaryTable.AddRow(
            "Missing Images",
            $"[{noImagesColor}]{albumsWithNoImages:N0}[/]",
            $"[{noImagesColor}]{noImagesPercent:F1}%[/]"
        );

        summaryTable.AddRow(
            "Locked",
            lockedAlbums > 0 ? $"[yellow]{lockedAlbums:N0}[/]" : $"{lockedAlbums:N0}",
            totalAlbums > 0 ? $"{(double)lockedAlbums / totalAlbums * 100:F1}%" : "0%"
        );

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        var statusTable = new Table();
        statusTable.Border = TableBorder.Rounded;
        statusTable.Title = new TableTitle("[bold blue]Albums by Status[/]");
        statusTable.AddColumn(new TableColumn("[bold]Status[/]"));
        statusTable.AddColumn(new TableColumn("[bold]Count[/]").RightAligned());
        statusTable.AddColumn(new TableColumn("[bold]%[/]").RightAligned());

        foreach (var statusCount in statusCounts.OrderByDescending(s => s.Count))
        {
            var status = SafeParser.ToEnum<AlbumStatus>(statusCount.Status);
            var statusColor = status switch
            {
                AlbumStatus.Ok => "green",
                AlbumStatus.New => "cyan",
                AlbumStatus.Invalid => "yellow",
                _ => "grey"
            };

            var percent = totalAlbums > 0 ? (double)statusCount.Count / totalAlbums * 100 : 0;
            statusTable.AddRow(
                $"[{statusColor}]{status}[/]",
                statusCount.Count.ToString("N0"),
                $"{percent:F1}%"
            );
        }

        AnsiConsole.Write(statusTable);

        return 0;
    }
}
