using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public sealed class UserBookmarkService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<MelodeeModels.OperationResult<Bookmark[]>> GetBookmarksAsync(int userId, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var bookmarks = await scopedContext.Bookmarks
            .Include(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
            .Include(x => x.Song).ThenInclude(x => x.UserSongs.Where(ua => ua.UserId == userId))
            .Where(x => x.UserId == userId)
            .AsSplitQuery()
            .AsNoTracking()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);
        sw.Stop();
        Logger.Debug("[UserBookmarkService] GetBookmarksAsync loaded {Count} bookmarks for user {UserId} in {ElapsedMs} ms",
            bookmarks.Length, userId, sw.ElapsedMilliseconds);

        return new MelodeeModels.OperationResult<Bookmark[]>
        {
            Data = bookmarks
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> CreateBookmarkAsync(int userId, Guid songApiKey, int positionMs, string? comment, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x == Guid.Empty, songApiKey, nameof(songApiKey));

        var result = false;
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var song = await scopedContext.Songs
            .FirstOrDefaultAsync(x => x.ApiKey == songApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (song != null)
        {
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var existingBookmark = await scopedContext.Bookmarks
                .FirstOrDefaultAsync(x => x.UserId == userId && x.SongId == song.Id, cancellationToken)
                .ConfigureAwait(false);

            if (existingBookmark != null)
            {
                existingBookmark.LastUpdatedAt = now;
                existingBookmark.Comment = comment;
                existingBookmark.Position = positionMs;
            }
            else
            {
                var newBookmark = new Bookmark
                {
                    CreatedAt = now,
                    UserId = userId,
                    SongId = song.Id,
                    Comment = comment,
                    Position = positionMs
                };
                scopedContext.Bookmarks.Add(newBookmark);
            }

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteBookmarkAsync(int userId, Guid songApiKey, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, userId, nameof(userId));
        Guard.Against.Expression(x => x == Guid.Empty, songApiKey, nameof(songApiKey));

        var result = false;
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var song = await scopedContext.Songs
            .FirstOrDefaultAsync(x => x.ApiKey == songApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (song != null)
        {
            var existingBookmark = await scopedContext.Bookmarks
                .FirstOrDefaultAsync(x => x.UserId == userId && x.SongId == song.Id, cancellationToken)
                .ConfigureAwait(false);

            if (existingBookmark != null)
            {
                scopedContext.Bookmarks.Remove(existingBookmark);
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }
}
