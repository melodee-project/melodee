using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Play queue management endpoints for cross-device queue persistence.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/queue")]
public sealed class QueueController(
    ISerializer serializer,
    EtagRepository etagRepository,
    UserProfileService userProfileService,
    UserQueueService userQueueService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    /// <summary>
    /// Get the current user's play queue.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(QueueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetQueueAsync(CancellationToken cancellationToken = default)
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

        var queueResult = await userQueueService.GetPlayQueueByUserIdAsync(user.Id, cancellationToken).ConfigureAwait(false);

        if (!queueResult.IsSuccess || queueResult.Data == null)
        {
            return Ok(new
            {
                songs = Array.Empty<Song>(),
                currentSongId = (Guid?)null,
                position = 0.0,
                changedBy = user.UserName,
                lastUpdatedAt = (string?)null
            });
        }

        var baseUrl = await GetBaseUrlAsync(cancellationToken).ConfigureAwait(false);
        var userModel = user.ToUserModel(baseUrl);

        return Ok(new
        {
            songs = queueResult.Data.Songs.Select(s => s.ToSongModel(baseUrl, userModel, user.PublicKey, GetClientBinding())).ToArray(),
            currentSongId = queueResult.Data.CurrentSongApiKey,
            position = queueResult.Data.Position,
            changedBy = queueResult.Data.ChangedBy,
            lastUpdatedAt = queueResult.Data.LastUpdatedAt
        });
    }

    /// <summary>
    /// Save/update the current user's play queue.
    /// </summary>
    [HttpPut]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveQueueAsync([FromBody] SaveQueueRequest request, CancellationToken cancellationToken = default)
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

        var songApiKeys = request.SongIds ?? [];
        var changedBy = request.ChangedBy ?? user.UserName;

        var result = await userQueueService.SavePlayQueueByUserIdAsync(
            user.Id,
            songApiKeys,
            request.CurrentSongId,
            request.Position,
            changedBy,
            cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiBadRequest("Unable to save play queue.");
        }

        return Ok(new { message = "Queue saved successfully" });
    }

    /// <summary>
    /// Clear the current user's play queue.
    /// </summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ClearQueueAsync(CancellationToken cancellationToken = default)
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

        await userQueueService.ClearPlayQueueByUserIdAsync(user.Id, cancellationToken).ConfigureAwait(false);

        return Ok(new { message = "Queue cleared successfully" });
    }
}

/// <summary>
/// Request model for saving/updating the play queue.
/// </summary>
/// <param name="SongIds">Array of song API keys in queue order.</param>
/// <param name="CurrentSongId">The API key of the currently playing song.</param>
/// <param name="Position">The playback position in seconds of the current song.</param>
/// <param name="ChangedBy">The client name that modified the queue (optional).</param>
public record SaveQueueRequest(Guid[]? SongIds, Guid? CurrentSongId, double? Position, string? ChangedBy);

/// <summary>
/// Response model for queue data.
/// </summary>
public record QueueResponse(Song[] Songs, Guid? CurrentSongId, double Position, string ChangedBy, string? LastUpdatedAt);
