using System.Diagnostics;
using System.Text.Json;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Cli.Command;

/// <summary>
///     Validates library integrity by checking that:
///     1. All albums/songs in the database have corresponding files on disk
///     2. All album directories on disk are represented in the database
/// </summary>
public class LibraryValidateCommand : CommandBase<LibraryValidateSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, LibraryValidateSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var startTime = Stopwatch.GetTimestamp();

        var libraryService = scope.ServiceProvider.GetRequiredService<LibraryService>();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        var serializer = scope.ServiceProvider.GetRequiredService<ISerializer>();

        var libraries = await libraryService.ListAsync(new MelodeeModels.PagedRequest { PageSize = short.MaxValue }, cancellationToken);
        var library = libraries.Data.FirstOrDefault(x => x.Name.ToNormalizedString() == settings.LibraryName?.ToNormalizedString());

        if (library == null)
        {
            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Library '{settings.LibraryName}' not found.");
            }
            return 1;
        }

        if (library.TypeValue != LibraryType.Storage)
        {
            if (!settings.Json)
            {
                AnsiConsole.MarkupLine($"[red]Error:[/] Library validation requires a Storage library. '{settings.LibraryName}' is type '{library.TypeValue}'.");
            }
            return 1;
        }

        var result = new ValidationResult();

        if (!settings.Json)
        {
            AnsiConsole.Write(new Panel(
                new Grid()
                    .AddColumn(new GridColumn().NoWrap().PadRight(4))
                    .AddColumn()
                    .AddRow("[b]Library[/]", library.Name.EscapeMarkup())
                    .AddRow("[b]Path[/]", library.Path.EscapeMarkup())
                    .AddRow("[b]Fix Mode[/]", settings.Fix ? "[yellow]Yes (will remove orphaned records)[/]" : "[dim]No[/]"))
                .Header("[yellow]Library Validation[/]")
                .RoundedBorder()
                .BorderColor(Color.Blue));

            AnsiConsole.WriteLine();
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (!settings.Json)
        {
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
                    var dbTask = ctx.AddTask("[green]Validating database records against disk[/]");
                    dbTask.IsIndeterminate = true;
                    dbTask.StartTask();

                    await ValidateDatabaseAgainstDiskAsync(dbContext, library, result, settings.Fix, settings.Verbose, cancellationToken);

                    dbTask.Description = "[green]✓ Database validation complete[/]";
                    dbTask.Value = 100;
                    dbTask.MaxValue = 100;
                    dbTask.IsIndeterminate = false;
                    dbTask.StopTask();

                    var diskTask = ctx.AddTask("[green]Validating disk directories against database[/]");
                    diskTask.IsIndeterminate = true;
                    diskTask.StartTask();

                    await ValidateDiskAgainstDatabaseAsync(dbContext, library, result, settings.Verbose, serializer, cancellationToken);

                    diskTask.Description = "[green]✓ Disk validation complete[/]";
                    diskTask.Value = 100;
                    diskTask.MaxValue = 100;
                    diskTask.IsIndeterminate = false;
                    diskTask.StopTask();
                });
        }
        else
        {
            await ValidateDatabaseAgainstDiskAsync(dbContext, library, result, settings.Fix, settings.Verbose, cancellationToken);
            await ValidateDiskAgainstDatabaseAsync(dbContext, library, result, settings.Verbose, serializer, cancellationToken);
        }

        var elapsed = Stopwatch.GetElapsedTime(startTime);

        if (settings.Json)
        {
            var jsonOutput = new
            {
                success = result.IsValid,
                libraryName = library.Name,
                libraryPath = library.Path,
                durationSeconds = elapsed.TotalSeconds,
                summary = new
                {
                    artistsChecked = result.ArtistsChecked,
                    albumsChecked = result.AlbumsChecked,
                    songsChecked = result.SongsChecked,
                    directoriesScanned = result.DirectoriesScanned
                },
                issues = new
                {
                    orphanedArtists = result.OrphanedArtists.Select(a => new { a.Id, a.Name, a.Directory }).ToArray(),
                    orphanedAlbums = result.OrphanedAlbums.Select(a => new { a.Id, a.Name, a.Directory, ArtistName = a.Artist?.Name }).ToArray(),
                    missingSongs = result.MissingSongs.Select(s => new { s.Id, s.Title, s.FileName, AlbumName = s.Album?.Name }).ToArray(),
                    unregisteredDirectories = result.UnregisteredDirectories.ToArray(),
                    directoriesFlaggedForDeletion = result.DirectoriesFlaggedForDeletion.Select(d => new { d.Path, Status = d.Status.ToString() }).ToArray()
                },
                fixed_ = settings.Fix ? new
                {
                    artistsRemoved = result.ArtistsRemoved,
                    albumsRemoved = result.AlbumsRemoved,
                    songsRemoved = result.SongsRemoved
                } : null
            };

            Console.WriteLine(JsonSerializer.Serialize(jsonOutput, new JsonSerializerOptions { WriteIndented = true }));
            return result.IsValid ? 0 : 1;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Rule($"[green]Validation completed in {elapsed:mm\\:ss\\.fff}[/]") { Justification = Justify.Left });
        AnsiConsole.WriteLine();

        var summaryTable = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(Color.Blue)
            .Title("[yellow]Summary[/]");

        summaryTable.AddColumn("Category");
        summaryTable.AddColumn(new TableColumn("Checked").RightAligned());
        summaryTable.AddColumn(new TableColumn("Issues").RightAligned());

        summaryTable.AddRow("Artists", result.ArtistsChecked.ToString("N0"), FormatIssueCount(result.OrphanedArtists.Count));
        summaryTable.AddRow("Albums", result.AlbumsChecked.ToString("N0"), FormatIssueCount(result.OrphanedAlbums.Count));
        summaryTable.AddRow("Songs", result.SongsChecked.ToString("N0"), FormatIssueCount(result.MissingSongs.Count));
        summaryTable.AddRow("Disk Directories", result.DirectoriesScanned.ToString("N0"), FormatIssueCount(result.UnregisteredDirectories.Count));
        summaryTable.AddRow("Invalid Directories", result.DirectoriesFlaggedForDeletion.Count.ToString("N0"), FormatIssueCount(result.DirectoriesFlaggedForDeletion.Count));

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        if (result.OrphanedArtists.Count > 0)
        {
            var artistTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Red)
                .Title("[red]Orphaned Artists (in DB, not on disk)[/]");

            artistTable.AddColumn("ID");
            artistTable.AddColumn("Name");
            artistTable.AddColumn("Directory");

            foreach (var artist in result.OrphanedArtists.Take(25))
            {
                artistTable.AddRow(
                    artist.Id.ToString(),
                    artist.Name.EscapeMarkup(),
                    (artist.Directory ?? "[none]").EscapeMarkup());
            }

            if (result.OrphanedArtists.Count > 25)
            {
                artistTable.AddRow($"... and {result.OrphanedArtists.Count - 25} more", "", "");
            }

            AnsiConsole.Write(artistTable);
            AnsiConsole.WriteLine();
        }

        if (result.OrphanedAlbums.Count > 0)
        {
            var albumTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Red)
                .Title("[red]Orphaned Albums (in DB, not on disk)[/]");

            albumTable.AddColumn("ID");
            albumTable.AddColumn("Artist");
            albumTable.AddColumn("Album");
            albumTable.AddColumn("Directory");

            foreach (var album in result.OrphanedAlbums.Take(25))
            {
                albumTable.AddRow(
                    album.Id.ToString(),
                    (album.Artist?.Name ?? "[unknown]").EscapeMarkup(),
                    album.Name.EscapeMarkup(),
                    (album.Directory ?? "[none]").EscapeMarkup());
            }

            if (result.OrphanedAlbums.Count > 25)
            {
                albumTable.AddRow($"... and {result.OrphanedAlbums.Count - 25} more", "", "", "");
            }

            AnsiConsole.Write(albumTable);
            AnsiConsole.WriteLine();
        }

        if (result.MissingSongs.Count > 0)
        {
            var songTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Red)
                .Title("[red]Missing Songs (in DB, file not on disk)[/]");

            songTable.AddColumn("ID");
            songTable.AddColumn("Album");
            songTable.AddColumn("Title");
            songTable.AddColumn("FileName");

            foreach (var song in result.MissingSongs.Take(25))
            {
                songTable.AddRow(
                    song.Id.ToString(),
                    (song.Album?.Name ?? "[unknown]").EscapeMarkup(),
                    song.Title.EscapeMarkup(),
                    song.FileName.EscapeMarkup());
            }

            if (result.MissingSongs.Count > 25)
            {
                songTable.AddRow($"... and {result.MissingSongs.Count - 25} more", "", "", "");
            }

            AnsiConsole.Write(songTable);
            AnsiConsole.WriteLine();
        }

        if (result.UnregisteredDirectories.Count > 0)
        {
            var dirTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Yellow)
                .Title("[yellow]Unregistered Directories (on disk, not in DB)[/]");

            dirTable.AddColumn("Directory Path");

            foreach (var dir in result.UnregisteredDirectories.Take(25))
            {
                dirTable.AddRow(dir.EscapeMarkup());
            }

            if (result.UnregisteredDirectories.Count > 25)
            {
                dirTable.AddRow($"... and {result.UnregisteredDirectories.Count - 25} more");
            }

            AnsiConsole.Write(dirTable);
            AnsiConsole.WriteLine();
        }

        if (result.DirectoriesFlaggedForDeletion.Count > 0)
        {
            var deletionTable = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(Color.Red)
                .Title("[red]Invalid Directories (on disk, flagged for deletion)[/]");

            deletionTable.AddColumn("Directory Path");
            deletionTable.AddColumn("Detected Status");

            foreach (var dir in result.DirectoriesFlaggedForDeletion.Take(25))
            {
                deletionTable.AddRow(dir.Path.EscapeMarkup(), dir.Status.ToString().EscapeMarkup());
            }

            if (result.DirectoriesFlaggedForDeletion.Count > 25)
            {
                deletionTable.AddRow($"... and {result.DirectoriesFlaggedForDeletion.Count - 25} more", "");
            }

            AnsiConsole.Write(deletionTable);
            AnsiConsole.WriteLine();
        }

        if (settings.Fix && (result.ArtistsRemoved > 0 || result.AlbumsRemoved > 0 || result.SongsRemoved > 0))
        {
            AnsiConsole.Write(new Panel(
                $"[yellow]Fixed:[/] Removed {result.ArtistsRemoved} artists, {result.AlbumsRemoved} albums, {result.SongsRemoved} songs")
                .Header("[bold yellow]Fix Results[/]")
                .RoundedBorder()
                .BorderColor(Color.Yellow));
            AnsiConsole.WriteLine();
        }

        if (result.IsValid)
        {
            AnsiConsole.MarkupLine("[green]✓ Library validation passed - no issues found.[/]");
        }
        else
        {
            var totalIssues = result.OrphanedArtists.Count + result.OrphanedAlbums.Count +
                              result.MissingSongs.Count + result.UnregisteredDirectories.Count +
                              result.DirectoriesFlaggedForDeletion.Count;
            AnsiConsole.MarkupLine($"[red]✗ Library validation failed - {totalIssues} issue(s) found.[/]");

            if (!settings.Fix && (result.OrphanedArtists.Count > 0 || result.OrphanedAlbums.Count > 0 || result.MissingSongs.Count > 0))
            {
                AnsiConsole.MarkupLine("[dim]Tip: Use --fix to remove orphaned database records.[/]");
            }

            if (result.UnregisteredDirectories.Count > 0)
            {
                AnsiConsole.MarkupLine("[dim]Tip: Run 'mcli library scan' to add unregistered directories to the database.[/]");
            }
        }

        return result.IsValid ? 0 : 1;
    }

    private static string FormatIssueCount(int count)
    {
        return count == 0 ? "[green]0[/]" : $"[red]{count:N0}[/]";
    }

    private static async Task ValidateDatabaseAgainstDiskAsync(
        MelodeeDbContext dbContext,
        Library library,
        ValidationResult result,
        bool fix,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var artists = await dbContext.Artists
            .Include(a => a.Albums)
                .ThenInclude(al => al.Songs)
            .Where(a => a.LibraryId == library.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        result.ArtistsChecked = artists.Count;

        foreach (var artist in artists)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var artistPath = Path.Combine(library.Path, artist.Directory?.TrimStart('/').TrimEnd('/') ?? string.Empty);

            if (!Directory.Exists(artistPath))
            {
                result.OrphanedArtists.Add(artist);
                continue;
            }

            foreach (var album in artist.Albums)
            {
                result.AlbumsChecked++;

                var albumPath = Path.Combine(artistPath, album.Directory?.TrimEnd('/') ?? string.Empty);

                if (!Directory.Exists(albumPath))
                {
                    result.OrphanedAlbums.Add(album);
                    continue;
                }

                foreach (var song in album.Songs)
                {
                    result.SongsChecked++;

                    var songPath = Path.Combine(albumPath, song.FileName);

                    if (!File.Exists(songPath))
                    {
                        result.MissingSongs.Add(song);
                    }
                }
            }
        }

        if (fix && (result.OrphanedArtists.Count > 0 || result.OrphanedAlbums.Count > 0 || result.MissingSongs.Count > 0))
        {
            await using var writeContext = dbContext;

            if (result.MissingSongs.Count > 0)
            {
                var songIds = result.MissingSongs.Select(s => s.Id).ToArray();
                result.SongsRemoved = await writeContext.Songs
                    .Where(s => songIds.Contains(s.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            if (result.OrphanedAlbums.Count > 0)
            {
                var albumIds = result.OrphanedAlbums.Select(a => a.Id).ToArray();
                result.AlbumsRemoved = await writeContext.Albums
                    .Where(a => albumIds.Contains(a.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }

            if (result.OrphanedArtists.Count > 0)
            {
                var artistIds = result.OrphanedArtists.Select(a => a.Id).ToArray();
                result.ArtistsRemoved = await writeContext.Artists
                    .Where(a => artistIds.Contains(a.Id))
                    .ExecuteDeleteAsync(cancellationToken);
            }
        }
    }

    private static async Task ValidateDiskAgainstDatabaseAsync(
        MelodeeDbContext dbContext,
        Library library,
        ValidationResult result,
        bool verbose,
        ISerializer serializer,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(library.Path))
        {
            return;
        }

        var knownAlbumDirectories = await dbContext.Albums
            .Include(a => a.Artist)
            .Where(a => a.Artist.LibraryId == library.Id)
            .Select(a => new { ArtistDir = a.Artist.Directory, AlbumDir = a.Directory })
            .ToListAsync(cancellationToken);

        var knownPaths = knownAlbumDirectories
            .Where(x => x.ArtistDir != null && x.AlbumDir != null)
            .Select(x => Path.Combine(library.Path, x.ArtistDir!.TrimStart('/').TrimEnd('/'), x.AlbumDir!.TrimEnd('/')))
            .Select(p => p.Replace('\\', '/').ToLowerInvariant())
            .ToHashSet();

        var topLevelDirs = Directory.GetDirectories(library.Path, "*", SearchOption.TopDirectoryOnly);

        foreach (var letterDir in topLevelDirs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var letterDirInfo = new DirectoryInfo(letterDir);

            if (letterDirInfo.Name.Length != 1)
            {
                continue;
            }

            var twoLetterDirs = Directory.GetDirectories(letterDir, "*", SearchOption.TopDirectoryOnly);

            foreach (var twoLetterDir in twoLetterDirs)
            {
                var twoLetterDirInfo = new DirectoryInfo(twoLetterDir);

                if (twoLetterDirInfo.Name.Length != 2)
                {
                    continue;
                }

                var artistDirs = Directory.GetDirectories(twoLetterDir, "*", SearchOption.TopDirectoryOnly);

                foreach (var artistDir in artistDirs)
                {
                    var albumDirs = Directory.GetDirectories(artistDir, "*", SearchOption.TopDirectoryOnly);

                    foreach (var albumDir in albumDirs)
                    {
                        result.DirectoriesScanned++;

                        var hasMediaFiles = Directory.GetFiles(albumDir)
                            .Any(f => FileHelper.IsFileMediaType(Path.GetExtension(f)));

                        if (!hasMediaFiles)
                        {
                            continue;
                        }

                        var normalizedPath = albumDir.Replace('\\', '/').ToLowerInvariant();

                        if (!knownPaths.Contains(normalizedPath))
                        {
                            var albumStatus = await TryGetAlbumStatusAsync(albumDir, serializer, cancellationToken);

                            if (albumStatus.HasValue && albumStatus.Value != AlbumStatus.Ok)
                            {
                                result.DirectoriesFlaggedForDeletion.Add(new InvalidDirectory(albumDir, albumStatus.Value));
                                continue;
                            }

                            result.UnregisteredDirectories.Add(albumDir);
                        }
                    }
                }
            }
        }
    }

    private static async Task<AlbumStatus?> TryGetAlbumStatusAsync(
        string albumDir,
        ISerializer serializer,
        CancellationToken cancellationToken)
    {
        var melodeeFile = Directory.GetFiles(albumDir, $"*{MelodeeModels.Album.JsonFileName}", SearchOption.TopDirectoryOnly)
            .FirstOrDefault()
                        ?? Directory.GetFiles(albumDir, $"*{MelodeeModels.Album.JsonFileName}", SearchOption.AllDirectories)
                .FirstOrDefault();

        if (melodeeFile == null)
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(melodeeFile);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var statusProperty = document.RootElement
                .EnumerateObject()
                .FirstOrDefault(p => string.Equals(p.Name, "status", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(statusProperty.Name))
            {
                var rawStatus = statusProperty.Value.GetString();
                if (!string.IsNullOrWhiteSpace(rawStatus) &&
                    Enum.TryParse<AlbumStatus>(rawStatus, true, out var parsedStatus))
                {
                    return parsedStatus;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.WriteLine($"Unable to parse melodee.json status for [{albumDir}]: {ex.Message}");
        }

        try
        {
            var album = await MelodeeModels.Album.DeserializeAndInitializeAlbumAsync(serializer, melodeeFile, cancellationToken)
                .ConfigureAwait(false);
            return album?.Status;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.WriteLine($"Unable to deserialize melodee.json for [{albumDir}]: {ex.Message}");
            return null;
        }
    }

    private sealed class ValidationResult
    {
        public int ArtistsChecked { get; set; }
        public int AlbumsChecked { get; set; }
        public int SongsChecked { get; set; }
        public int DirectoriesScanned { get; set; }

        public List<Artist> OrphanedArtists { get; } = [];
        public List<Album> OrphanedAlbums { get; } = [];
        public List<Song> MissingSongs { get; } = [];
        public List<string> UnregisteredDirectories { get; } = [];
        public List<InvalidDirectory> DirectoriesFlaggedForDeletion { get; } = [];

        public int ArtistsRemoved { get; set; }
        public int AlbumsRemoved { get; set; }
        public int SongsRemoved { get; set; }

        public bool IsValid => OrphanedArtists.Count == 0 &&
                               OrphanedAlbums.Count == 0 &&
                               MissingSongs.Count == 0 &&
                               UnregisteredDirectories.Count == 0 &&
                               DirectoriesFlaggedForDeletion.Count == 0;
    }

    private sealed record InvalidDirectory(string Path, AlbumStatus Status);
}
