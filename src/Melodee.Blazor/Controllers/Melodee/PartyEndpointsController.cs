using System.Security.Claims;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Enums.PartyMode;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Controller for managing party session endpoints and heartbeats.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/party-endpoints")]
public sealed class PartyEndpointsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    PartySessionEndpointRegistryService endpointRegistryService,
    PartySessionService partySessionService,
    PartyPlaybackService partyPlaybackService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ILogger<PartyEndpointsController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private IDbContextFactory<MelodeeDbContext> ContextFactory { get; } = contextFactory;
    private ILogger<PartyEndpointsController> Logger { get; } = logger;

    /// <summary>
    /// Registers a new endpoint.
    /// </summary>
    [HttpPost]
    [Route("register")]
    [ProducesResponseType(typeof(Common.Data.Models.PartySessionEndpoint), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register(
        [FromBody] RegisterEndpointRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        var userId = string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var parsedUserId)
            ? null
            : parsedUserId;

        var result = await endpointRegistryService.RegisterAsync(
            request.Name,
            request.Type,
            userId,
            request.CapabilitiesJson,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to register endpoint");
        }

        return CreatedAtAction(nameof(Get), new { id = result.Data.ApiKey }, result.Data);
    }

    /// <summary>
    /// Gets an endpoint by ID.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}")]
    [ProducesResponseType(typeof(Common.Data.Models.PartySessionEndpoint), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await endpointRegistryService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return ApiNotFound("Endpoint");
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Updates endpoint capabilities.
    /// </summary>
    [HttpPut]
    [Route("{id:guid}/capabilities")]
    [ProducesResponseType(typeof(Common.Data.Models.PartySessionEndpoint), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCapabilities(
        Guid id,
        [FromBody] UpdateCapabilitiesRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await endpointRegistryService.UpdateCapabilitiesAsync(
            id,
            request.CapabilitiesJson,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type == OperationResponseType.NotFound
                ? ApiNotFound("Endpoint")
                : ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to update capabilities");
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Sends a heartbeat for an endpoint.
    /// </summary>
    [HttpPost]
    [Route("{id:guid}/heartbeat")]
    [ProducesResponseType(typeof(Common.Data.Models.PartyPlaybackState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Heartbeat(
        Guid id,
        [FromBody] HeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        var updateResult = await endpointRegistryService.UpdateLastSeenAsync(id, cancellationToken).ConfigureAwait(false);
        if (!updateResult.IsSuccess)
        {
            return ApiNotFound("Endpoint");
        }

        if (request.SessionApiKey.HasValue)
        {
            var user = HttpContext.User;
            var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            {
                return ApiUnauthorized();
            }

            var playbackResult = await partyPlaybackService.UpdateFromHeartbeatAsync(
                request.SessionApiKey.Value,
                request.CurrentQueueItemApiKey,
                request.PositionSeconds,
                request.IsPlaying,
                request.Volume,
                userId,
                cancellationToken
            ).ConfigureAwait(false);

            if (!playbackResult.IsSuccess)
            {
                return playbackResult.Type == OperationResponseType.NotFound
                    ? ApiNotFound("Party session")
                    : ApiBadRequest(playbackResult.Errors?.FirstOrDefault()?.Message ?? "Failed to update playback state");
            }

            return Ok(playbackResult.Data);
        }

        return Ok(new { success = true });
    }

    /// <summary>
    /// Attaches an endpoint to a session.
    /// </summary>
    [HttpPost]
    [Route("{id:guid}/attach")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AttachToSession(
        Guid id,
        [FromBody] AttachToSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await endpointRegistryService.AttachToSessionAsync(
            id,
            request.SessionApiKey,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Endpoint or session"),
                OperationResponseType.BadRequest => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to attach endpoint"),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to attach endpoint")
            };
        }

        return NoContent();
    }

    /// <summary>
    /// Detaches an endpoint from its session.
    /// </summary>
    [HttpPost]
    [Route("{id:guid}/detach")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detach(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await endpointRegistryService.DetachAsync(id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiNotFound("Endpoint");
        }

        return NoContent();
    }
}

/// <summary>
/// Request model for registering an endpoint.
/// </summary>
public record RegisterEndpointRequest
{
    /// <summary>
    /// Name of the endpoint.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Type of the endpoint.
    /// </summary>
    public required PartySessionEndpointType Type { get; init; }

    /// <summary>
    /// JSON-encoded capabilities of the endpoint.
    /// </summary>
    public string? CapabilitiesJson { get; init; }
}

/// <summary>
/// Request model for updating endpoint capabilities.
/// </summary>
public record UpdateCapabilitiesRequest
{
    /// <summary>
    /// JSON-encoded capabilities.
    /// </summary>
    public required string CapabilitiesJson { get; init; }
}

/// <summary>
/// Request model for sending a heartbeat.
/// </summary>
public record HeartbeatRequest
{
    /// <summary>
    /// Optional session API key to update playback state for.
    /// </summary>
    public Guid? SessionApiKey { get; init; }

    /// <summary>
    /// Current queue item API key.
    /// </summary>
    public Guid? CurrentQueueItemApiKey { get; init; }

    /// <summary>
    /// Current playback position in seconds.
    /// </summary>
    public required double PositionSeconds { get; init; }

    /// <summary>
    /// Whether playback is currently active.
    /// </summary>
    public required bool IsPlaying { get; init; }

    /// <summary>
    /// Volume level between 0.0 and 1.0.
    /// </summary>
    public double? Volume { get; init; }
}

/// <summary>
/// Request model for attaching an endpoint to a session.
/// </summary>
public record AttachToSessionRequest
{
    /// <summary>
    /// Session API key to attach to.
    /// </summary>
    public required Guid SessionApiKey { get; init; }
}
