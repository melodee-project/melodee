using Melodee.Blazor.Controllers.Jellyfin.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Serialization;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Blazor.Controllers.Jellyfin;

[ApiController]
[Route("api/jf/[controller]")]
[ApiExplorerSettings(GroupName = "jellyfin")]
[EnableRateLimiting("jellyfin-api")]
public class ItemsController(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> dbContextFactory,
    IClock clock,
    ILoggerFactory loggerFactory,
    ILogger<ItemsController> logger) : JellyfinControllerBase(etagRepository, serializer, configuration, configurationFactory, dbContextFactory, clock, loggerFactory)
{
    private const int StreamBufferSize = 65536;

    private static readonly HashSet<string> SupportedItemTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "MusicArtist", "MusicAlbum", "Audio"
    };

    [HttpGet]
    public async Task<IActionResult> GetItemsAsync(
        [FromQuery] string? searchTerm,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? parentId,
        [FromQuery] string? includeItemTypes,
        [FromQuery] string? fields,
        [FromQuery] bool? recursive,
        [FromQuery] bool? enableUserData,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GetItemsAsync: includeItemTypes={IncludeItemTypes} parentId={ParentId} searchTerm={SearchTerm} startIndex={StartIndex} limit={Limit} recursive={Recursive}",
            LogSanitizer.Sanitize(includeItemTypes), LogSanitizer.Sanitize(parentId),
            LogSanitizer.Sanitize(searchTerm), startIndex, limit, recursive);

        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        var skip = Math.Max(0, startIndex ?? 0);
        var take = Math.Clamp(limit ?? 100, 1, 500);

        var requestedTypes = ParseItemTypes(includeItemTypes);

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        if (requestedTypes.Contains("MusicArtist"))
        {
            return await GetArtistItemsAsync(dbContext, searchTerm, skip, take, enableUserData == true, cancellationToken);
        }

        if (requestedTypes.Contains("MusicAlbum"))
        {
            return await GetAlbumItemsAsync(dbContext, searchTerm, skip, take, parentId, enableUserData == true, cancellationToken);
        }

        if (requestedTypes.Contains("Audio"))
        {
            return await GetSongItemsAsync(dbContext, searchTerm, skip, take, parentId, enableUserData == true, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(parentId) && TryParseJellyfinGuid(parentId, out var parentApiKey))
        {
            var artist = await dbContext.Artists
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ApiKey == parentApiKey, cancellationToken);
            if (artist != null)
            {
                return await GetAlbumItemsAsync(dbContext, searchTerm, skip, take, parentId, enableUserData == true, cancellationToken);
            }

            var album = await dbContext.Albums
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.ApiKey == parentApiKey, cancellationToken);
            if (album != null)
            {
                return await GetSongItemsAsync(dbContext, searchTerm, skip, take, parentId, enableUserData == true, cancellationToken);
            }
        }

        return Ok(new JellyfinItemsResult
        {
            Items = [],
            TotalRecordCount = 0,
            StartIndex = skip
        });
    }

    [HttpGet("{itemId}")]
    public async Task<IActionResult> GetItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(itemId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var artist = await dbContext.Artists
            .AsNoTracking()
            .Where(a => a.ApiKey == apiKey && !a.IsLocked)
            .Select(a => new { a.ApiKey, a.Name, a.SortName, a.Description, a.CreatedAt, a.LastUpdatedAt, AlbumCount = a.Albums.Count(alb => !alb.IsLocked) })
            .FirstOrDefaultAsync(cancellationToken);

        if (artist != null)
        {
            return Ok(MapArtist(artist.ApiKey, artist.Name, artist.SortName, artist.Description, artist.CreatedAt, artist.LastUpdatedAt, artist.AlbumCount, user.HasDownloadRole));
        }

        var album = await dbContext.Albums
            .AsNoTracking()
            .Include(a => a.Artist)
            .Where(a => a.ApiKey == apiKey && !a.IsLocked)
            .Select(a => new
            {
                a.ApiKey,
                a.Name,
                a.SortName,
                a.Description,
                AlbumYear = a.ReleaseDate.Year,
                a.Duration,
                a.CreatedAt,
                a.LastUpdatedAt,
                a.Genres,
                ArtistName = a.Artist.Name,
                ArtistApiKey = a.Artist.ApiKey,
                SongCount = a.Songs.Count(s => !s.IsLocked)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (album != null)
        {
            return Ok(MapAlbum(
                album.ApiKey, album.Name, album.SortName, album.Description,
                album.AlbumYear, album.Duration, album.CreatedAt, album.LastUpdatedAt,
                album.Genres, album.ArtistName, album.ArtistApiKey, album.SongCount, user.HasDownloadRole, true));
        }

        var song = await dbContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .ThenInclude(ar => ar.Library)
            .Where(s => s.ApiKey == apiKey && !s.IsLocked)
            .FirstOrDefaultAsync(cancellationToken);

        if (song != null)
        {
            return Ok(MapSong(song, user.HasDownloadRole, true));
        }

        return JellyfinNotFound("Item not found.");
    }

    [HttpGet("{itemId}/PlaybackInfo")]
    [HttpPost("{itemId}/PlaybackInfo")]
    public async Task<IActionResult> GetPlaybackInfoAsync(string itemId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(itemId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var song = await dbContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .ThenInclude(ar => ar.Library)
            .Where(s => s.ApiKey == apiKey && !s.IsLocked)
            .FirstOrDefaultAsync(cancellationToken);

        if (song == null)
        {
            return JellyfinNotFound("Item not found.");
        }

        var mediaSource = CreateMediaSourceWithDetails(song);

        return Ok(new JellyfinPlaybackInfoResult
        {
            MediaSources = [mediaSource],
            PlaySessionId = Guid.NewGuid().ToString("N")
        });
    }

    /// <summary>
    /// Delete an item (playlist only - songs/albums/artists cannot be deleted via API).
    /// </summary>
    [HttpDelete("{itemId}")]
    public async Task<IActionResult> DeleteItemAsync(string itemId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(itemId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var playlist = await dbContext.Playlists
            .FirstOrDefaultAsync(p => p.ApiKey == apiKey && p.UserId == user.Id, cancellationToken);

        if (playlist != null)
        {
            dbContext.Playlists.Remove(playlist);
            await dbContext.SaveChangesAsync(cancellationToken);
            return NoContent();
        }

        return JellyfinNotFound("Item not found or cannot be deleted.");
    }

    /// <summary>
    /// Refresh/rescan an item (library refresh).
    /// </summary>
    [HttpPost("{itemId}/Refresh")]
    public async Task<IActionResult> RefreshItemAsync(
        string itemId,
        [FromQuery] bool? recursive,
        [FromQuery] string? imageRefreshMode,
        [FromQuery] string? metadataRefreshMode,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(itemId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        logger.LogInformation("JellyfinRefreshRequest ItemId={ItemId} Recursive={Recursive} by User={UserId}",
            LogSanitizer.Sanitize(itemId), recursive ?? false, LogSanitizer.Sanitize(user.Id.ToString()));

        return NoContent();
    }

    /// <summary>
    /// Gets filter values (genres, tags, years) for filtering items. Used by Feishin for tag filtering.
    /// </summary>
    [HttpGet("Filters")]
    public async Task<IActionResult> GetFiltersAsync(
        [FromQuery] string? userId,
        [FromQuery] string? parentId,
        [FromQuery] string? includeItemTypes,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var genres = await dbContext.Albums
            .AsNoTracking()
            .Where(a => !a.IsLocked && a.Genres != null && a.Genres.Any())
            .SelectMany(a => a.Genres!)
            .Distinct()
            .OrderBy(g => g)
            .ToListAsync(cancellationToken);

        var years = await dbContext.Albums
            .AsNoTracking()
            .Where(a => !a.IsLocked && a.ReleaseDate.Year > 0)
            .Select(a => a.ReleaseDate.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync(cancellationToken);

        return Ok(new
        {
            Genres = genres,
            Tags = Array.Empty<string>(),
            Years = years
        });
    }

    /// <summary>
    /// Gets similar items for a given item. Used by Feishin for "Similar" recommendations.
    /// </summary>
    [HttpGet("{itemId}/Similar")]
    public async Task<IActionResult> GetSimilarItemsAsync(
        string itemId,
        [FromQuery] string? userId,
        [FromQuery] int? limit,
        [FromQuery] string? fields,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(itemId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        var maxItems = Math.Clamp(limit ?? 20, 1, 100);

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var seedSong = await dbContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .Where(s => s.ApiKey == apiKey && !s.IsLocked)
            .FirstOrDefaultAsync(cancellationToken);

        if (seedSong != null)
        {
            var genres = seedSong.Album?.Genres ?? [];
            var seedArtistId = seedSong.Album?.ArtistId;
            var similarSongs = await dbContext.Songs
                .AsNoTracking()
                .Include(s => s.Album)
                .ThenInclude(a => a.Artist)
                .Where(s => !s.IsLocked && !s.Album.IsLocked && s.Id != seedSong.Id &&
                    (s.Album.ArtistId == seedArtistId ||
                     (s.Album.Genres != null && genres.Any(g => s.Album.Genres.Contains(g)))))
                .OrderBy(s => EF.Functions.Random())
                .Take(maxItems)
                .ToListAsync(cancellationToken);

            var items = similarSongs.Select(s => MapSong(s, user.HasDownloadRole, true)).ToArray();

            return Ok(new JellyfinItemsResult
            {
                Items = items,
                TotalRecordCount = items.Length,
                StartIndex = 0
            });
        }

        var seedAlbum = await dbContext.Albums
            .AsNoTracking()
            .Include(a => a.Artist)
            .Where(a => a.ApiKey == apiKey && !a.IsLocked)
            .FirstOrDefaultAsync(cancellationToken);

        if (seedAlbum != null)
        {
            var genres = seedAlbum.Genres ?? [];
            var similarAlbums = await dbContext.Albums
                .AsNoTracking()
                .Include(a => a.Artist)
                .Where(a => !a.IsLocked && a.Id != seedAlbum.Id &&
                    (a.ArtistId == seedAlbum.ArtistId ||
                     (a.Genres != null && genres.Any(g => a.Genres.Contains(g)))))
                .OrderBy(a => EF.Functions.Random())
                .Take(maxItems)
                .Select(a => new
                {
                    a.ApiKey,
                    a.Name,
                    a.SortName,
                    a.Description,
                    AlbumYear = a.ReleaseDate.Year,
                    a.Duration,
                    a.CreatedAt,
                    a.LastUpdatedAt,
                    a.Genres,
                    ArtistName = a.Artist.Name,
                    ArtistApiKey = a.Artist.ApiKey,
                    SongCount = a.Songs.Count(s => !s.IsLocked)
                })
                .ToListAsync(cancellationToken);

            var items = similarAlbums.Select(a =>
                MapAlbum(a.ApiKey, a.Name, a.SortName, a.Description, a.AlbumYear, a.Duration,
                    a.CreatedAt, a.LastUpdatedAt, a.Genres, a.ArtistName, a.ArtistApiKey, a.SongCount, user.HasDownloadRole, true)).ToArray();

            return Ok(new JellyfinItemsResult
            {
                Items = items,
                TotalRecordCount = items.Length,
                StartIndex = 0
            });
        }

        return Ok(new JellyfinItemsResult
        {
            Items = [],
            TotalRecordCount = 0,
            StartIndex = 0
        });
    }

    /// <summary>
    /// Gets instant mix (radio) based on an item. Used by Finamp for the "Instant Mix" feature.
    /// Returns a shuffled selection of similar songs.
    /// </summary>
    [HttpGet("{itemId}/InstantMix")]
    public async Task<IActionResult> GetInstantMixAsync(
        string itemId,
        [FromQuery] string? userId,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(itemId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        var maxItems = Math.Clamp(limit ?? 200, 1, 300);

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        // First, try to find the seed item (could be a song, album, or artist)
        var seedSong = await dbContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .Where(s => s.ApiKey == apiKey && !s.IsLocked)
            .FirstOrDefaultAsync(cancellationToken);

        IQueryable<Common.Data.Models.Song> mixQuery;

        if (seedSong != null)
        {
            // Seed is a song - get songs from the same album and similar genres
            var genres = seedSong.Album?.Genres ?? [];
            var artistId = seedSong.Album?.ArtistId;

            mixQuery = dbContext.Songs
                .AsNoTracking()
                .Include(s => s.Album)
                .ThenInclude(a => a.Artist)
                .Where(s => !s.IsLocked && !s.Album.IsLocked && s.Id != seedSong.Id);

            // Prefer songs from the same artist or genre
            if (artistId.HasValue)
            {
                mixQuery = mixQuery.Where(s => s.Album.ArtistId == artistId ||
                    (s.Album.Genres != null && genres.Any(g => s.Album.Genres.Contains(g))));
            }
        }
        else
        {
            // Check if it's an album
            var seedAlbum = await dbContext.Albums
                .AsNoTracking()
                .Where(a => a.ApiKey == apiKey && !a.IsLocked)
                .FirstOrDefaultAsync(cancellationToken);

            if (seedAlbum != null)
            {
                var genres = seedAlbum.Genres ?? [];
                mixQuery = dbContext.Songs
                    .AsNoTracking()
                    .Include(s => s.Album)
                    .ThenInclude(a => a.Artist)
                    .Where(s => !s.IsLocked && !s.Album.IsLocked &&
                        (s.AlbumId == seedAlbum.Id ||
                         s.Album.ArtistId == seedAlbum.ArtistId ||
                         (s.Album.Genres != null && genres.Any(g => s.Album.Genres.Contains(g)))));
            }
            else
            {
                // Check if it's an artist
                var seedArtist = await dbContext.Artists
                    .AsNoTracking()
                    .Where(a => a.ApiKey == apiKey && !a.IsLocked)
                    .FirstOrDefaultAsync(cancellationToken);

                if (seedArtist != null)
                {
                    mixQuery = dbContext.Songs
                        .AsNoTracking()
                        .Include(s => s.Album)
                        .ThenInclude(a => a.Artist)
                        .Where(s => !s.IsLocked && !s.Album.IsLocked && s.Album.ArtistId == seedArtist.Id);
                }
                else
                {
                    return JellyfinNotFound("Item not found.");
                }
            }
        }

        // Get random songs for the mix
        var songs = await mixQuery
            .OrderBy(s => EF.Functions.Random())
            .Take(maxItems)
            .Select(s => new
            {
                s.Id,
                s.ApiKey,
                s.Title,
                s.SongNumber,
                s.Duration,
                s.CreatedAt,
                s.LastUpdatedAt,
                AlbumApiKey = s.Album.ApiKey,
                AlbumName = s.Album.Name,
                AlbumYear = (int?)s.Album.ReleaseDate.Year,
                ArtistApiKey = s.Album.Artist.ApiKey,
                ArtistName = s.Album.Artist.Name,
                s.Album.Genres
            })
            .ToListAsync(cancellationToken);

        var items = songs.Select(s => new JellyfinBaseItem
        {
            Name = s.Title,
            ServerId = GetServerId(),
            Id = ToJellyfinId(s.ApiKey),
            DateCreated = FormatInstantForJellyfin(s.CreatedAt),
            SortName = s.Title,
            Type = "Audio",
            IsFolder = false,
            CanDownload = user.HasDownloadRole,
            RunTimeTicks = (long)(s.Duration * 10_000_000),
            IndexNumber = s.SongNumber,
            Album = s.AlbumName,
            AlbumId = ToJellyfinId(s.AlbumApiKey),
            AlbumArtist = s.ArtistName,
            Artists = [s.ArtistName],
            ArtistItems = [new JellyfinNameGuidPair { Name = s.ArtistName, Id = ToJellyfinId(s.ArtistApiKey) }],
            AlbumArtists = [new JellyfinNameGuidPair { Name = s.ArtistName, Id = ToJellyfinId(s.ArtistApiKey) }],
            ProductionYear = s.AlbumYear,
            Genres = s.Genres?.ToArray(),
            MediaType = "Audio",
            ImageTags = new Dictionary<string, string> { ["Primary"] = ToJellyfinId(s.AlbumApiKey) },
            BackdropImageTags = []
        }).ToArray();

        return Ok(new JellyfinItemsResult
        {
            Items = items,
            TotalRecordCount = items.Length,
            StartIndex = 0
        });
    }

    [HttpGet("{itemId}/File")]
    [HttpGet("{itemId}/Download")]
    [EnableRateLimiting("jellyfin-stream")]
    public async Task<IActionResult> GetItemFileAsync(string itemId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!user.HasDownloadRole)
        {
            return JellyfinForbidden("Download permission required.");
        }

        if (!TryParseJellyfinGuid(itemId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid item ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var song = await dbContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .ThenInclude(ar => ar.Library)
            .Where(s => s.ApiKey == apiKey && !s.IsLocked)
            .FirstOrDefaultAsync(cancellationToken);

        if (song == null)
        {
            return JellyfinNotFound("Item not found.");
        }

        var filePath = GetSongFilePath(song);
        if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
        {
            logger.LogWarning("JellyfinFileNotFound ItemId={ItemId} FilePath={FilePath}",
                LogSanitizer.Sanitize(itemId), LogSanitizer.Sanitize(filePath ?? "null"));
            return JellyfinNotFound("File not found.");
        }

        var fileInfo = new FileInfo(filePath);
        var contentType = GetContentTypeForFile(filePath);

        Response.Headers.Append("Accept-Ranges", "bytes");
        Response.Headers.Append("Content-Disposition", $"attachment; filename=\"{Uri.EscapeDataString(fileInfo.Name)}\"");

        if (Request.Headers.TryGetValue("Range", out var rangeHeader))
        {
            return await HandleRangeRequestAsync(filePath, fileInfo.Length, rangeHeader.ToString(), contentType, cancellationToken);
        }

        return PhysicalFile(filePath, contentType, enableRangeProcessing: true);
    }

    private async Task<IActionResult> GetArtistItemsAsync(
        MelodeeDbContext dbContext,
        string? searchTerm,
        int skip,
        int take,
        bool enableUserData,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Artists
            .AsNoTracking()
            .Where(a => !a.IsLocked);

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var normalizedSearch = searchTerm.ToUpperInvariant();
            query = query.Where(a => a.NameNormalized.Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var artists = await query
            .OrderBy(a => a.SortName ?? a.Name)
            .Skip(skip)
            .Take(take)
            .Select(a => new
            {
                a.ApiKey,
                a.Name,
                a.SortName,
                a.Description,
                a.CreatedAt,
                a.LastUpdatedAt,
                AlbumCount = a.Albums.Count(alb => !alb.IsLocked)
            })
            .ToListAsync(cancellationToken);

        var items = artists.Select(a =>
            MapArtist(a.ApiKey, a.Name, a.SortName, a.Description, a.CreatedAt, a.LastUpdatedAt, a.AlbumCount, true, enableUserData)).ToArray();

        return Ok(new JellyfinItemsResult
        {
            Items = items,
            TotalRecordCount = totalCount,
            StartIndex = skip
        });
    }

    private async Task<IActionResult> GetAlbumItemsAsync(
        MelodeeDbContext dbContext,
        string? searchTerm,
        int skip,
        int take,
        string? parentId,
        bool enableUserData,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Albums
            .AsNoTracking()
            .Include(a => a.Artist)
            .Where(a => !a.IsLocked);

        if (!string.IsNullOrWhiteSpace(parentId) && TryParseJellyfinGuid(parentId, out var artistApiKey))
        {
            query = query.Where(a => a.Artist.ApiKey == artistApiKey);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var normalizedSearch = searchTerm.ToUpperInvariant();
            query = query.Where(a => a.NameNormalized.Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var albums = await query
            .OrderBy(a => a.ReleaseDate.Year)
            .ThenBy(a => a.SortName ?? a.Name)
            .Skip(skip)
            .Take(take)
            .Select(a => new
            {
                a.ApiKey,
                a.Name,
                a.SortName,
                a.Description,
                AlbumYear = a.ReleaseDate.Year,
                a.Duration,
                a.CreatedAt,
                a.LastUpdatedAt,
                a.Genres,
                ArtistName = a.Artist.Name,
                ArtistApiKey = a.Artist.ApiKey,
                SongCount = a.Songs.Count(s => !s.IsLocked)
            })
            .ToListAsync(cancellationToken);

        var items = albums.Select(a =>
            MapAlbum(a.ApiKey, a.Name, a.SortName, a.Description, a.AlbumYear, a.Duration,
                a.CreatedAt, a.LastUpdatedAt, a.Genres, a.ArtistName, a.ArtistApiKey, a.SongCount, true, enableUserData)).ToArray();

        return Ok(new JellyfinItemsResult
        {
            Items = items,
            TotalRecordCount = totalCount,
            StartIndex = skip
        });
    }

    private async Task<IActionResult> GetSongItemsAsync(
        MelodeeDbContext dbContext,
        string? searchTerm,
        int skip,
        int take,
        string? parentId,
        bool enableUserData,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .ThenInclude(ar => ar.Library)
            .Where(s => !s.IsLocked);

        if (!string.IsNullOrWhiteSpace(parentId) && TryParseJellyfinGuid(parentId, out var parentApiKey))
        {
            var library = await dbContext.Libraries
                .AsNoTracking()
                .FirstOrDefaultAsync(l => l.ApiKey == parentApiKey, cancellationToken);

            query = library != null
                ? query.Where(s => s.Album.Artist.Library.ApiKey == parentApiKey)
                : query.Where(s => s.Album.ApiKey == parentApiKey);
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var normalizedSearch = searchTerm.ToUpperInvariant();
            query = query.Where(s => s.TitleNormalized.Contains(normalizedSearch));
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var songs = await query
            .OrderBy(s => s.SongNumber)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        var items = songs.Select(s => MapSong(s, true, enableUserData)).ToArray();

        return Ok(new JellyfinItemsResult
        {
            Items = items,
            TotalRecordCount = totalCount,
            StartIndex = skip
        });
    }

    private JellyfinBaseItem MapArtist(Guid apiKey, string name, string? sortName, string? description,
        Instant createdAt, Instant? lastUpdatedAt, int albumCount, bool canDownload, bool enableUserData = false)
    {
        return new JellyfinBaseItem
        {
            Name = name,
            ServerId = GetServerId(),
            Id = ToJellyfinId(apiKey),
            Etag = ComputeEtag(apiKey, lastUpdatedAt ?? createdAt),
            DateCreated = FormatInstantForJellyfin(createdAt),
            SortName = sortName ?? name,
            Type = "MusicArtist",
            IsFolder = true,
            CanDownload = canDownload,
            Overview = description,
            ChildCount = albumCount,
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            MediaType = "Audio",
            UserData = enableUserData ? new JellyfinUserItemData { Key = apiKey.ToString("N") } : null
        };
    }

    private JellyfinBaseItem MapAlbum(Guid apiKey, string name, string? sortName, string? description,
        int year, double duration, Instant createdAt, Instant? lastUpdatedAt, string[]? genres,
        string artistName, Guid artistApiKey, int songCount, bool canDownload, bool enableUserData = false)
    {
        var genreList = genres ?? [];
        var runTimeTicks = (long)(duration * 10_000_000);

        return new JellyfinBaseItem
        {
            Name = name,
            ServerId = GetServerId(),
            Id = ToJellyfinId(apiKey),
            Etag = ComputeEtag(apiKey, lastUpdatedAt ?? createdAt),
            DateCreated = FormatInstantForJellyfin(createdAt),
            SortName = sortName ?? name,
            Type = "MusicAlbum",
            IsFolder = true,
            CanDownload = canDownload,
            Overview = description,
            ProductionYear = year > 0 ? year : null,
            RunTimeTicks = runTimeTicks > 0 ? runTimeTicks : null,
            Genres = genreList,
            ChildCount = songCount,
            AlbumArtist = artistName,
            AlbumArtists = [new JellyfinNameGuidPair { Name = artistName, Id = ToJellyfinId(artistApiKey) }],
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            MediaType = "Audio",
            UserData = enableUserData ? new JellyfinUserItemData { Key = apiKey.ToString("N") } : null
        };
    }

    private JellyfinBaseItem MapSong(Common.Data.Models.Song song, bool canDownload, bool enableUserData = false)
    {
        var album = song.Album;
        var artist = album?.Artist;
        var runTimeTicks = (long)(song.Duration * 10_000_000);

        return new JellyfinBaseItem
        {
            Name = song.Title,
            ServerId = GetServerId(),
            Id = ToJellyfinId(song.ApiKey),
            Etag = ComputeEtag(song.ApiKey, song.LastUpdatedAt ?? song.CreatedAt),
            DateCreated = FormatInstantForJellyfin(song.CreatedAt),
            SortName = song.TitleSort ?? song.Title,
            Type = "Audio",
            IsFolder = false,
            CanDownload = canDownload,
            IndexNumber = song.SongNumber,
            RunTimeTicks = runTimeTicks > 0 ? runTimeTicks : 0,
            Album = album?.Name ?? "Unknown Album",
            AlbumId = album != null ? ToJellyfinId(album.ApiKey) : ToJellyfinId(Guid.Empty),
            AlbumArtist = artist?.Name,
            AlbumArtists = artist != null ? [new JellyfinNameGuidPair { Name = artist.Name, Id = ToJellyfinId(artist.ApiKey) }] : [],
            Artists = artist != null ? [artist.Name] : [],
            ArtistItems = artist != null ? [new JellyfinNameGuidPair { Name = artist.Name, Id = ToJellyfinId(artist.ApiKey) }] : [],
            Container = Path.GetExtension(song.FileName)?.TrimStart('.'),
            MediaType = "Audio",
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            MediaSources = CreateMediaSources(song),
            UserData = new JellyfinUserItemData { Key = song.ApiKey.ToString("N") },
            HasLyrics = false
        };
    }

    private JellyfinMediaSource[] CreateMediaSources(Common.Data.Models.Song song)
    {
        var container = Path.GetExtension(song.FileName)?.TrimStart('.') ?? "mp3";
        var runTimeTicks = (long)(song.Duration * 10_000_000);

        return
        [
            new JellyfinMediaSource
            {
                Id = ToJellyfinId(song.ApiKey),
                Container = container,
                Size = song.FileSize,
                Name = song.Title,
                RunTimeTicks = runTimeTicks > 0 ? runTimeTicks : null,
                Bitrate = song.BitRate > 0 ? song.BitRate * 1000 : null,
                MediaStreams =
                [
                    new JellyfinMediaStream
                    {
                        Codec = container,
                        Type = "Audio",
                        Index = 0,
                        IsDefault = true,
                        BitRate = song.BitRate > 0 ? song.BitRate * 1000 : null,
                        SampleRate = song.SamplingRate > 0 ? song.SamplingRate : null,
                        Channels = song.ChannelCount > 0 ? song.ChannelCount : null
                    }
                ]
            }
        ];
    }

    private JellyfinMediaSource CreateMediaSourceWithDetails(Common.Data.Models.Song song)
    {
        var container = Path.GetExtension(song.FileName)?.TrimStart('.') ?? "mp3";
        var runTimeTicks = (long)(song.Duration * 10_000_000);
        var codec = GetCodecFromContainer(container);

        return new JellyfinMediaSource
        {
            Id = ToJellyfinId(song.ApiKey),
            Protocol = "File",
            Type = "Default",
            Container = container,
            Size = song.FileSize,
            Name = song.Title,
            RunTimeTicks = runTimeTicks > 0 ? runTimeTicks : null,
            Bitrate = song.BitRate > 0 ? song.BitRate * 1000 : null,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            SupportsTranscoding = false,
            MediaStreams =
            [
                new JellyfinMediaStream
                {
                    Codec = codec,
                    Type = "Audio",
                    Index = 0,
                    IsDefault = true,
                    BitRate = song.BitRate > 0 ? song.BitRate * 1000 : null,
                    SampleRate = song.SamplingRate > 0 ? song.SamplingRate : null,
                    Channels = song.ChannelCount > 0 ? song.ChannelCount : null,
                    DisplayTitle = $"{codec.ToUpperInvariant()} - {(song.ChannelCount == 2 ? "Stereo" : song.ChannelCount == 1 ? "Mono" : $"{song.ChannelCount} channels")}"
                }
            ]
        };
    }

    private static string GetCodecFromContainer(string container)
    {
        return container.ToLowerInvariant() switch
        {
            "mp3" => "mp3",
            "flac" => "flac",
            "m4a" => "aac",
            "aac" => "aac",
            "ogg" => "vorbis",
            "opus" => "opus",
            "wav" => "pcm",
            "wma" => "wma",
            _ => container
        };
    }

    private static string GetSongFilePath(Common.Data.Models.Song song)
    {
        var album = song.Album;
        var artist = album?.Artist;
        var library = artist?.Library;
        if (library == null || artist == null || album == null)
        {
            return string.Empty;
        }
        return Path.Combine(library.Path, artist.Directory, album.Directory, song.FileName);
    }

    private static HashSet<string> ParseItemTypes(string? includeItemTypes)
    {
        if (string.IsNullOrWhiteSpace(includeItemTypes))
        {
            return [];
        }

        return includeItemTypes.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => SupportedItemTypes.Contains(t.Trim()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<IActionResult> HandleRangeRequestAsync(string filePath, long fileLength, string rangeHeader, string contentType, CancellationToken cancellationToken)
    {
        if (!rangeHeader.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
        {
            return JellyfinRangeNotSatisfiable();
        }

        var rangeSpec = rangeHeader[6..];
        var dashIndex = rangeSpec.IndexOf('-');
        if (dashIndex < 0)
        {
            return JellyfinRangeNotSatisfiable();
        }

        long start;
        long end;

        var startPart = rangeSpec[..dashIndex];
        var endPart = rangeSpec[(dashIndex + 1)..];

        if (string.IsNullOrEmpty(startPart))
        {
            if (!long.TryParse(endPart, out var suffixLength) || suffixLength <= 0)
            {
                return JellyfinRangeNotSatisfiable();
            }
            start = Math.Max(0, fileLength - suffixLength);
            end = fileLength - 1;
        }
        else
        {
            if (!long.TryParse(startPart, out start) || start < 0 || start >= fileLength)
            {
                Response.Headers.Append("Content-Range", $"bytes */{fileLength}");
                return JellyfinRangeNotSatisfiable();
            }
            end = string.IsNullOrEmpty(endPart) ? fileLength - 1 : long.TryParse(endPart, out var parsedEnd) ? Math.Min(parsedEnd, fileLength - 1) : fileLength - 1;
        }

        if (start > end)
        {
            Response.Headers.Append("Content-Range", $"bytes */{fileLength}");
            return JellyfinRangeNotSatisfiable();
        }

        var length = end - start + 1;
        Response.StatusCode = StatusCodes.Status206PartialContent;
        Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{fileLength}");
        Response.Headers.Append("Content-Length", length.ToString());
        Response.ContentType = contentType;

        await using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, StreamBufferSize, true);
        fileStream.Seek(start, SeekOrigin.Begin);

        var buffer = new byte[StreamBufferSize];
        var remaining = length;
        while (remaining > 0 && !cancellationToken.IsCancellationRequested)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = await fileStream.ReadAsync(buffer.AsMemory(0, toRead), cancellationToken);
            if (read == 0) break;
            await Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            remaining -= read;
        }

        return new EmptyResult();
    }

    private static string GetContentTypeForFile(string filePath)
    {
        var ext = Path.GetExtension(filePath)?.ToLowerInvariant();
        return ext switch
        {
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".aac" => "audio/aac",
            ".wav" => "audio/wav",
            ".opus" => "audio/opus",
            _ => "application/octet-stream"
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
