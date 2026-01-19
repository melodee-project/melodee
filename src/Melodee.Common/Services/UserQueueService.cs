using Melodee.Common.Data;
using Melodee.Common.Data.Constants;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;
using dbModels = Melodee.Common.Data.Models;

namespace Melodee.Common.Services;

/// <summary>
/// Handles user queue operations.
/// </summary>
public class UserQueueService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    UserProfileService userProfileService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    public async Task<PlayQueue?> GetPlayQueueForUserAsync(string username,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await userProfileService.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
        if (!user.IsSuccess || user.Data == null)
        {
            return null;
        }

        var usersPlayQues = await scopedContext
            .PlayQues.Include(x => x.Song).ThenInclude(x => x.Album).ThenInclude(x => x.Artist)
            .Where(x => x.UserId == user.Data.Id)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (usersPlayQues.Length == 0)
        {
            return null;
        }

        var current = usersPlayQues.FirstOrDefault(x => x.IsCurrentSong);
        // Get the most recent LastUpdatedAt from any queue item, or use CreatedAt as fallback
        var lastUpdated = usersPlayQues
            .Select(x => x.LastUpdatedAt ?? x.CreatedAt)
            .Max();
        var changedDateTime = lastUpdated.ToString("yyyy-MM-ddTHH:mm:ss", null);
        
        return new PlayQueue
        {
            Current = current?.PlayQueId ?? 0,
            Position = current?.Position ?? 0,
            ChangedBy = current?.ChangedBy ?? user.Data.UserName,
            Changed = changedDateTime,
            Username = user.Data.UserName,
            Entry = usersPlayQues.Select(x => x.Song.ToApiChild(x.Song.Album, null)).ToArray()
        };
    }

    /// <summary>
    /// Gets the play queue for a user by their ID (for Melodee API).
    /// </summary>
    public async Task<OperationResult<UserPlayQueue?>> GetPlayQueueByUserIdAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new OperationResult<UserPlayQueue?>("User not found")
            {
                Data = null,
                Type = OperationResponseType.NotFound
            };
        }

        var usersPlayQues = await scopedContext
            .PlayQues
            .Include(x => x.Song)
            .ThenInclude(x => x.Album)
            .ThenInclude(x => x.Artist)
            .Where(x => x.UserId == userId)
            .OrderBy(x => x.PlayQueId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (usersPlayQues.Length == 0)
        {
            return new OperationResult<UserPlayQueue?>
            {
                Data = new UserPlayQueue(
                    [],
                    null,
                    0,
                    user.UserName,
                    null)
            };
        }

        var current = usersPlayQues.FirstOrDefault(x => x.IsCurrentSong);
        var songs = usersPlayQues.Select(pq => new SongDataInfo(
            pq.Song.Id,
            pq.Song.ApiKey,
            pq.Song.IsLocked,
            pq.Song.Title,
            pq.Song.TitleNormalized,
            pq.Song.SongNumber,
            pq.Song.Album.ReleaseDate,
            pq.Song.Album.Name,
            pq.Song.Album.ApiKey,
            pq.Song.Album.Artist.Name,
            pq.Song.Album.Artist.ApiKey,
            pq.Song.FileSize,
            pq.Song.Duration,
            pq.Song.CreatedAt,
            pq.Song.Tags ?? string.Empty,
            false, // UserStarred
            0, // UserRating
            pq.Song.AlbumId,
            pq.Song.LastPlayedAt,
            pq.Song.PlayedCount,
            pq.Song.CalculatedRating
        )).ToArray();

        return new OperationResult<UserPlayQueue?>
        {
            Data = new UserPlayQueue(
                songs,
                current?.SongApiKey,
                current?.Position ?? 0,
                current?.ChangedBy ?? user.UserName,
                current?.LastUpdatedAt.ToString())
        };
    }

    /// <summary>
    /// Saves the play queue for a user by their ID (for Melodee API).
    /// </summary>
    public async Task<OperationResult<bool>> SavePlayQueueByUserIdAsync(
        int userId,
        Guid[] songApiKeys,
        Guid? currentSongApiKey,
        double? position,
        string changedBy,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var user = await scopedContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            .ConfigureAwait(false);

        if (user == null)
        {
            return new OperationResult<bool>("User not found")
            {
                Data = false,
                Type = OperationResponseType.NotFound
            };
        }

        // Clear existing queue
        var existingQueue = await scopedContext.PlayQues
            .Where(pq => pq.UserId == userId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingQueue.Length > 0)
        {
            scopedContext.PlayQues.RemoveRange(existingQueue);
        }

        // If no songs provided, just clear the queue
        if (songApiKeys.Length == 0)
        {
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return new OperationResult<bool> { Data = true };
        }

        // Look up all songs
        var songs = await scopedContext.Songs
            .AsNoTracking()
            .Where(s => songApiKeys.Contains(s.ApiKey))
            .ToDictionaryAsync(s => s.ApiKey, cancellationToken)
            .ConfigureAwait(false);

        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
        var newQueue = new List<dbModels.PlayQueue>();
        var playQueId = 1;

        foreach (var apiKey in songApiKeys)
        {
            if (!songs.TryGetValue(apiKey, out var song))
            {
                continue;
            }

            newQueue.Add(new dbModels.PlayQueue
            {
                PlayQueId = playQueId++,
                CreatedAt = now,
                IsCurrentSong = apiKey == currentSongApiKey,
                UserId = userId,
                SongId = song.Id,
                SongApiKey = song.ApiKey,
                ChangedBy = changedBy,
                Position = apiKey == currentSongApiKey && position.HasValue ? position.Value : 0
            });
        }

        if (newQueue.Count > 0)
        {
            await scopedContext.PlayQues.AddRangeAsync(newQueue, cancellationToken).ConfigureAwait(false);
        }

        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new OperationResult<bool> { Data = true };
    }

    /// <summary>
    /// Clears the play queue for a user.
    /// </summary>
    public async Task<OperationResult<bool>> ClearPlayQueueByUserIdAsync(
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var existingQueue = await scopedContext.PlayQues
            .Where(pq => pq.UserId == userId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (existingQueue.Length > 0)
        {
            scopedContext.PlayQues.RemoveRange(existingQueue);
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new OperationResult<bool> { Data = true };
    }

    public async Task<bool> SavePlayQueueForUserAsync(string username, string[]? apiIds, string? currentApiId, double? position, string? client,
        CancellationToken cancellationToken = default)
    {
        var apiKeys = apiIds?.Select(ApiKeyFromId).Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        var current = ApiKeyFromId(currentApiId);

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // If the apikey is blank then remove any current saved que
        if (apiKeys == null)
        {
            var user = await userProfileService.GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
            if (user.IsSuccess && user.Data != null)
            {
                var playQuesToDelete = await scopedContext.PlayQues
                    .Where(pq => pq.UserId == user.Data.Id)
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                scopedContext.PlayQues.RemoveRange(playQuesToDelete);
                await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            return true;
        }
        else
        {
            var foundQuesSongApiKeys = new List<Guid>();
            var user = await userProfileService.GetByUsernameAsync(username, cancellationToken)
                .ConfigureAwait(false);

            if (!user.IsSuccess || user.Data == null)
            {
                return false;
            }

            var usersPlayQues = await scopedContext
                .PlayQues.Include(x => x.Song)
                .Where(x => x.UserId == user.Data.Id)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var changedByValue = client ?? user.Data.UserName;
            if (usersPlayQues.Length > 0)
            {
                foreach (var userPlay in usersPlayQues)
                {
                    if (!apiKeys.Contains(userPlay.Song.ApiKey))
                    {
                        scopedContext.PlayQues.Remove(userPlay);
                        continue;
                    }

                    if (userPlay.Song.ApiKey == current)
                    {
                        userPlay.Position = position ?? 0;
                    }

                    userPlay.IsCurrentSong = userPlay.Song.ApiKey == current;
                    userPlay.LastUpdatedAt = now;
                    userPlay.ChangedBy = changedByValue;
                    foundQuesSongApiKeys.Add(userPlay.Song.ApiKey);
                }
            }

            var addedPlayQues = new List<dbModels.PlayQueue>();
            foreach (var apiKeyToAdd in apiKeys.Except(foundQuesSongApiKeys))
            {
                var song = await scopedContext.Songs
                    .FirstOrDefaultAsync(x => x.ApiKey == apiKeyToAdd, cancellationToken).ConfigureAwait(false);
                if (song != null)
                {
                    addedPlayQues.Add(new dbModels.PlayQueue
                    {
                        PlayQueId = addedPlayQues.Count + 1,
                        CreatedAt = now,
                        IsCurrentSong = song.ApiKey == current,
                        UserId = user.Data.Id,
                        SongId = song.Id,
                        SongApiKey = song.ApiKey,
                        ChangedBy = changedByValue,
                        Position = apiKeyToAdd == current && position.HasValue ? position.Value : 0
                    });
                }
            }

            if (addedPlayQues.Count > 0)
            {
                await scopedContext.PlayQues.AddRangeAsync(addedPlayQues, cancellationToken).ConfigureAwait(false);
            }

            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
    }

    private static Guid? ApiKeyFromId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var apiIdParts = id!.Split(OpenSubsonicServer.ApiIdSeparator);
        var toParse = id;
        if (apiIdParts.Length >= 2)
        {
            toParse = apiIdParts[1];
        }

        return SafeParser.ToGuid(toParse);
    }
}
