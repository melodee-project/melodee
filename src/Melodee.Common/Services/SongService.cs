using System.Globalization;
using Ardalis.GuardClauses;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Extensions;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.OpenSubsonic.DTO;
using Melodee.Common.Models.OpenSubsonic.Responses;
using Melodee.Common.Models.Streaming;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Primitives;
using NodaTime;
using Serilog;
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public class SongService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:song:apikey:{0}";
    private const string CacheKeyDetailByTitleNormalizedTemplate = "urn:song:titlenormalized:{0}";
    private const string CacheKeyDetailTemplate = "urn:song:{0}";

    /// <summary>
    ///     Entries without a heartbeat for longer than this are considered stale.
    ///     Set to 60 minutes to accommodate very long songs (some tracks exceed 40 minutes).
    /// </summary>
    private static readonly Duration StaleThreshold = Duration.FromMinutes(60);

    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListNowPlayingAsync(MelodeeModels.PagedRequest pagedRequest, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var threshold = SystemClock.Instance.GetCurrentInstant().Minus(StaleThreshold);

        // Query directly from database for songs currently being played
        var baseQuery = scopedContext.UserSongPlayHistories
            .AsNoTracking()
            .Where(h => h.IsNowPlaying && h.LastHeartbeatAt != null && h.LastHeartbeatAt >= threshold)
            .Include(h => h.Song)
                .ThenInclude(s => s.Album)
                    .ThenInclude(a => a.Artist);

        var songCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);
        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest && songCount > 0)
        {
            var rawRecords = await baseQuery
                .OrderByDescending(h => h.LastHeartbeatAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawRecords.Select(h => new SongDataInfo(
                h.Song.Id,
                h.Song.ApiKey,
                h.Song.IsLocked,
                h.Song.Title,
                h.Song.TitleNormalized,
                h.Song.SongNumber,
                h.Song.Album.ReleaseDate,
                h.Song.Album.Name,
                h.Song.Album.ApiKey,
                h.Song.Album.Artist.Name,
                h.Song.Album.Artist.ApiKey,
                h.Song.FileSize,
                h.Song.Duration,
                h.Song.CreatedAt,
                h.Song.Tags ?? string.Empty,
                false,
                0,
                h.Song.AlbumId,
                h.Song.LastPlayedAt,
                h.Song.PlayedCount,
                h.Song.CalculatedRating
            )).ToArray();
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListForContributorsAsync(MelodeeModels.PagedRequest pagedRequest, string contributorName, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Create base query using EF Core with proper joins and filtering
        // Use normalized string comparison for better performance
        var normalizedContributorName = contributorName.ToNormalizedString() ?? contributorName;

        var contributorsQuery = scopedContext.Contributors
            .Where(c => c.ContributorName != null && c.ContributorName.Contains(normalizedContributorName))
            .Where(c => c.Song != null)
            .Include(c => c.Song!)
            .ThenInclude(s => s.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        // Get the songs from contributors and project to distinct song IDs first
        var songIds = await contributorsQuery
            .Select(c => c.Song!.Id)
            .Distinct()
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var songCount = songIds.Length;
        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest && songIds.Length > 0)
        {
            // Now query songs directly with proper includes
            var baseQuery = scopedContext.Songs
                .Where(s => songIds.Contains(s.Id))
                .Include(s => s.Album)
                .ThenInclude(a => a.Artist)
                .AsNoTracking();

            // Apply ordering and paging
            var orderedQuery = ApplyOrdering(baseQuery, pagedRequest);

            var rawSongs = await orderedQuery
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawSongs.Select(s => new SongDataInfo(
                s.Id,
                s.ApiKey,
                s.IsLocked,
                s.Title,
                s.TitleNormalized,
                s.SongNumber,
                s.Album.ReleaseDate,
                s.Album.Name,
                s.Album.ApiKey,
                s.Album.Artist.Name,
                s.Album.Artist.ApiKey,
                s.FileSize,
                s.Duration,
                s.CreatedAt,
                s.Tags ?? string.Empty,
                false, // UserStarred - would need user context
                0, // UserRating - would need user context
                s.AlbumId,
                s.LastPlayedAt,
                s.PlayedCount,
                s.CalculatedRating
            )).ToArray();
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListAsync(MelodeeModels.PagedRequest pagedRequest, int userId, CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Create base query with user-specific data
        var baseQuery = scopedContext.Songs
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .Include(s => s.UserSongs.Where(us => us.UserId == userId))
            .AsNoTracking();

        // Apply filters
        var filteredQuery = ApplyFilters(baseQuery, pagedRequest, userId);

        // Get total count efficiently
        var songCount = await filteredQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            // Apply ordering and paging
            var orderedQuery = ApplyOrdering(filteredQuery, pagedRequest);

            var rawSongs = await orderedQuery
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawSongs.Select(s => new SongDataInfo(
                s.Id,
                s.ApiKey,
                s.IsLocked,
                s.Title,
                s.TitleNormalized,
                s.SongNumber,
                s.Album.ReleaseDate,
                s.Album.Name,
                s.Album.ApiKey,
                s.Album.Artist.Name,
                s.Album.Artist.ApiKey,
                s.FileSize,
                s.Duration,
                s.CreatedAt,
                s.Tags ?? string.Empty,
                s.UserSongs.FirstOrDefault()?.IsStarred ?? false,
                s.UserSongs.FirstOrDefault()?.Rating ?? 0,
                s.AlbumId,
                s.LastPlayedAt,
                s.PlayedCount,
                s.CalculatedRating
            )).ToArray();
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    private static IQueryable<Song> ApplyFilters(IQueryable<Song> query, MelodeeModels.PagedRequest pagedRequest, int? userId = null)
    {
        if (pagedRequest.FilterBy == null || pagedRequest.FilterBy.Length == 0)
        {
            return query;
        }

        // Apply each filter individually with AND logic (simplified approach)
        foreach (var filter in pagedRequest.FilterBy)
        {
            var value = filter.Value.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                var normalizedValue = value.ToNormalizedString() ?? value;
                query = filter.PropertyName.ToLowerInvariant() switch
                {
                    "title" or "titlenormalized" => query.Where(s => s.TitleNormalized.Contains(normalizedValue)),
                    "albumname" => query.Where(s => s.Album.NameNormalized.Contains(normalizedValue)),
                    "artistname" => query.Where(s => s.Album.Artist.NameNormalized.Contains(normalizedValue)),
                    "artistapikey" => Guid.TryParse(value, out var artistApiKeyValue) // Use original value for GUID
                        ? query.Where(s => s.Album.Artist.ApiKey == artistApiKeyValue)
                        : query,
                    "tags" => query.Where(s => s.Tags != null && s.Tags.Contains(normalizedValue)),
                    "islocked" => bool.TryParse(value, out var lockedValue)
                        ? query.Where(s => s.IsLocked == lockedValue)
                        : query,
                    "userstarred" when userId.HasValue => bool.TryParse(value, out var starredValue)
                        ? query.Where(s => s.UserSongs.Any(us => us.UserId == userId.Value && us.IsStarred == starredValue))
                        : query,
                    "userrating" when userId.HasValue => int.TryParse(value, out var ratingValue)
                        ? query.Where(s => s.UserSongs.Any(us => us.UserId == userId.Value && us.Rating == ratingValue))
                        : query,
                    _ => query
                };
            }
        }

        return query;
    }

    private static IQueryable<Song> ApplyOrdering(IQueryable<Song> query, MelodeeModels.PagedRequest pagedRequest)
    {
        // Use the existing OrderByValue method from PagedRequest
        var orderByClause = pagedRequest.OrderByValue("Title", MelodeeModels.PagedRequest.OrderAscDirection);

        // Parse the order by clause to determine field and direction
        var isDescending = orderByClause.Contains("DESC", StringComparison.OrdinalIgnoreCase);
        var fieldName = orderByClause.Split(' ')[0].Trim('"').ToLowerInvariant();

        return fieldName switch
        {
            "title" or "titlenormalized" => isDescending ? query.OrderByDescending(s => s.Title) : query.OrderBy(s => s.Title),
            "songnumber" => isDescending ? query.OrderByDescending(s => s.SongNumber) : query.OrderBy(s => s.SongNumber),
            "albumid" => isDescending ? query.OrderByDescending(s => s.AlbumId) : query.OrderBy(s => s.AlbumId),
            "albumname" => isDescending ? query.OrderByDescending(s => s.Album.Name) : query.OrderBy(s => s.Album.Name),
            "artistname" => isDescending ? query.OrderByDescending(s => s.Album.Artist.Name) : query.OrderBy(s => s.Album.Artist.Name),
            "duration" => isDescending ? query.OrderByDescending(s => s.Duration) : query.OrderBy(s => s.Duration),
            "filesize" => isDescending ? query.OrderByDescending(s => s.FileSize) : query.OrderBy(s => s.FileSize),
            "createdat" => isDescending ? query.OrderByDescending(s => s.CreatedAt) : query.OrderBy(s => s.CreatedAt),
            "releasedate" => isDescending ? query.OrderByDescending(s => s.Album.ReleaseDate) : query.OrderBy(s => s.Album.ReleaseDate),
            "lastplayedat" => isDescending ? query.OrderByDescending(s => s.LastPlayedAt) : query.OrderBy(s => s.LastPlayedAt),
            "playedcount" => isDescending ? query.OrderByDescending(s => s.PlayedCount) : query.OrderBy(s => s.PlayedCount),
            "calculatedrating" => isDescending ? query.OrderByDescending(s => s.CalculatedRating) : query.OrderBy(s => s.CalculatedRating),
            _ => query.OrderBy(s => s.Title)
        };
    }

    public async Task<MelodeeModels.OperationResult<Song?>> GetAsync(int id,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, id, nameof(id));

        var result = await CacheManager.GetAsync(CacheKeyDetailTemplate.FormatSmart(id), async () =>
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var song = await scopedContext
                    .Songs
                    .Include(x => x.Contributors).ThenInclude(x => x.Artist)
                    .Include(x => x.Album).ThenInclude(x => x.Artist)
                    .AsSplitQuery()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                    .ConfigureAwait(false);
                sw.Stop();
                Logger.Debug("[SongService] GetAsync({Id}) completed in {ElapsedMs} ms", id, sw.ElapsedMilliseconds);
                return song;
            }
        }, cancellationToken);
        return new MelodeeModels.OperationResult<Song?>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<Song?>> GetByApiKeyAsync(Guid apiKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(_ => apiKey == Guid.Empty, apiKey, nameof(apiKey));

        var id = await CacheManager.GetAsync(CacheKeyDetailByApiKeyTemplate.FormatSmart(apiKey), async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            return await scopedContext.Songs
                .AsNoTracking()
                .Where(s => s.ApiKey == apiKey)
                .Select(s => (int?)s.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken);

        if (id == null)
        {
            return new MelodeeModels.OperationResult<Song?>("Unknown song")
            {
                Data = null
            };
        }

        return await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearCacheAsync(int songId, CancellationToken cancellationToken)
    {
        var song = await GetAsync(songId, cancellationToken).ConfigureAwait(false);
        if (song.Data != null)
        {
            CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(song.Data.ApiKey));
            CacheManager.Remove(CacheKeyDetailByTitleNormalizedTemplate.FormatSmart(song.Data.TitleNormalized));
            CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(song.Data.Id));
        }
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(int[] songIds, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(songIds, nameof(songIds));

        bool result;
        var albumIds = new List<int>();

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var songs = await scopedContext.Songs
                .Include(s => s.Album)
                .ThenInclude(a => a.Artist)
                .ThenInclude(ar => ar.Library)
                .Where(s => songIds.Contains(s.Id))
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            var foundIds = songs.Select(s => s.Id).ToHashSet();
            foreach (var songId in songIds)
            {
                if (!foundIds.Contains(songId))
                {
                    Logger.Warning("Song with Id [{SongId}] not found for deletion", songId);
                }
            }

            foreach (var song in songs)
            {
                // Delete associated media file from disk if it exists
                var songFilePath = Path.Combine(
                    song.Album.Artist.Library.Path,
                    song.Album.Artist.Directory,
                    song.Album.Directory,
                    song.FileName);

                if (File.Exists(songFilePath))
                {
                    try
                    {
                        File.Delete(songFilePath);
                        Logger.Debug("Deleted song file [{FilePath}]", songFilePath);
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to delete song file [{FilePath}]", songFilePath);
                        // Continue with DB deletion even if file deletion fails
                    }
                }
                else
                {
                    Logger.Debug("Song file [{FilePath}] does not exist, skipping file deletion", songFilePath);
                }

                // Note: NowPlaying is managed separately and songs will be removed when they expire
                // or when the user stops playing. No direct removal method exists.

                // Clear related cache entries
                CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(song.ApiKey));
                CacheManager.Remove(CacheKeyDetailByTitleNormalizedTemplate.FormatSmart(song.TitleNormalized));
                CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(song.Id));

                // Cascade delete will handle:
                // - Contributors (via foreign key)
                // - UserSongs (via foreign key)
                // - PlaylistSongs (via foreign key)
                // Note: Database is configured with cascade delete for these relationships

                scopedContext.Songs.Remove(song);
                albumIds.Add(song.AlbumId);
            }

            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Logger.Information("Deleted songs [{SongIds}]", songIds);
            result = true;
        }

        // Update album aggregate values for affected albums
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var albumId in albumIds.Distinct())
            {
                var album = await scopedContext.Albums
                    .Include(a => a.Songs)
                    .FirstOrDefaultAsync(a => a.Id == albumId, cancellationToken)
                    .ConfigureAwait(false);

                if (album != null)
                {
                    // Update album song count and duration
                    album.SongCount = (short?)album.Songs.Count;
                    album.Duration = album.Songs.Sum(s => s.Duration);

                    // If album has no songs left, it should probably be marked as invalid or deleted
                    // Following the requirements: if all songs are missing, this would be handled
                    // by the clean operation, not here. We just update the counts.

                    await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    /// <summary>
    /// Get streaming descriptor for song - memory-efficient approach without loading file contents
    /// </summary>
    public async Task<MelodeeModels.OperationResult<StreamingDescriptor>> GetStreamingDescriptorAsync(
        MelodeeModels.UserInfo user,
        Guid apiKey,
        string? rangeHeader = null,
        bool isDownload = false,
        CancellationToken cancellationToken = default)
    {
        var song = await GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (song.Data == null)
        {
            return new MelodeeModels.OperationResult<StreamingDescriptor>("Unknown song")
            {
                Type = MelodeeModels.OperationResponseType.NotFound,
                Data = null!
            };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Get song file information - fetch path components separately for cross-platform Path.Combine
        var songData = await scopedContext.Songs
            .Where(s => s.ApiKey == apiKey)
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .ThenInclude(ar => ar.Library)
            .AsNoTracking()
            .Select(s => new
            {
                LibraryPath = s.Album.Artist.Library.Path,
                ArtistDirectory = s.Album.Artist.Directory,
                AlbumDirectory = s.Album.Directory,
                s.FileName,
                s.FileSize,
                s.Duration,
                s.BitRate,
                s.ContentType
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var songStreamInfo = songData == null
            ? null
            : new SongStreamInfo(
                Path.Combine(songData.LibraryPath, songData.ArtistDirectory, songData.AlbumDirectory, songData.FileName),
                songData.FileSize,
                songData.Duration / 1000.0,
                songData.BitRate,
                songData.ContentType);

        if (!(songStreamInfo?.TrackFileInfo.Exists ?? false))
        {
            Logger.Warning("[{ServiceName}] Stream request for song that was not found.",
                nameof(SongService));
            return new MelodeeModels.OperationResult<StreamingDescriptor>("Song file not found")
            {
                Type = MelodeeModels.OperationResponseType.NotFound,
                Data = null!
            };
        }

        // Parse range if provided
        RangeInfo? range = null;
        if (!string.IsNullOrWhiteSpace(rangeHeader))
        {
            range = RangeParser.ParseRange(rangeHeader, songStreamInfo.FileSize);
            if (range == null)
            {
                Logger.Warning("[{ServiceName}] Invalid range request.", nameof(SongService));
                return new MelodeeModels.OperationResult<StreamingDescriptor>("Invalid range request")
                {
                    Type = MelodeeModels.OperationResponseType.Error,
                    Data = null!
                };
            }

            Logger.Debug("[{ServiceName}] Parsed range for song request.", nameof(SongService));
        }

        // Create base headers
        var baseHeaders = new Dictionary<string, StringValues>
        {
            { "Access-Control-Allow-Origin", "*" },
            { "Cache-Control", "no-store, must-revalidate, no-cache, max-age=0" },
            { "Content-Duration", songStreamInfo.Duration.ToString(CultureInfo.InvariantCulture) },
            { "Expires", "Mon, 01 Jan 1990 00:00:00 GMT" }
        };

        // Get file info for cache headers
        var fileInfo = songStreamInfo.TrackFileInfo;
        var lastModified = fileInfo.LastWriteTimeUtc;
        var etag = $"{fileInfo.Length}-{lastModified.Ticks}";

        var descriptor = new StreamingDescriptor
        {
            FilePath = songStreamInfo.TrackFileInfo.FullName,
            FileSize = songStreamInfo.FileSize,
            ContentType = songStreamInfo.ContentType,
            ResponseHeaders = baseHeaders,
            Range = range,
            FileName = isDownload ? fileInfo.Name : null,
            IsDownload = isDownload,
            LastModified = lastModified,
            ETag = etag
        };

        return new MelodeeModels.OperationResult<StreamingDescriptor>
        {
            Data = descriptor
        };
    }

    /// <summary>
    /// DEPRECATED: Use GetStreamingDescriptorAsync instead for memory-efficient streaming
    /// </summary>
    [Obsolete("Use GetStreamingDescriptorAsync instead for memory-efficient streaming")]
    public async Task<MelodeeModels.OperationResult<StreamResponse>> GetStreamForSongAsync(MelodeeModels.UserInfo user, Guid apiKey, CancellationToken cancellationToken = default)
    {
        var song = await GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (song.Data == null)
        {
            return new MelodeeModels.OperationResult<StreamResponse>("Unknown song")
            {
                Type = MelodeeModels.OperationResponseType.NotFound,
                Data = new StreamResponse
                (
                    new Dictionary<string, StringValues>([]),
                    false,
                    []
                )
            };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Use EF Core query instead of raw SQL - fetch path components separately for cross-platform Path.Combine
        var songData = await scopedContext.Songs
            .Where(s => s.ApiKey == apiKey)
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .ThenInclude(ar => ar.Library)
            .AsNoTracking()
            .Select(s => new
            {
                LibraryPath = s.Album.Artist.Library.Path,
                ArtistDirectory = s.Album.Artist.Directory,
                AlbumDirectory = s.Album.Directory,
                s.FileName,
                s.FileSize,
                s.Duration,
                s.BitRate,
                s.ContentType
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var songStreamInfo = songData == null
            ? null
            : new SongStreamInfo(
                Path.Combine(songData.LibraryPath, songData.ArtistDirectory, songData.AlbumDirectory, songData.FileName),
                songData.FileSize,
                songData.Duration / 1000.0,
                songData.BitRate,
                songData.ContentType);

        if (!(songStreamInfo?.TrackFileInfo.Exists ?? false))
        {
            Logger.Warning("[{ServiceName}] Stream request for song that was not found. User [{ApiRequest}] Song ApiKey [{ApiKey}]",
                nameof(SongService), user.ToString(), song.Data.ApiKey);
            return new MelodeeModels.OperationResult<StreamResponse>
            {
                Data = new StreamResponse
                (
                    new Dictionary<string, StringValues>([]),
                    false,
                    []
                )
            };
        }

        var bytesToRead = (int)songStreamInfo.FileSize;
        var trackBytes = new byte[bytesToRead];
        var numberOfBytesRead = 0;
        await using (var fs = songStreamInfo.TrackFileInfo.OpenRead())
        {
            try
            {
                fs.Seek(0, SeekOrigin.Begin);
                numberOfBytesRead = await fs.ReadAsync(trackBytes.AsMemory(0, bytesToRead), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Reading song [{SongInfo}]", songStreamInfo);
            }
        }

        return new MelodeeModels.OperationResult<StreamResponse>
        {
            Data = new StreamResponse
            (
                new Dictionary<string, StringValues>
                {
                    { "Access-Control-Allow-Origin", "*" },
                    { "Accept-Ranges", "bytes" },
                    { "Cache-Control", "no-store, must-revalidate, no-cache, max-age=0" },
                    { "Content-Duration", songStreamInfo.Duration.ToString(CultureInfo.InvariantCulture) },
                    { "Content-Length", numberOfBytesRead.ToString() },
                    { "Content-Range", $"bytes 0-{songStreamInfo.FileSize}/{numberOfBytesRead}" },
                    { "Content-Type", songStreamInfo.ContentType },
                    { "Expires", "Mon, 01 Jan 1990 00:00:00 GMT" }
                },
                numberOfBytesRead > 0,
                trackBytes
            )
        };
    }

    /// <summary>
    /// DEPRECATED: Use GetStreamingDescriptorAsync instead for memory-efficient streaming
    /// </summary>
    [Obsolete("Use GetStreamingDescriptorAsync instead for memory-efficient streaming")]
    public async Task<MelodeeModels.OperationResult<StreamResponse>> GetStreamForSongAsync(
        MelodeeModels.UserInfo user,
        Guid apiKey,
        long rangeBegin = 0,
        long rangeEnd = 0,
        string? format = null,
        int? maxBitRate = null,
        bool isDownloadRequest = false,
        CancellationToken cancellationToken = default)
    {
        var song = await GetByApiKeyAsync(apiKey, cancellationToken).ConfigureAwait(false);
        if (song.Data == null)
        {
            return new MelodeeModels.OperationResult<StreamResponse>("Unknown song")
            {
                Type = MelodeeModels.OperationResponseType.NotFound,
                Data = new StreamResponse
                (
                    new Dictionary<string, StringValues>([]),
                    false,
                    []
                )
            };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Use EF Core query instead of raw SQL - fetch path components separately for cross-platform Path.Combine
        var songData = await scopedContext.Songs
            .Where(s => s.ApiKey == apiKey)
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .ThenInclude(ar => ar.Library)
            .AsNoTracking()
            .Select(s => new
            {
                LibraryPath = s.Album.Artist.Library.Path,
                ArtistDirectory = s.Album.Artist.Directory,
                AlbumDirectory = s.Album.Directory,
                s.FileName,
                s.FileSize,
                s.Duration,
                s.BitRate,
                s.ContentType
            })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var songStreamInfo = songData == null
            ? null
            : new SongStreamInfo(
                Path.Combine(songData.LibraryPath, songData.ArtistDirectory, songData.AlbumDirectory, songData.FileName),
                songData.FileSize,
                songData.Duration / 1000.0,
                songData.BitRate,
                songData.ContentType);

        if (!(songStreamInfo?.TrackFileInfo.Exists ?? false))
        {
            Logger.Warning("[{ServiceName}] Stream request for song that was not found. User [{User}] Song ApiKey [{ApiKey}]",
                nameof(SongService), user.ToString(), apiKey);
            return new MelodeeModels.OperationResult<StreamResponse>
            {
                Data = new StreamResponse
                (
                    new Dictionary<string, StringValues>([]),
                    false,
                    []
                )
            };
        }

        // Handle range requests
        rangeEnd = rangeEnd == 0 ? songStreamInfo.FileSize : rangeEnd;
        var bytesToRead = (int)(rangeEnd - rangeBegin) + 1;
        if (bytesToRead > songStreamInfo.FileSize)
        {
            bytesToRead = (int)songStreamInfo.FileSize;
        }

        // Handle transcoding (placeholder for future implementation)
        if (format != null || maxBitRate != null)
        {
            if (maxBitRate != null && maxBitRate != songStreamInfo.BitRate)
            {
                Logger.Warning("[{ServiceName}] Transcoding requested but not implemented. MaxBitRate [{MaxBitRate}] vs Song BitRate [{SongBitRate}]",
                    nameof(SongService), maxBitRate, songStreamInfo.BitRate);
                // For now, continue with original file
            }
        }

        var trackBytes = new byte[bytesToRead];
        var numberOfBytesRead = 0;
        await using (var fs = songStreamInfo.TrackFileInfo.OpenRead())
        {
            try
            {
                fs.Seek(rangeBegin, SeekOrigin.Begin);
                numberOfBytesRead = await fs.ReadAsync(trackBytes.AsMemory(0, bytesToRead), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Reading song [{SongInfo}] range [{RangeBegin}-{RangeEnd}]", songStreamInfo, rangeBegin, rangeEnd);
            }
        }

        var headers = new Dictionary<string, StringValues>
        {
            { "Access-Control-Allow-Origin", "*" },
            { "Accept-Ranges", "bytes" },
            { "Cache-Control", "no-store, must-revalidate, no-cache, max-age=0" },
            { "Content-Duration", songStreamInfo.Duration.ToString(CultureInfo.InvariantCulture) },
            { "Content-Length", numberOfBytesRead.ToString() },
            { "Content-Range", $"bytes {rangeBegin}-{rangeEnd}/{songStreamInfo.FileSize}" },
            { "Content-Type", songStreamInfo.ContentType },
            { "Expires", "Mon, 01 Jan 1990 00:00:00 GMT" }
        };

        return new MelodeeModels.OperationResult<StreamResponse>
        {
            Data = new StreamResponse
            (
                headers,
                numberOfBytesRead > 0,
                trackBytes,
                isDownloadRequest ? songStreamInfo.TrackFileInfo.Name : null,
                isDownloadRequest ? songStreamInfo.ContentType : null
            )
        };
    }

    /// <summary>
    /// Get song with full path info for lyrics processing
    /// </summary>
    public async Task<MelodeeModels.OperationResult<(Song song, string libraryPath, string artistDirectory)>> GetSongWithPathInfoAsync(
        Guid apiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var song = await scopedContext.Songs
            .Include(x => x.Album).ThenInclude(x => x.Artist).ThenInclude(x => x.Library)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken)
            .ConfigureAwait(false);

        if (song == null)
        {
            return new MelodeeModels.OperationResult<(Song, string, string)>("Song not found.")
            {
                Data = (null!, string.Empty, string.Empty),
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return new MelodeeModels.OperationResult<(Song, string, string)>
        {
            Data = (song, song.Album.Artist.Library.Path, song.Album.Artist.Directory)
        };
    }

    /// <summary>
    /// Get song by artist and title for lyrics processing
    /// </summary>
    public async Task<MelodeeModels.OperationResult<(Song song, string libraryPath, string artistDirectory)>> GetSongByArtistAndTitleAsync(
        string artistName,
        string songTitle,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var artistNameNormalized = artistName.ToNormalizedString() ?? artistName;
        var titleNormalized = songTitle.ToNormalizedString() ?? songTitle;

        var artist = await scopedContext.Artists
            .Include(x => x.Library)
            .Include(x => x.Albums).ThenInclude(x => x.Songs)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.NameNormalized == artistNameNormalized, cancellationToken)
            .ConfigureAwait(false);

        if (artist == null)
        {
            return new MelodeeModels.OperationResult<(Song, string, string)>("Artist not found.")
            {
                Data = (null!, string.Empty, string.Empty),
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        var song = artist.Albums.SelectMany(x => x.Songs).FirstOrDefault(x => x.TitleNormalized == titleNormalized);
        if (song == null)
        {
            return new MelodeeModels.OperationResult<(Song, string, string)>("Song not found.")
            {
                Data = (null!, string.Empty, string.Empty),
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        return new MelodeeModels.OperationResult<(Song, string, string)>
        {
            Data = (song, artist.Library.Path, artist.Directory)
        };
    }

    /// <summary>
    /// List songs by genre with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListByGenreAsync(
        MelodeeModels.PagedRequest pagedRequest,
        string genre,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Query songs that have the specified genre in their Genres array
        var baseQuery = scopedContext.Songs
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .Include(s => s.UserSongs.Where(us => us.UserId == userId))
            .Where(s => s.Genres != null && s.Genres.Contains(genre))
            .AsNoTracking();

        // Get total count
        var songCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            // Apply ordering and paging
            var orderedQuery = ApplyOrdering(baseQuery, pagedRequest);

            var rawSongs = await orderedQuery
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawSongs.Select(s => new SongDataInfo(
                s.Id,
                s.ApiKey,
                s.IsLocked,
                s.Title,
                s.TitleNormalized,
                s.SongNumber,
                s.Album.ReleaseDate,
                s.Album.Name,
                s.Album.ApiKey,
                s.Album.Artist.Name,
                s.Album.Artist.ApiKey,
                s.FileSize,
                s.Duration,
                s.CreatedAt,
                s.Tags ?? string.Empty,
                s.UserSongs.FirstOrDefault()?.IsStarred ?? false,
                s.UserSongs.FirstOrDefault()?.Rating ?? 0,
                s.AlbumId,
                s.LastPlayedAt,
                s.PlayedCount,
                s.CalculatedRating
            )
            {
                Genre = genre
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    /// <summary>
    /// List starred songs for a user with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListStarredAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserSongs
            .Where(us => us.UserId == userId && us.IsStarred)
            .Include(us => us.Song)
            .ThenInclude(s => s.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var songCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserSongs = await baseQuery
                .OrderByDescending(us => us.StarredAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawUserSongs.Select(us => new SongDataInfo(
                us.Song.Id,
                us.Song.ApiKey,
                us.Song.IsLocked,
                us.Song.Title,
                us.Song.TitleNormalized,
                us.Song.SongNumber,
                us.Song.Album.ReleaseDate,
                us.Song.Album.Name,
                us.Song.Album.ApiKey,
                us.Song.Album.Artist.Name,
                us.Song.Album.Artist.ApiKey,
                us.Song.FileSize,
                us.Song.Duration,
                us.Song.CreatedAt,
                us.Song.Tags ?? string.Empty,
                us.IsStarred,
                us.Rating,
                us.Song.AlbumId,
                us.Song.LastPlayedAt,
                us.Song.PlayedCount,
                us.Song.CalculatedRating
            )).ToArray();
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    /// <summary>
    /// List hated songs for a user with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListHatedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserSongs
            .Where(us => us.UserId == userId && us.IsHated)
            .Include(us => us.Song)
            .ThenInclude(s => s.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var songCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserSongs = await baseQuery
                .OrderByDescending(us => us.LastUpdatedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawUserSongs.Select(us => new SongDataInfo(
                us.Song.Id,
                us.Song.ApiKey,
                us.Song.IsLocked,
                us.Song.Title,
                us.Song.TitleNormalized,
                us.Song.SongNumber,
                us.Song.Album.ReleaseDate,
                us.Song.Album.Name,
                us.Song.Album.ApiKey,
                us.Song.Album.Artist.Name,
                us.Song.Album.Artist.ApiKey,
                us.Song.FileSize,
                us.Song.Duration,
                us.Song.CreatedAt,
                us.Song.Tags ?? string.Empty,
                us.IsStarred,
                us.Rating,
                us.Song.AlbumId,
                us.Song.LastPlayedAt,
                us.Song.PlayedCount,
                us.Song.CalculatedRating
            )).ToArray();
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    /// <summary>
    /// List top-rated songs (4+ stars) for a user with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListTopRatedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        int minRating = 4,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserSongs
            .Where(us => us.UserId == userId && us.Rating >= minRating)
            .Include(us => us.Song)
            .ThenInclude(s => s.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var songCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserSongs = await baseQuery
                .OrderByDescending(us => us.Rating)
                .ThenByDescending(us => us.LastUpdatedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawUserSongs.Select(us => new SongDataInfo(
                us.Song.Id,
                us.Song.ApiKey,
                us.Song.IsLocked,
                us.Song.Title,
                us.Song.TitleNormalized,
                us.Song.SongNumber,
                us.Song.Album.ReleaseDate,
                us.Song.Album.Name,
                us.Song.Album.ApiKey,
                us.Song.Album.Artist.Name,
                us.Song.Album.Artist.ApiKey,
                us.Song.FileSize,
                us.Song.Duration,
                us.Song.CreatedAt,
                us.Song.Tags ?? string.Empty,
                us.IsStarred,
                us.Rating,
                us.Song.AlbumId,
                us.Song.LastPlayedAt,
                us.Song.PlayedCount,
                us.Song.CalculatedRating
            )).ToArray();
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    /// <summary>
    /// List all rated songs for a user with pagination, sorted by rating descending
    /// </summary>
    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListRatedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserSongs
            .Where(us => us.UserId == userId && us.Rating > 0)
            .Include(us => us.Song)
            .ThenInclude(s => s.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var songCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserSongs = await baseQuery
                .OrderByDescending(us => us.Rating)
                .ThenByDescending(us => us.LastUpdatedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawUserSongs.Select(us => new SongDataInfo(
                us.Song.Id,
                us.Song.ApiKey,
                us.Song.IsLocked,
                us.Song.Title,
                us.Song.TitleNormalized,
                us.Song.SongNumber,
                us.Song.Album.ReleaseDate,
                us.Song.Album.Name,
                us.Song.Album.ApiKey,
                us.Song.Album.Artist.Name,
                us.Song.Album.Artist.ApiKey,
                us.Song.FileSize,
                us.Song.Duration,
                us.Song.CreatedAt,
                us.Song.Tags ?? string.Empty,
                us.IsStarred,
                us.Rating,
                us.Song.AlbumId,
                us.Song.LastPlayedAt,
                us.Song.PlayedCount,
                us.Song.CalculatedRating
            )).ToArray();
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    /// <summary>
    /// List recently played songs for a user with pagination, sorted by last played descending
    /// </summary>
    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListRecentlyPlayedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserSongs
            .Where(us => us.UserId == userId && us.LastPlayedAt != null)
            .Include(us => us.Song)
            .ThenInclude(s => s.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var songCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        SongDataInfo[] songs = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserSongs = await baseQuery
                .OrderByDescending(us => us.LastPlayedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            songs = rawUserSongs.Select(us => new SongDataInfo(
                us.Song.Id,
                us.Song.ApiKey,
                us.Song.IsLocked,
                us.Song.Title,
                us.Song.TitleNormalized,
                us.Song.SongNumber,
                us.Song.Album.ReleaseDate,
                us.Song.Album.Name,
                us.Song.Album.ApiKey,
                us.Song.Album.Artist.Name,
                us.Song.Album.Artist.ApiKey,
                us.Song.FileSize,
                us.Song.Duration,
                us.Song.CreatedAt,
                us.Song.Tags ?? string.Empty,
                us.IsStarred,
                us.Rating,
                us.Song.AlbumId,
                us.Song.LastPlayedAt,
                us.Song.PlayedCount,
                us.Song.CalculatedRating
            )).ToArray();
        }

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = songCount,
            TotalPages = pagedRequest.TotalPages(songCount),
            Data = songs
        };
    }

    /// <summary>
    ///     Gets random songs from the library.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<SongDataInfo[]>> GetRandomSongsAsync(
        int count,
        int? userId = null,
        Guid? filterByArtistApiKey = null,
        Guid? filterByAlbumApiKey = null,
        string? filterByGenre = null,
        int? fromYear = null,
        int? toYear = null,
        CancellationToken cancellationToken = default)
    {
        if (count < 1)
        {
            count = 10;
        }

        if (count > 500)
        {
            count = 500;
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = scopedContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .AsQueryable();

        // Apply filters
        if (filterByArtistApiKey.HasValue)
        {
            query = query.Where(s => s.Album.Artist.ApiKey == filterByArtistApiKey.Value);
        }

        if (filterByAlbumApiKey.HasValue)
        {
            query = query.Where(s => s.Album.ApiKey == filterByAlbumApiKey.Value);
        }

        if (!string.IsNullOrWhiteSpace(filterByGenre))
        {
            var genreNormalized = filterByGenre.ToUpperInvariant().Trim();
            query = query.Where(s => s.Genres != null && s.Genres.Any(g => g.ToUpper().Contains(genreNormalized)));
        }

        if (fromYear.HasValue)
        {
            query = query.Where(s => s.Album.ReleaseDate.Year >= fromYear.Value);
        }

        if (toYear.HasValue)
        {
            query = query.Where(s => s.Album.ReleaseDate.Year <= toYear.Value);
        }

        // If user specified, exclude hated songs
        if (userId.HasValue)
        {
            var hatedSongIds = await scopedContext.UserSongs
                .AsNoTracking()
                .Where(us => us.UserId == userId.Value && us.IsHated)
                .Select(us => us.SongId)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (hatedSongIds.Length > 0)
            {
                query = query.Where(s => !hatedSongIds.Contains(s.Id));
            }
        }

        // Get random songs using cross-provider random ordering (GUID-based)
        var randomSongs = await query
            .OrderBy(_ => Guid.NewGuid())
            .Take(count)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var songs = randomSongs.Select(s => new SongDataInfo(
            s.Id,
            s.ApiKey,
            s.IsLocked,
            s.Title,
            s.TitleNormalized,
            s.SongNumber,
            s.Album.ReleaseDate,
            s.Album.Name,
            s.Album.ApiKey,
            s.Album.Artist.Name,
            s.Album.Artist.ApiKey,
            s.FileSize,
            s.Duration,
            s.CreatedAt,
            s.Tags ?? string.Empty,
            false, // UserStarred - would need user context
            0, // UserRating - would need user context
            s.AlbumId,
            s.LastPlayedAt,
            s.PlayedCount,
            s.CalculatedRating
        )).ToArray();

        return new MelodeeModels.OperationResult<SongDataInfo[]>
        {
            Data = songs
        };
    }

    public async Task<MelodeeModels.OperationResult<Song[]>> ListByGenreAndBpmAsync(
        string[] genres,
        int? minBpm,
        int? maxBpm,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (genres == null || genres.Length == 0)
        {
            return new MelodeeModels.OperationResult<Song[]> { Data = [] };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var normalizedGenres = genres.Select(g => g.ToUpperInvariant()).ToArray();

        var query = scopedContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .Where(s => s.Genres != null && s.Genres.Any(g => normalizedGenres.Contains(g.ToUpper())));

        if (minBpm.HasValue)
        {
            query = query.Where(s => s.BPM >= minBpm.Value);
        }

        if (maxBpm.HasValue)
        {
            query = query.Where(s => s.BPM <= maxBpm.Value);
        }

        var songs = await query
            .OrderByDescending(s => s.PlayedCount)
            .ThenBy(s => s.Title)
            .Take(limit)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<Song[]> { Data = songs };
    }

    public async Task<MelodeeModels.PagedResult<Song>> ListByBpmRangeAsync(
        double minBpm,
        double maxBpm,
        MelodeeModels.PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = scopedContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .Where(s => s.BPM >= minBpm && s.BPM <= maxBpm);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (pagedRequest.IsTotalCountOnlyRequest)
        {
            return new MelodeeModels.PagedResult<Song>
            {
                TotalCount = totalCount,
                Data = []
            };
        }

        var songs = await query
            .OrderBy(s => s.BPM)
            .ThenBy(s => s.Title)
            .Skip(pagedRequest.SkipValue)
            .Take(pagedRequest.TakeValue)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.PagedResult<Song>
        {
            TotalCount = totalCount,
            TotalPages = pagedRequest.TotalPages(totalCount),
            Data = songs
        };
    }

    public async Task<MelodeeModels.PagedResult<SongDataInfo>> ListTopRatedGlobalAsync(
        MelodeeModels.PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var query = scopedContext.Songs
            .AsNoTracking()
            .Include(s => s.Album)
            .ThenInclude(a => a.Artist)
            .Where(s => s.CalculatedRating > 0)
            .OrderByDescending(s => s.CalculatedRating)
            .ThenByDescending(s => s.PlayedCount);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        if (pagedRequest.IsTotalCountOnlyRequest)
        {
            return new MelodeeModels.PagedResult<SongDataInfo>
            {
                TotalCount = totalCount,
                Data = []
            };
        }

        var songs = await query
            .Skip(pagedRequest.SkipValue)
            .Take(pagedRequest.TakeValue)
            .Select(s => new SongDataInfo(
                s.Id,
                s.ApiKey,
                s.IsLocked,
                s.Title,
                s.TitleNormalized,
                s.SongNumber,
                s.Album.ReleaseDate,
                s.Album.Name,
                s.Album.ApiKey,
                s.Album.Artist.Name,
                s.Album.Artist.ApiKey,
                s.FileSize,
                s.Duration,
                s.CreatedAt,
                s.Tags ?? string.Empty,
                false,
                0,
                s.AlbumId,
                s.LastPlayedAt,
                s.PlayedCount,
                s.CalculatedRating
            ))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.PagedResult<SongDataInfo>
        {
            TotalCount = totalCount,
            TotalPages = pagedRequest.TotalPages(totalCount),
            Data = songs
        };
    }
}
