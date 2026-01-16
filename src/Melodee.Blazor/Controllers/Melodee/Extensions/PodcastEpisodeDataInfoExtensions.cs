using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Collection.Extensions;
using Melodee.Common.Enums;
using NodaTime;

namespace Melodee.Blazor.Controllers.Melodee.Extensions;

public static class PodcastEpisodeDataInfoExtensions
{
    public static Models.PodcastEpisode ToPodcastEpisodeModel(this PodcastEpisodeDataInfo episode)
    {
        return new Models.PodcastEpisode(
            episode.Id,
            episode.ApiKey,
            episode.Title,
            episode.Description,
            episode.PublishDate?.ToIso8601String() ?? null,
            episode.Duration?.TotalMilliseconds,
            episode.Duration != null ? Duration.FromTimeSpan(episode.Duration.Value).ToDurationString() : string.Empty,
            episode.ChannelTitle,
            episode.ChannelApiKey,
            episode.IsDownloaded,
            episode.CreatedAt.ToIso8601String(),
            episode.Tags,
            episode.UserStarred,
            episode.UserRating,
            episode.DownloadStatus.ToString(),
            episode.DownloadError,
            episode.EnclosureUrl,
            episode.LastPlayedAt?.ToIso8601String() ?? null,
            episode.PlayedCount
        );
    }
}
