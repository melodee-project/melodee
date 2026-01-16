using System.Security.Claims;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Enums.PartyMode;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PartyPlaybackStateEntity = Melodee.Common.Data.Models.PartyPlaybackState;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Controller for managing party playback operations.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/party-sessions/{sessionId:guid}/playback")]
public sealed class PartyPlaybackController(
    ISerializer serializer,
    EtagRepository etagRepository,
    PartySessionService partySessionService,
    PartyPlaybackService partyPlaybackService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ILogger<PartyPlaybackController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private IDbContextFactory<MelodeeDbContext> ContextFactory { get; } = contextFactory;
    private ILogger<PartyPlaybackController> Logger { get; } = logger;

    /// <summary>
    /// Gets playback state for a party session.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(Models.PartyPlaybackState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetState(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var result = await partyPlaybackService.GetPlaybackStateAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return result.Type == OperationResponseType.NotFound
                ? ApiNotFound("Party session")
                : ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to get playback state");
        }

        return Ok(result.Data.ToPartyPlaybackStateDto());
    }

    /// <summary>
    /// Starts or resumes playback.
    /// </summary>
    [HttpPost]
    [Route("play")]
    [ProducesResponseType(typeof(Models.PartyPlaybackState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Play(
        Guid sessionId,
        [FromBody] PlaybackIntentRequest? request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partyPlaybackService.UpdateIntentAsync(
            sessionId,
            PlaybackIntent.Play,
            request?.PositionSeconds,
            userId,
            request?.ExpectedRevision ?? 0,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HandlePlaybackError(result);
        }

        return Ok(result.Data.ToPartyPlaybackStateDto());
    }

    /// <summary>
    /// Pauses playback.
    /// </summary>
    [HttpPost]
    [Route("pause")]
    [ProducesResponseType(typeof(Models.PartyPlaybackState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pause(
        Guid sessionId,
        [FromBody] PlaybackIntentRequest? request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partyPlaybackService.UpdateIntentAsync(
            sessionId,
            PlaybackIntent.Pause,
            request?.PositionSeconds,
            userId,
            request?.ExpectedRevision ?? 0,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HandlePlaybackError(result);
        }

        return Ok(result.Data.ToPartyPlaybackStateDto());
    }

    /// <summary>
    /// Stops playback.
    /// </summary>
    [HttpPost]
    [Route("stop")]
    [ProducesResponseType(typeof(Models.PartyPlaybackState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Stop(
        Guid sessionId,
        [FromBody] PlaybackIntentRequest? request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partyPlaybackService.UpdateIntentAsync(
            sessionId,
            PlaybackIntent.Stop,
            null,
            userId,
            request?.ExpectedRevision ?? 0,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HandlePlaybackError(result);
        }

        return Ok(result.Data.ToPartyPlaybackStateDto());
    }

    /// <summary>
    /// Skips to next track.
    /// </summary>
    [HttpPost]
    [Route("next")]
    [ProducesResponseType(typeof(Models.PartyPlaybackState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Skip(
        Guid sessionId,
        [FromBody] PlaybackIntentRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partyPlaybackService.UpdateIntentAsync(
            sessionId,
            PlaybackIntent.Skip,
            null,
            userId,
            request.ExpectedRevision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HandlePlaybackError(result);
        }

        return Ok(result.Data.ToPartyPlaybackStateDto());
    }

    /// <summary>
    /// Seeks to a specific position.
    /// </summary>
    [HttpPost]
    [Route("seek")]
    [ProducesResponseType(typeof(Models.PartyPlaybackState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Seek(
        Guid sessionId,
        [FromBody] SeekRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        if (request.PositionSeconds < 0)
        {
            return ApiBadRequest("Position must be non-negative");
        }

        var result = await partyPlaybackService.UpdateIntentAsync(
            sessionId,
            PlaybackIntent.Seek,
            request.PositionSeconds,
            userId,
            request.ExpectedRevision,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HandlePlaybackError(result);
        }

        return Ok(result.Data.ToPartyPlaybackStateDto());
    }

    /// <summary>
    /// Sets volume level.
    /// </summary>
    [HttpPost]
    [Route("volume")]
    [ProducesResponseType(typeof(Models.PartyPlaybackState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetVolume(
        Guid sessionId,
        [FromBody] VolumeRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        if (request.Volume is < 0 or > 1)
        {
            return ApiBadRequest("Volume must be between 0.0 and 1.0");
        }

        var result = await partyPlaybackService.UpdateFromHeartbeatAsync(
            sessionId,
            null,
            0,
            false,
            request.Volume,
            userId,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return HandlePlaybackError(result);
        }

        return Ok(result.Data.ToPartyPlaybackStateDto());
    }

    private IActionResult HandlePlaybackError(OperationResult<PartyPlaybackStateEntity> result)
    {
        return result.Type switch
        {
            OperationResponseType.NotFound => ApiNotFound("Party session or playback state"),
            OperationResponseType.Conflict => Conflict(new ApiError(ApiError.Codes.BadRequest,
                result.Errors?.FirstOrDefault()?.Message ?? "Concurrent modification detected",
                GetCorrelationId())),
            _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to update playback")
        };
    }
}

/// <summary>
/// Request model for playback intent operations.
/// </summary>
public record PlaybackIntentRequest
{
    /// <summary>
    /// Optional position in seconds.
    /// </summary>
    public double? PositionSeconds { get; init; }

    /// <summary>
    /// Expected revision for optimistic concurrency.
    /// </summary>
    public long ExpectedRevision { get; init; }
}

/// <summary>
/// Request model for seek operation.
/// </summary>
public record SeekRequest
{
    /// <summary>
    /// Target position in seconds.
    /// </summary>
    public required double PositionSeconds { get; init; }

    /// <summary>
    /// Expected revision for optimistic concurrency.
    /// </summary>
    public long ExpectedRevision { get; init; }
}

/// <summary>
/// Request model for setting volume.
/// </summary>
public record VolumeRequest
{
    /// <summary>
    /// Volume level between 0.0 and 1.0.
    /// </summary>
    public required double Volume { get; init; }
}
