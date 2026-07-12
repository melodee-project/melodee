using System.Text;
using System.Web;
using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
/// Service for importing M3U/M3U8 playlist files.
/// </summary>
public class PlaylistImportService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ISerializer serializer)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    /// <summary>
    /// Result of a playlist import operation.
    /// </summary>
    public record PlaylistImportResult(
        Guid PlaylistApiKey,
        int TotalEntries,
        int MatchedCount,
        int MissingCount,
        IReadOnlyList<string> MissingReferences);

    /// <summary>
    /// Hints extracted from a playlist entry for song matching.
    /// </summary>
    private record SongMatchHints(
        string Filename,
        string? ArtistFolder,
        string? AlbumFolder);

    /// <summary>
    /// Imports an M3U/M3U8 playlist file for a user.
    /// </summary>
    public async Task<OperationResult<PlaylistImportResult>> ImportPlaylistAsync(
        int userId,
        string originalFileName,
        byte[] fileContent,
        string? playlistName = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.NullOrEmpty(originalFileName, nameof(originalFileName));
        Guard.Against.NullOrEmpty(fileContent, nameof(fileContent));

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        // Determine playlist name from filename if not provided
        var name = playlistName ?? Path.GetFileNameWithoutExtension(originalFileName);

        // Parse the M3U file
        var parsedLines = ParseM3UFile(fileContent);
        if (parsedLines.Count == 0)
        {
            return new OperationResult<PlaylistImportResult>(["No valid playlist entries found in file."])
            {
                Data = null!,
                Type = OperationResponseType.ValidationFailure
            };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Match songs from the parsed entries
        var matchResults = await MatchSongsAsync(scopedContext, parsedLines, cancellationToken).ConfigureAwait(false);

        // Create the playlist
        var playlist = new Playlist
        {
            Name = name,
            UserId = userId,
            IsPublic = false,
            CreatedAt = now,
            Songs = matchResults.MatchedSongs
                .Select((songId, index) => new PlaylistSong
                {
                    SongId = songId,
                    SongApiKey = Guid.NewGuid(), // Will be set correctly via navigation property
                    PlaylistOrder = index
                })
                .ToList()
        };

        scopedContext.Playlists.Add(playlist);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Reload to get Song navigation properties
        await scopedContext.Entry(playlist)
            .Collection(p => p.Songs)
            .Query()
            .Include(ps => ps.Song)
            .LoadAsync(cancellationToken)
            .ConfigureAwait(false);

        // Set the correct SongApiKey values
        foreach (var ps in playlist.Songs)
        {
            ps.SongApiKey = ps.Song.ApiKey;
        }

        // Update playlist metadata
        playlist.SongCount = (short)playlist.Songs.Count;
        playlist.Duration = playlist.Songs.Sum(ps => ps.Song.Duration);

        // Create the uploaded file record
        var uploadedFile = new PlaylistUploadedFile
        {
            UserId = userId,
            OriginalFileName = originalFileName,
            ContentType = originalFileName.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
                ? "audio/x-mpegurl; charset=utf-8"
                : "audio/x-mpegurl",
            Length = fileContent.Length,
            Content = fileContent,
            PlaylistId = playlist.Id,
            CreatedAt = now,
            Items = matchResults.AllItems
        };

        scopedContext.PlaylistUploadedFiles.Add(uploadedFile);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Logger.Information(
            "Imported playlist [{PlaylistName}] for user [{UserId}]: {TotalEntries} entries, {MatchedCount} matched, {MissingCount} missing",
            LogSanitizer.Sanitize(name), userId, parsedLines.Count, matchResults.MatchedSongs.Count, matchResults.MissingCount);

        return new OperationResult<PlaylistImportResult>
        {
            Data = new PlaylistImportResult(
                playlist.ApiKey,
                parsedLines.Count,
                matchResults.MatchedSongs.Count,
                matchResults.MissingCount,
                matchResults.MissingReferences)
        };
    }

    /// <summary>
    /// Parses an M3U/M3U8 file and returns the non-comment, non-empty lines.
    /// </summary>
    private List<string> ParseM3UFile(byte[] fileContent)
    {
        var result = new List<string>();

        try
        {
            // Detect encoding and handle BOM
            var encoding = DetectEncoding(fileContent);
            var content = encoding.GetString(fileContent);

            // Remove BOM if present
            if (content.StartsWith('\uFEFF'))
            {
                content = content[1..];
            }

            var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                // Skip empty lines and comments
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith('#'))
                {
                    continue;
                }

                result.Add(trimmed);
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Error parsing M3U file");
        }

        return result;
    }

    /// <summary>
    /// Detects the encoding of the file content.
    /// </summary>
    private static Encoding DetectEncoding(byte[] fileContent)
    {
        // Check for UTF-8 BOM
        if (fileContent.Length >= 3 &&
            fileContent[0] == 0xEF &&
            fileContent[1] == 0xBB &&
            fileContent[2] == 0xBF)
        {
            return Encoding.UTF8;
        }

        // Default to UTF-8
        return Encoding.UTF8;
    }

    /// <summary>
    /// Matches songs from parsed playlist entries.
    /// </summary>
    private async Task<MatchResults> MatchSongsAsync(
        MelodeeDbContext context,
        List<string> references,
        CancellationToken cancellationToken)
    {
        var matchedSongs = new List<int>();
        var allItems = new List<PlaylistUploadedFileItem>();
        var missingReferences = new List<string>();
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        for (var i = 0; i < references.Count; i++)
        {
            var rawRef = references[i];
            var normalizedRef = NormalizeReference(rawRef);
            var hints = ExtractHints(normalizedRef);

            // Try to match the song
            var songId = await TryMatchSongAsync(context, normalizedRef, hints, cancellationToken).ConfigureAwait(false);

            var item = new PlaylistUploadedFileItem
            {
                SortOrder = i,
                Status = PlaylistUploadedFileItemStatus.Missing,
                RawReference = rawRef,
                NormalizedReference = normalizedRef,
                HintsJson = serializer.Serialize(hints),
                CreatedAt = now,
                LastAttemptAt = now
            };

            if (songId.HasValue)
            {
                item.Status = PlaylistUploadedFileItemStatus.Resolved;
                item.SongId = songId.Value;
                matchedSongs.Add(songId.Value);
            }
            else
            {
                item.Status = PlaylistUploadedFileItemStatus.Missing;
                missingReferences.Add(rawRef);
            }

            allItems.Add(item);
        }

        return new MatchResults(matchedSongs, allItems, missingReferences.Count, missingReferences);
    }

    /// <summary>
    /// Normalizes a playlist reference (URL decode, convert backslashes, trim quotes).
    /// </summary>
    private static string NormalizeReference(string reference)
    {
        var normalized = reference.Trim();

        // Remove surrounding quotes
        if ((normalized.StartsWith('"') && normalized.EndsWith('"')) ||
            (normalized.StartsWith('\'') && normalized.EndsWith('\'')))
        {
            normalized = normalized[1..^1];
        }

        // Convert backslashes to forward slashes
        normalized = normalized.Replace('\\', '/');

        // URL decode (handle %xx sequences)
        try
        {
            normalized = HttpUtility.UrlDecode(normalized);
        }
        catch
        {
            // If URL decode fails, use original
        }

        return normalized.Trim();
    }

    /// <summary>
    /// Extracts hints (filename, artist folder, album folder) from a reference.
    /// </summary>
    private static SongMatchHints ExtractHints(string normalizedReference)
    {
        var filename = Path.GetFileName(normalizedReference);
        var parts = normalizedReference.Split('/', StringSplitOptions.RemoveEmptyEntries);

        string? artistFolder = null;
        string? albumFolder = null;

        // Try to extract folder structure: Artist/Album/Song
        if (parts.Length >= 3)
        {
            albumFolder = parts[^2]; // Second to last part (Album)
            artistFolder = parts[^3]; // Third to last part (Artist)
        }
        else if (parts.Length == 2)
        {
            albumFolder = parts[^2]; // Could be Artist or Album
        }

        return new SongMatchHints(filename, artistFolder, albumFolder);
    }

    /// <summary>
    /// Tries to match a song using various strategies.
    /// </summary>
    private async Task<int?> TryMatchSongAsync(
        MelodeeDbContext context,
        string normalizedReference,
        SongMatchHints hints,
        CancellationToken cancellationToken)
    {
        // Strategy 1: Exact filename match
        var filenameLower = hints.Filename.ToLowerInvariant();
        var exactMatch = await context.Songs
            .AsNoTracking()
            .Where(s => s.FileName.ToLower() == filenameLower)
            .Select(s => s.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (exactMatch > 0)
        {
            return exactMatch;
        }

        // Strategy 2: Filename match with album folder hint
        if (!string.IsNullOrWhiteSpace(hints.AlbumFolder))
        {
            var albumFolderLower = hints.AlbumFolder.ToLowerInvariant();
            var albumMatch = await context.Songs
                .AsNoTracking()
                .Include(s => s.Album)
                .Where(s => s.FileName.ToLower() == filenameLower &&
                           s.Album.Name.ToLower().Contains(albumFolderLower))
                .Select(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (albumMatch > 0)
            {
                return albumMatch;
            }
        }

        // Strategy 3: Filename match with artist folder hint
        if (!string.IsNullOrWhiteSpace(hints.ArtistFolder))
        {
            var artistFolderLower = hints.ArtistFolder.ToLowerInvariant();
            var artistMatch = await context.Songs
                .AsNoTracking()
                .Include(s => s.Album)
                .ThenInclude(a => a.Artist)
                .Where(s => s.FileName.ToLower() == filenameLower &&
                           s.Album.Artist.Name.ToLower().Contains(artistFolderLower))
                .Select(s => s.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (artistMatch > 0)
            {
                return artistMatch;
            }
        }

        // No match found
        return null;
    }

    /// <summary>
    /// Results from matching songs.
    /// </summary>
    private record MatchResults(
        List<int> MatchedSongs,
        List<PlaylistUploadedFileItem> AllItems,
        int MissingCount,
        IReadOnlyList<string> MissingReferences);
}
