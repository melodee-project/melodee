using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Extensions;
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
using NodaTime;

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
    [ProducesResponseType(typeof(EndpointDto), StatusCodes.Status201Created)]
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

        return CreatedAtAction(nameof(Get), new { id = result.Data.ApiKey }, result.Data.ToEndpointDto(userId));
    }

    /// <summary>
    /// Gets an endpoint by ID.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}")]
    [ProducesResponseType(typeof(EndpointDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        var userId = string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var parsedUserId)
            ? null
            : parsedUserId;

        var result = await endpointRegistryService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return ApiNotFound("Endpoint");
        }

        return Ok(result.Data.ToEndpointDto(userId));
    }

    /// <summary>
    /// Updates endpoint capabilities.
    /// </summary>
    [HttpPut]
    [Route("{id:guid}/capabilities")]
    [ProducesResponseType(typeof(EndpointDto), StatusCodes.Status200OK)]
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

        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        var userId = string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var parsedUserId)
            ? null
            : parsedUserId;

        return Ok(result.Data.ToEndpointDto(userId));
    }

    /// <summary>
    /// Sends a heartbeat for an endpoint.
    /// </summary>
    [HttpPost]
    [Route("{id:guid}/heartbeat")]
    [ProducesResponseType(typeof(Models.PartyPlaybackState), StatusCodes.Status200OK)]
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

            var sessionResult = await partySessionService.GetAsync(request.SessionApiKey.Value, cancellationToken).ConfigureAwait(false);
            if (!sessionResult.IsSuccess || sessionResult.Data == null)
            {
                return ApiNotFound("Party session");
            }

            var session = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var sessionEntity = await session.PartySessions
                .FirstOrDefaultAsync(x => x.ApiKey == request.SessionApiKey.Value, cancellationToken)
                .ConfigureAwait(false);

            if (sessionEntity != null)
            {
                var playbackResult = await partyPlaybackService.UpdateLastHeartbeatAsync(
                    sessionEntity.Id,
                    id,
                    cancellationToken
                ).ConfigureAwait(false);

                if (playbackResult.IsSuccess && playbackResult.Data != null)
                {
                    return Ok(playbackResult.Data.ToPartyPlaybackStateDto());
                }
            }
        }

        var endpointResult = await endpointRegistryService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (endpointResult.IsSuccess && endpointResult.Data != null)
        {
            var user = HttpContext.User;
            var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
            var userId = string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var parsedUserId)
                ? null
                : parsedUserId;

            return Ok(endpointResult.Data.ToEndpointDto(userId));
        }

        return ApiNotFound("Endpoint");
    }

    /// <summary>
    /// Gets current playback state for an endpoint's active session.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}/state")]
    [ProducesResponseType(typeof(Models.PartyPlaybackState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetStateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var endpointResult = await endpointRegistryService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!endpointResult.IsSuccess || endpointResult.Data == null)
        {
            return ApiNotFound("Endpoint");
        }

        var endpoint = endpointResult.Data;
        if (endpoint.OwnerUserId.HasValue)
        {
            var sessionResult = await partySessionService.GetActiveSessionForEndpointAsync(endpoint.ApiKey, cancellationToken).ConfigureAwait(false);
            if (sessionResult.IsSuccess && sessionResult.Data != null)
            {
                var playbackResult = await partyPlaybackService.GetStateAsync(sessionResult.Data.Id, cancellationToken).ConfigureAwait(false);
                if (playbackResult.IsSuccess && playbackResult.Data != null)
                {
                    return Ok(playbackResult.Data.ToPartyPlaybackStateDto());
                }
            }
        }

        return ApiBadRequest("No active playback state");
    }

    /// <summary>
    /// Detaches an endpoint from its session.
    /// </summary>
    [HttpPost]
    [Route("{id:guid}/detach")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Detach(Guid id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var endpointResult = await endpointRegistryService.GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (!endpointResult.IsSuccess || endpointResult.Data == null)
        {
            return ApiNotFound("Endpoint");
        }

        if (endpointResult.Data.OwnerUserId.HasValue && endpointResult.Data.OwnerUserId != userId)
        {
            return ApiForbidden("You can only detach your own endpoints");
        }

        var result = await endpointRegistryService.DetachAsync(id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiNotFound("Endpoint");
        }

        Logger.LogInformation("User {UserId} detached endpoint {EndpointId}", userId, id);

        return NoContent();
    }
}

public record RegisterEndpointRequest(string Name, PartySessionEndpointType Type, string? CapabilitiesJson = null);
public record UpdateCapabilitiesRequest(string CapabilitiesJson);
public record HeartbeatRequest(Guid? SessionApiKey);
