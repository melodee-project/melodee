using System.Globalization;
using System.Security.Claims;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums.PartyMode;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NodaTime;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Controller for managing party session endpoints registry.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/endpoints")]
public sealed class PartySessionEndpointRegistryController(
    ISerializer serializer,
    EtagRepository etagRepository,
    PartySessionEndpointRegistryService endpointRegistryService,
    PartySessionService partySessionService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ILogger<PartySessionEndpointRegistryController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private IDbContextFactory<MelodeeDbContext> ContextFactory { get; } = contextFactory;
    private ILogger<PartySessionEndpointRegistryController> Logger { get; } = logger;

    /// <summary>
    /// Gets all endpoints visible to the current user.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<EndpointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetEndpoints(CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await endpointRegistryService.GetEndpointsForUserAsync(userId, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to get endpoints");
        }

        var dtos = result.Data.Select(x => new EndpointDto(
            x.ApiKey,
            x.Name,
            x.Type.ToString(),
            x.IsShared,
            x.Room,
            x.LastSeenAt?.ToString("O", CultureInfo.InvariantCulture),
            x.CapabilitiesJson,
            x.OwnerUserId == userId));

        return Ok(dtos);
    }

    /// <summary>
    /// Gets endpoints available for a specific session.
    /// </summary>
    [HttpGet]
    [Route("for-session/{sessionId:guid}")]
    [ProducesResponseType(typeof(IEnumerable<EndpointDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetEndpointsForSession(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var sessionResult = await partySessionService.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return ApiNotFound("Party session");
        }

        var userRole = await partySessionService.GetUserRoleAsync(sessionId, userId, cancellationToken).ConfigureAwait(false);
        if (userRole.Data == null)
        {
            return ApiNotFound("Participant not found in session");
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var userEndpoints = await endpointRegistryService.GetEndpointsForUserAsync(userId, cancellationToken).ConfigureAwait(false);
        var allEndpoints = userEndpoints.Data?.ToList() ?? new List<PartySessionEndpoint>();

        if (userRole.Data == PartyRole.Owner || userRole.Data == PartyRole.DJ)
        {
            var systemEndpoints = await scopedContext.PartySessionEndpoints
                .AsNoTracking()
                .Where(x => x.OwnerUserId == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);
            allEndpoints.AddRange(systemEndpoints);
        }

        var session = await scopedContext.PartySessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == sessionId, cancellationToken)
            .ConfigureAwait(false);

        var dtos = allEndpoints.Select(x => new SessionEndpointDto(
            x.ApiKey,
            x.Name,
            x.Type.ToString(),
            x.IsShared,
            x.Room,
            x.LastSeenAt?.ToString("O", CultureInfo.InvariantCulture),
            x.CapabilitiesJson,
            x.OwnerUserId == userId,
            x.ApiKey == session?.ActiveEndpointId,
            IsEndpointStale(x)));

        return Ok(dtos);
    }

    /// <summary>
    /// Attaches an endpoint to a session.
    /// </summary>
    [HttpPost]
    [Route("{endpointId:guid}/attach")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AttachEndpoint(
        Guid endpointId,
        [FromBody] AttachEndpointRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var sessionResult = await partySessionService.GetAsync(request.SessionApiKey, cancellationToken).ConfigureAwait(false);
        if (!sessionResult.IsSuccess || sessionResult.Data == null)
        {
            return ApiNotFound("Party session");
        }

        if (sessionResult.Data.Status != PartySessionStatus.Active)
        {
            return ApiBadRequest("Cannot attach endpoint to inactive session");
        }

        var endpointResult = await endpointRegistryService.GetAsync(endpointId, cancellationToken).ConfigureAwait(false);
        if (!endpointResult.IsSuccess || endpointResult.Data == null)
        {
            return ApiNotFound("Endpoint not found");
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var session = await scopedContext.PartySessions
            .FirstOrDefaultAsync(x => x.ApiKey == request.SessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return ApiNotFound("Party session");
        }

        if (session.ActiveEndpointId.HasValue && session.ActiveEndpointId.Value != endpointResult.Data.ApiKey)
        {
            var existingEndpoint = await scopedContext.PartySessionEndpoints
                .FirstOrDefaultAsync(x => x.ApiKey == session.ActiveEndpointId.Value, cancellationToken)
                .ConfigureAwait(false);

            if (existingEndpoint != null && !IsEndpointStale(existingEndpoint))
            {
                return ApiBadRequest("Another endpoint is already attached. Detach it first.");
            }
        }

        var result = await endpointRegistryService.AttachToSessionAsync(endpointId, request.SessionApiKey, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Endpoint or session"),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to attach endpoint")
            };
        }

        Logger.LogInformation("User {UserId} attached endpoint {EndpointId} to session {SessionApiKey}",
            userId, endpointId, request.SessionApiKey);

        return NoContent();
    }

    /// <summary>
    /// Detaches an endpoint from its session.
    /// </summary>
    [HttpPost]
    [Route("{endpointId:guid}/detach")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DetachEndpoint(Guid endpointId, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var endpointResult = await endpointRegistryService.GetAsync(endpointId, cancellationToken).ConfigureAwait(false);
        if (!endpointResult.IsSuccess || endpointResult.Data == null)
        {
            return ApiNotFound("Endpoint not found");
        }

        if (endpointResult.Data.OwnerUserId.HasValue && endpointResult.Data.OwnerUserId != userId)
        {
            return ApiForbidden("You can only detach your own endpoints");
        }

        var result = await endpointRegistryService.DetachAsync(endpointId, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiNotFound("Endpoint");
        }

        Logger.LogInformation("User {UserId} detached endpoint {EndpointId}", userId, endpointId);

        return NoContent();
    }

    /// <summary>
    /// Checks if an endpoint is stale based on LastSeenAt.
    /// </summary>
    private static bool IsEndpointStale(PartySessionEndpoint endpoint)
    {
        if (!endpoint.LastSeenAt.HasValue)
        {
            return true;
        }

        var staleThreshold = NodaTime.Duration.FromTimeSpan(TimeSpan.FromSeconds(30));
        var threshold = SystemClock.Instance.GetCurrentInstant() - staleThreshold;
        return endpoint.LastSeenAt < threshold;
    }
}

/// <summary>
/// Request model for attaching an endpoint to a session.
/// </summary>
public record AttachEndpointRequest(Guid SessionApiKey);

/// <summary>
/// DTO for endpoint information.
/// </summary>
public record EndpointDto(
    Guid ApiKey,
    string Name,
    string Type,
    bool IsShared,
    string? Room,
    string? LastSeenAt,
    string? CapabilitiesJson,
    bool IsOwner);

/// <summary>
/// DTO for endpoint information in session context.
/// </summary>
public record SessionEndpointDto(
    Guid ApiKey,
    string Name,
    string Type,
    bool IsShared,
    string? Room,
    string? LastSeenAt,
    string? CapabilitiesJson,
    bool IsOwner,
    bool IsActive,
    bool IsStale);
