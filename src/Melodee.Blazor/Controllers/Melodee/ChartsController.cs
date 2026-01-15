using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Read-only API for curated album charts.
/// All chart management operations must be performed through the Admin UI.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/charts")]
public sealed class ChartsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    ChartService chartService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// List all visible charts with pagination and optional filtering.
    /// </summary>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="pageSize">Number of items per page (default: 20)</param>
    /// <param name="tags">Filter by tags (AND semantics, case-insensitive)</param>
    /// <param name="year">Filter by year</param>
    /// <param name="source">Filter by source name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [ProducesResponseType(typeof(ChartPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(
        short page = 1,
        short pageSize = 20,
        [FromQuery] string[]? tags = null,
        int? year = null,
        string? source = null,
        CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        if (!TryValidatePaging(page, pageSize, out var validatedPage, out var validatedPageSize, out var pagingError))
        {
            return pagingError!;
        }

        var charts = await chartService.ListAsync(
            new PagedRequest { Page = validatedPage, PageSize = validatedPageSize },
            includeHidden: false,
            filterByTags: tags,
            filterByYear: year,
            filterBySource: source,
            cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new
        {
            meta = new PaginationMetadata(
                charts.TotalCount,
                validatedPageSize,
                validatedPage,
                charts.TotalPages
            ),
            data = charts.Data.Select(x => x.ToChartListModel(baseUrl)).ToArray()
        });
    }

    /// <summary>
    /// Get a chart by ID or slug.
    /// </summary>
    /// <param name="idOrSlug">Chart ID or slug</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [Route("{idOrSlug}")]
    [ProducesResponseType(typeof(ChartDetailModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdOrSlug(string idOrSlug, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        Common.Data.Models.Chart? chart = null;

        if (int.TryParse(idOrSlug, out var id))
        {
            var result = await chartService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            chart = result.Data;
        }
        else
        {
            var result = await chartService.GetBySlugAsync(idOrSlug, cancellationToken).ConfigureAwait(false);
            chart = result.Data;
        }

        if (chart == null || !chart.IsVisible)
        {
            return ApiNotFound("Chart");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(chart.ToChartDetailModel(baseUrl));
    }

    /// <summary>
    /// Get generated playlist tracks for a chart.
    /// </summary>
    /// <param name="idOrSlug">Chart ID or slug</param>
    /// <param name="cancellationToken">Cancellation token</param>
    [HttpGet]
    [Route("{idOrSlug}/playlist")]
    [ProducesResponseType(typeof(ChartPlaylistResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPlaylistTracks(string idOrSlug, CancellationToken cancellationToken = default)
    {
        if (!ApiRequest.IsAuthorized)
        {
            return ApiUnauthorized();
        }

        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return ApiUnauthorized();
        }

        if (user.IsLocked)
        {
            return ApiUserLocked();
        }

        Common.Data.Models.Chart? chart = null;

        if (int.TryParse(idOrSlug, out var id))
        {
            var result = await chartService.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
            chart = result.Data;
        }
        else
        {
            var result = await chartService.GetBySlugAsync(idOrSlug, cancellationToken).ConfigureAwait(false);
            chart = result.Data;
        }

        if (chart == null || !chart.IsVisible)
        {
            return ApiNotFound("Chart");
        }

        if (!chart.IsGeneratedPlaylistEnabled)
        {
            return ApiBadRequest("Generated playlist is not enabled for this chart.");
        }

        var tracksResult = await chartService.GetGeneratedPlaylistTracksAsync(chart.Id, cancellationToken).ConfigureAwait(false);
        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new ChartPlaylistResponse
        {
            ChartId = chart.Id,
            ChartSlug = chart.Slug,
            ChartTitle = chart.Title,
            PlaylistId = $"chart:{chart.Id}",
            TrackCount = tracksResult.Data?.Count() ?? 0,
            Tracks = tracksResult.Data?.Select(t => t.ToChartTrackModel(baseUrl)).ToArray() ?? []
        });
    }
}

public sealed record ChartPagedResponse
{
    public required PaginationMetadata Meta { get; init; }
    public required ChartListModel[] Data { get; init; }
}

public record ChartListModel
{
    public int Id { get; init; }
    public Guid ApiKey { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public string? SourceName { get; init; }
    public string? SourceUrl { get; init; }
    public int? Year { get; init; }
    public string? Description { get; init; }
    public string[]? Tags { get; init; }
    public int ItemCount { get; init; }
    public bool IsGeneratedPlaylistEnabled { get; init; }
    public string? PlaylistId { get; init; }
    public string? ImageUrl { get; init; }
}

public sealed record ChartDetailModel : ChartListModel
{
    public required ChartItemModel[] Items { get; init; }
}

public sealed record ChartItemModel
{
    public int Id { get; init; }
    public int Rank { get; init; }
    public required string ArtistName { get; init; }
    public required string AlbumTitle { get; init; }
    public int? ReleaseYear { get; init; }
    public string LinkStatus { get; init; } = "Unlinked";
    public int? LinkedArtistId { get; init; }
    public Guid? LinkedArtistApiKey { get; init; }
    public string? LinkedArtistName { get; init; }
    public int? LinkedAlbumId { get; init; }
    public Guid? LinkedAlbumApiKey { get; init; }
    public string? LinkedAlbumName { get; init; }
    public string? AlbumImageUrl { get; init; }
}

public sealed record ChartPlaylistResponse
{
    public int ChartId { get; init; }
    public required string ChartSlug { get; init; }
    public required string ChartTitle { get; init; }
    public required string PlaylistId { get; init; }
    public int TrackCount { get; init; }
    public required ChartTrackModel[] Tracks { get; init; }
}

public sealed record ChartTrackModel
{
    public int ChartRank { get; init; }
    public int SongId { get; init; }
    public Guid SongApiKey { get; init; }
    public required string SongTitle { get; init; }
    public int AlbumId { get; init; }
    public Guid AlbumApiKey { get; init; }
    public required string AlbumName { get; init; }
    public int ArtistId { get; init; }
    public Guid ArtistApiKey { get; init; }
    public required string ArtistName { get; init; }
    public int SongNumber { get; init; }
    public double Duration { get; init; }
    public string? CoverArtUrl { get; init; }
}

public static class ChartModelExtensions
{
    public static ChartListModel ToChartListModel(this Common.Data.Models.Chart chart, string baseUrl)
    {
        return new ChartListModel
        {
            Id = chart.Id,
            ApiKey = chart.ApiKey,
            Slug = chart.Slug,
            Title = chart.Title,
            SourceName = chart.SourceName,
            SourceUrl = chart.SourceUrl,
            Year = chart.Year,
            Description = chart.Description,
            Tags = chart.Tags?.Split('|', StringSplitOptions.RemoveEmptyEntries),
            ItemCount = chart.Items.Count,
            IsGeneratedPlaylistEnabled = chart.IsGeneratedPlaylistEnabled,
            PlaylistId = chart.IsGeneratedPlaylistEnabled ? $"chart:{chart.Id}" : null,
            ImageUrl = $"{baseUrl}/api/v1/charts/{chart.ApiKey}/image"
        };
    }

    public static ChartDetailModel ToChartDetailModel(this Common.Data.Models.Chart chart, string baseUrl)
    {
        return new ChartDetailModel
        {
            Id = chart.Id,
            ApiKey = chart.ApiKey,
            Slug = chart.Slug,
            Title = chart.Title,
            SourceName = chart.SourceName,
            SourceUrl = chart.SourceUrl,
            Year = chart.Year,
            Description = chart.Description,
            Tags = chart.Tags?.Split('|', StringSplitOptions.RemoveEmptyEntries),
            ItemCount = chart.Items.Count,
            IsGeneratedPlaylistEnabled = chart.IsGeneratedPlaylistEnabled,
            PlaylistId = chart.IsGeneratedPlaylistEnabled ? $"chart:{chart.Id}" : null,
            ImageUrl = $"{baseUrl}/api/v1/charts/{chart.ApiKey}/image",
            Items = chart.Items.OrderBy(i => i.Rank).Select(i => i.ToChartItemModel(baseUrl)).ToArray()
        };
    }

    public static ChartItemModel ToChartItemModel(this Common.Data.Models.ChartItem item, string baseUrl)
    {
        return new ChartItemModel
        {
            Id = item.Id,
            Rank = item.Rank,
            ArtistName = item.ArtistName,
            AlbumTitle = item.AlbumTitle,
            ReleaseYear = item.ReleaseYear,
            LinkStatus = item.LinkStatusValue.ToString(),
            LinkedArtistId = item.LinkedArtistId,
            LinkedArtistApiKey = item.LinkedArtist?.ApiKey,
            LinkedArtistName = item.LinkedArtist?.Name,
            LinkedAlbumId = item.LinkedAlbumId,
            LinkedAlbumApiKey = item.LinkedAlbum?.ApiKey,
            LinkedAlbumName = item.LinkedAlbum?.Name,
            AlbumImageUrl = item.LinkedAlbum != null ? $"{baseUrl}/api/v1/images/album/{item.LinkedAlbum.ApiKey}" : null
        };
    }

    public static ChartTrackModel ToChartTrackModel(this ChartPlaylistTrack track, string baseUrl)
    {
        return new ChartTrackModel
        {
            ChartRank = track.ChartRank,
            SongId = track.SongId,
            SongApiKey = track.SongApiKey,
            SongTitle = track.SongTitle,
            AlbumId = track.AlbumId,
            AlbumApiKey = track.AlbumApiKey,
            AlbumName = track.AlbumName,
            ArtistId = track.ArtistId,
            ArtistApiKey = track.ArtistApiKey,
            ArtistName = track.ArtistName,
            SongNumber = track.SongNumber,
            Duration = track.Duration,
            CoverArtUrl = $"{baseUrl}/api/v1/images/album/{track.AlbumApiKey}"
        };
    }
}
