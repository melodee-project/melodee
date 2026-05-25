using System.Text.RegularExpressions;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Imaging;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Find albums with image issues (missing, invalid, or incorrectly numbered).
/// </summary>
public class AlbumImageIssuesCommand : CommandBase<AlbumImageIssuesSettings>
{
    private static readonly Regex ImageNameRegex = new(@"^i-(\d+)-(\w+)\.jpg$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    protected override async Task<int> ExecuteAsync(CommandContext context, AlbumImageIssuesSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var imageProcessor = scope.ServiceProvider.GetRequiredService<IImageProcessor>();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        var configFactory = scope.ServiceProvider.GetRequiredService<IMelodeeConfigurationFactory>();

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var configuration = await configFactory.GetConfigurationAsync(cancellationToken);
        var imageValidator = new ImageValidator(imageProcessor, configuration);

        var albums = await dbContext.Albums
            .Include(a => a.Artist)
            .ThenInclude(a => a.Library)
            .AsNoTracking()
            .Take(settings.Limit * 3)
            .ToListAsync(cancellationToken);

        var issues = new List<AlbumImageIssue>();

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Scanning albums for image issues...[/]", maxValue: albums.Count);

                foreach (var album in albums)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    var albumIssues = await CheckAlbumImages(album, imageValidator, configuration, settings, cancellationToken);
                    issues.AddRange(albumIssues);

                    task.Increment(1);

                    if (issues.Count >= settings.Limit) break;
                }
            });

        issues = issues.Take(settings.Limit).ToList();

        if (issues.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]✓ No image issues found.[/]");
            return 0;
        }

        if (settings.ReturnRaw)
        {
            var jsonOutput = issues.Select(i => new
            {
                i.AlbumId,
                i.AlbumName,
                i.ArtistName,
                i.Directory,
                i.IssueType,
                i.Details
            });
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonOutput, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var groupedIssues = issues.GroupBy(i => i.IssueType).OrderBy(g => g.Key);

        foreach (var group in groupedIssues)
        {
            var color = group.Key switch
            {
                "Missing" => "red",
                "Invalid" => "yellow",
                "Misnumbered" => "cyan",
                _ => "grey"
            };

            var table = new Table();
            table.Border = TableBorder.Rounded;
            table.Title = new TableTitle($"[bold {color}]{group.Key} Images ({group.Count()})[/]");
            table.AddColumn(new TableColumn("[bold]ID[/]").RightAligned());
            table.AddColumn(new TableColumn("[bold]Artist[/]"));
            table.AddColumn(new TableColumn("[bold]Album[/]"));
            table.AddColumn(new TableColumn("[bold]Details[/]"));

            foreach (var issue in group.Take(50))
            {
                table.AddRow(
                    issue.AlbumId.ToString(),
                    issue.ArtistName.EscapeMarkup().Length > 25
                        ? issue.ArtistName.EscapeMarkup()[..22] + "..."
                        : issue.ArtistName.EscapeMarkup(),
                    issue.AlbumName.EscapeMarkup().Length > 35
                        ? issue.AlbumName.EscapeMarkup()[..32] + "..."
                        : issue.AlbumName.EscapeMarkup(),
                    issue.Details.EscapeMarkup()
                );
            }

            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        AnsiConsole.MarkupLine($"[grey]Found {issues.Count} albums with image issues[/]");

        return 0;
    }

    private async Task<List<AlbumImageIssue>> CheckAlbumImages(
        Album album,
        ImageValidator imageValidator,
        IMelodeeConfiguration configuration,
        AlbumImageIssuesSettings settings,
        CancellationToken cancellationToken)
    {
        var issues = new List<AlbumImageIssue>();
        var albumDir = album.ToFileSystemDirectoryInfo();

        if (!Directory.Exists(albumDir.Path))
        {
            return issues;
        }

        var imageFiles = Directory.GetFiles(albumDir.Path, "*.jpg", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(albumDir.Path, "*.png", SearchOption.TopDirectoryOnly))
            .Concat(Directory.GetFiles(albumDir.Path, "*.webp", SearchOption.TopDirectoryOnly))
            .Select(f => new FileInfo(f))
            .Where(f => f.Name.StartsWith("i-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Name)
            .ToList();

        // Check for missing images
        if (settings.IncludeMissing && imageFiles.Count == 0)
        {
            issues.Add(new AlbumImageIssue
            {
                AlbumId = album.Id,
                AlbumName = album.Name,
                ArtistName = album.Artist.Name,
                Directory = albumDir.Path,
                IssueType = "Missing",
                Details = "No images found"
            });
            return issues;
        }

        // Check for invalid images
        if (settings.IncludeInvalid)
        {
            foreach (var imageFile in imageFiles)
            {
                var validationResult = await imageValidator.ValidateImage(
                    imageFile,
                    PictureIdentifier.Front,
                    cancellationToken);

                if (!validationResult.IsSuccess || !validationResult.Data.IsValid)
                {
                    var messages = validationResult.Data?.Messages?.Select(m => m.Message) ?? ["Validation failed"];
                    issues.Add(new AlbumImageIssue
                    {
                        AlbumId = album.Id,
                        AlbumName = album.Name,
                        ArtistName = album.Artist.Name,
                        Directory = albumDir.Path,
                        IssueType = "Invalid",
                        Details = $"{imageFile.Name}: {string.Join("; ", messages)}"
                    });
                }
            }
        }

        // Check for misnumbered images
        if (settings.IncludeMisnumbered && imageFiles.Count != 0)
        {
            var expectedNumber = 1;
            var misnumberedFiles = new List<string>();

            foreach (var imageFile in imageFiles)
            {
                var match = ImageNameRegex.Match(imageFile.Name);
                if (!match.Success)
                {
                    misnumberedFiles.Add($"{imageFile.Name} (invalid format)");
                    continue;
                }

                var actualNumber = SafeParser.ToNumber<int>(match.Groups[1].Value);
                if (actualNumber != expectedNumber)
                {
                    misnumberedFiles.Add($"{imageFile.Name} (expected {expectedNumber:D2})");
                }
                expectedNumber++;
            }

            if (misnumberedFiles.Count != 0)
            {
                issues.Add(new AlbumImageIssue
                {
                    AlbumId = album.Id,
                    AlbumName = album.Name,
                    ArtistName = album.Artist.Name,
                    Directory = albumDir.Path,
                    IssueType = "Misnumbered",
                    Details = string.Join(", ", misnumberedFiles.Take(3)) + (misnumberedFiles.Count > 3 ? $" (+{misnumberedFiles.Count - 3} more)" : "")
                });
            }
        }

        return issues;
    }

    private sealed class AlbumImageIssue
    {
        public int AlbumId { get; init; }
        public string AlbumName { get; init; } = string.Empty;
        public string ArtistName { get; init; } = string.Empty;
        public string Directory { get; init; } = string.Empty;
        public string IssueType { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
    }
}
