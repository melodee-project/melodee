namespace Melodee.Blazor.Controllers.Melodee.Models;

public record PodcastChannel(
    Guid Id,
    string Title,
    string Description,
    string ImageUrl,
    string FeedUrl,
    string Website,
    string? LastSyncAt,
    string CreatedAt,
    string Tags,
    bool UserStarred,
    int UserRating,
    string? SiteUrl = null,
    int EpisodeCount = 0,
    string? LastPlayedAt = null,
    int PlayedCount = 0,
    int UnplayedDownloadedCount = 0);
