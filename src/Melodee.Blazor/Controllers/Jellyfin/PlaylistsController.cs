using Melodee.Blazor.Controllers.Jellyfin.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Blazor.Controllers.Jellyfin;

/// <summary>
/// Jellyfin-compatible playlist endpoints.
/// Reuses Melodee's existing PlaylistService for playlist management.
/// </summary>
[ApiController]
[Route("api/jf/[controller]")]
[ApiExplorerSettings(GroupName = "jellyfin")]
[EnableRateLimiting("jellyfin-api")]
public class PlaylistsController(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> dbContextFactory,
    IClock clock,
    ILoggerFactory loggerFactory,
    PlaylistService playlistService) : JellyfinControllerBase(etagRepository, serializer, configuration, configurationFactory, dbContextFactory, clock, loggerFactory)
{
    /// <summary>
    /// Get all playlists for the authenticated user.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPlaylistsAsync(
        [FromQuery] string? userId,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        var skip = Math.Max(0, startIndex ?? 0);
        var take = Math.Clamp(limit ?? 100, 1, 500);

        var userInfo = user.ToUserInfo();
        var pagedRequest = new PagedRequest { Page = 1, PageSize = (short)(take + skip) };

        var playlistResult = await playlistService.ListAsync(userInfo, pagedRequest, cancellationToken);

        var playlists = playlistResult.Data
            .Skip(skip)
            .Take(take)
            .Select(p => MapToJellyfinPlaylist(p, user.HasDownloadRole))
            .ToArray();

        return Ok(new JellyfinItemsResult
        {
            Items = playlists,
            TotalRecordCount = playlistResult.TotalCount,
            StartIndex = skip
        });
    }

    /// <summary>
    /// Get a specific playlist by ID.
    /// </summary>
    [HttpGet("{playlistId}")]
    public async Task<IActionResult> GetPlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(playlistId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid playlist ID format.");
        }

        var userInfo = user.ToUserInfo();
        var playlistResult = await playlistService.GetByApiKeyAsync(userInfo, apiKey, cancellationToken);

        if (!playlistResult.IsSuccess || playlistResult.Data == null)
        {
            return JellyfinNotFound("Playlist not found.");
        }

        var playlist = playlistResult.Data;
        var etag = ComputeEtag(playlist.ApiKey, playlist.LastUpdatedAt ?? playlist.CreatedAt);

        if (IsNotModified(etag))
        {
            return NotModified(etag);
        }

        SetETagHeader(etag);
        return Ok(MapToJellyfinPlaylist(playlist, user.HasDownloadRole));
    }

    /// <summary>
    /// Get items (songs) in a playlist.
    /// </summary>
    [HttpGet("{playlistId}/Items")]
    public async Task<IActionResult> GetPlaylistItemsAsync(
        string playlistId,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? fields,
        [FromQuery] bool? enableUserData,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(playlistId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid playlist ID format.");
        }

        var skip = Math.Max(0, startIndex ?? 0);
        var take = Math.Clamp(limit ?? 100, 1, 500);

        var userInfo = user.ToUserInfo();
        var pagedRequest = new PagedRequest { Page = 1, PageSize = (short)(take + skip) };

        var songsResult = await playlistService.SongsForPlaylistAsync(apiKey, userInfo, pagedRequest, cancellationToken);

        if (!songsResult.IsSuccess)
        {
            return JellyfinNotFound("Playlist not found.");
        }

        var songs = songsResult.Data
            .Skip(skip)
            .Take(take)
            .Select((s, index) => MapToJellyfinAudioItem(s, user.HasDownloadRole, enableUserData ?? false, skip + index))
            .ToArray();

        return Ok(new JellyfinItemsResult
        {
            Items = songs,
            TotalRecordCount = songsResult.TotalCount,
            StartIndex = skip
        });
    }

    /// <summary>
    /// Create a new playlist.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreatePlaylistAsync(
        [FromBody] JellyfinCreatePlaylistRequest request,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return JellyfinBadRequest("Playlist name is required.");
        }

        Guid[]? songApiKeys = null;
        if (request.Ids is { Length: > 0 })
        {
            songApiKeys = request.Ids
                .Select(id => TryParseJellyfinGuid(id, out var apiKey) ? apiKey : (Guid?)null)
                .Where(k => k.HasValue)
                .Select(k => k!.Value)
                .ToArray();
        }

        var createResult = await playlistService.CreatePlaylistAsync(
            request.Name,
            user.Id,
            null,
            request.IsPublic ?? false,
            songApiKeys,
            false,
            cancellationToken);

        if (!createResult.IsSuccess || string.IsNullOrEmpty(createResult.Data))
        {
            return JellyfinBadRequest(createResult.Messages?.FirstOrDefault() ?? "Failed to create playlist.");
        }

        return Ok(new JellyfinCreatePlaylistResponse
        {
            Id = createResult.Data.Replace("-", string.Empty)
        });
    }

    /// <summary>
    /// Add items to a playlist.
    /// </summary>
    [HttpPost("{playlistId}/Items")]
    public async Task<IActionResult> AddItemsToPlaylistAsync(
        string playlistId,
        [FromQuery] string? ids,
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(playlistId, out var playlistApiKey))
        {
            return JellyfinBadRequest("Invalid playlist ID format.");
        }

        if (string.IsNullOrWhiteSpace(ids))
        {
            return NoContent();
        }

        var songApiKeys = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => TryParseJellyfinGuid(id.Trim(), out var apiKey) ? apiKey : (Guid?)null)
            .Where(k => k.HasValue)
            .Select(k => k!.Value)
            .ToArray();

        if (songApiKeys.Length == 0)
        {
            return NoContent();
        }

        var result = await playlistService.AddSongsToPlaylistAsync(playlistApiKey, songApiKeys, cancellationToken);

        if (!result.IsSuccess)
        {
            return JellyfinNotFound("Playlist not found or access denied.");
        }

        return NoContent();
    }

    /// <summary>
    /// Remove items from a playlist.
    /// </summary>
    [HttpDelete("{playlistId}/Items")]
    public async Task<IActionResult> RemoveItemsFromPlaylistAsync(
        string playlistId,
        [FromQuery] string? entryIds,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(playlistId, out var playlistApiKey))
        {
            return JellyfinBadRequest("Invalid playlist ID format.");
        }

        if (string.IsNullOrWhiteSpace(entryIds))
        {
            return NoContent();
        }

        var songApiKeys = entryIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => TryParseJellyfinGuid(id.Trim(), out var apiKey) ? apiKey : (Guid?)null)
            .Where(k => k.HasValue)
            .Select(k => k!.Value)
            .ToArray();

        if (songApiKeys.Length == 0)
        {
            return NoContent();
        }

        var result = await playlistService.RemoveSongsFromPlaylistAsync(playlistApiKey, songApiKeys, cancellationToken);

        if (!result.IsSuccess)
        {
            return JellyfinNotFound("Playlist not found or access denied.");
        }

        return NoContent();
    }

    /// <summary>
    /// Move an item within a playlist to a new position.
    /// </summary>
    [HttpPost("{playlistId}/Items/{itemId}/Move/{newIndex:int}")]
    public async Task<IActionResult> MovePlaylistItemAsync(
        string playlistId,
        string itemId,
        int newIndex,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(playlistId, out var playlistApiKey))
        {
            return JellyfinBadRequest("Invalid playlist ID format.");
        }

        if (!TryParseJellyfinGuid(itemId, out var songApiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var playlist = await dbContext.Playlists
            .Include(p => p.Songs)
            .ThenInclude(ps => ps.Song)
            .FirstOrDefaultAsync(p => p.ApiKey == playlistApiKey, cancellationToken);

        if (playlist == null)
        {
            return JellyfinNotFound("Playlist not found.");
        }

        var playlistSong = playlist.Songs.FirstOrDefault(ps => ps.Song.ApiKey == songApiKey);
        if (playlistSong == null)
        {
            return JellyfinNotFound("Item not found in playlist.");
        }

        var orderedSongs = playlist.Songs.OrderBy(ps => ps.PlaylistOrder).ToList();
        var currentIndex = orderedSongs.IndexOf(playlistSong);

        if (currentIndex == newIndex || newIndex < 0 || newIndex >= orderedSongs.Count)
        {
            return NoContent();
        }

        orderedSongs.RemoveAt(currentIndex);
        orderedSongs.Insert(newIndex, playlistSong);

        for (var i = 0; i < orderedSongs.Count; i++)
        {
            orderedSongs[i].PlaylistOrder = i + 1;
        }

        playlist.LastUpdatedAt = Clock.GetCurrentInstant();
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Delete a playlist.
    /// </summary>
    [HttpDelete("{playlistId}")]
    public async Task<IActionResult> DeletePlaylistAsync(string playlistId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(playlistId, out var playlistApiKey))
        {
            return JellyfinBadRequest("Invalid playlist ID format.");
        }

        var result = await playlistService.DeleteByApiKeyAsync(playlistApiKey, user.Id, cancellationToken);

        if (!result.IsSuccess)
        {
            return JellyfinNotFound("Playlist not found or access denied.");
        }

        return NoContent();
    }

    /// <summary>
    /// Update a playlist. Used by Feishin.
    /// </summary>
    [HttpPost("{playlistId}")]
    public async Task<IActionResult> UpdatePlaylistAsync(
        string playlistId,
        [FromBody] JellyfinUpdatePlaylistRequest request,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(playlistId, out var playlistApiKey))
        {
            return JellyfinBadRequest("Invalid playlist ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var playlist = await dbContext.Playlists
            .FirstOrDefaultAsync(p => p.ApiKey == playlistApiKey && p.UserId == user.Id, cancellationToken);

        if (playlist == null)
        {
            return JellyfinNotFound("Playlist not found or access denied.");
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            playlist.Name = request.Name;
        }

        if (request.IsPublic.HasValue)
        {
            playlist.IsPublic = request.IsPublic.Value;
        }

        playlist.LastUpdatedAt = Clock.GetCurrentInstant();
        await dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private JellyfinBaseItem MapToJellyfinPlaylist(Common.Data.Models.Playlist playlist, bool canDownload)
    {
        return new JellyfinBaseItem
        {
            Name = playlist.Name,
            ServerId = GetServerId(),
            Id = ToJellyfinId(playlist.ApiKey),
            Etag = ComputeEtag(playlist.ApiKey, playlist.LastUpdatedAt ?? playlist.CreatedAt),
            DateCreated = FormatInstantForJellyfin(playlist.CreatedAt),
            SortName = playlist.Name,
            Overview = playlist.Comment,
            Type = "Playlist",
            IsFolder = true,
            CanDownload = canDownload,
            ChildCount = playlist.SongCount,
            RunTimeTicks = (long)(playlist.Duration * 10_000_000),
            MediaType = "Audio",
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = []
        };
    }

    private JellyfinBaseItem MapToJellyfinAudioItem(
        Common.Models.Collection.SongDataInfo song,
        bool canDownload,
        bool enableUserData,
        int playlistIndex)
    {
        var runTimeTicks = (long)(song.Duration * 10_000_000);

        return new JellyfinBaseItem
        {
            Name = song.Title,
            ServerId = GetServerId(),
            Id = ToJellyfinId(song.ApiKey),
            Etag = ComputeEtag(song.ApiKey, song.CreatedAt),
            DateCreated = FormatInstantForJellyfin(song.CreatedAt),
            SortName = song.Title,
            Type = "Audio",
            IsFolder = false,
            CanDownload = canDownload,
            IndexNumber = song.SongNumber,
            ParentIndexNumber = playlistIndex + 1,
            RunTimeTicks = runTimeTicks > 0 ? runTimeTicks : null,
            Album = song.AlbumName,
            AlbumId = ToJellyfinId(song.AlbumApiKey),
            Artists = !string.IsNullOrWhiteSpace(song.ArtistName) ? [song.ArtistName] : null,
            ArtistItems = !string.IsNullOrWhiteSpace(song.ArtistName)
                ? [new JellyfinNameGuidPair { Name = song.ArtistName, Id = ToJellyfinId(song.ArtistApiKey) }]
                : null,
            MediaType = "Audio",
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            UserData = enableUserData ? new JellyfinUserItemData
            {
                Key = song.ApiKey.ToString("N"),
                IsFavorite = song.UserStarred,
                PlayCount = song.PlayedCount,
                Played = song.PlayedCount > 0
            } : null
        };
    }

    private static string ComputeEtag(Guid apiKey, Instant lastUpdated)
    {
        var input = $"{apiKey:N}-{lastUpdated.ToUnixTimeTicks()}";
        // NOTE: MD5 is used here for generating ETag values for HTTP caching in Jellyfin API compatibility.
        // This is NOT a cryptographic use - ETags are public cache identifiers, not security tokens.
        // lgtm[cs/weak-crypto] MD5 used for non-cryptographic ETag generation, not for security
        return HashHelper.CreateMd5(input) ?? string.Empty;
    }
}

