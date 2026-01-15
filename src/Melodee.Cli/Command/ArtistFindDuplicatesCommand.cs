using System.Text.Json;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Services;
using Melodee.Common.Services.Models.ArtistDuplicate;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
/// Find potential duplicate artists in the database based on external IDs, name similarity, and album overlap.
/// </summary>
public class ArtistFindDuplicatesCommand : CommandBase<ArtistFindDuplicatesSettings>
{
    public override async Task<int> ExecuteAsync(
        CommandContext context,
        ArtistFindDuplicatesSettings settings,
        CancellationToken cancellationToken)
    {
        if (settings.MinScore is < 0 or > 1)
        {
            AnsiConsole.MarkupLine("[red]Error: --min-score must be between 0.0 and 1.0[/]");
            return 1;
        }

        using var scope = CreateServiceProvider().CreateScope();
        var duplicateFinder = scope.ServiceProvider.GetRequiredService<ArtistDuplicateFinder>();

        var criteria = new ArtistDuplicateSearchCriteria(
            MinScore: settings.MinScore,
            Limit: settings.Limit,
            Source: settings.Source,
            ArtistId: settings.ArtistId,
            IncludeLowConfidence: settings.IncludeLowConfidence);

        IReadOnlyList<ArtistDuplicateGroup> groups = [];

        AnsiConsole.MarkupLine("[blue]Searching for duplicate artists...[/]");
        AnsiConsole.MarkupLine($"[grey]  Min score: {criteria.MinScore:P0}, Limit: {criteria.Limit?.ToString() ?? "unlimited"}[/]");

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("green bold"))
            .StartAsync("Querying database for potential duplicates...", async ctx =>
            {
                groups = await duplicateFinder.FindDuplicatesAsync(criteria, cancellationToken);
                ctx.Status($"Found {groups.Count} duplicate group(s)");
            });

        AnsiConsole.MarkupLine($"[green]Found {groups.Count} potential duplicate group(s)[/]");

        if (groups.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No potential duplicate artists found.[/]");
            return 0;
        }

        if (settings.JsonOutput)
        {
            OutputJson(groups);
        }
        else
        {
            OutputTable(groups);
        }

        if (settings.Merge)
        {
            var artistService = scope.ServiceProvider.GetRequiredService<ArtistService>();
            await MergeDuplicatesAsync(groups, artistService, cancellationToken);
        }

        return 0;
    }

    private static async Task MergeDuplicatesAsync(
        IReadOnlyList<ArtistDuplicateGroup> groups,
        ArtistService artistService,
        CancellationToken cancellationToken)
    {
        if (groups.Count == 0)
        {
            return;
        }

        var confirmed = AnsiConsole.Confirm(
            $"[yellow]Are you sure you want to merge {groups.Count} duplicate group(s)?[/]",
            defaultValue: false);

        if (!confirmed)
        {
            AnsiConsole.MarkupLine("[grey]Merge operation cancelled.[/]");
            return;
        }

        var successCount = 0;
        var failCount = 0;

        await AnsiConsole.Progress()
            .Columns(
                new TaskDescriptionColumn(),
                new ProgressBarColumn(),
                new PercentageColumn(),
                new SpinnerColumn())
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Merging artists...[/]", maxValue: groups.Count);

                foreach (var group in groups)
                {
                    var primaryArtistId = group.SuggestedPrimaryArtistId;
                    var artistsToMerge = group.Artists
                        .Where(a => a.ArtistId != primaryArtistId)
                        .Select(a => a.ArtistId)
                        .ToArray();

                    if (artistsToMerge.Length == 0)
                    {
                        task.Increment(1);
                        continue;
                    }

                    var primaryArtist = group.Artists.FirstOrDefault(a => a.ArtistId == primaryArtistId);
                    var primaryName = primaryArtist?.Name ?? $"ID:{primaryArtistId}";

                    try
                    {
                        var result = await artistService.MergeArtistsAsync(
                            primaryArtistId,
                            artistsToMerge,
                            cancellationToken);

                        if (result.IsSuccess)
                        {
                            successCount++;
                            AnsiConsole.MarkupLine($"  [green]✓[/] Merged {artistsToMerge.Length} artist(s) into [cyan]{primaryName.EscapeMarkup()}[/]");
                        }
                        else
                        {
                            failCount++;
                            AnsiConsole.MarkupLine($"  [red]✗[/] Failed to merge into [cyan]{primaryName.EscapeMarkup()}[/]: {string.Join(", ", result.Messages ?? [])}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        var errorMessage = ex.InnerException?.Message ?? ex.Message;
                        AnsiConsole.MarkupLine($"  [red]✗[/] Error merging into [cyan]{primaryName.EscapeMarkup()}[/]: {errorMessage.EscapeMarkup()}");
                    }

                    task.Increment(1);
                }
            });

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Merge complete:[/] [green]{successCount} succeeded[/], [red]{failCount} failed[/]");
    }

    private static void OutputJson(IReadOnlyList<ArtistDuplicateGroup> groups)
    {
        var output = new
        {
            groups = groups.Select(g => new
            {
                groupId = g.GroupId,
                maxScore = g.MaxScore,
                suggestedPrimaryArtistId = g.SuggestedPrimaryArtistId,
                artists = g.Artists.Select(a => new
                {
                    artistId = a.ArtistId,
                    apiKey = a.ApiKey,
                    name = a.Name,
                    sortName = a.SortName,
                    externalIds = a.ExternalIds,
                    albumCount = a.AlbumCount,
                    songCount = a.SongCount,
                    isPrimary = a.ArtistId == g.SuggestedPrimaryArtistId
                }),
                pairs = g.Pairs.Select(p => new
                {
                    leftArtistId = p.LeftArtistId,
                    rightArtistId = p.RightArtistId,
                    score = p.Score,
                    reasons = p.Reasons
                })
            })
        };

        Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }

    private static void OutputTable(IReadOnlyList<ArtistDuplicateGroup> groups)
    {
        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn(new TableColumn("[bold]Group[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Score[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Keep (←)[/]"));
        table.AddColumn(new TableColumn("[bold]ID[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Merge (→)[/]"));
        table.AddColumn(new TableColumn("[bold]ID[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Shared IDs[/]"));
        table.AddColumn(new TableColumn("[bold]Reasons[/]"));

        foreach (var group in groups)
        {
            var isFirst = true;
            var primaryArtist = group.Artists.FirstOrDefault(a => a.ArtistId == group.SuggestedPrimaryArtistId);

            foreach (var pair in group.Pairs)
            {
                var artistA = group.Artists.FirstOrDefault(a => a.ArtistId == pair.LeftArtistId);
                var artistB = group.Artists.FirstOrDefault(a => a.ArtistId == pair.RightArtistId);

                if (artistA == null || artistB == null)
                {
                    continue;
                }

                var keepArtist = artistA.ArtistId == group.SuggestedPrimaryArtistId ? artistA : artistB;
                var mergeArtist = artistA.ArtistId == group.SuggestedPrimaryArtistId ? artistB : artistA;

                var sharedIds = GetSharedExternalIds(artistA.ExternalIds, artistB.ExternalIds);
                var scoreColor = pair.Score switch
                {
                    >= 0.9 => "green",
                    >= 0.75 => "yellow",
                    _ => "grey"
                };

                var reasonsDisplay = FormatReasons(pair.Reasons);

                table.AddRow(
                    isFirst ? group.GroupId : string.Empty,
                    $"[{scoreColor}]{pair.Score:F2}[/]",
                    $"[green]{keepArtist.Name.EscapeMarkup()}[/]",
                    keepArtist.ArtistId.ToString(),
                    $"[yellow]{mergeArtist.Name.EscapeMarkup()}[/]",
                    mergeArtist.ArtistId.ToString(),
                    sharedIds,
                    reasonsDisplay
                );

                isFirst = false;
            }

            table.AddEmptyRow();
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();

        var totalPairs = groups.Sum(g => g.Pairs.Count);
        var totalArtists = groups.SelectMany(g => g.Artists).Select(a => a.ArtistId).Distinct().Count();

        AnsiConsole.MarkupLine($"[grey]Found {groups.Count} duplicate group(s) with {totalPairs} pair(s) involving {totalArtists} artist(s)[/]");

        var highConfidence = groups.Count(g => g.MaxScore >= 0.9);
        var mediumConfidence = groups.Count(g => g.MaxScore is >= 0.75 and < 0.9);
        var lowConfidence = groups.Count(g => g.MaxScore < 0.75);

        if (highConfidence > 0)
        {
            AnsiConsole.MarkupLine($"  [green]High confidence (≥0.9):[/] {highConfidence}");
        }

        if (mediumConfidence > 0)
        {
            AnsiConsole.MarkupLine($"  [yellow]Medium confidence (0.75-0.9):[/] {mediumConfidence}");
        }

        if (lowConfidence > 0)
        {
            AnsiConsole.MarkupLine($"  [grey]Low confidence (<0.75):[/] {lowConfidence}");
        }
    }

    private static string GetSharedExternalIds(
        IReadOnlyDictionary<string, string> idsA,
        IReadOnlyDictionary<string, string> idsB)
    {
        var shared = new List<string>();

        foreach (var (key, valueA) in idsA)
        {
            if (idsB.TryGetValue(key, out var valueB) &&
                string.Equals(valueA, valueB, StringComparison.OrdinalIgnoreCase))
            {
                shared.Add(key);
            }
        }

        return shared.Count > 0 ? string.Join(", ", shared) : "-";
    }

    private static string FormatReasons(IReadOnlyCollection<string> reasons)
    {
        var display = new List<string>();

        foreach (var reason in reasons.Take(3))
        {
            var shortReason = reason switch
            {
                ArtistDuplicateMatchReason.SharedSpotifyId => "Spotify",
                ArtistDuplicateMatchReason.SharedMusicBrainzId => "MBrainz",
                ArtistDuplicateMatchReason.SharedDiscogsId => "Discogs",
                ArtistDuplicateMatchReason.SharedItunesId => "iTunes",
                ArtistDuplicateMatchReason.SharedDeezerId => "Deezer",
                ArtistDuplicateMatchReason.SharedLastFmId => "Last.fm",
                ArtistDuplicateMatchReason.SharedAmgId => "AMG",
                ArtistDuplicateMatchReason.SharedWikiDataId => "WikiData",
                ArtistDuplicateMatchReason.MultipleSharedExternalIds => "Multi-ID",
                ArtistDuplicateMatchReason.ExactNormalizedNameMatch => "ExactName",
                ArtistDuplicateMatchReason.NameFirstLastReversal => "NameFlip",
                ArtistDuplicateMatchReason.HighNameSimilarity => "NameSim",
                ArtistDuplicateMatchReason.HighTokenSimilarity => "TokenSim",
                ArtistDuplicateMatchReason.SharedAlbums => "Albums",
                ArtistDuplicateMatchReason.HighAlbumOverlap => "AlbumOverlap",
                _ => reason.Length > 10 ? reason[..10] : reason
            };
            display.Add(shortReason);
        }

        if (reasons.Count > 3)
        {
            display.Add($"+{reasons.Count - 3}");
        }

        return string.Join(", ", display);
    }
}
