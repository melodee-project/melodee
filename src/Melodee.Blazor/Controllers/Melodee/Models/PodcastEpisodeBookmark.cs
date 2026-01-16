namespace Melodee.Blazor.Controllers.Melodee.Models;

public record PodcastEpisodeBookmark(
    int Id,
    int PodcastEpisodeId,
    int PositionSeconds,
    string? Comment,
    string CreatedAt,
    string UpdatedAt);
