using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Common.Extensions;

namespace Melodee.Blazor.Controllers.Melodee.Extensions;

public static class PodcastEpisodeDataInfoExtensions
{
    public static PodcastEpisode ToPodcastEpisodeModel(this Melodee.Common.Models.Collection.PodcastEpisodeDataInfo episode)
    {
        return new PodcastEpisode(
            episode.Id,
            episode.ApiKey,
            episode.Title,
            episode.Description,
            episode.PublishDate?.ToString("O") ?? null,
            episode.Duration?.TotalMilliseconds,
            episode.Duration?.ToDuration() ?? string.Empty,
            episode.ChannelTitle,
            episode.ChannelApiKey,
            episode.IsDownloaded,
            episode.CreatedAt.ToString("O"),
            episode.Tags,
            episode.UserStarred,
            episode.UserRating,
            episode.DownloadStatus.ToString(),
            episode.DownloadError,
            episode.EnclosureUrl,
            episode.LastPlayedAt?.ToString("O") ?? null,
            episode.PlayedCount
        );
    }
}
