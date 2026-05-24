using Melodee.Blazor.Security.Extensions;
using Melodee.Common.Enums.PartyMode;
using Melodee.Common.Models;
using Melodee.Common.Services;

namespace Melodee.Blazor.Services;

public sealed class PartyModeService(
    IAuthService authService,
    PartySessionService partySessionService,
    PartyQueueService partyQueueService,
    PartyPlaybackService partyPlaybackService,
    PartySessionEndpointRegistryService endpointRegistryService,
    ILogger<PartyModeService> logger)
{
    public async Task<OperationResult<PartySessionDto>?> CreateSessionAsync(string name, string? joinCode = null, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] CreateSessionAsync: Name={Name}, HasJoinCode={HasJoinCode}", name, joinCode is not null);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partySessionService.CreateAsync(name, userId, joinCode, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] CreateSessionAsync failed: {Messages}", string.Join(", ", result.Messages ?? []));
            return new OperationResult<PartySessionDto>(result.Errors?.FirstOrDefault()?.Message ?? "Failed to create session")
            {
                Type = result.Type,
                Data = null!
            };
        }

        logger.LogDebug("[PartyModeService] CreateSessionAsync succeeded: ApiKey={ApiKey}", result.Data.ApiKey);
        return new OperationResult<PartySessionDto>
        {
            Data = MapToDto(result.Data)
        };
    }

    public async Task<IEnumerable<PartySessionDto>?> GetMySessionsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] GetMySessionsAsync starting");

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partySessionService.GetUserSessionsAsync(userId, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] GetMySessionsAsync failed");
            return null;
        }

        var sessions = result.Data.Select(MapToDto);
        logger.LogDebug("[PartyModeService] GetMySessionsAsync succeeded: Count={Count}", result.Data.Count());
        return sessions;
    }

    public async Task<IEnumerable<PartySessionDto>?> GetActiveSessionsAsync(CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] GetActiveSessionsAsync starting");

        var result = await partySessionService.GetActiveSessionsAsync(cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] GetActiveSessionsAsync failed");
            return null;
        }

        var sessions = result.Data.Select(MapToDto);
        logger.LogDebug("[PartyModeService] GetActiveSessionsAsync succeeded: Count={Count}", result.Data.Count());
        return sessions;
    }

    public async Task<OperationResult<PartySessionDto>?> GetSessionAsync(Guid sessionApiKey, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] GetSessionAsync: ApiKey={ApiKey}", sessionApiKey);

        var result = await partySessionService.GetAsync(sessionApiKey, cancellationToken);

        if (!result.IsSuccess || result.Data is null)
        {
            logger.LogWarning("[PartyModeService] GetSessionAsync failed: ApiKey={ApiKey}", sessionApiKey);
            return new OperationResult<PartySessionDto>(result.Errors?.FirstOrDefault()?.Message ?? "Session not found")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        logger.LogDebug("[PartyModeService] GetSessionAsync succeeded: Name={Name}", result.Data.Name);
        return new OperationResult<PartySessionDto>
        {
            Data = MapToDto(result.Data)
        };
    }

    public async Task<OperationResult<PartySessionParticipantDto>?> JoinSessionAsync(Guid sessionApiKey, string? joinCode = null, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] JoinSessionAsync: ApiKey={ApiKey}, HasJoinCode={HasJoinCode}", sessionApiKey, joinCode is not null);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partySessionService.JoinAsync(sessionApiKey, userId, joinCode, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] JoinSessionAsync failed: ApiKey={ApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return new OperationResult<PartySessionParticipantDto>(result.Errors?.FirstOrDefault()?.Message ?? "Failed to join session")
            {
                Type = result.Type,
                Data = null!
            };
        }

        logger.LogDebug("[PartyModeService] JoinSessionAsync succeeded: ApiKey={ApiKey}", sessionApiKey);
        return new OperationResult<PartySessionParticipantDto>
        {
            Data = new PartySessionParticipantDto(result.Data.UserId, result.Data.Role.ToString(), result.Data.JoinedAt.ToString())
        };
    }

    public async Task<bool> LeaveSessionAsync(Guid sessionApiKey, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] LeaveSessionAsync: ApiKey={ApiKey}", sessionApiKey);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return false;
        }

        var result = await partySessionService.LeaveAsync(sessionApiKey, userId, cancellationToken);
        logger.LogDebug("[PartyModeService] LeaveSessionAsync: ApiKey={ApiKey}, Success={Success}", sessionApiKey, result.IsSuccess);
        return result.IsSuccess;
    }

    public async Task<bool> EndSessionAsync(Guid sessionApiKey, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] EndSessionAsync: ApiKey={ApiKey}", sessionApiKey);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return false;
        }

        var result = await partySessionService.EndAsync(sessionApiKey, userId, cancellationToken);
        logger.LogDebug("[PartyModeService] EndSessionAsync: ApiKey={ApiKey}, Success={Success}", sessionApiKey, result.IsSuccess);
        return result.IsSuccess;
    }

    public async Task<OperationResult<IEnumerable<PartySessionParticipantDto>>?> GetParticipantsAsync(Guid sessionApiKey, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] GetParticipantsAsync: ApiKey={ApiKey}", sessionApiKey);

        var result = await partySessionService.GetParticipantsAsync(sessionApiKey, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] GetParticipantsAsync failed: ApiKey={ApiKey}", sessionApiKey);
            return null;
        }

        var participants = result.Data.Select(p => new PartySessionParticipantDto(p.UserId, p.Role.ToString(), p.JoinedAt.ToString()));
        return new OperationResult<IEnumerable<PartySessionParticipantDto>>
        {
            Data = participants
        };
    }

    public async Task<OperationResult<QueueResponseDto>?> GetQueueAsync(Guid sessionApiKey, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] GetQueueAsync: ApiKey={ApiKey}", sessionApiKey);

        var result = await partyQueueService.GetQueueAsync(sessionApiKey, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] GetQueueAsync failed: ApiKey={ApiKey}", sessionApiKey);
            return null;
        }

        var (revision, items) = result.Data;
        return new OperationResult<QueueResponseDto>
        {
            Data = new QueueResponseDto(
                revision,
                items.Select(i => new PartyQueueItemDto(i.ApiKey, i.SongApiKey, i.EnqueuedByUserId, i.EnqueuedAt.ToString(), i.SortOrder, i.Source)))
        };
    }

    public async Task<OperationResult<AddItemsResponseDto>?> AddToQueueAsync(
        Guid sessionApiKey,
        IEnumerable<Guid> songApiKeys,
        string? source = null,
        long expectedRevision = 1,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] AddToQueueAsync: ApiKey={ApiKey}, SongCount={SongCount}, ExpectedRevision={ExpectedRevision}",
            sessionApiKey, songApiKeys.Count(), expectedRevision);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partyQueueService.AddItemsAsync(sessionApiKey, songApiKeys, userId, source, expectedRevision, cancellationToken);

        if (!result.IsSuccess)
        {
            if (result.Type == OperationResponseType.Conflict)
            {
                logger.LogWarning("[PartyModeService] AddToQueueAsync conflict (revision mismatch): ApiKey={ApiKey}", sessionApiKey);
            }
            else
            {
                logger.LogWarning("[PartyModeService] AddToQueueAsync failed: ApiKey={ApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            }
            return null;
        }

        var (newRevision, addedItems) = result.Data;
        return new OperationResult<AddItemsResponseDto>
        {
            Data = new AddItemsResponseDto(
                newRevision,
                addedItems.Select(i => new PartyQueueItemDto(i.ApiKey, i.SongApiKey, i.EnqueuedByUserId, i.EnqueuedAt.ToString(), i.SortOrder, i.Source)))
        };
    }

    public async Task<OperationResult<long>?> RemoveFromQueueAsync(Guid sessionApiKey, Guid itemApiKey, long expectedRevision, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] RemoveFromQueueAsync: SessionApiKey={SessionApiKey}, ItemApiKey={ItemApiKey}", sessionApiKey, itemApiKey);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partyQueueService.RemoveItemAsync(sessionApiKey, itemApiKey, userId, expectedRevision, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] RemoveFromQueueAsync failed: SessionApiKey={SessionApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return null;
        }

        return new OperationResult<long> { Data = result.Data };
    }

    public async Task<OperationResult<long>?> ReorderQueueItemAsync(
        Guid sessionApiKey,
        Guid itemApiKey,
        int newIndex,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] ReorderQueueItemAsync: SessionApiKey={SessionApiKey}, ItemApiKey={ItemApiKey}, NewIndex={NewIndex}",
            sessionApiKey, itemApiKey, newIndex);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partyQueueService.ReorderItemAsync(sessionApiKey, itemApiKey, newIndex, userId, expectedRevision, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] ReorderQueueItemAsync failed: SessionApiKey={SessionApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return null;
        }

        return new OperationResult<long> { Data = result.Data };
    }

    public async Task<OperationResult<long>?> ClearQueueAsync(Guid sessionApiKey, long expectedRevision, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] ClearQueueAsync: SessionApiKey={SessionApiKey}, ExpectedRevision={ExpectedRevision}", sessionApiKey, expectedRevision);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partyQueueService.ClearAsync(sessionApiKey, userId, expectedRevision, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] ClearQueueAsync failed: SessionApiKey={SessionApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return null;
        }

        return new OperationResult<long> { Data = result.Data };
    }

    public async Task<OperationResult<PartyPlaybackStateDto>?> GetPlaybackStateAsync(Guid sessionApiKey, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] GetPlaybackStateAsync: ApiKey={ApiKey}", sessionApiKey);

        var result = await partyPlaybackService.GetPlaybackStateAsync(sessionApiKey, cancellationToken);

        if (!result.IsSuccess || result.Data is null)
        {
            logger.LogWarning("[PartyModeService] GetPlaybackStateAsync failed: ApiKey={ApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return null;
        }

        return new OperationResult<PartyPlaybackStateDto>
        {
            Data = new PartyPlaybackStateDto(
                result.Data.CurrentQueueItemApiKey,
                result.Data.PositionSeconds,
                result.Data.IsPlaying,
                result.Data.Volume)
        };
    }

    public async Task<OperationResult<PartyPlaybackStateDto>?> PlayAsync(Guid sessionApiKey, double? position = null, long expectedRevision = 0, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] PlayAsync: ApiKey={ApiKey}, Position={Position}", sessionApiKey, position);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partyPlaybackService.UpdateIntentAsync(sessionApiKey, PlaybackIntent.Play, position, userId, expectedRevision, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] PlayAsync failed: ApiKey={ApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return null;
        }

        return new OperationResult<PartyPlaybackStateDto>
        {
            Data = new PartyPlaybackStateDto(
                result.Data.CurrentQueueItemApiKey,
                result.Data.PositionSeconds,
                result.Data.IsPlaying,
                result.Data.Volume)
        };
    }

    public async Task<OperationResult<PartyPlaybackStateDto>?> PauseAsync(Guid sessionApiKey, double? position = null, long expectedRevision = 0, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] PauseAsync: ApiKey={ApiKey}, Position={Position}", sessionApiKey, position);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partyPlaybackService.UpdateIntentAsync(sessionApiKey, PlaybackIntent.Pause, position, userId, expectedRevision, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] PauseAsync failed: ApiKey={ApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return null;
        }

        return new OperationResult<PartyPlaybackStateDto>
        {
            Data = new PartyPlaybackStateDto(
                result.Data.CurrentQueueItemApiKey,
                result.Data.PositionSeconds,
                result.Data.IsPlaying,
                result.Data.Volume)
        };
    }

    public async Task<OperationResult<PartyPlaybackStateDto>?> SkipAsync(Guid sessionApiKey, long expectedRevision, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] SkipAsync: ApiKey={ApiKey}, ExpectedRevision={ExpectedRevision}", sessionApiKey, expectedRevision);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partyPlaybackService.UpdateIntentAsync(sessionApiKey, PlaybackIntent.Skip, null, userId, expectedRevision, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] SkipAsync failed: ApiKey={ApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return null;
        }

        return new OperationResult<PartyPlaybackStateDto>
        {
            Data = new PartyPlaybackStateDto(
                result.Data.CurrentQueueItemApiKey,
                result.Data.PositionSeconds,
                result.Data.IsPlaying,
                result.Data.Volume)
        };
    }

    public async Task<OperationResult<PartyPlaybackStateDto>?> SeekAsync(Guid sessionApiKey, double position, long expectedRevision, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] SeekAsync: ApiKey={ApiKey}, Position={Position}", sessionApiKey, position);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partyPlaybackService.UpdateIntentAsync(sessionApiKey, PlaybackIntent.Seek, position, userId, expectedRevision, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] SeekAsync failed: ApiKey={ApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return null;
        }

        return new OperationResult<PartyPlaybackStateDto>
        {
            Data = new PartyPlaybackStateDto(
                result.Data.CurrentQueueItemApiKey,
                result.Data.PositionSeconds,
                result.Data.IsPlaying,
                result.Data.Volume)
        };
    }

    public async Task<OperationResult<PartyPlaybackStateDto>?> SetVolumeAsync(Guid sessionApiKey, double volume, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] SetVolumeAsync: ApiKey={ApiKey}, Volume={Volume}", sessionApiKey, volume);

        var userId = authService.CurrentUser.UserId();
        if (userId == 0)
        {
            return null;
        }

        var result = await partyPlaybackService.UpdateFromHeartbeatAsync(sessionApiKey, null, 0, false, volume, userId, cancellationToken);

        if (!result.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] SetVolumeAsync failed: ApiKey={ApiKey}, StatusCode={Type}", sessionApiKey, result.Type);
            return null;
        }

        return new OperationResult<PartyPlaybackStateDto>
        {
            Data = new PartyPlaybackStateDto(
                result.Data.CurrentQueueItemApiKey,
                result.Data.PositionSeconds,
                result.Data.IsPlaying,
                result.Data.Volume)
        };
    }

    public async Task<OperationResult<IEnumerable<SessionEndpointDto>>?> GetEndpointsForSessionAsync(Guid sessionApiKey, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] GetEndpointsForSessionAsync: ApiKey={ApiKey}", sessionApiKey);

        var userId = authService.CurrentUser.UserId();

        var sessionResult = await partySessionService.GetAsync(sessionApiKey, cancellationToken);
        if (!sessionResult.IsSuccess || sessionResult.Data is null)
        {
            logger.LogWarning("[PartyModeService] GetEndpointsForSessionAsync session not found: ApiKey={ApiKey}", sessionApiKey);
            return null;
        }

        var session = sessionResult.Data;
        var endpointsResult = await endpointRegistryService.GetEndpointsForUserAsync(userId, cancellationToken);

        if (!endpointsResult.IsSuccess)
        {
            logger.LogWarning("[PartyModeService] GetEndpointsForSessionAsync failed: ApiKey={ApiKey}", sessionApiKey);
            return null;
        }

        var now = NodaTime.SystemClock.Instance.GetCurrentInstant();
        var staleThreshold = NodaTime.Duration.FromTimeSpan(TimeSpan.FromSeconds(30));

        var dtos = endpointsResult.Data.Select(e =>
        {
            var isStale = !e.LastSeenAt.HasValue || e.LastSeenAt.Value < (now - staleThreshold);
            return new SessionEndpointDto(
                e.ApiKey,
                e.Name,
                e.Type.ToString(),
                e.IsShared,
                e.Room,
                e.LastSeenAt?.ToString(),
                e.CapabilitiesJson,
                e.OwnerUserId == userId,
                e.ApiKey == session.ActiveEndpointId,
                isStale);
        });

        return new OperationResult<IEnumerable<SessionEndpointDto>> { Data = dtos };
    }

    public async Task<bool> AttachEndpointAsync(Guid endpointApiKey, Guid sessionApiKey, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] AttachEndpointAsync: EndpointApiKey={EndpointApiKey}, SessionApiKey={SessionApiKey}", endpointApiKey, sessionApiKey);

        var result = await endpointRegistryService.AttachToSessionAsync(endpointApiKey, sessionApiKey, cancellationToken);
        logger.LogDebug("[PartyModeService] AttachEndpointAsync: Success={Success}", result.IsSuccess);
        return result.IsSuccess;
    }

    public async Task<bool> DetachEndpointAsync(Guid endpointApiKey, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("[PartyModeService] DetachEndpointAsync: EndpointApiKey={EndpointApiKey}", endpointApiKey);

        var result = await endpointRegistryService.DetachAsync(endpointApiKey, cancellationToken);
        logger.LogDebug("[PartyModeService] DetachEndpointAsync: Success={Success}", result.IsSuccess);
        return result.IsSuccess;
    }

    private static PartySessionDto MapToDto(Common.Data.Models.PartySession session)
    {
        return new PartySessionDto(
            session.ApiKey,
            session.Name,
            session.OwnerUserId,
            session.Status.ToString(),
            session.QueueRevision,
            session.PlaybackRevision);
    }
}

public record PartySessionDto(Guid ApiKey, string Name, int OwnerUserId, string Status, long QueueRevision, long PlaybackRevision);
public record PartySessionParticipantDto(int UserId, string Role, string JoinedAt);
public record QueueResponseDto(long Revision, IEnumerable<PartyQueueItemDto> Items);
public record PartyQueueItemDto(Guid ApiKey, Guid SongApiKey, int EnqueuedByUserId, string EnqueuedAt, int SortOrder, string? Source);
public record AddItemsResponseDto(long NewRevision, IEnumerable<PartyQueueItemDto> AddedItems);
public record PartyPlaybackStateDto(Guid? CurrentQueueItemApiKey, double PositionSeconds, bool IsPlaying, double? Volume);
public record SessionEndpointDto(Guid ApiKey, string Name, string Type, bool IsShared, string? Room, string? LastSeenAt, string? CapabilitiesJson, bool IsOwner, bool IsActive, bool IsStale);
