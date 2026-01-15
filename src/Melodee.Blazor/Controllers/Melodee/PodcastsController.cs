using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data.Models;
using Melodee.Common.Filtering;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Blazor.Controllers.Melodee;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/podcasts")]
public sealed class PodcastsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    PodcastService podcastService,
    PodcastPlaybackService? podcastPlaybackService,
    PodcastOpmlService? podcastOpmlService,
    PodcastDiscoveryService? podcastDiscoveryService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    ILogger<PodcastsController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private ILogger<PodcastsController> Logger { get; } = logger;

    /// <summary>
    /// List podcast channels.
    /// </summary>
    [HttpGet]
    [Route("channels")]
    [ProducesResponseType(typeof(PagedResult<PodcastChannelDataInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListChannelsAsync(
        short page = 1,
        short pageSize = 50,
        string? orderBy = null,
        string? orderDirection = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();
        if (user.IsLocked) return ApiUserLocked();

        if (!TryValidatePaging(page, pageSize, out var validatedPage, out var validatedPageSize, out var pagingError))
            return pagingError!;

        var pagedRequest = new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedPageSize,
            OrderBy = new Dictionary<string, string> { { orderBy ?? "Title", orderDirection ?? "ASC" } }
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            pagedRequest.FilterBy = new[] { new FilterOperatorInfo(nameof(PodcastChannelDataInfo.TitleNormalized), FilterOperator.Contains, search) };
        }

        var result = await podcastService.ListChannelsAsync(pagedRequest, user.Id, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Create a new podcast channel.
    /// </summary>
    [HttpPost]
    [Route("channels")]
    [ProducesResponseType(typeof(PodcastChannel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateChannelAsync([FromBody] CreateChannelRequest request, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (string.IsNullOrWhiteSpace(request?.Url))
            return ApiBadRequest("Feed URL is required");

        var result = await podcastService.CreateChannelAsync(user.Id, request.Url, cancellationToken);
        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Failed to create channel");

        return Ok(result.Data);
    }

    /// <summary>
    /// Refresh a podcast channel.
    /// </summary>
    [HttpPost]
    [Route("channels/{id:int}/refresh")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RefreshChannelAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        var channel = await podcastService.GetChannelAsync(id, user.Id, cancellationToken);
        if (channel.Data == null) return ApiNotFound("Channel");

        var result = await podcastService.RefreshChannelAsync(id, cancellationToken);
        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Failed to refresh channel");

        return Ok();
    }

    /// <summary>
    /// Delete a podcast channel.
    /// </summary>
    [HttpDelete]
    [Route("channels/{id:int}")]
    public async Task<IActionResult> DeleteChannelAsync(int id, bool softDelete = true, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        var result = await podcastService.DeleteChannelAsync(id, user.Id, softDelete, cancellationToken);
        if (!result.IsSuccess) return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error deleting channel");

        return Ok();
    }

    /// <summary>
    /// Update podcast channel settings.
    /// </summary>
    [HttpPatch]
    [Route("channels/{id:int}")]
    [ProducesResponseType(typeof(PodcastChannel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateChannelAsync(int id, [FromBody] UpdateChannelRequest request, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        var result = await podcastService.UpdateChannelAsync(
            id,
            user.Id,
            request.AutoDownloadEnabled,
            request.RefreshIntervalHours,
            request.MaxDownloadedEpisodes,
            request.MaxStorageBytes,
            cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.Type == OperationResponseType.NotFound)
                return ApiNotFound("Channel");
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error updating channel");
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// List episodes for a channel.
    /// </summary>
    [HttpGet]
    [Route("channels/{id:int}/episodes")]
    public async Task<IActionResult> ListEpisodesAsync(
        int id,
        short page = 1,
        short pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        var channel = await podcastService.GetChannelAsync(id, user.Id, cancellationToken);
        if (channel.Data == null) return ApiNotFound("Channel");

        if (!TryValidatePaging(page, pageSize, out var validatedPage, out var validatedPageSize, out var pagingError))
            return pagingError!;

        var offset = (validatedPage - 1) * validatedPageSize;
        var limit = validatedPageSize;

        var result = await podcastService.ListEpisodesAsync(id, user.Id, limit, offset, cancellationToken);

        if (!result.IsSuccess) return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error listing episodes");

        return Ok(new
        {
            Data = result.Data!.Select(x => new
            {
                x.Id,
                x.Title,
                x.PublishDate,
                x.DownloadStatus,
                x.Duration,
                x.EnclosureLength,
                x.Guid
            }),
            TotalCount = result.AdditionalData!.TryGetValue("TotalCount", out var count) && count is int intCount ? intCount : 0
        });
    }

    /// <summary>
    /// Download an episode.
    /// </summary>
    [HttpPost]
    [Route("episodes/{id:int}/download")]
    public async Task<IActionResult> DownloadEpisodeAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        var result = await podcastService.QueueDownloadAsync(id, user.Id, cancellationToken);
        if (!result.IsSuccess) return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error queuing download");

        return Ok();
    }

    /// <summary>
    /// Delete an episode.
    /// </summary>
    [HttpDelete]
    [Route("episodes/{id:int}")]
    public async Task<IActionResult> DeleteEpisodeAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        var result = await podcastService.DeleteEpisodeAsync(id, user.Id, cancellationToken);
        if (!result.IsSuccess) return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error deleting episode");

        return Ok();
    }

    /// <summary>
    /// Update "now playing" status for a podcast episode (heartbeat mechanism).
    /// </summary>
    [HttpPost]
    [Route("episodes/{id:int}/play")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> NowPlayingAsync(
        int id,
        [FromQuery] int? secondsPlayed = null,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastPlaybackService == null)
        {
            return ApiBadRequest("Podcast playback tracking is not configured");
        }

        var result = await podcastPlaybackService.NowPlayingAsync(
            user.Id,
            id,
            secondsPlayed,
            "Melodee.Blazor");

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error updating now playing status");

        return Ok();
    }

    /// <summary>
    /// Get bookmark (resume position) for a podcast episode.
    /// </summary>
    [HttpGet]
    [Route("episodes/{id:int}/bookmark")]
    [ProducesResponseType(typeof(PodcastEpisodeBookmark), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBookmarkAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastPlaybackService == null)
        {
            return ApiBadRequest("Podcast playback tracking is not configured");
        }

        var result = await podcastPlaybackService.GetBookmarkAsync(user.Id, id, cancellationToken);

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error retrieving bookmark");

        return Ok(result.Data);
    }

    /// <summary>
    /// Create or update a bookmark (resume position) for a podcast episode.
    /// </summary>
    [HttpPut]
    [Route("episodes/{id:int}/bookmark")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveBookmarkAsync(
        int id,
        [FromBody] SaveBookmarkRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastPlaybackService == null)
        {
            return ApiBadRequest("Podcast playback tracking is not configured");
        }

        var result = await podcastPlaybackService.SaveBookmarkAsync(
            user.Id,
            id,
            request.PositionSeconds,
            request.Comment,
            cancellationToken);

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error saving bookmark");

        return Ok();
    }

    /// <summary>
    /// Delete a bookmark for a podcast episode.
    /// </summary>
    [HttpDelete]
    [Route("episodes/{id:int}/bookmark")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteBookmarkAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastPlaybackService == null)
        {
            return ApiBadRequest("Podcast playback tracking is not configured");
        }

        var result = await podcastPlaybackService.DeleteBookmarkAsync(user.Id, id, cancellationToken);

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error deleting bookmark");

        return Ok();
    }

    /// <summary>
    /// Get play history for podcast episodes.
    /// </summary>
    [HttpGet]
    [Route("episodes/{id:int}/history")]
    [ProducesResponseType(typeof(IEnumerable<UserPodcastEpisodePlayHistory>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPlayHistoryAsync(
        int id,
        [FromQuery] int limit = 50,
        [FromQuery] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastPlaybackService == null)
        {
            return ApiBadRequest("Podcast playback tracking is not configured");
        }

        var result = await podcastPlaybackService.GetPlayHistoryAsync(user.Id, id, limit, offset, cancellationToken);

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error retrieving play history");

        return Ok(result.Data);
    }

    /// <summary>
    /// Search podcast episodes by title and description.
    /// </summary>
    [HttpGet]
    [Route("episodes/search")]
    [ProducesResponseType(typeof(PagedResult<PodcastEpisodeDataInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchEpisodesAsync(
        [FromQuery] string query,
        [FromQuery] short page = 1,
        [FromQuery] short pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();
        if (user.IsLocked) return ApiUserLocked();

        if (string.IsNullOrWhiteSpace(query))
            return ApiBadRequest("Search query is required");

        if (!TryValidatePaging(page, pageSize, out var validatedPage, out var validatedPageSize, out var pagingError))
            return pagingError!;

        var pagedRequest = new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedPageSize
        };

        var result = await podcastService.SearchEpisodesAsync(query, user.Id, pagedRequest, cancellationToken).ConfigureAwait(false);
        return Ok(result);
    }

    /// <summary>
    /// Export podcast subscriptions to OPML format.
    /// </summary>
    [HttpGet]
    [Route("opml/export")]
    [Produces("application/xml")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "application/xml")]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportOpmlAsync(CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastOpmlService == null)
        {
            return ApiBadRequest("OPML service is not configured");
        }

        var result = await podcastOpmlService.ExportAsync(user.Id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error exporting OPML");

        return Content(result.Data!, "application/xml");
    }

    /// <summary>
    /// Import podcast subscriptions from OPML format.
    /// </summary>
    [HttpPost]
    [Route("opml/import")]
    [Consumes("application/xml", "text/xml")]
    [ProducesResponseType(typeof(OpmlImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportOpmlAsync(CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastOpmlService == null)
        {
            return ApiBadRequest("OPML service is not configured");
        }

        using var reader = new StreamReader(Request.Body);
        var opmlContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(opmlContent))
            return ApiBadRequest("OPML content is required");

        var result = await podcastOpmlService.ImportAsync(user.Id, opmlContent, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error importing OPML");

        return Ok(result.Data);
    }

    /// <summary>
    /// Search podcast directories for new podcasts to subscribe to.
    /// Uses iTunes Search API.
    /// </summary>
    [HttpGet]
    [Route("discover/search")]
    [ProducesResponseType(typeof(PodcastSearchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DiscoverSearchAsync(
        [FromQuery] string query,
        [FromQuery] int limit = 25,
        [FromQuery] string? country = "US",
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastDiscoveryService == null)
        {
            return ApiBadRequest("Podcast discovery service is not configured");
        }

        if (string.IsNullOrWhiteSpace(query))
            return ApiBadRequest("Search query is required");

        var result = await podcastDiscoveryService.SearchAsync(query, limit, country, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error searching podcasts");

        return Ok(result.Data);
    }

    /// <summary>
    /// Get trending/popular podcasts from directory.
    /// </summary>
    [HttpGet]
    [Route("discover/trending")]
    [ProducesResponseType(typeof(PodcastSearchResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DiscoverTrendingAsync(
        [FromQuery] int limit = 25,
        [FromQuery] string? genre = null,
        [FromQuery] string? country = "US",
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastDiscoveryService == null)
        {
            return ApiBadRequest("Podcast discovery service is not configured");
        }

        var result = await podcastDiscoveryService.GetTrendingAsync(limit, genre, country, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error getting trending podcasts");

        return Ok(result.Data);
    }

    /// <summary>
    /// Lookup a specific podcast by iTunes ID.
    /// </summary>
    [HttpGet]
    [Route("discover/lookup/{itunesId}")]
    [ProducesResponseType(typeof(PodcastSearchItem), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DiscoverLookupAsync(
        string itunesId,
        CancellationToken cancellationToken = default)
    {
        var user = await ResolveUserAsync(userProfileService, cancellationToken).ConfigureAwait(false);
        if (user == null) return ApiUnauthorized();

        if (podcastDiscoveryService == null)
        {
            return ApiBadRequest("Podcast discovery service is not configured");
        }

        var result = await podcastDiscoveryService.LookupByItunesIdAsync(itunesId, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
            return ApiBadRequest(result.Messages?.FirstOrDefault() ?? "Error looking up podcast");

        if (result.Data == null)
            return ApiNotFound("Podcast");

        return Ok(result.Data);
    }
}

public record CreateChannelRequest(string Url);
public record SaveBookmarkRequest(int PositionSeconds, string? Comment = null);

/// <summary>
/// Request to update podcast channel settings.
/// All fields are optional - only provided fields will be updated.
/// </summary>
public record UpdateChannelRequest(
    bool? AutoDownloadEnabled = null,
    int? RefreshIntervalHours = null,
    int? MaxDownloadedEpisodes = null,
    long? MaxStorageBytes = null);
