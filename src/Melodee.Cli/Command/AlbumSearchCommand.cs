using Melodee.Cli.CommandSettings;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Filtering;
using Melodee.Common.Models;
using Melodee.Common.Services;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Search for albums by name with optional date filtering and bulk delete.
/// </summary>
public class AlbumSearchCommand : CommandBase<AlbumSearchSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, AlbumSearchSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var albumService = scope.ServiceProvider.GetRequiredService<AlbumService>();

        var searchTerm = settings.Query.ToNormalizedString() ?? settings.Query;
        var isWildcard = settings.Query == "*";

        var filters = new List<FilterOperatorInfo>();

        if (!isWildcard)
        {
            filters.Add(new FilterOperatorInfo("NameNormalized", FilterOperator.Contains, searchTerm));
        }

        if (settings.SinceDays.HasValue)
        {
            var sinceDate = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromDays(settings.SinceDays.Value));
            filters.Add(new FilterOperatorInfo("CreatedAt", FilterOperator.GreaterThanOrEquals, sinceDate));
        }

        var sortColumn = MapSortColumn(settings.SortBy);
        var sortDir = settings.SortDirection.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

        Dictionary<string, string> orderBy;
        if (!string.IsNullOrWhiteSpace(sortColumn))
        {
            orderBy = new Dictionary<string, string> { { sortColumn, sortDir } };
        }
        else if (settings.SinceDays.HasValue)
        {
            orderBy = new Dictionary<string, string> { { "CreatedAt", "DESC" } };
        }
        else
        {
            orderBy = new Dictionary<string, string> { { "Name", "ASC" } };
        }

        var pagedRequest = new PagedRequest
        {
            PageSize = (short)settings.Limit,
            OrderBy = orderBy,
            FilterBy = filters.ToArray()
        };

        var result = await albumService.ListAsync(pagedRequest, cancellationToken);

        if (!result.IsSuccess || result.Data == null || !result.Data.Any())
        {
            if (settings.SinceDays.HasValue)
            {
                AnsiConsole.MarkupLine($"[yellow]No albums found created in the last {settings.SinceDays} days{(isWildcard ? "" : $" matching: {settings.Query.EscapeMarkup()}")}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]No albums found matching:[/] {settings.Query.EscapeMarkup()}");
            }
            return 0;
        }

        var albums = result.Data.ToList();

        if (settings.ReturnRaw && !settings.Delete)
        {
            var jsonOutput = albums.Select(a => new
            {
                a.Id,
                a.ApiKey,
                a.Name,
                a.NameNormalized,
                a.ArtistName,
                a.ArtistApiKey,
                a.SongCount,
                a.Duration,
                ReleaseDate = a.ReleaseDate.ToString(),
                Status = a.AlbumStatusValue.ToString(),
                a.IsLocked,
                CreatedAt = a.CreatedAt.ToDateTimeUtc(),
                a.PlayedCount,
                a.CalculatedRating
            });
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonOutput, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        if (settings.SinceDays.HasValue)
        {
            AnsiConsole.MarkupLine($"[blue]Albums created in the last {settings.SinceDays} days{(isWildcard ? "" : $" matching: {settings.Query.EscapeMarkup()}")}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[blue]Search results for:[/] {settings.Query.EscapeMarkup()}");
        }
        AnsiConsole.WriteLine();

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn(new TableColumn("[bold]Artist[/]"));
        table.AddColumn(new TableColumn("[bold]Album[/]"));
        table.AddColumn(new TableColumn("[bold]Year[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Songs[/]").RightAligned());
        if (settings.SinceDays.HasValue)
        {
            table.AddColumn(new TableColumn("[bold]Added[/]").Centered());
        }
        table.AddColumn(new TableColumn("[bold]Status[/]").Centered());

        foreach (var album in albums)
        {
            var statusColor = album.AlbumStatusValue switch
            {
                AlbumStatus.Ok => "green",
                AlbumStatus.New => "cyan",
                AlbumStatus.Invalid => "yellow",
                _ => "grey"
            };

            var row = new List<string>
            {
                album.ArtistName.EscapeMarkup().Length > 30
                    ? album.ArtistName.EscapeMarkup()[..27] + "..."
                    : album.ArtistName.EscapeMarkup(),
                album.Name.EscapeMarkup().Length > 40
                    ? album.Name.EscapeMarkup()[..37] + "..."
                    : album.Name.EscapeMarkup(),
                album.ReleaseDate.Year.ToString(),
                album.SongCount.ToString()
            };

            if (settings.SinceDays.HasValue)
            {
                row.Add(album.CreatedAt.ToDateTimeUtc().ToString(Iso8601DateFormat));
            }

            row.Add($"[{statusColor}]{album.AlbumStatusValue}[/]");

            table.AddRow(row.ToArray());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Found {result.TotalCount:N0} matching albums (showing {albums.Count})[/]");

        if (settings.Delete)
        {
            AnsiConsole.WriteLine();

            var lockedAlbums = albums.Where(a => a.IsLocked).ToList();
            if (lockedAlbums.Any())
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ {lockedAlbums.Count} album(s) are locked and will be skipped.[/]");
            }

            var deletableAlbums = albums.Where(a => !a.IsLocked).ToList();
            if (!deletableAlbums.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No albums available for deletion (all are locked).[/]");
                return 0;
            }

            var totalSongs = deletableAlbums.Sum(a => a.SongCount);

            AnsiConsole.Write(new Rule("[red]⚠️  DESTRUCTIVE OPERATION  ⚠️[/]").RuleStyle("red"));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red bold]This will permanently delete:[/]");
            AnsiConsole.MarkupLine($"  • [white]{deletableAlbums.Count:N0}[/] album(s)");
            AnsiConsole.MarkupLine($"  • [white]{totalSongs:N0}[/] song(s)");
            if (!settings.KeepFiles)
            {
                AnsiConsole.MarkupLine($"  • [white]All associated files on disk[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"  • [dim]Files will be kept on disk (--keep-files)[/]");
            }
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[red]This action cannot be undone![/]");
            AnsiConsole.WriteLine();

            var proceed = settings.SkipConfirmation;
            if (!proceed)
            {
                proceed = AnsiConsole.Confirm("[yellow]Are you sure you want to delete these albums?[/]", defaultValue: false);
            }

            if (!proceed)
            {
                AnsiConsole.MarkupLine("[grey]Operation cancelled.[/]");
                return 0;
            }

            AnsiConsole.WriteLine();

            var deleted = 0;
            var failed = 0;

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
                    var task = ctx.AddTask("[red]Deleting albums...[/]", maxValue: deletableAlbums.Count);

                    foreach (var album in deletableAlbums)
                    {
                        try
                        {
                            var deleteResult = await albumService.DeleteAsync([album.Id], !settings.KeepFiles, cancellationToken);
                            if (deleteResult.IsSuccess)
                            {
                                deleted++;
                            }
                            else
                            {
                                failed++;
                            }
                        }
                        catch
                        {
                            failed++;
                        }

                        task.Increment(1);
                        task.Description = $"[red]Deleting albums...[/] [dim]({deleted} deleted, {failed} failed)[/]";
                    }

                    task.Description = $"[green]✓ Deletion complete[/] [dim]({deleted} deleted, {failed} failed)[/]";
                });

            AnsiConsole.WriteLine();

            if (deleted > 0)
            {
                AnsiConsole.MarkupLine($"[green]✓ Successfully deleted {deleted:N0} album(s).[/]");
            }

            if (failed > 0)
            {
                AnsiConsole.MarkupLine($"[red]✗ Failed to delete {failed:N0} album(s).[/]");
            }
        }

        return 0;
    }

    private static string? MapSortColumn(string? displayColumn)
    {
        if (string.IsNullOrWhiteSpace(displayColumn))
        {
            return null;
        }

        return displayColumn.ToLowerInvariant() switch
        {
            "artist" => "ArtistName",
            "album" => "Name",
            "year" => "ReleaseDate",
            "songs" => "SongCount",
            "added" => "CreatedAt",
            "status" => "AlbumStatus",
            _ => null
        };
    }
}
