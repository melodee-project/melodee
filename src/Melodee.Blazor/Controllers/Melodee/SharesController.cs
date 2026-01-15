using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NodaTime;
using NodaTime.Text;

namespace Melodee.Blazor.Controllers.Melodee;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[RequireCapability(UserCapability.Share)]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/shares")]
public sealed class SharesController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    ShareService shareService,
    ArtistService artistService,
    AlbumService albumService,
    SongService songService,
    PlaylistService playlistService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// Get a share by API key.
    /// </summary>
    [HttpGet]
    [Route("{apiKey:guid}")]
    [ProducesResponseType(typeof(Models.Share), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetShareByApiKey(Guid apiKey, CancellationToken cancellationToken = default)
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

        var shareResult = await shareService.GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (!shareResult.IsSuccess || shareResult.Data == null)
        {
            return ApiNotFound("Share");
        }

        // Only allow users to access their own shares (unless admin)
        if (shareResult.Data.UserId != user.Id && !user.IsAdmin)
        {
            return ApiForbidden("You do not have permission to access this share.");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        var shareModel = await BuildShareModelAsync(shareResult.Data, baseUrl, user.ToUserModel(baseUrl), cancellationToken).ConfigureAwait(false);

        if (shareModel == null)
        {
            return ApiNotFound("Shared resource");
        }

        return Ok(shareModel);
    }

    /// <summary>
    /// List shares for the current user with pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(SharePagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync(short page, short pageSize, CancellationToken cancellationToken = default)
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

        var shares = await shareService.ListAsync(new PagedRequest
        {
            Page = validatedPage,
            PageSize = validatedPageSize,
            FilterBy =
            [
                new Common.Filtering.FilterOperatorInfo("UserId", Common.Filtering.FilterOperator.Equals, user.Id)
            ]
        }, cancellationToken).ConfigureAwait(false);

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        var userModel = user.ToUserModel(baseUrl);

        var shareModels = new List<Models.Share>();
        foreach (var share in shares.Data)
        {
            var shareModel = await BuildShareModelAsync(share, baseUrl, userModel, cancellationToken).ConfigureAwait(false);
            if (shareModel != null)
            {
                shareModels.Add(shareModel);
            }
        }

        return Ok(new
        {
            meta = new PaginationMetadata(
                shares.TotalCount,
                validatedPageSize,
                validatedPage,
                shares.TotalPages
            ),
            data = shareModels.ToArray()
        });
    }

    /// <summary>
    /// Create a new share for a resource.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Models.Share), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateShare([FromBody] CreateShareRequest request, CancellationToken cancellationToken = default)
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

        if (request.ShareType == ShareType.NotSet)
        {
            return ApiValidationError("ShareType is required.");
        }

        if (request.ResourceId == Guid.Empty)
        {
            return ApiValidationError("ResourceId is required.");
        }

        // Validate the resource exists and get its database ID
        var resourceId = await GetResourceIdAsync(request.ShareType, request.ResourceId, cancellationToken).ConfigureAwait(false);
        if (resourceId == null)
        {
            return ApiNotFound($"{request.ShareType}");
        }

        // Parse expiration date if provided
        Instant? expiresAt = null;
        if (!string.IsNullOrEmpty(request.ExpiresAt))
        {
            var parseResult = InstantPattern.ExtendedIso.Parse(request.ExpiresAt);
            if (!parseResult.Success)
            {
                return ApiValidationError("Invalid ExpiresAt format. Use ISO 8601 format.");
            }
            expiresAt = parseResult.Value;
        }

        var share = new Common.Data.Models.Share
        {
            UserId = user.Id,
            ShareId = resourceId.Value,
            ShareType = (int)request.ShareType,
            Description = request.Description,
            IsDownloadable = request.IsDownloadable,
            ExpiresAt = expiresAt,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        var createResult = await shareService.AddAsync(share, cancellationToken).ConfigureAwait(false);
        if (!createResult.IsSuccess || createResult.Data == null)
        {
            return ApiBadRequest(createResult.Messages?.FirstOrDefault() ?? "Unable to create share.");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        var shareModel = await BuildShareModelAsync(createResult.Data, baseUrl, user.ToUserModel(baseUrl), cancellationToken).ConfigureAwait(false);

        if (shareModel == null)
        {
            return ApiBadRequest("Share created but unable to retrieve details.");
        }

        return Ok(shareModel);
    }

    /// <summary>
    /// Update an existing share.
    /// </summary>
    [HttpPut]
    [Route("{apiKey:guid}")]
    [ProducesResponseType(typeof(Models.Share), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateShare(Guid apiKey, [FromBody] UpdateShareRequest request, CancellationToken cancellationToken = default)
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

        var shareResult = await shareService.GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (!shareResult.IsSuccess || shareResult.Data == null)
        {
            return ApiNotFound("Share");
        }

        // Only allow users to update their own shares (unless admin)
        if (shareResult.Data.UserId != user.Id && !user.IsAdmin)
        {
            return ApiForbidden("You do not have permission to update this share.");
        }

        var share = shareResult.Data;

        if (request.Description != null)
        {
            share.Description = request.Description;
        }

        if (request.IsDownloadable.HasValue)
        {
            share.IsDownloadable = request.IsDownloadable.Value;
        }

        if (request.ExpiresAt != null)
        {
            if (string.IsNullOrEmpty(request.ExpiresAt))
            {
                share.ExpiresAt = null;
            }
            else
            {
                var parseResult = InstantPattern.ExtendedIso.Parse(request.ExpiresAt);
                if (!parseResult.Success)
                {
                    return ApiValidationError("Invalid ExpiresAt format. Use ISO 8601 format.");
                }
                share.ExpiresAt = parseResult.Value;
            }
        }

        var updateResult = await shareService.UpdateAsync(share, cancellationToken).ConfigureAwait(false);
        if (!updateResult.IsSuccess)
        {
            return ApiBadRequest(updateResult.Messages?.FirstOrDefault() ?? "Unable to update share.");
        }

        // Fetch updated share
        var updatedShareResult = await shareService.GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (!updatedShareResult.IsSuccess || updatedShareResult.Data == null)
        {
            return ApiBadRequest("Share updated but unable to retrieve details.");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        var shareModel = await BuildShareModelAsync(updatedShareResult.Data, baseUrl, user.ToUserModel(baseUrl), cancellationToken).ConfigureAwait(false);

        if (shareModel == null)
        {
            return ApiBadRequest("Share updated but unable to retrieve resource details.");
        }

        return Ok(shareModel);
    }

    /// <summary>
    /// Delete a share.
    /// </summary>
    [HttpDelete]
    [Route("{apiKey:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteShare(Guid apiKey, CancellationToken cancellationToken = default)
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

        var shareResult = await shareService.GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (!shareResult.IsSuccess || shareResult.Data == null)
        {
            return ApiNotFound("Share");
        }

        // Only allow users to delete their own shares (unless admin)
        if (shareResult.Data.UserId != user.Id && !user.IsAdmin)
        {
            return ApiForbidden("You do not have permission to delete this share.");
        }

        var deleteResult = await shareService.DeleteAsync(user.Id, [shareResult.Data.Id], cancellationToken).ConfigureAwait(false);
        if (!deleteResult.IsSuccess)
        {
            return ApiBadRequest(deleteResult.Messages?.FirstOrDefault() ?? "Unable to delete share.");
        }

        return Ok();
    }

    /// <summary>
    /// Get public share content by share unique ID (anonymous access).
    /// </summary>
    [HttpGet]
    [Route("public/{shareUniqueId}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PublicShareResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status410Gone)]
    public async Task<IActionResult> GetPublicShare(string shareUniqueId, CancellationToken cancellationToken = default)
    {
        var shareResult = await shareService.GetByUniqueIdAsync(shareUniqueId, cancellationToken).ConfigureAwait(false);
        if (!shareResult.IsSuccess || shareResult.Data == null)
        {
            return ApiNotFound("Share");
        }

        var share = shareResult.Data;

        // Check if share has expired
        if (share.ExpiresAt.HasValue && share.ExpiresAt.Value < SystemClock.Instance.GetCurrentInstant())
        {
            return StatusCode(StatusCodes.Status410Gone, new ApiError(ApiError.Codes.NotFound, "Share has expired", GetCorrelationId()));
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        var publicShare = await BuildPublicShareResponseAsync(share, baseUrl, cancellationToken).ConfigureAwait(false);

        if (publicShare == null)
        {
            return ApiNotFound("Shared resource");
        }

        return Ok(publicShare);
    }

    private async Task<int?> GetResourceIdAsync(ShareType shareType, Guid resourceApiKey, CancellationToken cancellationToken)
    {
        switch (shareType)
        {
            case ShareType.Artist:
                var artistResult = await artistService.GetByApiKeyAsync(resourceApiKey, cancellationToken).ConfigureAwait(false);
                return artistResult is { IsSuccess: true, Data: not null } ? artistResult.Data.Id : null;

            case ShareType.Album:
                var albumResult = await albumService.GetByApiKeyAsync(resourceApiKey, cancellationToken).ConfigureAwait(false);
                return albumResult is { IsSuccess: true, Data: not null } ? albumResult.Data.Id : null;

            case ShareType.Song:
                var songResult = await songService.GetByApiKeyAsync(resourceApiKey, cancellationToken).ConfigureAwait(false);
                return songResult is { IsSuccess: true, Data: not null } ? songResult.Data.Id : null;

            case ShareType.Playlist:
                var playlistResult = await playlistService.GetByApiKeyAsync(resourceApiKey, cancellationToken).ConfigureAwait(false);
                return playlistResult is { IsSuccess: true, Data: not null } ? playlistResult.Data.Id : null;

            default:
                return null;
        }
    }

    private async Task<Models.Share?> BuildShareModelAsync(
        Common.Data.Models.Share share,
        string baseUrl,
        Models.User owner,
        CancellationToken cancellationToken)
    {
        var shareType = share.ShareTypeValue;
        string? resourceName = null;
        Guid resourceApiKey = Guid.Empty;
        string resourceThumbnailUrl;
        string resourceImageUrl;

        switch (shareType)
        {
            case ShareType.Artist:
                var artistResult = await artistService.GetAsync(share.ShareId, cancellationToken).ConfigureAwait(false);
                if (artistResult is not { IsSuccess: true, Data: not null }) return null;
                resourceName = artistResult.Data.Name;
                resourceApiKey = artistResult.Data.ApiKey;
                resourceThumbnailUrl = $"{baseUrl}/images/{artistResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}";
                resourceImageUrl = $"{baseUrl}/images/{artistResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultImageSize}";
                break;

            case ShareType.Album:
                var albumResult = await albumService.GetAsync(share.ShareId, cancellationToken).ConfigureAwait(false);
                if (albumResult is not { IsSuccess: true, Data: not null }) return null;
                resourceName = albumResult.Data.Name;
                resourceApiKey = albumResult.Data.ApiKey;
                resourceThumbnailUrl = $"{baseUrl}/images/{albumResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}";
                resourceImageUrl = $"{baseUrl}/images/{albumResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultImageSize}";
                break;

            case ShareType.Song:
                var songResult = await songService.GetAsync(share.ShareId, cancellationToken).ConfigureAwait(false);
                if (songResult is not { IsSuccess: true, Data: not null }) return null;
                resourceName = songResult.Data.Title;
                resourceApiKey = songResult.Data.ApiKey;
                resourceThumbnailUrl = $"{baseUrl}/images/{songResult.Data.ToCoverArtId()}/{MelodeeConfiguration.DefaultThumbNailSize}";
                resourceImageUrl = $"{baseUrl}/images/{songResult.Data.ToCoverArtId()}/{MelodeeConfiguration.DefaultImageSize}";
                break;

            case ShareType.Playlist:
                var playlistResult = await playlistService.GetAsync(share.ShareId, cancellationToken).ConfigureAwait(false);
                if (playlistResult is not { IsSuccess: true, Data: not null }) return null;
                resourceName = playlistResult.Data.Name;
                resourceApiKey = playlistResult.Data.ApiKey;
                resourceThumbnailUrl = $"{baseUrl}/images/{playlistResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}";
                resourceImageUrl = $"{baseUrl}/images/{playlistResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultImageSize}";
                break;

            default:
                return null;
        }

        return share.ToShareModel(
            baseUrl,
            owner,
            shareType.ToString(),
            resourceApiKey,
            resourceName,
            resourceThumbnailUrl,
            resourceImageUrl);
    }

    private async Task<PublicShareResponse?> BuildPublicShareResponseAsync(
        Common.Data.Models.Share share,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        var shareType = share.ShareTypeValue;

        switch (shareType)
        {
            case ShareType.Artist:
                var artistResult = await artistService.GetAsync(share.ShareId, cancellationToken).ConfigureAwait(false);
                if (artistResult is not { IsSuccess: true, Data: not null }) return null;
                return new PublicShareResponse(
                    shareType.ToString(),
                    artistResult.Data.Name,
                    share.Description,
                    $"{baseUrl}/images/{artistResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}",
                    $"{baseUrl}/images/{artistResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultImageSize}",
                    share.IsDownloadable,
                    share.CreatedAt.ToString(),
                    share.ExpiresAt?.ToString(),
                    new PublicArtistInfo(artistResult.Data.ApiKey, artistResult.Data.Name),
                    null,
                    null,
                    null);

            case ShareType.Album:
                var albumResult = await albumService.GetAsync(share.ShareId, cancellationToken).ConfigureAwait(false);
                if (albumResult is not { IsSuccess: true, Data: not null }) return null;
                var albumSongs = albumResult.Data.Songs.Select(s => new PublicSongInfo(
                    s.ApiKey,
                    s.Title,
                    s.SongNumber,
                    s.Duration,
                    $"{baseUrl}/rest/stream?id={s.ToApiKey()}"
                )).ToArray();
                return new PublicShareResponse(
                    shareType.ToString(),
                    albumResult.Data.Name,
                    share.Description,
                    $"{baseUrl}/images/{albumResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}",
                    $"{baseUrl}/images/{albumResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultImageSize}",
                    share.IsDownloadable,
                    share.CreatedAt.ToString(),
                    share.ExpiresAt?.ToString(),
                    null,
                    new PublicAlbumInfo(albumResult.Data.ApiKey, albumResult.Data.Name, albumResult.Data.Artist.Name, albumResult.Data.ReleaseDate.Year, albumSongs),
                    null,
                    null);

            case ShareType.Song:
                var songResult = await songService.GetAsync(share.ShareId, cancellationToken).ConfigureAwait(false);
                if (songResult is not { IsSuccess: true, Data: not null }) return null;
                return new PublicShareResponse(
                    shareType.ToString(),
                    songResult.Data.Title,
                    share.Description,
                    $"{baseUrl}/images/{songResult.Data.ToCoverArtId()}/{MelodeeConfiguration.DefaultThumbNailSize}",
                    $"{baseUrl}/images/{songResult.Data.ToCoverArtId()}/{MelodeeConfiguration.DefaultImageSize}",
                    share.IsDownloadable,
                    share.CreatedAt.ToString(),
                    share.ExpiresAt?.ToString(),
                    null,
                    null,
                    new PublicSongInfo(
                        songResult.Data.ApiKey,
                        songResult.Data.Title,
                        songResult.Data.SongNumber,
                        songResult.Data.Duration,
                        $"{baseUrl}/rest/stream?id={songResult.Data.ToApiKey()}"),
                    null);

            case ShareType.Playlist:
                var playlistResult = await playlistService.GetAsync(share.ShareId, cancellationToken).ConfigureAwait(false);
                if (playlistResult is not { IsSuccess: true, Data: not null }) return null;
                var playlistSongs = playlistResult.Data.Songs.Select(ps => new PublicSongInfo(
                    ps.Song.ApiKey,
                    ps.Song.Title,
                    ps.PlaylistOrder,
                    ps.Song.Duration,
                    $"{baseUrl}/rest/stream?id={ps.Song.ToApiKey()}"
                )).ToArray();
                return new PublicShareResponse(
                    shareType.ToString(),
                    playlistResult.Data.Name,
                    share.Description,
                    $"{baseUrl}/images/{playlistResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultThumbNailSize}",
                    $"{baseUrl}/images/{playlistResult.Data.ToApiKey()}/{MelodeeConfiguration.DefaultImageSize}",
                    share.IsDownloadable,
                    share.CreatedAt.ToString(),
                    share.ExpiresAt?.ToString(),
                    null,
                    null,
                    null,
                    new PublicPlaylistInfo(playlistResult.Data.ApiKey, playlistResult.Data.Name, playlistResult.Data.Description, playlistSongs));

            default:
                return null;
        }
    }
}

/// <summary>
/// Paged response for shares.
/// </summary>
public record SharePagedResponse(PaginationMetadata Meta, Models.Share[] Data);

/// <summary>
/// Public share response for anonymous access.
/// </summary>
public record PublicShareResponse(
    string ShareType,
    string ResourceName,
    string? Description,
    string ThumbnailUrl,
    string ImageUrl,
    bool IsDownloadable,
    string CreatedAt,
    string? ExpiresAt,
    PublicArtistInfo? Artist,
    PublicAlbumInfo? Album,
    PublicSongInfo? Song,
    PublicPlaylistInfo? Playlist);

/// <summary>
/// Minimal artist info for public shares.
/// </summary>
public record PublicArtistInfo(Guid Id, string Name);

/// <summary>
/// Minimal album info for public shares including songs.
/// </summary>
public record PublicAlbumInfo(Guid Id, string Name, string ArtistName, int ReleaseYear, PublicSongInfo[] Songs);

/// <summary>
/// Minimal song info for public shares.
/// </summary>
public record PublicSongInfo(Guid Id, string Title, int TrackNumber, double DurationMs, string StreamUrl);

/// <summary>
/// Minimal playlist info for public shares including songs.
/// </summary>
public record PublicPlaylistInfo(Guid Id, string Name, string? Description, PublicSongInfo[] Songs);
