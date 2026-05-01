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

/// <summary>
/// Jellyfin-compatible MusicGenres endpoint. Used by Feishin for genre browsing.
/// This is an alias for the Genres endpoint at the /MusicGenres path.
/// </summary>
[ApiController]
[Route("api/jf/[controller]")]
[ApiExplorerSettings(GroupName = "jellyfin")]
[EnableRateLimiting("jellyfin-api")]
public class MusicGenresController(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> dbContextFactory,
    IClock clock,
    ILoggerFactory loggerFactory) : JellyfinControllerBase(etagRepository, serializer, configuration, configurationFactory, dbContextFactory, clock, loggerFactory)
{
    /// <summary>
    /// Gets all music genres. Used by Feishin for genre browsing.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMusicGenresAsync(
        [FromQuery] string? searchTerm,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? parentId,
        [FromQuery] string? userId,
        [FromQuery] bool? recursive,
        [FromQuery] bool? enableTotalRecordCount,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortOrder,
        [FromQuery] string? fields,
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

        var genresQuery = dbContext.Albums
            .AsNoTracking()
            .Where(a => !a.IsLocked && a.Genres != null && a.Genres.Any())
            .SelectMany(a => a.Genres!)
            .Distinct();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var normalizedSearch = searchTerm.ToUpperInvariant();
            genresQuery = genresQuery.Where(g => g.ToUpper().Contains(normalizedSearch));
        }

        var allGenres = await genresQuery.ToListAsync(cancellationToken);
        var totalCount = allGenres.Count;

        var genres = allGenres
            .OrderBy(g => g)
            .Skip(skip)
            .Take(take)
            .ToList();

        var now = Clock.GetCurrentInstant();
        var items = genres.Select(genre => new JellyfinBaseItem
        {
            Name = genre,
            ServerId = GetServerId(),
            Id = ToJellyfinId(ComputeGenreGuid(genre)),
            DateCreated = FormatInstantForJellyfin(now),
            SortName = genre,
            Type = "MusicGenre",
            IsFolder = true,
            CanDownload = false,
            ImageTags = new Dictionary<string, string>(),
            BackdropImageTags = [],
            MediaType = "Audio"
        }).ToArray();

        var collectionEtag = ComputeCollectionEtag(totalCount, skip, take, now);

        if (IsNotModified(collectionEtag))
        {
            return NotModified(collectionEtag);
        }

        SetETagHeader(collectionEtag);
        return Ok(new JellyfinItemsResult
        {
            Items = items,
            TotalRecordCount = totalCount,
            StartIndex = skip
        });
    }

    private static Guid ComputeGenreGuid(string genre)
    {
        // NOTE: MD5 is used here for deterministic GUID generation from genre names for Jellyfin API compatibility.
        // This is NOT a cryptographic use - it's purely for generating stable genre identifiers.
        // lgtm[cs/weak-crypto] MD5 used for non-cryptographic GUID generation, not for security
        var hashHex = HashHelper.CreateMd5($"genre:{genre.ToUpperInvariant()}");
        return Guid.TryParse(hashHex, out var result) ? result : Guid.Empty;
    }

    private static string ComputeCollectionEtag(int totalCount, int skip, int take, Instant latestUpdate)
    {
        var input = $"musicgenres-{totalCount}-{skip}-{take}-{latestUpdate.ToUnixTimeTicks()}";
        // NOTE: MD5 is used here for generating ETag values for HTTP caching in Jellyfin API compatibility.
        // This is NOT a cryptographic use - ETags are public cache identifiers, not security tokens.
        // lgtm[cs/weak-crypto] MD5 used for non-cryptographic ETag generation, not for security
        return HashHelper.CreateMd5(input) ?? string.Empty;
    }
}
