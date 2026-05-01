namespace Melodee.Blazor.Controllers.Melodee.Models;

public record PartyPlaybackState(
    int PartySessionId,
    Guid? CurrentQueueItemApiKey,
    PartyQueueItem? CurrentQueueItem,
    double PositionSeconds,
    bool IsPlaying,
    double? Volume,
    string? LastHeartbeatAt,
    int? UpdatedByUserId);
