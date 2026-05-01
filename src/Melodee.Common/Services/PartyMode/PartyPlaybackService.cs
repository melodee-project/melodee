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
/// Service for managing party playback state and operations.
/// </summary>
public sealed class PartyPlaybackService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IPartyNotificationService notificationService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string PlaybackStateCacheKeyTemplate = "urn:party:playback:{0}";
    private const string SkipCooldownCacheKeyTemplate = "urn:party:cooldown:skip:{0}";
    private readonly IPartyNotificationService _notificationService = notificationService;

    public async Task<OperationResult<PartyPlaybackState?>> GetPlaybackStateAsync(
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
            return new OperationResult<PartyPlaybackState?>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = null
            };
        }

        var cacheKey = string.Format(PlaybackStateCacheKeyTemplate, sessionApiKey);
        var playbackState = await CacheManager.GetAsync(
            cacheKey,
            async () =>
            {
                var state = await scopedContext.PartyPlaybackStates
                    .AsNoTracking()
                    .Include(x => x.CurrentQueueItem)
                    .FirstOrDefaultAsync(x => x.PartySessionId == session.Id, cancellationToken)
                    .ConfigureAwait(false);
                return state;
            },
            cancellationToken,
            TimeSpan.FromSeconds(5));

        return new OperationResult<PartyPlaybackState?>
        {
            Data = playbackState
        };
    }

    public async Task<OperationResult<PartyPlaybackState>> UpdateFromHeartbeatAsync(
        Guid sessionApiKey,
        Guid? currentQueueItemApiKey,
        double positionSeconds,
        bool isPlaying,
        double? volume01,
        int endpointUserId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.PlaybackState)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<PartyPlaybackState>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        if (session.PlaybackState == null)
        {
            return new OperationResult<PartyPlaybackState>("Playback state not found for session.")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        session.PlaybackState.PositionSeconds = positionSeconds;
        session.PlaybackState.IsPlaying = isPlaying;
        session.PlaybackState.Volume = volume01;
        session.PlaybackState.CurrentQueueItemApiKey = currentQueueItemApiKey;
        session.PlaybackState.LastHeartbeatAt = SystemClock.Instance.GetCurrentInstant();
        session.PlaybackState.UpdatedByUserId = endpointUserId;

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(PlaybackStateCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Debug("[PartyPlaybackService] Updated playback state for session {SessionId}: position={Position}, playing={IsPlaying}",
            sessionApiKey, positionSeconds, isPlaying);

        await _notificationService.NotifyPlaybackChangedAsync(sessionApiKey, session.PlaybackState);

        return new OperationResult<PartyPlaybackState>
        {
            Data = session.PlaybackState
        };
    }

    public async Task<OperationResult<PartyPlaybackState>> SetCurrentItemAsync(
        Guid sessionApiKey,
        Guid? queueItemApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.PlaybackState)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<PartyPlaybackState>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        if (session.PlaybackState == null)
        {
            return new OperationResult<PartyPlaybackState>("Playback state not found for session.")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        session.PlaybackState.CurrentQueueItemApiKey = queueItemApiKey;
        session.PlaybackState.PositionSeconds = 0;
        session.PlaybackState.IsPlaying = false;

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(PlaybackStateCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Information("[PartyPlaybackService] Set current item to {ItemApiKey} for session {SessionId}",
            queueItemApiKey ?? Guid.Empty, sessionApiKey);

        await _notificationService.NotifyPlaybackChangedAsync(sessionApiKey, session.PlaybackState);

        return new OperationResult<PartyPlaybackState>
        {
            Data = session.PlaybackState
        };
    }

    public async Task<OperationResult<PartyPlaybackState>> UpdateIntentAsync(
        Guid sessionApiKey,
        PlaybackIntent intent,
        double? positionSeconds,
        int requestingUserId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.PlaybackState)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<PartyPlaybackState>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == requestingUserId);
        if (participant == null || participant.IsBanned)
        {
            return new OperationResult<PartyPlaybackState>("User is not a valid participant or is banned.")
            {
                Type = OperationResponseType.Forbidden,
                Data = null!
            };
        }

        // Only Owner and DJ can control playback (Requirement 148, 209, 320 implied)
        if (participant.Role == Melodee.Common.Enums.PartyMode.PartyRole.Listener)
        {
            return new OperationResult<PartyPlaybackState>("Listeners cannot control playback.")
            {
                Type = OperationResponseType.Forbidden,
                Data = null!
            };
        }

        if (session.PlaybackState == null)
        {
            return new OperationResult<PartyPlaybackState>("Playback state not found for session.")
            {
                Type = OperationResponseType.NotFound,
                Data = null!
            };
        }

        if (session.PlaybackRevision != expectedRevision)
        {
            return new OperationResult<PartyPlaybackState>(
                $"Concurrent modification detected. Current revision: {session.PlaybackRevision}")
            {
                Type = OperationResponseType.Conflict,
                Data = null!
            };
        }

        switch (intent)
        {
            case PlaybackIntent.Play:
                session.PlaybackState.IsPlaying = true;
                if (positionSeconds.HasValue)
                {
                    session.PlaybackState.PositionSeconds = positionSeconds.Value;
                }
                break;

            case PlaybackIntent.Pause:
                session.PlaybackState.IsPlaying = false;
                if (positionSeconds.HasValue)
                {
                    session.PlaybackState.PositionSeconds = positionSeconds.Value;
                }
                break;

            case PlaybackIntent.Skip:
                var skipCacheKey = string.Format(SkipCooldownCacheKeyTemplate, sessionApiKey);
                if (await CacheManager.GetAsync<bool?>(skipCacheKey, () => Task.FromResult<bool?>(null), cancellationToken, TimeSpan.Zero) != null)
                {
                    return new OperationResult<PartyPlaybackState>("Skip cooldown active.")
                    {
                        Type = OperationResponseType.Conflict,
                        Data = null!
                    };
                }

                var queueItems = await scopedContext.PartyQueueItems
                    .Where(x => x.PartySessionId == session.Id)
                    .OrderBy(x => x.SortOrder)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var currentItemIndex = session.PlaybackState.CurrentQueueItemApiKey.HasValue
                    ? queueItems.FindIndex(x => x.ApiKey == session.PlaybackState.CurrentQueueItemApiKey)
                    : -1;

                PartyQueueItem? nextItem = null;
                if (currentItemIndex >= 0 && currentItemIndex < queueItems.Count - 1)
                {
                    nextItem = queueItems[currentItemIndex + 1];
                }
                else if (queueItems.Any())
                {
                    nextItem = queueItems.First();
                }

                session.PlaybackState.CurrentQueueItemApiKey = nextItem?.ApiKey;
                session.PlaybackState.PositionSeconds = 0;
                session.PlaybackState.IsPlaying = true;

                await CacheManager.GetAsync(skipCacheKey, () => Task.FromResult(true), cancellationToken, TimeSpan.FromSeconds(10));
                break;

            case PlaybackIntent.Seek:
                if (positionSeconds.HasValue && positionSeconds.Value >= 0)
                {
                    session.PlaybackState.PositionSeconds = positionSeconds.Value;
                }
                else
                {
                    return new OperationResult<PartyPlaybackState>("Invalid seek position.")
                    {
                        Type = OperationResponseType.BadRequest,
                        Data = null!
                    };
                }
                break;
        }

        session.PlaybackState.UpdatedByUserId = requestingUserId;
        session.PlaybackRevision++;
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(PlaybackStateCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Information("[PartyPlaybackService] Applied intent {Intent} for session {SessionId} by user {UserId}",
            intent, sessionApiKey, requestingUserId);

        await _notificationService.NotifyPlaybackChangedAsync(sessionApiKey, session.PlaybackState);

        return new OperationResult<PartyPlaybackState>
        {
            Data = session.PlaybackState
        };
    }
}
