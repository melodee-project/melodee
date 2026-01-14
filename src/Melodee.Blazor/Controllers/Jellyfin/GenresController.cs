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
public class GenresController(
    EtagRepository etagRepository,
    ISerializer serializer,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> dbContextFactory,
    IClock clock,
    ILoggerFactory loggerFactory) : JellyfinControllerBase(etagRepository, serializer, configuration, configurationFactory, dbContextFactory, clock, loggerFactory)
{
    /// <summary>
    /// Gets all music genres. Used by Finamp for genre browsing.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetGenresAsync(
        [FromQuery] string? searchTerm,
        [FromQuery] int? startIndex,
        [FromQuery] int? limit,
        [FromQuery] string? parentId,
        [FromQuery] string? includeItemTypes,
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

        // Get distinct genres from albums
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
        var hash = HashHelper.CreateMd5($"genre:{genre.ToUpperInvariant()}");
        return Guid.TryParse(hash?.Replace("-", ""), out var result) ? result : Guid.NewGuid();
    }

    private static string ComputeCollectionEtag(int totalCount, int skip, int take, Instant latestUpdate)
    {
        var input = $"genres-{totalCount}-{skip}-{take}-{latestUpdate.ToUnixTimeTicks()}";
        var hash = HashHelper.CreateMd5(input);
        return hash ?? Guid.NewGuid().ToString("N");
    }
}
