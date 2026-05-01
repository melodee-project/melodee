using Melodee.Cli.CommandSettings;
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
///     Search for artists by name with optional date filtering, sorting, and bulk delete.
/// </summary>
public class ArtistSearchCommand : CommandBase<ArtistSearchSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, ArtistSearchSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var artistService = scope.ServiceProvider.GetRequiredService<ArtistService>();

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

        var result = await artistService.ListAsync(pagedRequest, cancellationToken);

        if (!result.IsSuccess || result.Data == null || !result.Data.Any())
        {
            if (settings.SinceDays.HasValue)
            {
                AnsiConsole.MarkupLine($"[yellow]No artists found created in the last {settings.SinceDays} days{(isWildcard ? "" : $" matching: {settings.Query.EscapeMarkup()}")}[/]");
            }
            else
            {
                AnsiConsole.MarkupLine($"[yellow]No artists found matching:[/] {settings.Query.EscapeMarkup()}");
            }
            return 0;
        }

        var artists = result.Data.ToList();

        if (settings.ReturnRaw && !settings.Delete)
        {
            var jsonOutput = artists.Select(a => new
            {
                a.Id,
                a.ApiKey,
                a.Name,
                a.NameNormalized,
                a.AlternateNames,
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

        if (settings.SinceDays.HasValue)
        {
            AnsiConsole.MarkupLine($"[blue]Artists created in the last {settings.SinceDays} days{(isWildcard ? "" : $" matching: {settings.Query.EscapeMarkup()}")}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[blue]Search results for:[/] {settings.Query.EscapeMarkup()}");
        }
        AnsiConsole.WriteLine();

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn(new TableColumn("[bold]Name[/]"));
        table.AddColumn(new TableColumn("[bold]Albums[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Songs[/]").RightAligned());
        if (settings.SinceDays.HasValue)
        {
            table.AddColumn(new TableColumn("[bold]Added[/]").Centered());
        }
        table.AddColumn(new TableColumn("[bold]Rating[/]").Centered());

        foreach (var artist in artists)
        {
            var ratingDisplay = artist.CalculatedRating > 0
                ? $"[yellow]{artist.CalculatedRating:F1}★[/]"
                : "[grey]---[/]";

            var row = new List<string>
            {
                artist.Name.EscapeMarkup().Length > 40
                    ? artist.Name.EscapeMarkup()[..37] + "..."
                    : artist.Name.EscapeMarkup(),
                artist.AlbumCount.ToString(),
                artist.SongCount.ToString()
            };

            if (settings.SinceDays.HasValue)
            {
                row.Add(artist.CreatedAt.ToDateTimeUtc().ToString(Iso8601DateFormat));
            }

            row.Add(ratingDisplay);

            table.AddRow(row.ToArray());
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Found {result.TotalCount:N0} matching artists (showing {artists.Count})[/]");

        if (settings.Delete)
        {
            AnsiConsole.WriteLine();

            var lockedArtists = artists.Where(a => a.IsLocked).ToList();
            if (lockedArtists.Any())
            {
                AnsiConsole.MarkupLine($"[yellow]⚠ {lockedArtists.Count} artist(s) are locked and will be skipped.[/]");
            }

            var deletableArtists = artists.Where(a => !a.IsLocked).ToList();
            if (!deletableArtists.Any())
            {
                AnsiConsole.MarkupLine("[yellow]No artists available for deletion (all are locked).[/]");
                return 0;
            }

            var totalAlbums = deletableArtists.Sum(a => a.AlbumCount);
            var totalSongs = deletableArtists.Sum(a => a.SongCount);

            AnsiConsole.Write(new Rule("[red]⚠️  DESTRUCTIVE OPERATION  ⚠️[/]").RuleStyle("red"));
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine($"[red bold]This will permanently delete:[/]");
            AnsiConsole.MarkupLine($"  • [white]{deletableArtists.Count:N0}[/] artist(s)");
            AnsiConsole.MarkupLine($"  • [white]{totalAlbums:N0}[/] album(s)");
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
                proceed = AnsiConsole.Confirm("[yellow]Are you sure you want to delete these artists?[/]", defaultValue: false);
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
                    var task = ctx.AddTask("[red]Deleting artists...[/]", maxValue: deletableArtists.Count);

                    foreach (var artist in deletableArtists)
                    {
                        try
                        {
                            var deleteResult = await artistService.DeleteAsync([artist.Id], !settings.KeepFiles, cancellationToken);
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
                        task.Description = $"[red]Deleting artists...[/] [dim]({deleted} deleted, {failed} failed)[/]";
                    }

                    task.Description = $"[green]✓ Deletion complete[/] [dim]({deleted} deleted, {failed} failed)[/]";
                });

            AnsiConsole.WriteLine();

            if (deleted > 0)
            {
                AnsiConsole.MarkupLine($"[green]✓ Successfully deleted {deleted:N0} artist(s).[/]");
            }

            if (failed > 0)
            {
                AnsiConsole.MarkupLine($"[red]✗ Failed to delete {failed:N0} artist(s).[/]");
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
            "name" => "Name",
            "albums" => "AlbumCount",
            "songs" => "SongCount",
            "added" => "CreatedAt",
            "rating" => "CalculatedRating",
            _ => null
        };
    }
}
