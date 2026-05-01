namespace Melodee.Blazor.Controllers.Melodee.Models;

public record UserPodcastEpisodePlayHistory(
    int Id,
    int PodcastEpisodeId,
    string PlayedAt,
    string Client,
    string? ByUserAgent,
    string? IpAddress,
    int? SecondsPlayed,
    short Source,
    bool IsNowPlaying,
    string? LastHeartbeatAt);
