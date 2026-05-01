using System.ServiceModel.Syndication;
using System.Xml;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
///     Service for managing podcast channels and episodes.
/// </summary>
public sealed class PodcastService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMelodeeConfigurationFactory configurationFactory,
    LibraryService libraryService,
    ISsrfValidator ssrfValidator,
    PodcastHttpClient podcastHttpClient) : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<DbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        return await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<OperationResult<IEnumerable<PodcastChannel>>> ListChannelsAsync(
        int userId,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var query = context.PodcastChannels
                .Where(x => x.UserId == userId && !x.IsDeleted)
                .Include(x => x.Episodes)
                .OrderByDescending(x => x.LastSyncAt);

            var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            var channels = await query
                .Skip(offset ?? 0)
                .Take(limit ?? 100)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return new OperationResult<IEnumerable<PodcastChannel>>
            {
                Data = channels,
                AdditionalData = new Dictionary<string, object> { ["TotalCount"] = totalCount }
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error listing channels for user {UserId}", nameof(PodcastService), userId);
            return new OperationResult<IEnumerable<PodcastChannel>>(ex.Message) { Data = [] };
        }
    }

    /// <summary>
    /// Gets the count of downloaded but unplayed podcast episodes for a user.
    /// Used for displaying a badge on the Podcasts menu item.
    /// </summary>
    public async Task<int> GetUnplayedDownloadedEpisodeCountAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var count = await context.PodcastEpisodes
                .Where(e => e.PodcastChannel != null
                         && e.PodcastChannel.UserId == userId
                         && !e.PodcastChannel.IsDeleted
                         && e.DownloadStatus == PodcastEpisodeDownloadStatus.Downloaded
                         && !context.UserPodcastEpisodePlayHistories
                             .Any(h => h.UserId == userId && h.PodcastEpisodeId == e.Id))
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);

            return count;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error getting unplayed episode count for user {UserId}", nameof(PodcastService), userId);
            return 0;
        }
    }

    public async Task<OperationResult<PodcastChannel?>> GetChannelAsync(
        int channelId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var channel = await context.PodcastChannels
                .Include(x => x.Episodes)
                .FirstOrDefaultAsync(x => x.Id == channelId && x.UserId == userId && !x.IsDeleted, cancellationToken)
                .ConfigureAwait(false);

            if (channel == null)
            {
                return new OperationResult<PodcastChannel?>("Channel not found") { Data = null };
            }

            return new OperationResult<PodcastChannel?> { Data = channel };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error getting channel {ChannelId}", nameof(PodcastService), channelId);
            return new OperationResult<PodcastChannel?>(ex.Message) { Data = null };
        }
    }

    public async Task<OperationResult<PodcastChannel>> CreateChannelAsync(
        int userId,
        string feedUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Validate URL with SSRF protection
            var ssrfResult = await ssrfValidator.ValidateUrlAsync(feedUrl, cancellationToken).ConfigureAwait(false);
            if (!ssrfResult.IsValid)
            {
                return new OperationResult<PodcastChannel>(ssrfResult.ErrorMessage ?? "URL validation failed") { Data = null! };
            }

            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var existingChannel = await context.PodcastChannels
                .FirstOrDefaultAsync(x => x.UserId == userId && x.FeedUrl == feedUrl, cancellationToken)
                .ConfigureAwait(false);

            if (existingChannel != null)
            {
                return new OperationResult<PodcastChannel>("Channel with this feed URL already exists") { Data = null! };
            }

            var channel = new PodcastChannel
            {
                UserId = userId,
                FeedUrl = feedUrl,
                Title = "New Podcast",
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            };

            context.PodcastChannels.Add(channel);
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Logger.Information("[{ServiceName}] Created channel {ChannelId} for user {UserId}", nameof(PodcastService), channel.Id, userId);

            // Refresh the channel immediately to populate metadata and episodes
            await RefreshChannelAsync(channel.Id, cancellationToken).ConfigureAwait(false);

            // Reload to get the latest updates
            var updatedChannel = await context.PodcastChannels
                .FirstOrDefaultAsync(x => x.Id == channel.Id, cancellationToken)
                .ConfigureAwait(false);

            return new OperationResult<PodcastChannel> { Data = updatedChannel ?? channel };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error creating channel for user {UserId}", nameof(PodcastService), userId);
            return new OperationResult<PodcastChannel>(ex.Message) { Data = null! };
        }
    }

    public async Task<OperationResult<bool>> DeleteChannelAsync(
        int channelId,
        int userId,
        bool softDelete = true,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var channel = await context.PodcastChannels
                .Include(x => x.Episodes)
                .FirstOrDefaultAsync(x => x.Id == channelId && x.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            if (channel == null)
            {
                return new OperationResult<bool>("Channel not found") { Data = false };
            }

            // Get podcast library for file operations
            var podcastLibraryResult = await libraryService.GetPodcastLibraryAsync(cancellationToken).ConfigureAwait(false);
            var podcastLibrary = podcastLibraryResult?.Data;

            if (softDelete)
            {
                channel.IsDeleted = true;
                channel.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

                // Delete downloaded episode files
                foreach (var episode in channel.Episodes.Where(x => x.DownloadStatus == PodcastEpisodeDownloadStatus.Downloaded))
                {
                    if (podcastLibrary != null && !string.IsNullOrEmpty(episode.LocalPath))
                    {
                        var filePath = Path.Combine(podcastLibrary.Path, episode.LocalPath);
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                File.Delete(filePath);
                                Logger.Debug("[{ServiceName}] Deleted episode file: {FilePath}", nameof(PodcastService), filePath);
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning(ex, "[{ServiceName}] Failed to delete episode file: {FilePath}", nameof(PodcastService), filePath);
                            }
                        }
                    }

                    episode.DownloadStatus = PodcastEpisodeDownloadStatus.None;
                    episode.LocalPath = null;
                    episode.LocalFileSize = null;
                }
            }
            else
            {
                // Hard delete - remove all episode files and channel folder
                if (podcastLibrary != null)
                {
                    // Delete individual episode files first
                    foreach (var episode in channel.Episodes.Where(x => !string.IsNullOrEmpty(x.LocalPath)))
                    {
                        var filePath = Path.Combine(podcastLibrary.Path, episode.LocalPath!);
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                File.Delete(filePath);
                                Logger.Debug("[{ServiceName}] Deleted episode file: {FilePath}", nameof(PodcastService), filePath);
                            }
                            catch (Exception ex)
                            {
                                Logger.Warning(ex, "[{ServiceName}] Failed to delete episode file: {FilePath}", nameof(PodcastService), filePath);
                            }
                        }
                    }

                    // Delete channel folder (userId/channelId)
                    var channelFolder = Path.Combine(podcastLibrary.Path, userId.ToString(), channelId.ToString());
                    if (Directory.Exists(channelFolder))
                    {
                        try
                        {
                            Directory.Delete(channelFolder, recursive: true);
                            Logger.Debug("[{ServiceName}] Deleted channel folder: {FolderPath}", nameof(PodcastService), channelFolder);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "[{ServiceName}] Failed to delete channel folder: {FolderPath}", nameof(PodcastService), channelFolder);
                        }
                    }

                    // Try to delete user folder if empty
                    var userFolder = Path.Combine(podcastLibrary.Path, userId.ToString());
                    if (Directory.Exists(userFolder))
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(userFolder).Any())
                            {
                                Directory.Delete(userFolder);
                                Logger.Debug("[{ServiceName}] Deleted empty user folder: {FolderPath}", nameof(PodcastService), userFolder);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "[{ServiceName}] Failed to delete user folder: {FolderPath}", nameof(PodcastService), userFolder);
                        }
                    }
                }

                context.PodcastEpisodes.RemoveRange(channel.Episodes);
                context.PodcastChannels.Remove(channel);
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Logger.Information("[{ServiceName}] Deleted channel {ChannelId} for user {UserId} (softDelete={SoftDelete})",
                nameof(PodcastService), channelId, userId, softDelete);

            return new OperationResult<bool> { Data = true };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error deleting channel {ChannelId}", nameof(PodcastService), channelId);
            return new OperationResult<bool>(ex.Message) { Data = false };
        }
    }

    /// <summary>
    /// Updates channel settings such as auto-download and refresh interval.
    /// </summary>
    public async Task<OperationResult<PodcastChannel>> UpdateChannelAsync(
        int channelId,
        int userId,
        bool? autoDownloadEnabled = null,
        int? refreshIntervalHours = null,
        int? maxDownloadedEpisodes = null,
        long? maxStorageBytes = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var channel = await context.PodcastChannels
                .FirstOrDefaultAsync(x => x.Id == channelId && x.UserId == userId && !x.IsDeleted, cancellationToken)
                .ConfigureAwait(false);

            if (channel == null)
            {
                return new OperationResult<PodcastChannel>(OperationResponseType.NotFound, "Channel not found") { Data = null! };
            }

            var updated = false;

            if (autoDownloadEnabled.HasValue && channel.AutoDownloadEnabled != autoDownloadEnabled.Value)
            {
                channel.AutoDownloadEnabled = autoDownloadEnabled.Value;
                updated = true;
                Logger.Debug("[{ServiceName}] Updated AutoDownloadEnabled to {Value} for channel {ChannelId}",
                    nameof(PodcastService), autoDownloadEnabled.Value, channelId);
            }

            if (refreshIntervalHours.HasValue)
            {
                // Validate refresh interval (minimum 1 hour, null means use global)
                var newValue = refreshIntervalHours.Value <= 0 ? null : refreshIntervalHours;
                if (channel.RefreshIntervalHours != newValue)
                {
                    channel.RefreshIntervalHours = newValue;
                    updated = true;
                    Logger.Debug("[{ServiceName}] Updated RefreshIntervalHours to {Value} for channel {ChannelId}",
                        nameof(PodcastService), newValue, channelId);
                }
            }

            if (maxDownloadedEpisodes.HasValue)
            {
                var newValue = maxDownloadedEpisodes.Value <= 0 ? null : maxDownloadedEpisodes;
                if (channel.MaxDownloadedEpisodes != newValue)
                {
                    channel.MaxDownloadedEpisodes = newValue;
                    updated = true;
                    Logger.Debug("[{ServiceName}] Updated MaxDownloadedEpisodes to {Value} for channel {ChannelId}",
                        nameof(PodcastService), newValue, channelId);
                }
            }

            if (maxStorageBytes.HasValue)
            {
                var newValue = maxStorageBytes.Value <= 0 ? null : maxStorageBytes;
                if (channel.MaxStorageBytes != newValue)
                {
                    channel.MaxStorageBytes = newValue;
                    updated = true;
                    Logger.Debug("[{ServiceName}] Updated MaxStorageBytes to {Value} for channel {ChannelId}",
                        nameof(PodcastService), newValue, channelId);
                }
            }

            if (updated)
            {
                channel.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                Logger.Information("[{ServiceName}] Updated channel {ChannelId} settings for user {UserId}",
                    nameof(PodcastService), channelId, userId);
            }

            return new OperationResult<PodcastChannel> { Data = channel };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error updating channel {ChannelId}", nameof(PodcastService), channelId);
            return new OperationResult<PodcastChannel>(OperationResponseType.Error, ex.Message) { Data = null! };
        }
    }

    public async Task<OperationResult<IEnumerable<PodcastEpisode>>> ListEpisodesAsync(
        int channelId,
        int userId,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var channel = await context.PodcastChannels
                .FirstOrDefaultAsync(x => x.Id == channelId && x.UserId == userId && !x.IsDeleted, cancellationToken)
                .ConfigureAwait(false);

            if (channel == null)
            {
                return new OperationResult<IEnumerable<PodcastEpisode>>("Channel not found") { Data = [] };
            }

            var query = context.PodcastEpisodes
                .Where(x => x.PodcastChannelId == channelId)
                .OrderByDescending(x => x.PublishDate);

            var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

            var episodes = await query
                .Skip(offset ?? 0)
                .Take(limit ?? 100)
                .ToListAsync(cancellationToken).ConfigureAwait(false);

            return new OperationResult<IEnumerable<PodcastEpisode>>
            {
                Data = episodes,
                AdditionalData = new Dictionary<string, object> { ["TotalCount"] = totalCount }
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error listing episodes for channel {ChannelId}", nameof(PodcastService), channelId);
            return new OperationResult<IEnumerable<PodcastEpisode>>(ex.Message) { Data = [] };
        }
    }

    public async Task<OperationResult<PodcastEpisode?>> GetEpisodeAsync(
        int episodeId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var episode = await context.PodcastEpisodes
                .Include(x => x.PodcastChannel)
                .FirstOrDefaultAsync(x => x.Id == episodeId && x.PodcastChannel != null && x.PodcastChannel.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            if (episode == null)
            {
                return new OperationResult<PodcastEpisode?>("Episode not found") { Data = null };
            }

            return new OperationResult<PodcastEpisode?> { Data = episode };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error getting episode {EpisodeId}", nameof(PodcastService), episodeId);
            return new OperationResult<PodcastEpisode?>(ex.Message) { Data = null };
        }
    }

    /// <summary>
    /// Get a podcast episode by ID without user filtering.
    /// Used for streaming when authentication is handled at the endpoint level.
    /// </summary>
    public async Task<OperationResult<PodcastEpisode?>> GetEpisodeForStreamingAsync(
        int episodeId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var episode = await context.PodcastEpisodes
                .Include(x => x.PodcastChannel)
                .FirstOrDefaultAsync(x => x.Id == episodeId, cancellationToken)
                .ConfigureAwait(false);

            if (episode == null)
            {
                Logger.Warning("[{ServiceName}] Episode {EpisodeId} not found for streaming", nameof(PodcastService), episodeId);
                return new OperationResult<PodcastEpisode?>("Episode not found") { Data = null };
            }

            return new OperationResult<PodcastEpisode?> { Data = episode };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error getting episode {EpisodeId} for streaming", nameof(PodcastService), episodeId);
            return new OperationResult<PodcastEpisode?>(ex.Message) { Data = null };
        }
    }

    public async Task<OperationResult<bool>> QueueDownloadAsync(
        int episodeId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var episode = await context.PodcastEpisodes
                .Include(x => x.PodcastChannel)
                .FirstOrDefaultAsync(x => x.Id == episodeId && x.PodcastChannel != null && x.PodcastChannel.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            if (episode == null)
            {
                return new OperationResult<bool>("Episode not found") { Data = false };
            }

            if (episode.DownloadStatus == PodcastEpisodeDownloadStatus.Downloaded)
            {
                return new OperationResult<bool> { Data = true };
            }

            episode.DownloadStatus = PodcastEpisodeDownloadStatus.Queued;
            episode.DownloadError = null;
            episode.QueuedAt = SystemClock.Instance.GetCurrentInstant();
            episode.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Logger.Information("[{ServiceName}] Queued download for episode {EpisodeId}", nameof(PodcastService), episodeId);

            return new OperationResult<bool> { Data = true };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error queuing download for episode {EpisodeId}", nameof(PodcastService), episodeId);
            return new OperationResult<bool>(ex.Message) { Data = false };
        }
    }

    public async Task<OperationResult<bool>> DeleteEpisodeAsync(
        int episodeId,
        int userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var episode = await context.PodcastEpisodes
                .Include(x => x.PodcastChannel)
                .FirstOrDefaultAsync(x => x.Id == episodeId && x.PodcastChannel != null && x.PodcastChannel.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            if (episode == null)
            {
                return new OperationResult<bool>("Episode not found") { Data = false };
            }

            if (!string.IsNullOrEmpty(episode.LocalPath))
            {
                var podcastLibraryResult = await libraryService.GetPodcastLibraryAsync(cancellationToken).ConfigureAwait(false);
                var podcastLibrary = podcastLibraryResult?.Data;

                if (podcastLibrary != null)
                {
                    var filePath = Path.Combine(podcastLibrary.Path, episode.LocalPath);
                    if (File.Exists(filePath))
                    {
                        try
                        {
                            File.Delete(filePath);
                            Logger.Debug("[{ServiceName}] Deleted episode file: {FilePath}", nameof(PodcastService), filePath);
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "[{ServiceName}] Failed to delete episode file: {FilePath}", nameof(PodcastService), filePath);
                        }
                    }
                }
            }

            episode.DownloadStatus = PodcastEpisodeDownloadStatus.None;
            episode.LocalPath = null;
            episode.LocalFileSize = null;
            episode.LastUpdatedAt = SystemClock.Instance.GetCurrentInstant();

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Logger.Information("[{ServiceName}] Deleted episode {EpisodeId}", nameof(PodcastService), episodeId);

            return new OperationResult<bool> { Data = true };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error deleting episode {EpisodeId}", nameof(PodcastService), episodeId);
            return new OperationResult<bool>(ex.Message) { Data = false };
        }
    }

    public async Task<OperationResult<bool>> RefreshChannelAsync(
        int channelId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            var channel = await context.PodcastChannels
                .Include(x => x.Episodes)
                .FirstOrDefaultAsync(x => x.Id == channelId, cancellationToken)
                .ConfigureAwait(false);

            if (channel == null)
            {
                return new OperationResult<bool>("Channel not found") { Data = false };
            }

            var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
            var podcastLibraryResult = await libraryService.GetPodcastLibraryAsync(cancellationToken).ConfigureAwait(false);
            var podcastLibrary = podcastLibraryResult.Data;

            if (podcastLibrary == null)
            {
                return new OperationResult<bool>("No podcast library configured") { Data = false };
            }

            var maxItemsPerChannel = configuration.GetValue<int>(SettingRegistry.PodcastRefreshMaxItemsPerChannel);
            var maxFeedBytes = configuration.GetValue<long>(SettingRegistry.PodcastHttpMaxFeedBytes);

            Logger.Information("[{ServiceName}] Refreshing channel [{ChannelId}] {ChannelTitle}", nameof(PodcastService), channel.Id, channel.Title);

            channel.LastSyncAttemptAt = SystemClock.Instance.GetCurrentInstant();

            try
            {
                // Build conditional request headers
                var headers = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(channel.Etag))
                {
                    headers["If-None-Match"] = $"\"{channel.Etag}\"";
                }
                if (channel.LastModified.HasValue)
                {
                    headers["If-Modified-Since"] = channel.LastModified.Value.ToString("R");
                }

                // Use resilient HTTP client with SSRF protection and Polly retries
                var httpResult = await podcastHttpClient.GetAsync(
                    channel.FeedUrl,
                    headers,
                    maxFeedBytes,
                    cancellationToken).ConfigureAwait(false);

                if (!httpResult.IsSuccess)
                {
                    // Check if it's a 304 Not Modified (not exposed directly, but we handle via error message pattern)
                    if (httpResult.ErrorMessage?.Contains("304") == true)
                    {
                        Logger.Debug("[{ServiceName}] Channel [{ChannelId}] not modified, skipping", nameof(PodcastService), channel.Id);
                        channel.LastSyncAt = SystemClock.Instance.GetCurrentInstant();
                        channel.ConsecutiveFailureCount = 0;
                        channel.NextSyncAt = null;
                        channel.LastSyncError = null;
                        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                        return new OperationResult<bool> { Data = true };
                    }
                    throw new Exception(httpResult.ErrorMessage ?? "Failed to fetch feed");
                }

                using var response = httpResult.Response!;

                if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                {
                    Logger.Debug("[{ServiceName}] Channel [{ChannelId}] not modified, skipping", nameof(PodcastService), channel.Id);
                    channel.LastSyncAt = SystemClock.Instance.GetCurrentInstant();
                    channel.ConsecutiveFailureCount = 0;
                    channel.NextSyncAt = null;
                    channel.LastSyncError = null;
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return new OperationResult<bool> { Data = true };
                }

                var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                channel.Etag = response.Headers.ETag?.Tag;
                channel.LastModified = response.Content.Headers.LastModified;

                await ParseAndUpdateEpisodesAsync(context, channel, content, maxItemsPerChannel, podcastLibrary, configuration, cancellationToken).ConfigureAwait(false);

                channel.LastSyncAt = SystemClock.Instance.GetCurrentInstant();
                channel.LastSyncError = null;
                channel.ConsecutiveFailureCount = 0;

                // Set NextSyncAt based on per-channel interval, or null for global schedule
                channel.NextSyncAt = channel.RefreshIntervalHours.HasValue && channel.RefreshIntervalHours.Value > 0
                    ? SystemClock.Instance.GetCurrentInstant() + Duration.FromHours(channel.RefreshIntervalHours.Value)
                    : null;
            }
            catch (Exception ex)
            {
                channel.LastSyncError = ex.Message;
                channel.ConsecutiveFailureCount++;
                channel.NextSyncAt = CalculateNextSyncTime(channel.ConsecutiveFailureCount);
                Logger.Error(ex, "[{ServiceName}] Error refreshing channel [{ChannelId}]", nameof(PodcastService), channel.Id);
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return new OperationResult<bool>(ex.Message) { Data = false };
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new OperationResult<bool> { Data = true };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error in RefreshChannelAsync for channel {ChannelId}", nameof(PodcastService), channelId);
            return new OperationResult<bool>(ex.Message) { Data = false };
        }
    }

    private async Task ParseAndUpdateEpisodesAsync(MelodeeDbContext context, PodcastChannel channel, string feedContent, int maxItemsPerChannel, Library podcastLibrary, IMelodeeConfiguration configuration, CancellationToken cancellationToken)
    {
        var feed = SyndicationFeed.Load(
            XmlReader.Create(new StringReader(feedContent), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore, // Ignore DTD declarations in podcast feeds (many feeds include them for compatibility)
                XmlResolver = null // Prevent external entity resolution for security
            }));

        if (feed.Id.Nullify() == null && feed.Title.Text.Nullify() == null)
        {
            throw new Exception("Invalid feed: missing id or title");
        }

        channel.Title = feed.Title.Text;
        channel.TitleNormalized = feed.Title.Text.ToNormalizedString();
        channel.Description = feed.Description?.Text;
        channel.SiteUrl = feed.Links.FirstOrDefault(l => l.RelationshipType == "alternate")?.Uri?.ToString();

        var imageLink = feed.ImageUrl?.ToString() ??
                        feed.Links.FirstOrDefault(l => l.MediaType?.Contains("image") == true)?.Uri?.ToString();
        channel.ImageUrl = imageLink;

        if (!string.IsNullOrEmpty(imageLink))
        {
            try
            {
                channel.CoverArtLocalPath = await DownloadCoverArtAsync(channel, imageLink, podcastLibrary, configuration, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Warning(ex, "[{ServiceName}] Failed to download cover art for channel [{ChannelId}]", nameof(PodcastService), channel.Id);
            }
        }

        var existingEpisodes = channel.Episodes.ToDictionary(x => x.EpisodeKey);
        // If episodes weren't loaded, we might want to query them, but typically we Include them in RefreshChannelAsync or CreateChannelAsync is new.
        // In RefreshChannelAsync above we used .Include(x => x.Episodes).

        var episodeCount = 0;
        foreach (var item in feed.Items.Take(maxItemsPerChannel))
        {
            var episodeKey = item.Id.Nullify() ?? $"{item.Links.FirstOrDefault(l => l.Uri != null)?.Uri}#{item.PublishDate:yyyy-MM-dd HH:mm:ss}";

            var enclosure = item.Links.FirstOrDefault(l => l.MediaType?.StartsWith("audio/") == true || l.MediaType?.StartsWith("video/") == true);

            if (enclosure?.Uri == null)
            {
                continue;
            }

            if (!existingEpisodes.TryGetValue(episodeKey, out var episode))
            {
                episode = new PodcastEpisode
                {
                    PodcastChannelId = channel.Id,
                    EpisodeKey = episodeKey,
                    Title = "Untitled",
                    EnclosureUrl = enclosure.Uri.ToString(),
                    CreatedAt = SystemClock.Instance.GetCurrentInstant()
                };

                // Auto-queue new episodes for download if channel has auto-download enabled
                if (channel.AutoDownloadEnabled)
                {
                    episode.DownloadStatus = PodcastEpisodeDownloadStatus.Queued;
                    episode.QueuedAt = SystemClock.Instance.GetCurrentInstant();
                    Logger.Debug("[{ServiceName}] Auto-queued new episode '{EpisodeTitle}' for download", nameof(PodcastService), episode.Title);
                }

                context.PodcastEpisodes.Add(episode);
                existingEpisodes[episodeKey] = episode;
            }

            episode.Title = item.Title?.Text ?? "Untitled";
            episode.TitleNormalized = episode.Title.ToNormalizedString();
            episode.Description = item.Summary?.Text ?? item.Content?.ToString();
            episode.PublishDate = item.PublishDate != DateTimeOffset.MinValue
                ? Instant.FromDateTimeOffset(item.PublishDate)
                : null;
            episode.Guid = item.Id;
            episode.EnclosureUrl = enclosure.Uri.ToString();
            episode.MimeType = enclosure.MediaType;

            if (enclosure.Length > 0)
            {
                episode.EnclosureLength = enclosure.Length;
            }
            else
            {
                episode.EnclosureLength = null;
            }

            episodeCount++;
        }

        Logger.Information("[{ServiceName}] Processed {EpisodeCount} episodes for channel [{ChannelId}]", nameof(PodcastService), episodeCount, channel.Id);
    }

    private async Task<string?> DownloadCoverArtAsync(PodcastChannel channel, string imageUrl, Library podcastLibrary, IMelodeeConfiguration configuration, CancellationToken cancellationToken)
    {
        try
        {
            const long maxCoverArtBytes = 10 * 1024 * 1024; // 10 MB max for cover art

            var httpResult = await podcastHttpClient.GetAsync(imageUrl, maxResponseBytes: maxCoverArtBytes, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!httpResult.IsSuccess)
            {
                Logger.Warning("[{ServiceName}] Failed to download cover art for channel [{ChannelId}]: {Error}", nameof(PodcastService), channel.Id, httpResult.ErrorMessage);
                return null;
            }

            using var response = httpResult.Response!;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
            var extension = contentType.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/gif" => ".gif",
                "image/webp" => ".webp",
                _ => ".jpg"
            };

            var imageBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            var userDirectory = Path.Combine(podcastLibrary.Path, channel.UserId.ToString());
            var channelDirectory = Path.Combine(userDirectory, channel.Id.ToString());
            Directory.CreateDirectory(channelDirectory);

            var fileName = $"cover{extension}";
            var filePath = Path.Combine(channelDirectory, fileName);
            await File.WriteAllBytesAsync(filePath, imageBytes, cancellationToken).ConfigureAwait(false);

            var relativePath = Path.Combine(channel.UserId.ToString(), channel.Id.ToString(), fileName);
            Logger.Information("[{ServiceName}] Downloaded cover art for channel [{ChannelId}] to {Path}", nameof(PodcastService), channel.Id, relativePath);

            return relativePath;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "[{ServiceName}] Error downloading cover art for channel [{ChannelId}]", nameof(PodcastService), channel.Id);
            return null;
        }
    }

    private static Instant? CalculateNextSyncTime(int failureCount)
    {
        var initialInterval = TimeSpan.FromMinutes(15);
        var maxBackoff = TimeSpan.FromHours(24);

        var backoff = TimeSpan.FromMinutes(Math.Pow(2, failureCount) * initialInterval.TotalMinutes);
        if (backoff > maxBackoff)
        {
            backoff = maxBackoff;
        }

        return SystemClock.Instance.GetCurrentInstant() + Duration.FromTimeSpan(backoff);
    }

    public async Task<OperationResult<IEnumerable<PodcastEpisode>>> GetNewestEpisodesAsync(
        int userId,
        int count = 20,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // First get the channel IDs for this user that are not deleted
            var channelIds = await context.PodcastChannels
                .IgnoreQueryFilters()
                .Where(c => c.UserId == userId && !c.IsDeleted)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Get the episodes for those channels, then order in memory
            var episodes = await context.PodcastEpisodes
                .Include(x => x.PodcastChannel)
                .Where(e => channelIds.Contains(e.PodcastChannelId))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var orderedEpisodes = episodes
                .OrderByDescending(x => x.PublishDate)
                .Skip(offset)
                .Take(count)
                .ToList();

            return new OperationResult<IEnumerable<PodcastEpisode>> { Data = orderedEpisodes };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{ServiceName}] Error getting newest episodes for user {UserId}", nameof(PodcastService), userId);
            return new OperationResult<IEnumerable<PodcastEpisode>>(ex.Message) { Data = [] };
        }
    }

    public async Task<PagedResult<PodcastChannelDataInfo>> ListChannelsAsync(PagedRequest request, int userId, CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        Log.Information("[PodcastService.ListChannelsAsync] Starting query for UserId={UserId}, Skip={Skip}, Take={Take}",
            userId, request.SkipValue, request.TakeValue);

        var query = context.PodcastChannels
            .Include(x => x.Episodes)
            .Where(x => x.UserId == userId && !x.IsDeleted)
            .AsNoTracking();

        if (request.FilterBy?.Any() == true)
        {
            var filter = request.FilterBy.First();
            if (filter.PropertyName.Equals(nameof(PodcastChannelDataInfo.TitleNormalized), StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => EF.Functions.Like(x.Title, $"%{filter.Value}%"));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        Log.Information("[PodcastService.ListChannelsAsync] TotalCount={TotalCount} for UserId={UserId}", totalCount, userId);

        var channels = await query
            .OrderBy(x => x.Title)
            .Skip(request.SkipValue)
            .Take(request.TakeValue)
            .Select(x => new PodcastChannelDataInfo(
                x.Id,
                x.ApiKey,
                x.Title,
                x.TitleNormalized ?? x.Title,
                x.Description ?? string.Empty,
                x.ImageUrl ?? string.Empty,
                x.FeedUrl,
                x.SiteUrl ?? string.Empty,
                x.LastSyncAt,
                x.CreatedAt,
                string.Empty,
                false,
                0,
                x.SiteUrl,
                x.Episodes.Count,
                null,
                0,
                x.Episodes.Count(e =>
                    e.DownloadStatus == PodcastEpisodeDownloadStatus.Downloaded &&
                    !context.UserPodcastEpisodePlayHistories.Any(h => h.UserId == userId && h.PodcastEpisodeId == e.Id))))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        Log.Information("[PodcastService.ListChannelsAsync] Returning {Count} channels out of {TotalCount} for UserId={UserId}",
            channels.Length, totalCount, userId);

        return new PagedResult<PodcastChannelDataInfo>
        {
            TotalCount = totalCount,
            TotalPages = request.TotalPages(totalCount),
            Data = channels
        };
    }

    public async Task<PagedResult<PodcastEpisodeDataInfo>> ListEpisodesAsync(PagedRequest request, int userId, CancellationToken cancellationToken = default)
    {
        return await ListEpisodesAsync(request, userId, null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PagedResult<PodcastEpisodeDataInfo>> ListEpisodesAsync(PagedRequest request, int userId, int? channelId, CancellationToken cancellationToken = default)
    {
        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = context.PodcastEpisodes
            .Include(x => x.PodcastChannel)
            .Where(x => x.PodcastChannel != null && x.PodcastChannel.UserId == userId && !x.PodcastChannel.IsDeleted)
            .AsNoTracking();

        if (channelId.HasValue)
        {
            query = query.Where(x => x.PodcastChannelId == channelId.Value);
        }

        if (request.FilterBy?.Any() == true)
        {
            var filter = request.FilterBy.First();
            if (filter.PropertyName.Equals(nameof(PodcastEpisodeDataInfo.TitleNormalized), StringComparison.OrdinalIgnoreCase))
            {
                query = query.Where(x => EF.Functions.Like(x.Title, $"%{filter.Value}%"));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // Join with play history to get LastPlayedAt and PlayedCount
        var episodes = await query
            .OrderByDescending(x => x.PublishDate)
            .Skip(request.SkipValue)
            .Take(request.TakeValue)
            .Select(x => new PodcastEpisodeDataInfo(
                x.Id,
                x.ApiKey,
                x.Title,
                x.TitleNormalized ?? x.Title,
                x.Description ?? string.Empty,
                x.PublishDate,
                x.Duration,
                x.PodcastChannel!.Title,
                x.PodcastChannel!.ApiKey,
                x.DownloadStatus == PodcastEpisodeDownloadStatus.Downloaded,
                x.CreatedAt,
                string.Empty,
                false,
                0,
                x.DownloadStatus,
                x.DownloadError,
                x.EnclosureUrl,
                context.UserPodcastEpisodePlayHistories
                    .Where(h => h.UserId == userId && h.PodcastEpisodeId == x.Id && !h.IsNowPlaying)
                    .OrderByDescending(h => h.PlayedAt)
                    .Select(h => (Instant?)h.PlayedAt)
                    .FirstOrDefault(),
                context.UserPodcastEpisodePlayHistories
                    .Count(h => h.UserId == userId && h.PodcastEpisodeId == x.Id && !h.IsNowPlaying)))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new PagedResult<PodcastEpisodeDataInfo>
        {
            TotalCount = totalCount,
            TotalPages = request.TotalPages(totalCount),
            Data = episodes
        };
    }

    /// <summary>
    /// Searches podcast episodes by title and description across all channels for a user.
    /// </summary>
    public async Task<PagedResult<PodcastEpisodeDataInfo>> SearchEpisodesAsync(
        string query,
        int userId,
        PagedRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new PagedResult<PodcastEpisodeDataInfo>
            {
                TotalCount = 0,
                TotalPages = 0,
                Data = []
            };
        }

        await using var context = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var normalizedQuery = (query.ToNormalizedString() ?? query).ToLowerInvariant();

        // Get channel IDs for this user
        var channelIds = await context.PodcastChannels
            .Where(c => c.UserId == userId && !c.IsDeleted)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Search episodes by normalized title or description containing the query (case-insensitive)
        var episodeQuery = context.PodcastEpisodes
            .Include(x => x.PodcastChannel)
            .Where(e => channelIds.Contains(e.PodcastChannelId))
            .Where(e =>
                (e.TitleNormalized != null && e.TitleNormalized.ToLower().Contains(normalizedQuery)) ||
                (e.Description != null && e.Description.ToLower().Contains(normalizedQuery)));

        var totalCount = await episodeQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        // Load then order in memory for compatibility
        var allMatchingEpisodes = await episodeQuery.ToListAsync(cancellationToken).ConfigureAwait(false);

        var episodes = allMatchingEpisodes
            .OrderByDescending(x => x.PublishDate)
            .Skip(request.SkipValue)
            .Take(request.TakeValue)
            .Select(x => new PodcastEpisodeDataInfo(
                x.Id,
                x.ApiKey,
                x.Title,
                x.TitleNormalized ?? x.Title,
                x.Description ?? string.Empty,
                x.PublishDate,
                x.Duration,
                x.PodcastChannel?.Title ?? string.Empty,
                x.PodcastChannel?.ApiKey ?? Guid.Empty,
                x.DownloadStatus == PodcastEpisodeDownloadStatus.Downloaded,
                x.CreatedAt,
                string.Empty,
                false,
                0,
                x.DownloadStatus,
                x.DownloadError,
                x.EnclosureUrl,
                context.UserPodcastEpisodePlayHistories
                    .Where(h => h.UserId == userId && h.PodcastEpisodeId == x.Id && !h.IsNowPlaying)
                    .OrderByDescending(h => h.PlayedAt)
                    .Select(h => (Instant?)h.PlayedAt)
                    .FirstOrDefault(),
                context.UserPodcastEpisodePlayHistories
                    .Count(h => h.UserId == userId && h.PodcastEpisodeId == x.Id && !h.IsNowPlaying)))
            .ToArray();

        Logger.Information("[{ServiceName}] Episode search for '{Query}' found {Count} results for user {UserId}",
            nameof(PodcastService), LogSanitizer.Sanitize(query), totalCount, userId);

        return new PagedResult<PodcastEpisodeDataInfo>
        {
            TotalCount = totalCount,
            TotalPages = request.TotalPages(totalCount),
            Data = episodes
        };
    }
}
