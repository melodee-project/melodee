using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Common.Models.Collection.Extensions;

namespace Melodee.Blazor.Controllers.Melodee.Extensions;

public static class PodcastChannelDataInfoExtensions
{
    public static PodcastChannel ToPodcastChannelModel(this Melodee.Common.Models.Collection.PodcastChannelDataInfo channel)
    {
        return new PodcastChannel(
            channel.ApiKey,
            channel.Title,
            channel.Description,
            channel.ImageUrl,
            channel.FeedUrl,
            channel.Website,
            channel.LastSyncAt?.ToString("O") ?? null,
            channel.CreatedAt.ToString("O"),
            channel.Tags,
            channel.UserStarred,
            channel.UserRating,
            channel.SiteUrl,
            channel.EpisodeCount,
            channel.LastPlayedAt?.ToString("O") ?? null,
            channel.PlayedCount,
            channel.UnplayedDownloadedCount
        );
    }
}
