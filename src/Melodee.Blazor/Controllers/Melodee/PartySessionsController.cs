using System.Security.Claims;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Controller for managing party sessions.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/party-sessions")]
public sealed class PartySessionsController(
    ISerializer serializer,
    EtagRepository etagRepository,
    PartySessionService partySessionService,
    PartyQueueService partyQueueService,
    PartyPlaybackService partyPlaybackService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ILogger<PartySessionsController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private IDbContextFactory<MelodeeDbContext> ContextFactory { get; } = contextFactory;
    private ILogger<PartySessionsController> Logger { get; } = logger;

    /// <summary>
    /// Creates a new party session.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(Models.PartySession), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] CreatePartySessionRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partySessionService.CreateAsync(
            request.Name,
            userId,
            request.JoinCode,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.Forbidden => ApiForbidden(result.Errors?.FirstOrDefault()?.Message ?? "Party mode is not enabled"),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to create session")
            };
        }

        return CreatedAtAction(nameof(Get), new { id = result.Data.ApiKey }, result.Data);
    }

    /// <summary>
    /// Gets active sessions for the current user (sessions they own or participate in).
    /// </summary>
    [HttpGet]
    [Route("my")]
    [ProducesResponseType(typeof(IEnumerable<Models.PartySession>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMySessions(CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partySessionService.GetUserSessionsAsync(userId, cancellationToken).ConfigureAwait(false);

        return Ok(result.Data);
    }

    /// <summary>
    /// Gets all active public sessions that can be joined.
    /// </summary>
    [HttpGet]
    [Route("active")]
    [ProducesResponseType(typeof(IEnumerable<Models.PartySession>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetActiveSessions(CancellationToken cancellationToken = default)
    {
        var result = await partySessionService.GetActiveSessionsAsync(cancellationToken).ConfigureAwait(false);

        return Ok(result.Data);
    }

    /// <summary>
    /// Gets a party session by ID.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}")]
    [ProducesResponseType(typeof(Models.PartySession), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await partySessionService.GetAsync(id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess || result.Data == null)
        {
            return ApiNotFound("Party session");
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Joins a party session.
    /// </summary>
    [HttpPost]
    [Route("{id:guid}/join")]
    [ProducesResponseType(typeof(Models.PartySessionParticipant), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Join(
        Guid id,
        [FromBody] JoinPartySessionRequest? request,
        CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partySessionService.JoinAsync(
            id,
            userId,
            request?.JoinCode,
            cancellationToken
        ).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Party session"),
                OperationResponseType.Unauthorized => ApiForbidden("Invalid join code"),
                OperationResponseType.BadRequest => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to join session"),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to join session")
            };
        }

        return Ok(result.Data);
    }

    /// <summary>
    /// Leaves a party session.
    /// </summary>
    [HttpPost]
    [Route("{id:guid}/leave")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Leave(Guid id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partySessionService.LeaveAsync(id, userId, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Party session"),
                OperationResponseType.BadRequest => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to leave session"),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to leave session")
            };
        }

        return NoContent();
    }

    /// <summary>
    /// Ends a party session.
    /// </summary>
    [HttpPost]
    [Route("{id:guid}/end")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> End(Guid id, CancellationToken cancellationToken = default)
    {
        var user = HttpContext.User;
        var userIdStr = user.FindFirstValue(ClaimTypes.Sid);
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
        {
            return ApiUnauthorized();
        }

        var result = await partySessionService.EndAsync(id, userId, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return result.Type switch
            {
                OperationResponseType.NotFound => ApiNotFound("Party session"),
                OperationResponseType.Forbidden => ApiForbidden(result.Errors?.FirstOrDefault()?.Message ?? "Only the owner can end the session"),
                OperationResponseType.BadRequest => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to end session"),
                _ => ApiBadRequest(result.Errors?.FirstOrDefault()?.Message ?? "Failed to end session")
            };
        }

        return NoContent();
    }

    /// <summary>
    /// Gets participants for a party session.
    /// </summary>
    [HttpGet]
    [Route("{id:guid}/participants")]
    [ProducesResponseType(typeof(IEnumerable<Models.PartySessionParticipant>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetParticipants(Guid id, CancellationToken cancellationToken = default)
    {
        var result = await partySessionService.GetParticipantsAsync(id, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiNotFound("Party session");
        }

        return Ok(result.Data);
    }
}

/// <summary>
/// Request model for creating a party session.
/// </summary>
public record CreatePartySessionRequest
{
    /// <summary>
    /// Name of the party session.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Optional join code for the session.
    /// </summary>
    public string? JoinCode { get; init; }
}

/// <summary>
/// Request model for joining a party session.
/// </summary>
public record JoinPartySessionRequest
{
    /// <summary>
    /// Join code if required.
    /// </summary>
    public string? JoinCode { get; init; }
}
