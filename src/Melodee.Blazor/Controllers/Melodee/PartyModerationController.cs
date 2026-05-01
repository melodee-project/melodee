using System.Security.Claims;
using Asp.Versioning;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Blazor.Filters;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Enums.PartyMode;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Melodee.Blazor.Controllers.Melodee;

/// <summary>
/// Controller for party session moderation operations.
/// </summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ServiceFilter(typeof(MelodeeApiAuthFilter))]
[EnableRateLimiting("melodee-api")]
[ApiVersion(1)]
[Route("api/v{version:apiVersion}/party-sessions/{sessionId:guid}")]
public sealed class PartyModerationController(
    ISerializer serializer,
    EtagRepository etagRepository,
    PartySessionService partySessionService,
    IPartyAuditService partyAuditService,
    IConfiguration configuration,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ILogger<PartyModerationController> logger) : ControllerBase(
    etagRepository,
    serializer,
    configuration,
    configurationFactory)
{
    private IDbContextFactory<MelodeeDbContext> ContextFactory { get; } = contextFactory;
    private ILogger<PartyModerationController> Logger { get; } = logger;

    /// <summary>
    /// Sets the queue lock state (Owner only).
    /// </summary>
    [HttpPost]
    [Route("settings/queue-lock")]
    [ProducesResponseType(typeof(PartySessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetQueueLock(
        Guid sessionId,
        [FromBody] SetQueueLockRequest request,
        CancellationToken cancellationToken = default)
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

        if (sessionResult.Data.OwnerUserId != userId)
        {
            return ApiForbidden("Only the session owner can lock/unlock the queue");
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var session = await scopedContext.PartySessions
            .FirstOrDefaultAsync(x => x.ApiKey == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return ApiNotFound("Party session");
        }

        var wasLocked = session.IsQueueLocked;
        session.IsQueueLocked = request.IsLocked;

        var eventType = request.IsLocked ? PartyAuditEventType.QueueLocked : PartyAuditEventType.QueueUnlocked;
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await partyAuditService.LogEventAsync(
            session.Id,
            userId,
            eventType,
            new { wasLocked, isLocked = request.IsLocked }.ToJson(),
            cancellationToken).ConfigureAwait(false);

        Logger.Information("User {UserId} {action} queue for session {SessionId}",
            userId, request.IsLocked ? "locked" : "unlocked", sessionId);

        return Ok(new PartySessionDto(
            session.ApiKey,
            session.Name,
            session.OwnerUserId,
            session.Status.ToString(),
            session.QueueRevision,
            session.PlaybackRevision,
            session.IsQueueLocked));
    }

    /// <summary>
    /// Changes a participant's role (Owner only).
    /// </summary>
    [HttpPost]
    [Route("participants/{targetUserId:int}/role")]
    [ProducesResponseType(typeof(PartySessionParticipantDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeRole(
        Guid sessionId,
        int targetUserId,
        [FromBody] ChangeRoleRequest request,
        CancellationToken cancellationToken = default)
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

        if (sessionResult.Data.OwnerUserId != userId)
        {
            return ApiForbidden("Only the session owner can change roles");
        }

        if (targetUserId == userId)
        {
            return ApiBadRequest("Cannot change your own role");
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var session = await scopedContext.PartySessions
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return ApiNotFound("Party session");
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == targetUserId);
        if (participant == null)
        {
            return ApiNotFound("Participant not found in session");
        }

        var oldRole = participant.Role;
        participant.Role = request.Role;

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await partyAuditService.LogEventAsync(
            session.Id,
            userId,
            PartyAuditEventType.RoleChanged,
            new { targetUserId, oldRole = oldRole.ToString(), newRole = request.Role.ToString() }.ToJson(),
            cancellationToken).ConfigureAwait(false);

        Logger.Information("User {UserId} changed role of user {TargetUserId} from {OldRole} to {NewRole} in session {SessionId}",
            userId, targetUserId, oldRole, request.Role, sessionId);

        return Ok(new PartySessionParticipantDto(participant.UserId, participant.Role.ToString(), participant.IsBanned));
    }

    /// <summary>
    /// Kicks a participant from the session (Owner/DJ only).
    /// </summary>
    [HttpPost]
    [Route("participants/{targetUserId:int}/kick")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> KickParticipant(
        Guid sessionId,
        int targetUserId,
        CancellationToken cancellationToken = default)
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
        if (userRole.Data != PartyRole.Owner && userRole.Data != PartyRole.DJ)
        {
            return ApiForbidden("Only Owner or DJ can kick participants");
        }

        if (targetUserId == userId)
        {
            return ApiBadRequest("Cannot kick yourself");
        }

        if (sessionResult.Data.OwnerUserId == targetUserId)
        {
            return ApiForbidden("Cannot kick the session owner");
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var session = await scopedContext.PartySessions
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return ApiNotFound("Party session");
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == targetUserId);
        if (participant == null)
        {
            return ApiNotFound("Participant not found in session");
        }

        session.Participants.Remove(participant);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await partyAuditService.LogEventAsync(
            session.Id,
            userId,
            PartyAuditEventType.ParticipantKicked,
            new { kickedUserId = targetUserId }.ToJson(),
            cancellationToken).ConfigureAwait(false);

        Logger.Information("User {UserId} kicked user {TargetUserId} from session {SessionId}",
            userId, targetUserId, sessionId);

        return NoContent();
    }

    /// <summary>
    /// Bans a participant from the session (Owner only).
    /// </summary>
    [HttpPost]
    [Route("participants/{targetUserId:int}/ban")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> BanParticipant(
        Guid sessionId,
        int targetUserId,
        CancellationToken cancellationToken = default)
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

        if (sessionResult.Data.OwnerUserId != userId)
        {
            return ApiForbidden("Only the session owner can ban participants");
        }

        if (targetUserId == userId)
        {
            return ApiBadRequest("Cannot ban yourself");
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var session = await scopedContext.PartySessions
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return ApiNotFound("Party session");
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == targetUserId);
        if (participant == null)
        {
            return ApiNotFound("Participant not found in session");
        }

        if (sessionResult.Data.OwnerUserId == targetUserId)
        {
            return ApiForbidden("Cannot ban the session owner");
        }

        participant.IsBanned = true;
        session.Participants.Remove(participant);

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await partyAuditService.LogEventAsync(
            session.Id,
            userId,
            PartyAuditEventType.ParticipantBanned,
            new { bannedUserId = targetUserId }.ToJson(),
            cancellationToken).ConfigureAwait(false);

        Logger.Information("User {UserId} banned user {TargetUserId} from session {SessionId}",
            userId, targetUserId, sessionId);

        return NoContent();
    }

    /// <summary>
    /// Unbans a participant (Owner only).
    /// </summary>
    [HttpPost]
    [Route("participants/{targetUserId:int}/unban")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UnbanParticipant(
        Guid sessionId,
        int targetUserId,
        CancellationToken cancellationToken = default)
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

        if (sessionResult.Data.OwnerUserId != userId)
        {
            return ApiForbidden("Only the session owner can unban participants");
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var session = await scopedContext.PartySessions
            .FirstOrDefaultAsync(x => x.ApiKey == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return ApiNotFound("Party session");
        }

        await partyAuditService.LogEventAsync(
            session.Id,
            userId,
            PartyAuditEventType.ParticipantUnbanned,
            new { unbannedUserId = targetUserId }.ToJson(),
            cancellationToken).ConfigureAwait(false);

        Logger.Information("User {UserId} unbanned user {TargetUserId} in session {SessionId}",
            userId, targetUserId, sessionId);

        return NoContent();
    }

    /// <summary>
    /// Gets the audit log for a session (Owner only).
    /// </summary>
    [HttpGet]
    [Route("audit")]
    [ProducesResponseType(typeof(IEnumerable<PartyAuditEventDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiError), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAuditLog(
        Guid sessionId,
        [FromQuery] int? take,
        CancellationToken cancellationToken = default)
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

        if (sessionResult.Data.OwnerUserId != userId)
        {
            return ApiForbidden("Only the session owner can view the audit log");
        }

        var result = await partyAuditService.GetSessionAuditLogAsync(sessionId, take, cancellationToken).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            return ApiNotFound("Party session");
        }

        var dtos = result.Data.Select(x => new PartyAuditEventDto(
            x.ApiKey,
            x.EventType.ToString(),
            x.UserId,
            x.PayloadJson,
            x.CreatedAt.ToString("O")));

        return Ok(dtos);
    }
}

/// <summary>
/// Request model for setting queue lock.
/// </summary>
public record SetQueueLockRequest(bool IsLocked);

/// <summary>
/// Request model for changing participant role.
/// </summary>
public record ChangeRoleRequest(PartyRole Role);

/// <summary>
/// DTO for party session.
/// </summary>
public record PartySessionDto(
    Guid ApiKey,
    string Name,
    int OwnerUserId,
    string Status,
    long QueueRevision,
    long PlaybackRevision,
    bool IsQueueLocked);

/// <summary>
/// DTO for party session participant.
/// </summary>
public record PartySessionParticipantDto(int UserId, string Role, bool IsBanned);

/// <summary>
/// DTO for audit event.
/// </summary>
public record PartyAuditEventDto(Guid ApiKey, string EventType, int UserId, string? PayloadJson, string CreatedAt);
