using Melodee.Common.Extensions;
using Melodee.Common.Models.Collection;

namespace Melodee.Blazor.Controllers.Melodee.Extensions;

public static class PodcastChannelDataInfoExtensions
{
    public static Models.PodcastChannel ToPodcastChannelDto(this global::Melodee.Common.Data.Models.PodcastChannel entity)
    {
        return new Models.PodcastChannel(
            entity.ApiKey,
            entity.Title,
            entity.Description ?? string.Empty,
            string.Empty,
            entity.FeedUrl,
            entity.SiteUrl ?? string.Empty,
            null,
            entity.CreatedAt.ToIso8601String(),
            string.Empty,
            false,
            0,
            entity.SiteUrl ?? null,
            0,
            null,
            0,
            0
        );
    }

    public static Models.PodcastChannel ToPodcastChannelModel(this PodcastChannelDataInfo channel)
    {
        return new Models.PodcastChannel(
            channel.ApiKey,
            channel.Title,
            channel.Description,
            channel.ImageUrl,
            channel.FeedUrl,
            channel.Website,
            channel.LastSyncAt?.ToIso8601String() ?? null,
            channel.CreatedAt.ToIso8601String(),
            channel.Tags,
            channel.UserStarred,
            channel.UserRating,
            channel.SiteUrl,
            channel.EpisodeCount,
            channel.LastPlayedAt?.ToIso8601String() ?? null,
            channel.PlayedCount,
            channel.UnplayedDownloadedCount
        );
    }

    public static Models.PodcastChannel ToPodcastChannelDataInfoDto(this PodcastChannelDataInfo channel)
    {
        return new Models.PodcastChannel(
            channel.ApiKey,
            channel.Title,
            channel.Description,
            channel.ImageUrl,
            channel.FeedUrl,
            channel.Website,
            channel.LastSyncAt?.ToIso8601String() ?? null,
            channel.CreatedAt.ToIso8601String(),
            channel.Tags,
            channel.UserStarred,
            channel.UserRating,
            channel.SiteUrl,
            channel.EpisodeCount,
            channel.LastPlayedAt?.ToIso8601String() ?? null,
            channel.PlayedCount,
            channel.UnplayedDownloadedCount
        );
    }
}
