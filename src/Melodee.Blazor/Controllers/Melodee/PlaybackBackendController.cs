using System.Security.Claims;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Playback;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Controller for managing playback backend health and status.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/playback-backend")]
public sealed class PlaybackBackendController(
    ISerializer serializer,
    EtagRepository etagRepository,
    IPlaybackBackendService playbackBackendService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    ILogger<PlaybackBackendController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{

    private ILogger<PlaybackBackendController> Logger { get; } = logger;

    /// <summary>
    /// Gets the current backend status.
    /// </summary>
    [HttpGet]
    [Route("status")]
    [ProducesResponseType(typeof(BackendStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken = default)
    {
        var result = await playbackBackendService.GetBackendStatusAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type == OperationResponseType.BadRequest
                ? ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Jukebox is not enabled")
                : StatusCode(StatusCodes.Status500InternalServerError, result.Errors?.FirstOrDefault()?.Message ?? "Failed to get backend status");
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Gets the backend capabilities.
    /// </summary>
    [HttpGet]
    [Route("capabilities")]
    [ProducesResponseType(typeof(BackendCapabilities), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetCapabilities(CancellationToken cancellationToken = default)
    {
        var result = await playbackBackendService.GetBackendCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type == OperationResponseType.BadRequest
                ? ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Jukebox is not enabled")
                : StatusCode(StatusCodes.Status500InternalServerError, result.Errors?.FirstOrDefault()?.Message ?? "Failed to get backend capabilities");
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Initializes the playback backend.
    /// </summary>
    [HttpPost]
    [Route("initialize")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> InitializeBackend(CancellationToken cancellationToken = default)
    {
        var result = await playbackBackendService.InitializeBackendAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type == OperationResponseType.BadRequest
                ? ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Jukebox is not enabled")
                : StatusCode(StatusCodes.Status500InternalServerError, result.Errors?.FirstOrDefault()?.Message ?? "Failed to initialize backend");
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Shuts down the playback backend.
    /// </summary>
    [HttpPost]
    [Route("shutdown")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ShutdownBackend(CancellationToken cancellationToken = default)
    {
        var result = await playbackBackendService.ShutdownBackendAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, result.Errors?.FirstOrDefault()?.Message ?? "Failed to shutdown backend");
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Registers the MPV backend as a system endpoint for party sessions.
    /// </summary>
    [HttpPost]
    [Route("register-endpoint")]
    [ProducesResponseType(typeof(EndpointDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegisterEndpoint(CancellationToken cancellationToken = default)
    {
        var result = await playbackBackendService.RegisterBackendEndpointAsync(cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type == OperationResponseType.Error
                ? StatusCode(StatusCodes.Status500InternalServerError, result.Errors?.FirstOrDefault()?.Message ?? "Failed to register endpoint")
                : ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to register endpoint");
        }

        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        var userId = string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var parsedUserId)
            ? (int?)null
            : parsedUserId;

        if (result.Data != null && userId.HasValue)
        {
            return Ok(result.Data.ToEndpointDto(userId.Value));
        }

        return Ok(result.Data!.ToEndpointDto(userId));
    }

    /// <summary>
    /// Updates the backend heartbeat for a session.
    /// </summary>
    [HttpPost]
    [Route("heartbeat/{sessionId:guid}")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateHeartbeat(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        var result = await playbackBackendService.UpdateBackendHeartbeatAsync(sessionId, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound(result.Errors?.FirstOrDefault()?.Message ?? "Session or endpoint not found"),
                OperationResponseType.ValidationFailure => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Backend is not the active endpoint"),
                _ => StatusCode(StatusCodes.Status500InternalServerError, result.Errors?.FirstOrDefault()?.Message ?? "Failed to update heartbeat")
            };
        }

        return Ok(result.Data);
    }
}
