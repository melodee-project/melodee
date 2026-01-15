using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[RequireCapability(UserCapability.Playlist)]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/playlists")]
public sealed class PlaylistsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    IUserProfileService userProfileService,
    PlaylistService playlistService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// Get a playlist by ID.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}")]
    [ProducesResponseType(typeof(Playlist), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PlaylistById(Guid id, CancellationToken cancellationToken = default)
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

        var playlistResult = await playlistService.GetByApiKeyAsync(user.ToUserInfo(), id, cancellationToken).ConfigureAwait(false);
        if (!playlistResult.IsSuccess || playlistResult.Data == null)
        {
            return ApiNotFound("Playlist");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(playlistResult.Data.ToPlaylistModel(
            baseUrl,
            user.ToUserModel(baseUrl)));
    }

    /// <summary>
    /// List all playlists with pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(PlaylistPagedResponse), StatusCodes.Status200OK)]
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

        var playlists = await playlistService.ListAsync(user.ToUserInfo(), new PagedRequest { Page = validatedPage, PageSize = validatedPageSize }, cancellationToken).ConfigureAwait(false);
        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new
        {
            meta = new PaginationMetadata(
                playlists.TotalCount,
                validatedPageSize,
                validatedPage,
                playlists.TotalPages
            ),
            data = playlists.Data.Select(x => x.ToPlaylistModel(baseUrl, user.ToUserModel(baseUrl))).ToArray()
        });
    }

    /// <summary>
    /// Get songs for a playlist with pagination.
    /// </summary>
    [HttpGet]
    [Route("{apiKey:guid}/songs")]
    [ProducesResponseType(typeof(SongPagedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SongsForPlaylist(Guid apiKey, int? page, short? pageSize, CancellationToken cancellationToken = default)
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

        var normalizedPage = page ?? 1;
        var normalizedPageSize = pageSize ?? 50;
        if (!TryValidatePaging((short)normalizedPage, (short)normalizedPageSize, out var validatedPage, out var validatedPageSize, out var pagingError))
        {
            return pagingError!;
        }

        var userInfo = user.ToUserInfo();
        var playlistResult = await playlistService.GetByApiKeyAsync(userInfo, apiKey, cancellationToken).ConfigureAwait(false);
        if (!playlistResult.IsSuccess || playlistResult.Data == null)
        {
            return ApiNotFound("Playlist");
        }

        var songsForPlaylistResult = await playlistService.SongsForPlaylistAsync(apiKey,
            userInfo,
            new PagedRequest
            {
                PageSize = validatedPageSize,
                Page = validatedPage
            },
            cancellationToken).ConfigureAwait(false);
        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(new
        {
            meta = new PaginationMetadata(
                songsForPlaylistResult.TotalCount,
                validatedPageSize,
                validatedPage,
                songsForPlaylistResult.TotalPages
            ),
            data = songsForPlaylistResult.Data.Select(x => x.ToSongModel(baseUrl, user.ToUserModel(baseUrl), user.PublicKey, GetClientBinding()))
        });
    }

    /// <summary>
    /// Create a new playlist.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Playlist), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreatePlaylist([FromBody] CreatePlaylistRequest request, CancellationToken cancellationToken = default)
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

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiValidationError("Playlist name is required.");
        }

        var createResult = await playlistService.CreatePlaylistAsync(
            request.Name,
            user.Id,
            request.Comment,
            request.IsPublic,
            request.SongIds,
            returnPrefixedApiKey: false,
            cancellationToken).ConfigureAwait(false);

        if (!createResult.IsSuccess || string.IsNullOrEmpty(createResult.Data))
        {
            return ApiBadRequest("Unable to create playlist.");
        }

        // Fetch the created playlist to return full details
        var playlistApiKey = Guid.Parse(createResult.Data);
        var playlistResult = await playlistService.GetByApiKeyAsync(user.ToUserInfo(), playlistApiKey, cancellationToken).ConfigureAwait(false);
        if (!playlistResult.IsSuccess || playlistResult.Data == null)
        {
            return ApiBadRequest("Playlist created but unable to retrieve details.");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(playlistResult.Data.ToPlaylistModel(baseUrl, user.ToUserModel(baseUrl)));
    }

    /// <summary>
    /// Update an existing playlist's metadata.
    /// </summary>
    [HttpPut]
    [Route("{apiKey:guid}")]
    [ProducesResponseType(typeof(Playlist), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdatePlaylist(Guid apiKey, [FromBody] UpdatePlaylistRequest request, CancellationToken cancellationToken = default)
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

        var updateResult = await playlistService.UpdatePlaylistMetadataAsync(
            apiKey,
            user.Id,
            request.Name,
            request.Comment,
            request.IsPublic,
            cancellationToken).ConfigureAwait(false);

        if (!updateResult.IsSuccess)
        {
            if (updateResult.Type == OperationResponseType.NotFound)
            {
                return ApiNotFound("Playlist");
            }
            if (updateResult.Type == OperationResponseType.AccessDenied)
            {
                return ApiForbidden("You do not have permission to update this playlist.");
            }
            return ApiBadRequest("Unable to update playlist.");
        }

        // Fetch the updated playlist to return full details
        var playlistResult = await playlistService.GetByApiKeyAsync(user.ToUserInfo(), apiKey, cancellationToken).ConfigureAwait(false);
        if (!playlistResult.IsSuccess || playlistResult.Data == null)
        {
            return ApiBadRequest("Playlist updated but unable to retrieve details.");
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        return Ok(playlistResult.Data.ToPlaylistModel(baseUrl, user.ToUserModel(baseUrl)));
    }

    /// <summary>
    /// Delete a playlist.
    /// </summary>
    [HttpDelete]
    [Route("{apiKey:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeletePlaylist(Guid apiKey, CancellationToken cancellationToken = default)
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

        var deleteResult = await playlistService.DeleteByApiKeyAsync(apiKey, user.Id, cancellationToken).ConfigureAwait(false);

        if (!deleteResult.IsSuccess)
        {
            if (deleteResult.Messages?.Any(m => m.Contains("not found", StringComparison.OrdinalIgnoreCase)) == true)
            {
                return ApiNotFound("Playlist");
            }
            if (deleteResult.Messages?.Any(m => m.Contains("not authorized", StringComparison.OrdinalIgnoreCase)) == true)
            {
                return ApiForbidden("You do not have permission to delete this playlist.");
            }
            return ApiBadRequest("Unable to delete playlist.");
        }

        return Ok();
    }

    /// <summary>
    /// Add songs to a playlist.
    /// </summary>
    [HttpPost]
    [Route("{apiKey:guid}/songs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddSongsToPlaylist(Guid apiKey, [FromBody] Guid[] songIds, CancellationToken cancellationToken = default)
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

        if (songIds == null || songIds.Length == 0)
        {
            return ApiValidationError("At least one song ID is required.");
        }

        // Check if user owns the playlist
        var playlistResult = await playlistService.GetByApiKeyAsync(user.ToUserInfo(), apiKey, cancellationToken).ConfigureAwait(false);
        if (!playlistResult.IsSuccess || playlistResult.Data == null)
        {
            return ApiNotFound("Playlist");
        }

        if (playlistResult.Data.UserId != user.Id)
        {
            return ApiForbidden("You do not have permission to modify this playlist.");
        }

        var addResult = await playlistService.AddSongsToPlaylistAsync(apiKey, songIds, cancellationToken).ConfigureAwait(false);

        if (!addResult.IsSuccess)
        {
            return ApiBadRequest(addResult.Messages?.FirstOrDefault() ?? "Unable to add songs to playlist.");
        }

        return Ok();
    }

    /// <summary>
    /// Remove songs from a playlist by song IDs.
    /// </summary>
    [HttpDelete]
    [Route("{apiKey:guid}/songs")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RemoveSongsFromPlaylist(Guid apiKey, [FromBody] Guid[] songIds, CancellationToken cancellationToken = default)
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

        if (songIds == null || songIds.Length == 0)
        {
            return ApiValidationError("At least one song ID is required.");
        }

        // Check if user owns the playlist
        var playlistResult = await playlistService.GetByApiKeyAsync(user.ToUserInfo(), apiKey, cancellationToken).ConfigureAwait(false);
        if (!playlistResult.IsSuccess || playlistResult.Data == null)
        {
            return ApiNotFound("Playlist");
        }

        if (playlistResult.Data.UserId != user.Id)
        {
            return ApiForbidden("You do not have permission to modify this playlist.");
        }

        var removeResult = await playlistService.RemoveSongsFromPlaylistAsync(apiKey, songIds, cancellationToken).ConfigureAwait(false);

        if (!removeResult.IsSuccess)
        {
            return ApiBadRequest(removeResult.Messages?.FirstOrDefault() ?? "Unable to remove songs from playlist.");
        }

        return Ok();
    }

    /// <summary>
    /// Reorder songs in a playlist. The SongIds array represents the new order of songs.
    /// </summary>
    [HttpPut]
    [Route("{apiKey:guid}/songs/reorder")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReorderPlaylistSongs(Guid apiKey, [FromBody] ReorderPlaylistSongsRequest request, CancellationToken cancellationToken = default)
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

        if (request.SongIds == null || request.SongIds.Length == 0)
        {
            return ApiValidationError("At least one song ID is required.");
        }

        var reorderResult = await playlistService.ReorderPlaylistSongsAsync(apiKey, user.Id, request.SongIds, cancellationToken).ConfigureAwait(false);

        if (!reorderResult.IsSuccess)
        {
            if (reorderResult.Type == OperationResponseType.NotFound)
            {
                return ApiNotFound("Playlist");
            }
            if (reorderResult.Type == OperationResponseType.AccessDenied)
            {
                return ApiForbidden("You do not have permission to modify this playlist.");
            }
            return ApiBadRequest(reorderResult.Messages?.FirstOrDefault() ?? "Unable to reorder songs in playlist.");
        }

        return Ok();
    }

    /// <summary>
    /// Upload an image for a playlist.
    /// </summary>
    [HttpPost]
    [Route("{apiKey:guid}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UploadPlaylistImage(Guid apiKey, IFormFile file, CancellationToken cancellationToken = default)
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

        if (file == null || file.Length == 0)
        {
            return ApiValidationError("Image file is required.");
        }

        // Validate file size using configured max upload size
        var configuration = await ConfigurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var maxFileSize = configuration.GetValue<long>(SettingRegistry.SystemMaxUploadSize);
        if (file.Length > maxFileSize)
        {
            return ApiValidationError($"Image file size must be less than {maxFileSize.FormatFileSize()}.");
        }

        // Validate file type using magic bytes (defense in depth)
        await using var fileStream = file.OpenReadStream();
        if (!FileTypeValidator.IsValidImage(fileStream))
        {
            return ApiValidationError("Invalid image file. File content does not match a supported image type.");
        }

        byte[] imageBytes;
        using (var memoryStream = new MemoryStream())
        {
            await file.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            imageBytes = memoryStream.ToArray();
        }

        var uploadResult = await playlistService.UploadPlaylistImageAsync(apiKey, user.Id, imageBytes, cancellationToken).ConfigureAwait(false);

        if (!uploadResult.IsSuccess)
        {
            if (uploadResult.Type == OperationResponseType.NotFound)
            {
                return ApiNotFound("Playlist");
            }
            if (uploadResult.Type == OperationResponseType.AccessDenied)
            {
                return ApiForbidden("You do not have permission to modify this playlist.");
            }
            return ApiBadRequest(uploadResult.Messages?.FirstOrDefault() ?? "Unable to upload image.");
        }

        return Ok();
    }

    /// <summary>
    /// Delete the image for a playlist.
    /// </summary>
    [HttpDelete]
    [Route("{apiKey:guid}/image")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeletePlaylistImage(Guid apiKey, CancellationToken cancellationToken = default)
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

        var deleteResult = await playlistService.DeletePlaylistImageAsync(apiKey, user.Id, cancellationToken).ConfigureAwait(false);

        if (!deleteResult.IsSuccess)
        {
            if (deleteResult.Type == OperationResponseType.NotFound)
            {
                return ApiNotFound("Playlist");
            }
            if (deleteResult.Type == OperationResponseType.AccessDenied)
            {
                return ApiForbidden("You do not have permission to modify this playlist.");
            }
            return ApiBadRequest(deleteResult.Messages?.FirstOrDefault() ?? "Unable to delete image.");
        }

        return Ok();
    }
}
