using Melodee.Blazor.Controllers.Melodee.Models;
using NodaTime;
using PartyPlaybackStateEntity = Melodee.Common.Data.Models.PartyPlaybackState;
using PartyQueueItemEntity = Melodee.Common.Data.Models.PartyQueueItem;
using PartySessionEndpointEntity = Melodee.Common.Data.Models.PartySessionEndpoint;

namespace Melodee.Blazor.Controllers.Melodee.Extensions;

public static class PartyExtensions
{
    public static PartyQueueItem ToPartyQueueItemModel(this PartyQueueItemEntity entity)
    {
        return new PartyQueueItem(
            entity.ApiKey,
            entity.SongApiKey,
            entity.EnqueuedAt.ToString("O"),
            entity.SortOrder,
            entity.Source,
            entity.Note
        );
    }

    public static PartyPlaybackState ToPartyPlaybackStateDto(this PartyPlaybackStateEntity entity)
    {
        PartyQueueItem? currentQueueItem = null;
        if (entity.CurrentQueueItem != null)
        {
            currentQueueItem = entity.CurrentQueueItem.ToPartyQueueItemModel();
        }

        return new PartyPlaybackState(
            entity.PartySessionId,
            entity.CurrentQueueItemApiKey,
            currentQueueItem,
            entity.PositionSeconds,
            entity.IsPlaying,
            entity.Volume,
            entity.LastHeartbeatAt?.ToString("O"),
            entity.UpdatedByUserId
        );
    }

    public static EndpointDto ToEndpointDto(this PartySessionEndpointEntity entity, int? currentUserId = null, Guid? activeEndpointId = null)
    {
        return new EndpointDto(
            entity.ApiKey,
            entity.Name,
            entity.Type.ToString(),
            entity.IsShared,
            entity.Room,
            entity.LastSeenAt?.ToString("O"),
            entity.CapabilitiesJson,
            entity.OwnerUserId == currentUserId
        );
    }

    public static SessionEndpointDto ToSessionEndpointDto(this PartySessionEndpointEntity entity, int? currentUserId = null, Guid? activeEndpointId = null, bool isActive = false, bool isStale = false)
    {
        return new SessionEndpointDto(
            entity.ApiKey,
            entity.Name,
            entity.Type.ToString(),
            entity.IsShared,
            entity.Room,
            entity.LastSeenAt?.ToString("O"),
            entity.CapabilitiesJson,
            entity.OwnerUserId == currentUserId,
            isActive,
            isStale
        );
    }

    private static bool IsEndpointStale(this PartySessionEndpointEntity endpoint)
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
