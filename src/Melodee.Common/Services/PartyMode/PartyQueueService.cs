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
/// Service for managing party queue operations with optimistic concurrency.
/// </summary>
public sealed class PartyQueueService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IPartyNotificationService notificationService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string QueueCacheKeyTemplate = "urn:party:queue:{0}";
    private readonly IPartyNotificationService _notificationService = notificationService;

    public async Task<OperationResult<(long Revision, IEnumerable<PartyQueueItem> Items)>> GetQueueAsync(
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
            return new OperationResult<(long, IEnumerable<PartyQueueItem>)>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = (0, Enumerable.Empty<PartyQueueItem>())
            };
        }

        var cacheKey = string.Format(QueueCacheKeyTemplate, sessionApiKey);
        var result = await CacheManager.GetAsync(
            cacheKey,
            async () =>
            {
                var items = await scopedContext.PartyQueueItems
                    .AsNoTracking()
                    .Where(x => x.PartySessionId == session.Id)
                    .OrderBy(x => x.SortOrder)
                    .Include(x => x.EnqueuedByUser)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);
                return (session.QueueRevision, items.AsEnumerable());
            },
            cancellationToken,
            TimeSpan.FromSeconds(30));

        return new OperationResult<(long, IEnumerable<PartyQueueItem>)>
        {
            Data = result
        };
    }

    public async Task<OperationResult<(long NewRevision, IEnumerable<PartyQueueItem> AddedItems)>> AddItemsAsync(
        Guid sessionApiKey,
        IEnumerable<Guid> songApiKeys,
        int enqueuedByUserId,
        string? source,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.QueueItems)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<(long, IEnumerable<PartyQueueItem>)>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = (0, Enumerable.Empty<PartyQueueItem>())
            };
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == enqueuedByUserId);
        if (participant == null || participant.IsBanned)
        {
            return new OperationResult<(long, IEnumerable<PartyQueueItem>)>("User is not a valid participant or is banned.")
            {
                Type = OperationResponseType.Forbidden,
                Data = (0, Enumerable.Empty<PartyQueueItem>())
            };
        }

        if (session.IsQueueLocked && participant.Role == Melodee.Common.Enums.PartyMode.PartyRole.Listener)
        {
            return new OperationResult<(long, IEnumerable<PartyQueueItem>)>("Queue is locked.")
            {
                Type = OperationResponseType.Forbidden,
                Data = (0, Enumerable.Empty<PartyQueueItem>())
            };
        }

        if (session.QueueRevision != expectedRevision)
        {
            return new OperationResult<(long, IEnumerable<PartyQueueItem>)>(
                $"Concurrent modification detected. Current revision: {session.QueueRevision}")
            {
                Type = OperationResponseType.Conflict,
                Data = (0, Enumerable.Empty<PartyQueueItem>())
            };
        }

        var songKeysList = songApiKeys.ToList();
        if (!songKeysList.Any())
        {
            return new OperationResult<(long, IEnumerable<PartyQueueItem>)>("No song API keys provided.")
            {
                Type = OperationResponseType.BadRequest,
                Data = (0, Enumerable.Empty<PartyQueueItem>())
            };
        }

        var currentMaxSortOrder = session.QueueItems.Any()
            ? session.QueueItems.Max(x => x.SortOrder)
            : 0;

        var newItems = new List<PartyQueueItem>();
        var enqueuedAt = SystemClock.Instance.GetCurrentInstant();

        foreach (var songApiKey in songKeysList)
        {
            var item = new PartyQueueItem
            {
                PartySessionId = session.Id,
                SongApiKey = songApiKey,
                EnqueuedByUserId = enqueuedByUserId,
                EnqueuedAt = enqueuedAt,
                SortOrder = ++currentMaxSortOrder,
                Source = source,
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };

            scopedContext.PartyQueueItems.Add(item);
            newItems.Add(item);
        }

        session.QueueRevision++;
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(QueueCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Information("[PartyQueueService] Added {Count} items to session {SessionId}", newItems.Count, session.Id);

        var result = new OperationResult<(long, IEnumerable<PartyQueueItem>)>
        {
            Data = (session.QueueRevision, newItems)
        };

        await _notificationService.NotifyQueueChangedAsync(
            sessionApiKey,
            session.QueueRevision,
            QueueChangeType.Added,
            null,
            newItems);

        return result;
    }

    public async Task<OperationResult<long>> RemoveItemAsync(
        Guid sessionApiKey,
        Guid itemApiKey,
        int requestingUserId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.QueueItems)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<long>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = 0
            };
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == requestingUserId);
        if (participant == null || participant.IsBanned)
        {
            return new OperationResult<long>("User is not a valid participant or is banned.")
            {
                Type = OperationResponseType.Forbidden,
                Data = 0
            };
        }

        if (session.IsQueueLocked && participant.Role == Melodee.Common.Enums.PartyMode.PartyRole.Listener)
        {
            return new OperationResult<long>("Queue is locked.")
            {
                Type = OperationResponseType.Forbidden,
                Data = 0
            };
        }

        if (session.QueueRevision != expectedRevision)
        {
            return new OperationResult<long>($"Concurrent modification detected. Current revision: {session.QueueRevision}")
            {
                Type = OperationResponseType.Conflict,
                Data = 0
            };
        }

        var item = session.QueueItems.FirstOrDefault(x => x.ApiKey == itemApiKey);
        if (item == null)
        {
            return new OperationResult<long>("Queue item not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = 0
            };
        }

        scopedContext.PartyQueueItems.Remove(item);

        var itemsToReorder = session.QueueItems
            .Where(x => x.SortOrder > item.SortOrder)
            .OrderBy(x => x.SortOrder)
            .ToList();

        foreach (var itemToReorder in itemsToReorder)
        {
            itemToReorder.SortOrder--;
        }

        session.QueueRevision++;
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(QueueCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Information("[PartyQueueService] Removed item {ItemApiKey} from session {SessionId}", itemApiKey, session.Id);

        await _notificationService.NotifyQueueChangedAsync(
            sessionApiKey,
            session.QueueRevision,
            QueueChangeType.Removed,
            itemApiKey,
            []);

        return new OperationResult<long>
        {
            Data = session.QueueRevision
        };
    }

    public async Task<OperationResult<long>> ReorderItemAsync(
        Guid sessionApiKey,
        Guid itemApiKey,
        int newIndex,
        int requestingUserId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.QueueItems)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<long>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = 0
            };
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == requestingUserId);
        if (participant == null || participant.IsBanned)
        {
            return new OperationResult<long>("User is not a valid participant or is banned.")
            {
                Type = OperationResponseType.Forbidden,
                Data = 0
            };
        }

        if (session.IsQueueLocked && participant.Role == Melodee.Common.Enums.PartyMode.PartyRole.Listener)
        {
            return new OperationResult<long>("Queue is locked.")
            {
                Type = OperationResponseType.Forbidden,
                Data = 0
            };
        }

        if (session.QueueRevision != expectedRevision)
        {
            return new OperationResult<long>($"Concurrent modification detected. Current revision: {session.QueueRevision}")
            {
                Type = OperationResponseType.Conflict,
                Data = 0
            };
        }

        var item = session.QueueItems.FirstOrDefault(x => x.ApiKey == itemApiKey);
        if (item == null)
        {
            return new OperationResult<long>("Queue item not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = 0
            };
        }

        var oldIndex = item.SortOrder;
        if (oldIndex == newIndex)
        {
            return new OperationResult<long>
            {
                Data = session.QueueRevision
            };
        }

        if (newIndex < 0)
        {
            newIndex = 0;
        }

        var maxIndex = session.QueueItems.Count - 1;
        if (newIndex > maxIndex)
        {
            newIndex = maxIndex;
        }

        if (newIndex < oldIndex)
        {
            var itemsBetween = session.QueueItems
                .Where(x => x.SortOrder >= newIndex && x.SortOrder < oldIndex && x.ApiKey != itemApiKey)
                .OrderBy(x => x.SortOrder);

            foreach (var itemBetween in itemsBetween)
            {
                itemBetween.SortOrder++;
            }
        }
        else if (newIndex > oldIndex)
        {
            var itemsBetween = session.QueueItems
                .Where(x => x.SortOrder > oldIndex && x.SortOrder <= newIndex && x.ApiKey != itemApiKey)
                .OrderBy(x => x.SortOrder);

            foreach (var itemBetween in itemsBetween)
            {
                itemBetween.SortOrder--;
            }
        }

        item.SortOrder = newIndex;
        session.QueueRevision++;
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(QueueCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Information("[PartyQueueService] Reordered item {ItemApiKey} to index {NewIndex} in session {SessionId}",
            itemApiKey, newIndex, session.Id);

        var result = new OperationResult<long>
        {
            Data = session.QueueRevision
        };

        await _notificationService.NotifyQueueChangedAsync(
            sessionApiKey,
            session.QueueRevision,
            QueueChangeType.Reordered,
            itemApiKey,
            // In reorder, we might want to send the moved item with its new SortOrder, 
            // or just the full list. Usually full list is safer for clients to resync, 
            // but for optimization we send empty and let client reload if revision mismatch. 
            // However, SignalR contract implies we should send something. 
            // The notification service sends PartyQueueItemDto.
            // Let's send the single modified item.
            [item]);

        return result;
    }

    public async Task<OperationResult<long>> ClearAsync(
        Guid sessionApiKey,
        int requestingUserId,
        long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var session = await scopedContext.PartySessions
            .Include(x => x.QueueItems)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.ApiKey == sessionApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (session == null)
        {
            return new OperationResult<long>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = 0
            };
        }

        var participant = session.Participants.FirstOrDefault(p => p.UserId == requestingUserId);
        if (participant == null || participant.IsBanned)
        {
            return new OperationResult<long>("User is not a valid participant or is banned.")
            {
                Type = OperationResponseType.Forbidden,
                Data = 0
            };
        }

        if (session.IsQueueLocked && participant.Role == Melodee.Common.Enums.PartyMode.PartyRole.Listener)
        {
            return new OperationResult<long>("Queue is locked.")
            {
                Type = OperationResponseType.Forbidden,
                Data = 0
            };
        }

        if (session.QueueRevision != expectedRevision)
        {
            return new OperationResult<long>($"Concurrent modification detected. Current revision: {session.QueueRevision}")
            {
                Type = OperationResponseType.Conflict,
                Data = 0
            };
        }

        scopedContext.PartyQueueItems.RemoveRange(session.QueueItems);
        session.QueueItems.Clear();

        session.QueueRevision++;
        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var cacheKey = string.Format(QueueCacheKeyTemplate, sessionApiKey);
        CacheManager.Remove(cacheKey);

        Logger.Information("[PartyQueueService] Cleared queue for session {SessionId}", session.Id);

        var result = new OperationResult<long>
        {
            Data = session.QueueRevision
        };

        await _notificationService.NotifyQueueChangedAsync(
            sessionApiKey,
            session.QueueRevision,
            QueueChangeType.Cleared,
            null,
            []);

        return result;
    }

    public async Task<OperationResult<PartyQueueItem?>> GetNextItemAsync(
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
            return new OperationResult<PartyQueueItem?>("Session not found.")
            {
                Type = OperationResponseType.NotFound,
                Data = null
            };
        }

        var nextItem = await scopedContext.PartyQueueItems
            .AsNoTracking()
            .Where(x => x.PartySessionId == session.Id)
            .OrderBy(x => x.SortOrder)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return new OperationResult<PartyQueueItem?>
        {
            Data = nextItem
        };
    }
}
