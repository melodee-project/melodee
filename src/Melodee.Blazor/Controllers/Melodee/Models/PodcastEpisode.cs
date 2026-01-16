namespace Melodee.Blazor.Controllers.Melodee.Models;

public record PodcastEpisode(
    int Id,
    Guid ApiKey,
    string Title,
    string Description,
    string? PublishDate,
    double? DurationMs,
    string DurationFormatted,
    string ChannelTitle,
    Guid ChannelApiKey,
    bool IsDownloaded,
    string CreatedAt,
    string Tags,
    bool UserStarred,
    int UserRating,
    string DownloadStatus,
    string? DownloadError = null,
    string? EnclosureUrl = null,
    string? LastPlayedAt = null,
    int PlayedCount = 0);
