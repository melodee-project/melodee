using Melodee.Common.Data;
using Melodee.Common.Models;
using Melodee.Common.Models.Scrobbling;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Plugins.Scrobbling;

/// <summary>
///     Database-backed repository for tracking "Now Playing" state.
///     Uses the UserSongPlayHistory table with IsNowPlaying flag for persistence.
/// </summary>
public sealed class NowPlayingDatabaseRepository(
    ILogger logger,
    IDbContextFactory<MelodeeDbContext> contextFactory) : INowPlayingRepository
{
    /// <summary>
    ///     Entries without a heartbeat for longer than this are considered stale.
    ///     Set to 60 minutes to accommodate very long songs (some tracks exceed 40 minutes).
    /// </summary>
    private static readonly Duration StaleThreshold = Duration.FromMinutes(60);

    public async Task RemoveNowPlayingAsync(long uniqueId, CancellationToken token = default)
    {
        // uniqueId is a hash of user ApiKey and song ApiKey - we can't easily reverse this
        // Instead, this is handled by setting IsNowPlaying = false in the Scrobble operation
        // This method is kept for interface compatibility but the actual clearing happens
        // when a song is scrobbled (completed) in MelodeeScrobbler.Scrobble()
        logger.Debug("[{RepositoryName}] RemoveNowPlayingAsync called for uniqueId [{UniqueId}] - handled by Scrobble operation",
            nameof(NowPlayingDatabaseRepository), uniqueId);
        await Task.CompletedTask;
    }

    public async Task AddOrUpdateNowPlayingAsync(NowPlayingInfo nowPlaying, CancellationToken token = default)
    {
        // This method is called from MelodeeScrobbler.NowPlaying which already handles
        // the database operations. This is kept for interface compatibility.
        logger.Debug("[{RepositoryName}] AddOrUpdateNowPlayingAsync called - handled by MelodeeScrobbler",
            nameof(NowPlayingDatabaseRepository));
        await Task.CompletedTask;
    }

    public async Task<OperationResult<NowPlayingInfo[]>> GetNowPlayingAsync(CancellationToken token = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);

        var threshold = SystemClock.Instance.GetCurrentInstant().Minus(StaleThreshold);

        var nowPlayingRecords = await context.UserSongPlayHistories
            .AsNoTracking()
            .Where(h => h.IsNowPlaying && h.LastHeartbeatAt != null && h.LastHeartbeatAt >= threshold)
            .Include(h => h.User)
            .Include(h => h.Song)
                .ThenInclude(s => s.Album)
                    .ThenInclude(a => a.Artist)
            .ToArrayAsync(token)
            .ConfigureAwait(false);

        var nowPlayingInfos = nowPlayingRecords.Select(h => new NowPlayingInfo(
            new UserInfo(
                h.User.Id,
                h.User.ApiKey,
                h.User.UserName,
                h.User.Email,
                h.User.PublicKey,
                h.User.TimeZoneId),
            new ScrobbleInfo(
                h.Song.ApiKey,
                h.Song.Album.ArtistId,
                h.Song.AlbumId,
                h.Song.Id,
                h.Song.Title,
                h.Song.Album.Artist.Name,
                false,
                h.Song.Album.Name,
                (int?)(h.Song.Duration / 1000),
                h.Song.MusicBrainzId,
                h.Song.SongNumber,
                null,
                h.PlayedAt,
                h.Client,
                h.ByUserAgent,
                h.IpAddress,
                h.SecondsPlayed)
            {
                LastScrobbledAt = h.LastHeartbeatAt ?? h.PlayedAt
            })).ToArray();

        return new OperationResult<NowPlayingInfo[]>
        {
            Data = nowPlayingInfos
        };
    }

    public async Task ClearNowPlayingAsync(CancellationToken token = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);

        var cleared = await context.UserSongPlayHistories
            .Where(h => h.IsNowPlaying)
            .ExecuteUpdateAsync(h => h
                .SetProperty(p => p.IsNowPlaying, false), token)
            .ConfigureAwait(false);

        logger.Information("[{RepositoryName}] Cleared [{Count}] now playing entries",
            nameof(NowPlayingDatabaseRepository), cleared);
    }

    /// <summary>
    ///     Marks stale "now playing" entries as no longer playing.
    ///     Should be called periodically by a background job.
    /// </summary>
    public async Task<int> CleanupStaleEntriesAsync(CancellationToken token = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);

        var threshold = SystemClock.Instance.GetCurrentInstant().Minus(StaleThreshold);

        var cleaned = await context.UserSongPlayHistories
            .Where(h => h.IsNowPlaying && (h.LastHeartbeatAt == null || h.LastHeartbeatAt < threshold))
            .ExecuteUpdateAsync(h => h
                .SetProperty(p => p.IsNowPlaying, false), token)
            .ConfigureAwait(false);

        if (cleaned > 0)
        {
            logger.Information("[{RepositoryName}] Cleaned up [{Count}] stale now playing entries",
                nameof(NowPlayingDatabaseRepository), cleaned);
        }

        return cleaned;
    }

    /// <summary>
    ///     Marks a specific user's current "now playing" entry as no longer playing.
    /// </summary>
    public async Task ClearUserNowPlayingAsync(int userId, CancellationToken token = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);

        await context.UserSongPlayHistories
            .Where(h => h.UserId == userId && h.IsNowPlaying)
            .ExecuteUpdateAsync(h => h
                .SetProperty(p => p.IsNowPlaying, false), token)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Marks a specific song as no longer playing for a user (when scrobbled/completed).
    /// </summary>
    public async Task ClearSongNowPlayingAsync(int userId, int songId, CancellationToken token = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(token).ConfigureAwait(false);

        await context.UserSongPlayHistories
            .Where(h => h.UserId == userId && h.SongId == songId && h.IsNowPlaying)
            .ExecuteUpdateAsync(h => h
                .SetProperty(p => p.IsNowPlaying, false), token)
            .ConfigureAwait(false);
    }
}
