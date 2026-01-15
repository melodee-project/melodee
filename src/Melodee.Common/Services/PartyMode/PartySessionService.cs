using System.Security.Cryptography;
using System.Text;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums.PartyMode;
using Melodee.Common.Models;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.PartyMode;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
/// Service for managing party sessions.
/// </summary>
public sealed class PartySessionService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    IPartyNotificationService notificationService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string SessionCacheKeyTemplate = "urn:party:session:{0}";
    private readonly IPartyNotificationService _notificationService = notificationService;

    public async Task<OperationResult<PartySession>> CreateAsync(
        string name,
        int ownerUserId,
        string? joinCode,
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);
        if (!configuration.GetValue<bool>(SettingRegistry.PartyModeEnabled))
        {
            return new OperationResult<PartySession>("Party mode is not enabled.")
            {
                Type = OperationResponseType.AccessDenied,
                Data = null!
            };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var ownerUser = await scopedContext.Users.FindAsync([ownerUserId], cancellationToken).ConfigureAwait(false);
        if (ownerUser == null)
        {
            return new OperationResult<PartySession>($"User with ID {ownerUserId} not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        string? joinCodeHash = null;
        if (!string.IsNullOrEmpty(joinCode))
        {
            joinCodeHash = HashJoinCode(joinCode);
        }

        var session = new PartySession
        {
            Name = name,
            OwnerUserId = ownerUserId,
            JoinCodeHash = joinCodeHash,
            Status = PartySessionStatus.Active,
            QueueRevision = 1,
            PlaybackRevision = 1,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        var participant = new PartySessionParticipant
        {
            PartySessionId = session.Id,
            UserId = ownerUserId,
            Role = PartyRole.Owner,
            JoinedAt = SystemClock.Instance.GetCurrentInstant()
        };

        scopedContext.PartySessions.Add(session);
        scopedContext.PartySessionParticipants.Add(participant);

        var playbackState = new PartyPlaybackState
        {
            PartySessionId = session.Id,
            PositionSeconds = 0,
            IsPlaying = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        scopedContext.PartyPlaybackStates.Add(playbackState);

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        Logger.Information("[PartySessionService] Created party session {SessionName} (ID: {SessionId}) for user {UserId}",
            name, session.Id, ownerUserId);

        return new OperationResult<PartySession>
        {
            Data = session
        };
    }

    public async Task<OperationResult<PartySession?>> GetAsync(
        Guid sessionApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(SessionCacheKeyTemplate, sessionApiKey);
        var session = await CacheManager.GetAsync(
            cacheKey,
            async () =>
            {
                var s = await scopedContext.PartySessions
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
                    .ConfigureAwait(false);
                return s;
            },
            cancellationToken,
            TimeSpan.FromMinutes(5));

        return new OperationResult<PartySession?>
        {
            Data = session
        };
    }

    public async Task<OperationResult<IEnumerable<PartySession>>> GetUserSessionsAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var sessions = await scopedContext.PartySessions
            .AsNoTracking()
            .Where(s => s.Status == PartySessionStatus.Active &&
                        (s.OwnerUserId == userId ||
                         s.Participants.Any(p => p.UserId == userId)))
            .Include(s => s.OwnerUser)
            .Include(s => s.Participants)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OperationResult<IEnumerable<PartySession>>
        {
            Data = sessions
        };
    }

    public async Task<OperationResult<IEnumerable<PartySession>>> GetActiveSessionsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var sessions = await scopedContext.PartySessions
            .AsNoTracking()
            .Where(s => s.Status == PartySessionStatus.Active && s.JoinCodeHash == null)
            .Include(s => s.OwnerUser)
            .Include(s => s.Participants)
            .OrderByDescending(s => s.CreatedAt)
            .Take(50)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OperationResult<IEnumerable<PartySession>>
        {
            Data = sessions
        };
    }

    public async Task<OperationResult<PartySessionParticipant>> JoinAsync(
        Guid sessionApiKey,
        int userId,
        string? joinCode,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<PartySessionParticipant>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        if (session.Status == PartySessionStatus.Ended)
        {
            return new OperationResult<PartySessionParticipant>("Session has ended.")
            {
                Type = OperationResponseType.BadRequest,
                Data = null!
            };
        }

        var user = await scopedContext.Users.FindAsync([userId], cancellationToken).ConfigureAwait(false);
        if (user == null)
        {
            return new OperationResult<PartySessionParticipant>($"User with ID {userId} not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        if (!string.IsNullOrEmpty(session.JoinCodeHash))
        {
            if (string.IsNullOrEmpty(joinCode) || HashJoinCode(joinCode) != session.JoinCodeHash)
            {
                return new OperationResult<PartySessionParticipant>("Invalid join code.")
                {
                    Type = OperationResponseType.Unauthorized,
                    Data = null!
                };
            }
        }

        var existingParticipant = session.Participants.FirstOrDefault(p => p.UserId == userId);
        if (existingParticipant != null)
        {
            return new OperationResult<PartySessionParticipant>
            {
                Data = existingParticipant
            };
        }

        var participant = new PartySessionParticipant
        {
            PartySessionId = session.Id,
            UserId = userId,
            Role = PartyRole.Listener,
            JoinedAt = SystemClock.Instance.GetCurrentInstant()
        };

        session.Participants.Add(participant);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(SessionCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Information("[PartySessionService] User {UserId} joined session {SessionId}", userId, session.Id);

        var result = new OperationResult<PartySessionParticipant>
        {
            Data = participant
        };

        await _notificationService.NotifyParticipantsChangedAsync(sessionApiKey, session.Participants);

        return result;
    }

    public async Task<OperationResult<bool>> LeaveAsync(
        Guid sessionApiKey,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<bool>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = false
            };
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == userId);
        if (participant == null)
        {
            return new OperationResult<bool>("User is not a participant of this session.")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        if (participant.Role == PartyRole.Owner)
        {
            return new OperationResult<bool>("Owner cannot leave. Use EndSession instead.")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        session.Participants.Remove(participant);
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(SessionCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Information("[PartySessionService] User {UserId} left session {SessionId}", userId, session.Id);

        var result = new OperationResult<bool>
        {
            Data = true
        };

        await _notificationService.NotifyParticipantsChangedAsync(sessionApiKey, session.Participants);

        return result;
    }

    public async Task<OperationResult<bool>> EndAsync(
        Guid sessionApiKey,
        int requestingUserId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<bool>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = false
            };
        }

        if (session.OwnerUserId != requestingUserId)
        {
            return new OperationResult<bool>("Only the session owner can end the session.")
            {
                Type = OperationResponseType.Forbidden,
                Data = false
            };
        }

        if (session.Status == PartySessionStatus.Ended)
        {
            return new OperationResult<bool>("Session is already ended.")
            {
                Type = OperationResponseType.BadRequest,
                Data = false
            };
        }

        session.Status = PartySessionStatus.Ended;
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(SessionCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Information("[PartySessionService] Session {SessionId} ended by user {UserId}", session.Id, requestingUserId);

        var result = new OperationResult<bool>
        {
            Data = true
        };

        await _notificationService.NotifySessionEndedAsync(sessionApiKey);

        return result;
    }

    public async Task<OperationResult<IEnumerable<PartySessionParticipant>>> GetParticipantsAsync(
        Guid sessionApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<IEnumerable<PartySessionParticipant>>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = []
            };
        }

        var participants = await scopedContext.PartySessionParticipants
            .AsNoTracking()
            .Where(p => p.PartySessionId == session.Id)
            .Include(p => p.User)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OperationResult<IEnumerable<PartySessionParticipant>>
        {
            Data = participants
        };
    }

    public async Task<OperationResult<PartySessionParticipant?>> GetParticipantAsync(
        Guid sessionApiKey,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<PartySessionParticipant?>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = null
            };
        }

        var participant = await scopedContext.PartySessionParticipants
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.PartySessionId == session.Id && p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        return new OperationResult<PartySessionParticipant?>
        {
            Data = participant
        };
    }

    public async Task<OperationResult<PartyRole?>> GetUserRoleAsync(
        Guid sessionApiKey,
        int userId,
        CancellationToken cancellationToken = default)
    {
        var participantResult = await GetParticipantAsync(sessionApiKey, userId, cancellationToken).ConfigureAwait(false);

        if (!participantResult.IsSuccess)
        {
            return new OperationResult<PartyRole?>(participantResult.Errors?.FirstOrDefault()?.Message ?? "Error")
            {
                Type = participantResult.Type,
                Data = null
            };
        }

        return new OperationResult<PartyRole?>
        {
            Data = participantResult.Data?.Role
        };
    }

    private static string HashJoinCode(string joinCode)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(joinCode));
        return Convert.ToHexString(hashBytes);
    }
}
