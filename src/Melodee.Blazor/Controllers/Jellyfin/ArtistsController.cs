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
public class ArtistsController(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> dbContextFactory,
    IClock clock,
    ILoggerFactory loggerFactory) : JellyfinControllerBase(etagRepository, serializer, configuration, configurationFactory, dbContextFactory, clock, loggerFactory)
{
    [HttpGet]
    public async Task<IActionResult> GetArtistsAsync(
        [FromQuery] string? searchTerm,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? parentId,
        [FromQuery] string? fields,
        [FromQuery] bool? enableUserData,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        var skip = Math.Max(0, startIndex ?? 0);
        var take = Math.Clamp(limit ?? 100, 1, 500);

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
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
                a.Id,
                a.ApiKey,
                a.Name,
                a.SortName,
                a.Description,
                a.CreatedAt,
                a.LastUpdatedAt,
                AlbumCount = a.Albums.Count(alb => !alb.IsLocked)
            })
            .ToListAsync(cancellationToken);

        var items = artists.Select(a => new JellyfinBaseItem
        {
            Name = a.Name,
            ServerId = GetServerId(),
            Id = ToJellyfinId(a.ApiKey),
            Etag = ComputeEtag(a.ApiKey, a.LastUpdatedAt ?? a.CreatedAt),
            DateCreated = FormatInstantForJellyfin(a.CreatedAt),
            SortName = a.SortName ?? a.Name,
            Type = "MusicArtist",
            IsFolder = true,
            CanDownload = user.HasDownloadRole,
            Overview = a.Description,
            ChildCount = a.AlbumCount,
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            MediaType = "Audio",
            UserData = enableUserData == true ? new JellyfinUserItemData
            {
                PlaybackPositionTicks = 0,
                PlayCount = 0,
                IsFavorite = false,
                Played = false,
                Key = a.ApiKey.ToString("N")
            } : null
        }).ToArray();

        // Compute collection ETag based on latest update time
        var latestUpdate = artists.Any()
            ? artists.Max(a => a.LastUpdatedAt ?? a.CreatedAt)
            : Clock.GetCurrentInstant();
        var collectionEtag = ComputeCollectionEtag(totalCount, skip, take, latestUpdate);

        if (IsNotModified(collectionEtag))
        {
            return NotModified(collectionEtag);
        }

        SetETagHeader(collectionEtag);
        return Ok(new JellyfinArtistsResult
        {
            Items = items,
            TotalRecordCount = totalCount,
            StartIndex = skip
        });
    }

    /// <summary>
    /// Gets album artists - Finamp uses this endpoint for browsing artists.
    /// </summary>
    [HttpGet("AlbumArtists")]
    public async Task<IActionResult> GetAlbumArtistsAsync(
        [FromQuery] string? searchTerm,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? parentId,
        [FromQuery] string? fields,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? filters,
        [FromQuery] bool? enableUserData,
        [FromQuery] string? userId,
        CancellationToken cancellationToken)
    {
        // AlbumArtists is the same as Artists for Melodee - we return all artists that have albums
        return await GetArtistsAsync(searchTerm, startIndex, limit, parentId, fields, enableUserData, cancellationToken);
    }

    [HttpGet("{artistId}")]
    public async Task<IActionResult> GetArtistAsync(string artistId, CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(artistId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid artist ID format.");
        }

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);
        var artist = await dbContext.Artists
            .AsNoTracking()
            .Where(a => a.ApiKey == apiKey && !a.IsLocked)
            .Select(a => new
            {
                a.Id,
                a.ApiKey,
                a.Name,
                a.SortName,
                a.Description,
                a.CreatedAt,
                a.LastUpdatedAt,
                AlbumCount = a.Albums.Count(alb => !alb.IsLocked)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (artist == null)
        {
            return JellyfinNotFound("Artist not found.");
        }

        var etag = ComputeEtag(artist.ApiKey, artist.LastUpdatedAt ?? artist.CreatedAt);
        if (IsNotModified(etag))
        {
            return NotModified(etag);
        }

        SetETagHeader(etag);
        return Ok(new JellyfinBaseItem
        {
            Name = artist.Name,
            ServerId = GetServerId(),
            Id = ToJellyfinId(artist.ApiKey),
            Etag = etag,
            DateCreated = FormatInstantForJellyfin(artist.CreatedAt),
            SortName = artist.SortName ?? artist.Name,
            Type = "MusicArtist",
            IsFolder = true,
            CanDownload = user.HasDownloadRole,
            Overview = artist.Description,
            ChildCount = artist.AlbumCount,
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            MediaType = "Audio"
        });
    }

    /// <summary>
    /// Gets similar artists based on shared genres. Used by Feishin for artist recommendations.
    /// </summary>
    [HttpGet("{artistId}/Similar")]
    public async Task<IActionResult> GetSimilarArtistsAsync(
        string artistId,
        [FromQuery] int? limit,
        [FromQuery] string? fields,
        CancellationToken cancellationToken)
    {
        var user = await AuthenticateJellyfinAsync(cancellationToken);
        if (user == null)
        {
            return JellyfinUnauthorized();
        }

        if (!TryParseJellyfinGuid(artistId, out var apiKey))
        {
            return JellyfinBadRequest("Invalid artist ID format.");
        }

        var maxItems = Math.Clamp(limit ?? 10, 1, 50);

        await using var dbContext = await DbContextFactory.CreateDbContextAsync(cancellationToken);

        var seedArtist = await dbContext.Artists
            .AsNoTracking()
            .Include(a => a.Albums)
            .Where(a => a.ApiKey == apiKey && !a.IsLocked)
            .FirstOrDefaultAsync(cancellationToken);

        if (seedArtist == null)
        {
            return JellyfinNotFound("Artist not found.");
        }

        var artistGenres = seedArtist.Albums
            .Where(a => !a.IsLocked && a.Genres != null)
            .SelectMany(a => a.Genres!)
            .Distinct()
            .ToHashSet();

        var similarArtists = await dbContext.Artists
            .AsNoTracking()
            .Include(a => a.Albums)
            .Where(a => !a.IsLocked && a.Id != seedArtist.Id)
            .ToListAsync(cancellationToken);

        var scoredArtists = similarArtists
            .Select(a => new
            {
                Artist = a,
                GenreOverlap = a.Albums
                    .Where(alb => !alb.IsLocked && alb.Genres != null)
                    .SelectMany(alb => alb.Genres!)
                    .Distinct()
                    .Count(g => artistGenres.Contains(g))
            })
            .Where(x => x.GenreOverlap > 0)
            .OrderByDescending(x => x.GenreOverlap)
            .ThenBy(x => x.Artist.SortName ?? x.Artist.Name)
            .Take(maxItems)
            .ToList();

        var items = scoredArtists.Select(x => new JellyfinBaseItem
        {
            Name = x.Artist.Name,
            ServerId = GetServerId(),
            Id = ToJellyfinId(x.Artist.ApiKey),
            Etag = ComputeEtag(x.Artist.ApiKey, x.Artist.LastUpdatedAt ?? x.Artist.CreatedAt),
            DateCreated = FormatInstantForJellyfin(x.Artist.CreatedAt),
            SortName = x.Artist.SortName ?? x.Artist.Name,
            Type = "MusicArtist",
            IsFolder = true,
            CanDownload = user.HasDownloadRole,
            Overview = x.Artist.Description,
            ChildCount = x.Artist.Albums.Count(alb => !alb.IsLocked),
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            MediaType = "Audio"
        }).ToArray();

        return Ok(new JellyfinArtistsResult
        {
            Items = items,
            TotalRecordCount = items.Length,
            StartIndex = 0
        });
    }

    private static string ComputeEtag(Guid apiKey, Instant lastUpdated)
    {
        var input = $"{apiKey:N}-{lastUpdated.ToUnixTimeTicks()}";
        return HashHelper.CreateMd5(input) ?? string.Empty;
    }

    private static string ComputeCollectionEtag(int totalCount, int skip, int take, Instant latestUpdate)
    {
        var input = $"collection-{totalCount}-{skip}-{take}-{latestUpdate.ToUnixTimeTicks()}";
        return HashHelper.CreateMd5(input) ?? string.Empty;
    }
}
