using System.Diagnostics;
using System.Linq.Expressions;
using Ardalis.GuardClauses;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Data.Models.Extensions;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Filtering;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Models.OpenSubsonic;
using Melodee.Common.Models.OpenSubsonic.Enums;
using Melodee.Common.Models.OpenSubsonic.Requests;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Extensions;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Rebus.Bus;
using Serilog;
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public class AlbumService(
    ILogger logger,
    ICacheManager cacheManager,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IBus bus,
    ISerializer serializer,
    IHttpClientFactory httpClientFactory,
    MediaEditService mediaEditService,
    IFileSystemService fileSystemService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:album:apikey:{0}";
    private const string CacheKeyDetailByNameNormalizedTemplate = "urn:album:namenormalized:{0}";
    private const string CacheKeyDetailByMusicBrainzIdTemplate = "urn:album:musicbrainzid:{0}";
    private const string CacheKeyDetailTemplate = "urn:album:{0}";
    private const string CacheKeyAlbumImageBytesAndEtagTemplate = "urn:album:imagebytesandetag:{0}:{1}";
    private const string CacheKeyGenres = "urn:album:genres";

    public async Task ClearCacheForArtist(int artistId, CancellationToken cancellationToken = default)
    {
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Get the artist from db context
            var dbArtist = await scopedContext
                .Artists
                .Include(x => x.Albums)
                .FirstOrDefaultAsync(x => x.Id == artistId, cancellationToken).ConfigureAwait(false);

            // For each album for artist clear the cache for the artist
            foreach (var album in dbArtist?.Albums ?? [])
            {
                await ClearCacheAsync(album.Id, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task ClearCacheAsync(int albumId, CancellationToken cancellationToken = default)
    {
        var album = await GetAsync(albumId, cancellationToken).ConfigureAwait(false);
        ClearCache(album.Data!);
    }

    public void ClearCache(Album album)
    {
        CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(album.ApiKey), Album.CacheRegion);
        CacheManager.Remove(CacheKeyDetailByNameNormalizedTemplate.FormatSmart(album.NameNormalized), Album.CacheRegion);

        CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(album.Id), Album.CacheRegion);

        CacheManager.Remove(CacheKeyAlbumImageBytesAndEtagTemplate.FormatSmart(album.ApiKey, ImageSize.Thumbnail), Album.CacheRegion);
        CacheManager.Remove(CacheKeyAlbumImageBytesAndEtagTemplate.FormatSmart(album.ApiKey, ImageSize.Small), Album.CacheRegion);
        CacheManager.Remove(CacheKeyAlbumImageBytesAndEtagTemplate.FormatSmart(album.ApiKey, ImageSize.Medium), Album.CacheRegion);
        CacheManager.Remove(CacheKeyAlbumImageBytesAndEtagTemplate.FormatSmart(album.ApiKey, ImageSize.Large), Album.CacheRegion);

        // Clear genres cache as album genres may have changed
        CacheManager.Remove(CacheKeyGenres, Album.CacheRegion);

        if (album.MusicBrainzId != null)
        {
            CacheManager.Remove(CacheKeyDetailByMusicBrainzIdTemplate.FormatSmart(album.MusicBrainzId.Value.ToString()), Album.CacheRegion);
        }
    }

    public async Task<MelodeeModels.PagedResult<AlbumDataInfo>> ListForContributorsAsync(
        MelodeeModels.PagedRequest pagedRequest,
        string contributorName,
        CancellationToken cancellationToken = default)
    {
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Create base query using EF Core with proper joins and filtering
            var baseQuery = scopedContext.Contributors
                .Where(c => c.ContributorName != null && c.ContributorName.Contains(contributorName))
                .Select(c => c.Album)
                .Distinct();

            // Get total count efficiently
            var albumCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

            AlbumDataInfo[] albums = [];

            if (!pagedRequest.IsTotalCountOnlyRequest)
            {
                // First, get the album data with artist information
                var albumsQueryWithIncludes = baseQuery
                    .Include(a => a.Artist);

                // Apply ordering on the entity properties before projection
                var orderByClause = pagedRequest.OrderByValue("Name", MelodeeModels.PagedRequest.OrderAscDirection);
                var isDescending = orderByClause.Contains("DESC", StringComparison.OrdinalIgnoreCase);
                var fieldName = orderByClause.Split(' ')[0].Trim('"').ToLowerInvariant();

                IQueryable<Album> albumsQuery = fieldName switch
                {
                    "name" => isDescending ? albumsQueryWithIncludes.OrderByDescending(a => a.Name) : albumsQueryWithIncludes.OrderBy(a => a.Name),
                    "createdat" => isDescending ? albumsQueryWithIncludes.OrderByDescending(a => a.CreatedAt) : albumsQueryWithIncludes.OrderBy(a => a.CreatedAt),
                    "releasedate" => isDescending ? albumsQueryWithIncludes.OrderByDescending(a => a.ReleaseDate) : albumsQueryWithIncludes.OrderBy(a => a.ReleaseDate),
                    "songcount" => isDescending ? albumsQueryWithIncludes.OrderByDescending(a => a.SongCount) : albumsQueryWithIncludes.OrderBy(a => a.SongCount),
                    "duration" => isDescending ? albumsQueryWithIncludes.OrderByDescending(a => a.Duration) : albumsQueryWithIncludes.OrderBy(a => a.Duration),
                    "lastplayedat" => isDescending ? albumsQueryWithIncludes.OrderByDescending(a => a.LastPlayedAt) : albumsQueryWithIncludes.OrderBy(a => a.LastPlayedAt),
                    "playedcount" => isDescending ? albumsQueryWithIncludes.OrderByDescending(a => a.PlayedCount) : albumsQueryWithIncludes.OrderBy(a => a.PlayedCount),
                    "calculatedrating" => isDescending ? albumsQueryWithIncludes.OrderByDescending(a => a.CalculatedRating) : albumsQueryWithIncludes.OrderBy(a => a.CalculatedRating),
                    _ => albumsQueryWithIncludes.OrderBy(a => a.Name)
                };

                // Apply paging and get the raw entities
                var albumEntities = await albumsQuery
                    .Skip(pagedRequest.SkipValue)
                    .Take(pagedRequest.TakeValue)
                    .AsNoTracking()
                    .ToArrayAsync(cancellationToken)
                    .ConfigureAwait(false);

                // Project to AlbumDataInfo in memory
                albums = albumEntities
                    .Select(a => new AlbumDataInfo(
                        a.Id,
                        a.ApiKey,
                        a.IsLocked,
                        a.Name,
                        a.NameNormalized,
                        a.AlternateNames,
                        a.Artist.ApiKey,
                        a.Artist.Name,
                        a.SongCount ?? 0,
                        a.Duration,
                        a.CreatedAt,
                        a.Tags,
                        a.ReleaseDate,
                        a.AlbumStatus,
                        a.LastPlayedAt,
                        a.PlayedCount,
                        a.CalculatedRating
                    ))
                    .ToArray();
            }

            return new MelodeeModels.PagedResult<AlbumDataInfo>
            {
                TotalCount = albumCount,
                TotalPages = pagedRequest.TotalPages(albumCount),
                Data = albums
            };
        }
    }

    public async Task<MelodeeModels.PagedResult<AlbumDataInfo>> ListForArtistApiKeyAsync(
        MelodeeModels.PagedRequest pagedRequest,
        Guid filterToArtistApiKey,
        CancellationToken cancellationToken = default)
    {
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Create base query filtering by artist API key
            var baseQuery = scopedContext.Albums
                .Where(x => x.Artist.ApiKey == filterToArtistApiKey);

            // Apply name filter if provided
            var filterBy = pagedRequest.FilterBy?.FirstOrDefault(x => x.PropertyName == "Name")?.Value.ToString()
                .ToNormalizedString();
            if (!string.IsNullOrEmpty(filterBy))
            {
                baseQuery = baseQuery.Where(x => x.NameNormalized.Contains(filterBy));
            }

            // Get total count efficiently
            var albumCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

            AlbumDataInfo[] albums = [];

            if (!pagedRequest.IsTotalCountOnlyRequest)
            {
                // Apply ordering first on the base query, then project
                var orderByClause = pagedRequest.OrderByValue("Name", MelodeeModels.PagedRequest.OrderAscDirection);
                var isDescending = orderByClause.Contains("DESC", StringComparison.OrdinalIgnoreCase);
                var fieldName = orderByClause.Split(' ')[0].Trim('"').ToLowerInvariant();

                var orderedQuery = fieldName switch
                {
                    "name" or "namenormalized" => isDescending ? baseQuery.OrderByDescending(a => a.Name) : baseQuery.OrderBy(a => a.SortName).ThenBy(x => x.Name),
                    "createdat" => isDescending ? baseQuery.OrderByDescending(a => a.CreatedAt) : baseQuery.OrderBy(a => a.CreatedAt),
                    "directory" => isDescending ? baseQuery.OrderByDescending(a => a.Directory) : baseQuery.OrderBy(a => a.Directory),
                    "duration" => isDescending ? baseQuery.OrderByDescending(a => a.Duration) : baseQuery.OrderBy(a => a.Duration),
                    "releasedate" => isDescending ? baseQuery.OrderByDescending(a => a.ReleaseDate) : baseQuery.OrderBy(a => a.ReleaseDate),
                    "songcount" => isDescending ? baseQuery.OrderByDescending(a => a.SongCount) : baseQuery.OrderBy(a => a.SongCount),
                    "lastplayedat" => isDescending ? baseQuery.OrderByDescending(a => a.LastPlayedAt) : baseQuery.OrderBy(a => a.LastPlayedAt),
                    "playedcount" => isDescending ? baseQuery.OrderByDescending(a => a.PlayedCount) : baseQuery.OrderBy(a => a.PlayedCount),
                    "calculatedrating" => isDescending ? baseQuery.OrderByDescending(a => a.CalculatedRating) : baseQuery.OrderBy(a => a.CalculatedRating),
                    _ => baseQuery.OrderBy(a => a.Name)
                };

                // Apply paging and include Artist for projection
                var pagedQuery = orderedQuery
                    .Include(a => a.Artist)
                    .Skip(pagedRequest.SkipValue)
                    .Take(pagedRequest.TakeValue)
                    .AsNoTracking();

                // Execute query and project to AlbumDataInfo
                var rawAlbums = await pagedQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);

                albums = rawAlbums.Select(a => new AlbumDataInfo(
                    a.Id,
                    a.ApiKey,
                    a.IsLocked,
                    a.Name,
                    a.NameNormalized,
                    a.AlternateNames,
                    a.Artist.ApiKey,
                    a.Artist.Name,
                    a.SongCount ?? 0,
                    a.Duration,
                    a.CreatedAt,
                    a.Tags,
                    a.ReleaseDate,
                    a.AlbumStatus,
                    a.LastPlayedAt,
                    a.PlayedCount,
                    a.CalculatedRating
                )).ToArray();
            }

            return new MelodeeModels.PagedResult<AlbumDataInfo>
            {
                TotalCount = albumCount,
                TotalPages = pagedRequest.TotalPages(albumCount),
                Data = albums
            };
        }
    }

    public async Task<MelodeeModels.PagedResult<AlbumDataInfo>> ListAsync(
        MelodeeModels.PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Create base query
            var baseQuery = scopedContext.Albums.AsQueryable();

            // Apply filters
            baseQuery = ApplyFilters(baseQuery, pagedRequest);

            // Get total count efficiently
            var albumCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

            AlbumDataInfo[] albums = [];

            if (!pagedRequest.IsTotalCountOnlyRequest)
            {
                // Apply ordering first on the base query, then project
                var orderByClause = pagedRequest.OrderByValue("Name", MelodeeModels.PagedRequest.OrderAscDirection);
                var isDescending = orderByClause.Contains("DESC", StringComparison.OrdinalIgnoreCase);
                var fieldName = orderByClause.Split(' ')[0].Trim('"').ToLowerInvariant();

                var orderedQuery = fieldName switch
                {
                    "name" or "namenormalized" => isDescending ? baseQuery.OrderByDescending(a => a.SortName).ThenByDescending(x => x.Name) : baseQuery.OrderBy(a => a.SortName).ThenBy(x => x.Name),
                    "createdat" => isDescending ? baseQuery.OrderByDescending(a => a.CreatedAt) : baseQuery.OrderBy(a => a.CreatedAt),
                    "directory" => isDescending ? baseQuery.OrderByDescending(a => a.Directory) : baseQuery.OrderBy(a => a.Directory),
                    "duration" => isDescending ? baseQuery.OrderByDescending(a => a.Duration) : baseQuery.OrderBy(a => a.Duration),
                    "releasedate" => isDescending ? baseQuery.OrderByDescending(a => a.ReleaseDate) : baseQuery.OrderBy(a => a.ReleaseDate),
                    "songcount" => isDescending ? baseQuery.OrderByDescending(a => a.SongCount) : baseQuery.OrderBy(a => a.SongCount),
                    "lastplayedat" => isDescending ? baseQuery.OrderByDescending(a => a.LastPlayedAt) : baseQuery.OrderBy(a => a.LastPlayedAt),
                    "playedcount" => isDescending ? baseQuery.OrderByDescending(a => a.PlayedCount) : baseQuery.OrderBy(a => a.PlayedCount),
                    "calculatedrating" => isDescending ? baseQuery.OrderByDescending(a => a.CalculatedRating) : baseQuery.OrderBy(a => a.CalculatedRating),
                    _ => baseQuery.OrderBy(a => a.Name)
                };

                // Apply paging and include Artist for projection
                var pagedQuery = orderedQuery
                    .Include(a => a.Artist)
                    .Skip(pagedRequest.SkipValue)
                    .Take(pagedRequest.TakeValue)
                    .AsNoTracking();

                // Execute query and project to AlbumDataInfo
                var rawAlbums = await pagedQuery.ToArrayAsync(cancellationToken).ConfigureAwait(false);

                albums = rawAlbums.Select(a => new AlbumDataInfo(
                    a.Id,
                    a.ApiKey,
                    a.IsLocked,
                    a.Name,
                    a.NameNormalized,
                    a.AlternateNames,
                    a.Artist.ApiKey,
                    a.Artist.Name,
                    a.SongCount ?? 0,
                    a.Duration,
                    a.CreatedAt,
                    a.Tags,
                    a.ReleaseDate,
                    a.AlbumStatus,
                    a.LastPlayedAt,
                    a.PlayedCount,
                    a.CalculatedRating
                )).ToArray();
            }

            return new MelodeeModels.PagedResult<AlbumDataInfo>
            {
                TotalCount = albumCount,
                TotalPages = pagedRequest.TotalPages(albumCount),
                Data = albums
            };
        }
    }


    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(
        int[] albumIds,
        CancellationToken cancellationToken = default)
    {
        return await DeleteAsync(albumIds, deleteFiles: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(
        int[] albumIds,
        bool deleteFiles,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(albumIds, nameof(albumIds));

        bool result;

        var artistIds = new List<int>();
        var libraryIds = new List<int>();

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var albumId in albumIds)
            {
                var artist = await GetAsync(albumId, cancellationToken).ConfigureAwait(false);
                if (!artist.IsSuccess)
                {
                    return new MelodeeModels.OperationResult<bool>("Unknown album")
                    {
                        Data = false
                    };
                }
            }

            foreach (var albuMid in albumIds)
            {
                var album = await scopedContext
                    .Albums.Include(x => x.Artist).ThenInclude(x => x.Library)
                    .FirstAsync(x => x.Id == albuMid, cancellationToken)
                    .ConfigureAwait(false);

                if (deleteFiles)
                {
                    var albumDirectory = Path.Combine(album.Artist.Library.Path, album.Artist.Directory, album.Directory);
                    if (fileSystemService.DirectoryExists(albumDirectory))
                    {
                        fileSystemService.DeleteDirectory(albumDirectory, true);
                    }
                }

                scopedContext.Albums.Remove(album);
                artistIds.Add(album.ArtistId);
                libraryIds.Add(album.Artist.LibraryId);
            }

            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var artistId in artistIds.Distinct())
            {
                await UpdateArtistAggregateValuesByIdAsync(artistId, cancellationToken).ConfigureAwait(false);
            }

            foreach (var libraryId in libraryIds.Distinct())
            {
                await UpdateLibraryAggregateStatsByIdAsync(libraryId, cancellationToken).ConfigureAwait(false);
            }

            Logger.Information("Deleted albums [{AlbumIds}] (files deleted: {DeleteFiles}).", albumIds, deleteFiles);
            result = true;
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<Album?>> GetAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, id, nameof(id));

        var result = await CacheManager.GetAsync(CacheKeyDetailTemplate.FormatSmart(id), async () =>
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await scopedContext
                    .Albums
                    .Include(x => x.Artist).ThenInclude(x => x.Library)
                    .Include(x => x.Contributors).ThenInclude(x => x.Artist)
                    .Include(x => x.Songs).ThenInclude(x => x.Contributors).ThenInclude(x => x.Artist)
                    .AsSplitQuery()
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }, cancellationToken, region: Album.CacheRegion);

        return new MelodeeModels.OperationResult<Album?>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<Album?>> GetByMusicBrainzIdAsync(
        Guid musicBrainzId,
        CancellationToken cancellationToken = default)
    {
        var id = await CacheManager.GetAsync<int?>(
            CacheKeyDetailByMusicBrainzIdTemplate.FormatSmart(musicBrainzId.ToString()), async () =>
            {
                await using (var scopedContext =
                             await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    return await scopedContext.Albums
                        .Where(a => a.MusicBrainzId == musicBrainzId)
                        .Select(a => a.Id)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }, cancellationToken, region: Album.CacheRegion);
        if (id is null or 0)
        {
            return new MelodeeModels.OperationResult<Album?>("Unknown album")
            {
                Data = null
            };
        }

        return await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<Album?>> GetByApiKeyAsync(
        Guid apiKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(_ => apiKey == Guid.Empty, apiKey, nameof(apiKey));

        var id = await CacheManager.GetAsync<int?>(CacheKeyDetailByApiKeyTemplate.FormatSmart(apiKey), async () =>
        {
            await using (var scopedContext =
                         await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                return await scopedContext.Albums
                    .Where(a => a.ApiKey == apiKey)
                    .Select(a => a.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }, cancellationToken, region: Album.CacheRegion);
        if (id is null or 0)
        {
            return new MelodeeModels.OperationResult<Album?>("Unknown album")
            {
                Data = null
            };
        }

        return await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Get an album by its directory path.
    /// </summary>
    public async Task<Album?> GetByDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrWhiteSpace(directoryPath, nameof(directoryPath));

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            return await scopedContext.Albums
                .Include(a => a.Artist)
                .Where(a => a.Directory == directoryPath)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public async Task<MelodeeModels.OperationResult<bool>> UpdateAsync(
        Album album,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(album, nameof(album));

        var validationResult = ValidateModel(album);
        if (!validationResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<bool>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage)).Select(x => x.ErrorMessage!).ToArray() ?? [])
            {
                Data = false,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        bool result;
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var dbDetail = await scopedContext
                .Albums
                .Include(x => x.Artist).ThenInclude(x => x.Library)
                .FirstOrDefaultAsync(x => x.Id == album.Id, cancellationToken)
                .ConfigureAwait(false);

            if (dbDetail == null)
            {
                return new MelodeeModels.OperationResult<bool>
                {
                    Data = false,
                    Type = MelodeeModels.OperationResponseType.NotFound
                };
            }

            var didChangeName = dbDetail.Name != album.Name;

            var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);

            var albumDirectory = album.ToMelodeeAlbumModel().AlbumDirectoryName(configuration.Configuration);
            if (!albumDirectory.ToFileSystemDirectoryInfo().ToDirectoryInfo().IsSameDirectory(dbDetail.Directory))
            {
                // Details that are used to build the albums directory has changed, rename directory to new name
                var existingAlbumDirectory = Path.Combine(dbDetail.Artist.Library.Path, dbDetail.Artist.Directory,
                    dbDetail.Directory);
                var newAlbumDirectory =
                    Path.Combine(dbDetail.Artist.Library.Path, dbDetail.Artist.Directory, albumDirectory);
                if (!fileSystemService.DirectoryExists(existingAlbumDirectory))
                {
                    // Details that are used to build the albums directory has changed, rename directory to new name
                    // Directory does not exist, skip renaming
                }
                else
                {
                    fileSystemService.MoveDirectory(existingAlbumDirectory, newAlbumDirectory);
                }

                album.Directory = albumDirectory;
            }

            dbDetail.Directory = album.Directory;

            dbDetail.AlbumStatus = album.AlbumStatus;
            dbDetail.AlbumType = album.AlbumType;
            dbDetail.AlternateNames = album.AlternateNames;
            dbDetail.AmgId = album.AmgId;
            dbDetail.ArtistId = album.ArtistId;
            dbDetail.Comment = album.Comment;
            dbDetail.Description = album.Description;
            dbDetail.DeezerId = album.DeezerId;
            dbDetail.DiscogsId = album.DiscogsId;
            dbDetail.Genres = album.Genres;
            dbDetail.ImageCount = album.ImageCount;
            dbDetail.IsCompilation = album.IsCompilation;
            dbDetail.IsLocked = album.IsLocked;
            dbDetail.ItunesId = album.ItunesId;
            dbDetail.LastFmId = album.LastFmId;
            dbDetail.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
            dbDetail.Moods = album.Moods;
            dbDetail.MusicBrainzId = album.MusicBrainzId;
            dbDetail.Name = album.Name;
            dbDetail.NameNormalized = album.Name.ToNormalizedString() ?? album.Name;
            dbDetail.Notes = album.Notes;
            dbDetail.OriginalReleaseDate = album.OriginalReleaseDate;
            dbDetail.ReleaseDate = album.ReleaseDate;
            dbDetail.SortName = album.SortName;
            dbDetail.SortOrder = album.SortOrder;
            dbDetail.SpotifyId = album.SpotifyId;
            dbDetail.Tags = album.Tags;
            dbDetail.WikiDataId = album.WikiDataId;

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

            if (result)
            {
                ClearCache(dbDetail);

                if (didChangeName)
                {
                    try
                    {
                        await mediaEditService.InitializeAsync(token: cancellationToken);
                        var newAlbumPath = Path.Combine(dbDetail.Artist.Library.Path, dbDetail.Artist.Directory, dbDetail.Directory);

                        // Check if the melodee.json file exists before trying to read it
                        var melodeeJsonPath = Path.Combine(newAlbumPath, "melodee.json");
                        if (fileSystemService.FileExists(melodeeJsonPath))
                        {
                            var melodeeAlbum = await MelodeeModels.Album.DeserializeAndInitializeAlbumAsync(serializer, melodeeJsonPath, cancellationToken).ConfigureAwait(false);
                            if (melodeeAlbum != null)
                            {
                                melodeeAlbum.AlbumDbId = dbDetail.Id;
                                melodeeAlbum.Directory = newAlbumPath.ToFileSystemDirectoryInfo();
                                melodeeAlbum.MusicBrainzId = dbDetail.MusicBrainzId;
                                melodeeAlbum.SpotifyId = dbDetail.SpotifyId;
                                melodeeAlbum.SetTagValue(MetaTagIdentifier.Album, dbDetail.Name);
                                foreach (var song in melodeeAlbum.Songs ?? [])
                                {
                                    melodeeAlbum.SetSongTagValue(song.Id, MetaTagIdentifier.Album, dbDetail.Name);
                                }

                                await mediaEditService.SaveMelodeeAlbum(melodeeAlbum, true, cancellationToken).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            Logger.Warning("Melodee.json file not found at [{Path}] during album update.", melodeeJsonPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to update melodee.json file during album name change for album [{AlbumId}].", dbDetail.Id);
                        // Don't fail the entire operation if we can't update the melodee.json file
                    }
                }
            }
        }


        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }


    public async Task<MelodeeModels.OperationResult<Album?>> FindAlbumAsync(
        int artistId,
        MelodeeModels.Album melodeeAlbum,
        CancellationToken cancellationToken = default)
    {
        var albumTitle = melodeeAlbum.AlbumTitle()?.CleanStringAsIs() ??
                         throw new Exception("Album title is required.");
        var nameNormalized = albumTitle.ToNormalizedString() ?? albumTitle;

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            int? id = null;

            try
            {
                if (melodeeAlbum.AlbumDbId.HasValue)
                {
                    id = await scopedContext.Albums
                        .Where(a => a.Id == melodeeAlbum.AlbumDbId.Value)
                        .Select(a => (int?)a.Id)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                if (id == null && melodeeAlbum.Id != Guid.Empty)
                {
                    id = await scopedContext.Albums
                        .Where(a => a.ApiKey == melodeeAlbum.Id)
                        .Select(a => (int?)a.Id)
                        .FirstOrDefaultAsync(cancellationToken)
                        .ConfigureAwait(false);
                }

                id ??= await scopedContext.Albums
                    .Where(a => a.ArtistId == artistId)
                    .Where(a => a.NameNormalized == nameNormalized ||
                                (a.MusicBrainzId == melodeeAlbum.MusicBrainzId && a.MusicBrainzId != null) ||
                                (a.SpotifyId == melodeeAlbum.SpotifyId && a.SpotifyId != null))
                    .Select(a => (int?)a.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Logger.Error(e,
                    "[{ServiceName}] attempting to Find Album id [{Id}], apiKey [{ApiKey}], name [{Name}] musicbrainzId [{MbId}] spotifyId [{SpotifyId}].",
                    nameof(AlbumService),
                    melodeeAlbum.AlbumDbId,
                    melodeeAlbum.Id,
                    nameNormalized,
                    melodeeAlbum.MusicBrainzId,
                    melodeeAlbum.SpotifyId);
            }

            if (id is null or 0)
            {
                return new MelodeeModels.OperationResult<Album?>("Unknown album")
                {
                    Data = null
                };
            }

            return await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<MelodeeModels.OperationResult<bool>> RescanAsync(
        int[] albumIds,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(albumIds, nameof(albumIds));

        var successfulScans = 0;

        foreach (var albumId in albumIds)
        {
            var albumResult = await GetAsync(albumId, cancellationToken).ConfigureAwait(false);
            if (!albumResult.IsSuccess || albumResult.Data == null)
            {
                return new MelodeeModels.OperationResult<bool>("Unknown album")
                {
                    Data = false
                };
            }

            var album = albumResult.Data;
            var albumDirectory = Path.Combine(album.Artist.Library.Path, album.Artist.Directory, album.Directory);
            if (!fileSystemService.DirectoryExists(albumDirectory))
            {
                Logger.Warning("Album directory [{AlbumDirectory}] does not exist for rescan.", albumDirectory);
                // Continue with other albums but don't count this as successful
                continue;
            }

            await bus.SendLocal(new AlbumRescanEvent(album.Id, albumDirectory, false)).ConfigureAwait(false);
            successfulScans++;
        }

        // Return false if no albums were successfully scanned
        return new MelodeeModels.OperationResult<bool>
        {
            Data = successfulScans > 0
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> LockUnlockAlbumAsync(
        int albumId,
        bool doLock,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, albumId, nameof(albumId));

        var artistResult = await GetAsync(albumId, cancellationToken).ConfigureAwait(false);
        if (!artistResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<bool>($"Unknown album to lock [{albumId}].")
            {
                Data = false
            };
        }

        artistResult.Data!.IsLocked = doLock;
        var result = (await UpdateAsync(artistResult.Data, cancellationToken).ConfigureAwait(false)).Data;
        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<Album?>> AddAlbumAsync(
        Album album,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(album, nameof(album));
        Guard.Against.NullOrEmpty(album.Name, nameof(album.Name));
        Guard.Against.Null(album.Artist, nameof(album), "Artist is required for album.");
        Guard.Against.Expression(x => x < 1, album.ArtistId, nameof(album.ArtistId), "ArtistId is required for album.");

        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);

        album.ApiKey = Guid.NewGuid();
        album.Directory = album.ToMelodeeAlbumModel().AlbumDirectoryName(configuration.Configuration);
        album.CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        album.MetaDataStatus = (int)MetaDataModelStatus.ReadyToProcess;
        album.NameNormalized = album.NameNormalized.Nullify() ?? album.Name.ToNormalizedString() ?? album.Name;

        var validationResult = ValidateModel(album);
        if (!validationResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<Album?>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage)).Select(x => x.ErrorMessage!).ToArray() ?? [])
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            scopedContext.Albums.Add(album);
            var result = 0;
            try
            {
                result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            if (result > 0)
            {
                await UpdateLibraryAggregateStatsByIdAsync(album.Artist.LibraryId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        // After saving, the album.Id should be populated by EF Core
        if (album.Id > 0)
        {
            return await GetAsync(album.Id, cancellationToken);
        }

        // If for some reason the ID wasn't set, return the album as-is
        return new MelodeeModels.OperationResult<Album?>
        {
            Data = album
        };
    }

    public async Task<MelodeeModels.ImageBytesAndEtag> GetAlbumImageBytesAndEtagAsync(
        Guid? apiKey,
        string? size = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(apiKey, nameof(apiKey));
        Guard.Against.Expression(x => x == Guid.Empty, apiKey.Value, nameof(apiKey));

        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var sizeValue = size ?? nameof(ImageSize.Large);

        // Use apiKey and size in cache key - resized images are cached separately
        var cacheKey = CacheKeyAlbumImageBytesAndEtagTemplate.FormatSmart(apiKey.Value, sizeValue);
        var overallStopwatch = Stopwatch.StartNew();
        var wasCacheMiss = false;

        var result = await CacheManager.GetAsync(cacheKey, async () =>
        {
            wasCacheMiss = true;
            var badEtag = Instant.MinValue.ToEtag();

            // Database lookup only happens on cache miss
            var dbStopwatch = Stopwatch.StartNew();
            var album = await GetByApiKeyAsync(apiKey.Value, cancellationToken).ConfigureAwait(false);
            dbStopwatch.Stop();

            if (!album.IsSuccess || album.Data == null)
            {
                Logger.Debug("GetAlbumImageBytesAndEtagAsync: DB lookup failed for ApiKey [{ApiKey}] in {DbMs}ms", apiKey.Value, dbStopwatch.ElapsedMilliseconds);
                return new MelodeeModels.ImageBytesAndEtag(null, null);
            }

            var albumDirectory = album.Data.ToFileSystemDirectoryInfo();
            if (!albumDirectory.Exists())
            {
                Logger.Warning("Album directory [{Directory}] does not exist for album [{AlbumId}]. DB: {DbMs}ms", albumDirectory.FullName(), album.Data.Id, dbStopwatch.ElapsedMilliseconds);
                return new MelodeeModels.ImageBytesAndEtag(null, badEtag);
            }

            // Check if a pre-sized image exists on disk first
            var albumImages = albumDirectory.AllFileImageTypeFileInfos().ToArray();
            var imageFile = albumImages
                .FirstOrDefault(x => x.Name.Contains($"-{sizeValue}", StringComparison.OrdinalIgnoreCase))
                            ?? albumImages.OrderBy(x => x.Name).FirstOrDefault();

            if (imageFile is not { Exists: true })
            {
                Logger.Warning("No image found for album [{AlbumId}]. DB: {DbMs}ms", album.Data.Id, dbStopwatch.ElapsedMilliseconds);
                return new MelodeeModels.ImageBytesAndEtag(null, badEtag);
            }

            var fileStopwatch = Stopwatch.StartNew();
            var imageBytes = await File.ReadAllBytesAsync(imageFile.FullName, cancellationToken).ConfigureAwait(false);
            fileStopwatch.Stop();

            var eTag = (album.Data.LastUpdatedAt ?? album.Data.CreatedAt).ToEtag();

            // Resize if needed (when size is not Large and no pre-sized image was found)
            var parsedSize = SafeParser.ToEnum<ImageSize>(sizeValue);
            if (parsedSize != ImageSize.Large && !imageFile.Name.Contains($"-{sizeValue}", StringComparison.OrdinalIgnoreCase))
            {
                var resizeStopwatch = Stopwatch.StartNew();
                var targetSize = parsedSize switch
                {
                    ImageSize.Thumbnail => configuration.GetValue<int?>(SettingRegistry.ImagingThumbnailSize) ?? SafeParser.ToNumber<int>(ImageSize.Thumbnail),
                    ImageSize.Small => configuration.GetValue<int?>(SettingRegistry.ImagingSmallSize) ?? SafeParser.ToNumber<int>(ImageSize.Small),
                    ImageSize.Medium => configuration.GetValue<int?>(SettingRegistry.ImagingMediumSize) ?? SafeParser.ToNumber<int>(ImageSize.Medium),
                    _ => SafeParser.ToNumber<int>(sizeValue)
                };

                if (targetSize > 0)
                {
                    imageBytes = ImageConvertor.ResizeImageIfNeeded(imageBytes, targetSize, targetSize, false);
                    eTag = HashHelper.CreateSha256(eTag + targetSize);
                }
                resizeStopwatch.Stop();

                Logger.Debug("GetAlbumImageBytesAndEtagAsync MISS: Album [{AlbumId}] DB: {DbMs}ms, FileRead: {FileMs}ms, Resize: {ResizeMs}ms, Size: {Size}bytes",
                    album.Data.Id, dbStopwatch.ElapsedMilliseconds, fileStopwatch.ElapsedMilliseconds, resizeStopwatch.ElapsedMilliseconds, imageBytes.Length);
            }
            else
            {
                Logger.Debug("GetAlbumImageBytesAndEtagAsync MISS: Album [{AlbumId}] DB: {DbMs}ms, FileRead: {FileMs}ms, Size: {Size}bytes",
                    album.Data.Id, dbStopwatch.ElapsedMilliseconds, fileStopwatch.ElapsedMilliseconds, imageBytes.Length);
            }

            return new MelodeeModels.ImageBytesAndEtag(imageBytes, eTag);
        }, cancellationToken, configuration.CacheDuration(), Album.CacheRegion);

        overallStopwatch.Stop();

        if (!wasCacheMiss)
        {
            Logger.Debug("GetAlbumImageBytesAndEtagAsync HIT: ApiKey [{ApiKey}] Total: {TotalMs}ms", apiKey.Value, overallStopwatch.ElapsedMilliseconds);
        }
        else
        {
            Logger.Debug("GetAlbumImageBytesAndEtagAsync MISS Total: ApiKey [{ApiKey}] Total: {TotalMs}ms", apiKey.Value, overallStopwatch.ElapsedMilliseconds);
        }

        return result;
    }

    public async Task<MelodeeModels.OperationResult<bool>> SaveImageAsAlbumImageAsync(
        int albumId,
        bool deleteAllImages,
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, albumId, nameof(albumId));
        Guard.Against.NullOrEmpty(imageBytes, nameof(imageBytes));

        var album = await GetAsync(albumId, cancellationToken);
        if (!album.IsSuccess || album.Data == null)
        {
            return new MelodeeModels.OperationResult<bool>("Unknown album")
            {
                Data = false
            };
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = await SaveImageBytesAsAlbumImageAsync(
                    album.Data,
                    deleteAllImages,
                    imageBytes,
                    cancellationToken)
                .ConfigureAwait(false)
        };
    }

    private async Task<bool> SaveImageBytesAsAlbumImageAsync(
        Album album,
        bool deleteAllImages,
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);
        var imageConvertor = new ImageConvertor(configuration);

        var albumPath = album.ToFileSystemDirectoryInfo();
        var albumImages = albumPath.FileInfosForExtension("jpg", false).ToArray();
        if (deleteAllImages)
        {
            fileSystemService.DeleteAllFilesForExtension(albumPath, "*.jpg");
        }
        var totalAlbumImageCount = albumImages.Length == 1 ? 1 : albumImages.Length + 1;
        var newAlbumCoverFilename = Path.Combine(albumPath.FullName(), $"i-01-{Album.FrontImageType}.jpg");
        if (fileSystemService.FileExists(newAlbumCoverFilename))
        {
            fileSystemService.DeleteFile(newAlbumCoverFilename);
        }

        await fileSystemService.WriteAllBytesAsync(newAlbumCoverFilename, imageBytes, cancellationToken).ConfigureAwait(false);
        await imageConvertor.ProcessFileAsync(
            albumPath,
            new MelodeeModels.FileSystemFileInfo
            {
                Name = newAlbumCoverFilename,
                Size = imageBytes.Length
            },
            cancellationToken).ConfigureAwait(false);
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            await scopedContext.Albums
                .Where(x => x.Id == album.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LastUpdatedAt, now)
                    .SetProperty(x => x.ImageCount, albumPath.ImageFilesFound), cancellationToken)
                .ConfigureAwait(false);
        }

        await ClearCacheAsync(album.Id, cancellationToken).ConfigureAwait(false);
        Logger.Information("Saved image for album [{ArtistId}] with {ImageCount} images.",
            album.Id, totalAlbumImageCount);
        return true;
    }

    public async Task<MelodeeModels.OperationResult<bool>> SaveImageUrlAsAlbumImageAsync(
        int albumId,
        string imageUrl,
        bool deleteAllImages,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, albumId, nameof(albumId));
        Guard.Against.NullOrEmpty(imageUrl, nameof(imageUrl));

        var album = await GetAsync(albumId, cancellationToken);
        if (!album.IsSuccess || album.Data == null)
        {
            return new MelodeeModels.OperationResult<bool>("Unknown album")
            {
                Data = false
            };
        }

        var result = false;
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);
        try
        {
            var imageBytes = await httpClientFactory.BytesForImageUrlAsync(
                null, // ssrfValidator - will be null in test scenarios
                configuration.GetValue<string?>(SettingRegistry.SearchEngineUserAgent) ?? string.Empty,
                imageUrl,
                Logger,
                cancellationToken);
            if (imageBytes != null)
            {
                result = await SaveImageBytesAsAlbumImageAsync(
                    album.Data,
                    deleteAllImages,
                    imageBytes,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error attempting to download mage Url [{Url}] for album [{Album}]", imageUrl,
                album.Data.ToString());
        }

        return new MelodeeModels.OperationResult<bool>("An error has occured. OH NOES!")
        {
            Data = result
        };
    }

    private static IQueryable<Album> ApplyFilters(IQueryable<Album> query, MelodeeModels.PagedRequest pagedRequest)
    {
        if (pagedRequest.FilterBy == null || pagedRequest.FilterBy.Length == 0)
        {
            return query;
        }

        // If there's only one filter, apply it directly
        if (pagedRequest.FilterBy.Length == 1)
        {
            var filter = pagedRequest.FilterBy[0];
            var value = filter.Value.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                var normalizedValue = value.ToNormalizedString() ?? value;
                return filter.PropertyName.ToLowerInvariant() switch
                {
                    "name" or "namenormalized" => query.Where(a => a.NameNormalized.Contains(normalizedValue)),
                    "alternatenames" => query.Where(a => a.AlternateNames != null && a.AlternateNames.Contains(normalizedValue)),
                    "artistid" => int.TryParse(value, out var artistIdValue) ? query.Where(a => a.Artist.Id == artistIdValue) : query, // Use original value for int parsing
                    "artistapikey" => Guid.TryParse(value, out var apiKeyValue) ? query.Where(a => a.Artist.ApiKey == apiKeyValue) : query,
                    "artistname" => query.Where(a => a.Artist.NameNormalized.Contains(normalizedValue)),
                    "tags" => query.Where(a => a.Tags != null && a.Tags.Contains(normalizedValue)),
                    "albumstatus" => int.TryParse(value, out var statusValue)
                        ? query.Where(a => a.AlbumStatus == statusValue)
                        : query,
                    "islocked" => bool.TryParse(value, out var lockedValue)
                        ? query.Where(a => a.IsLocked == lockedValue)
                        : query,
                    "releasedate" => DateTime.TryParse(value, out var dateValue)
                        ? query.Where(a => a.ReleaseDate.Year == dateValue.Year)
                        : query,
                    "createdat" => ApplyCreatedAtFilter(query, filter),
                    _ => query
                };
            }

            return query;
        }

        // For multiple filters, respect the JoinOperator property
        var orGroupPredicates = new List<Expression<Func<Album, bool>>>();
        var andPredicates = new List<Expression<Func<Album, bool>>>();

        foreach (var filter in pagedRequest.FilterBy)
        {
            var value = filter.Value.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                var normalizedValue = value.ToNormalizedString();

                var predicate = filter.PropertyName.ToLowerInvariant() switch
                {
                    "name" or "namenormalized" => (Expression<Func<Album, bool>>)(a => a.NameNormalized.Contains(normalizedValue ?? "")),
                    "alternatenames" => (Expression<Func<Album, bool>>)(a => a.AlternateNames != null && a.AlternateNames.Contains(normalizedValue ?? "")),
                    "artistid" => int.TryParse(value, out var artistIdValue) ? (Expression<Func<Album, bool>>)(a => a.Artist.Id == artistIdValue) : null,
                    "artistapikey" => Guid.TryParse(value, out var apiKeyValue) ? (Expression<Func<Album, bool>>)(a => a.Artist.ApiKey == apiKeyValue) : null, // Use original value, not normalized
                    "artistname" => (Expression<Func<Album, bool>>)(a => a.Artist.NameNormalized.Contains(normalizedValue ?? "")),
                    "tags" => (Expression<Func<Album, bool>>)(a => a.Tags != null && a.Tags.Contains(normalizedValue ?? "")),
                    "albumstatus" => int.TryParse(value, out var statusValue)
                        ? (Expression<Func<Album, bool>>)(a => a.AlbumStatus == statusValue)
                        : null,
                    "islocked" => bool.TryParse(value, out var lockedValue)
                        ? (Expression<Func<Album, bool>>)(a => a.IsLocked == lockedValue)
                        : null,
                    "releasedate" => DateTime.TryParse(value, out var dateValue)
                        ? (Expression<Func<Album, bool>>)(a => a.ReleaseDate.Year == dateValue.Year)
                        : null,
                    "createdat" => GetCreatedAtPredicate(filter),
                    _ => null
                };

                if (predicate != null)
                {
                    // Group predicates by their join operator
                    if (filter.JoinOperator == "OR")
                    {
                        orGroupPredicates.Add(predicate);
                    }
                    else
                    {
                        andPredicates.Add(predicate);
                    }
                }
            }
        }

        // Combine OR group predicates first
        Expression<Func<Album, bool>>? orCombined = null;
        if (orGroupPredicates.Count > 0)
        {
            orCombined = orGroupPredicates.Aggregate((prev, next) =>
            {
                var parameter = Expression.Parameter(typeof(Album), "a");
                var left = Expression.Invoke(prev, parameter);
                var right = Expression.Invoke(next, parameter);
                var or = Expression.OrElse(left, right);
                return Expression.Lambda<Func<Album, bool>>(or, parameter);
            });
        }

        // Add the combined OR expression to AND predicates if it exists
        if (orCombined != null)
        {
            andPredicates.Add(orCombined);
        }

        // Apply all AND predicates
        if (andPredicates.Count > 0)
        {
            var finalPredicate = andPredicates.Aggregate((prev, next) =>
            {
                var parameter = Expression.Parameter(typeof(Album), "a");
                var left = Expression.Invoke(prev, parameter);
                var right = Expression.Invoke(next, parameter);
                var and = Expression.AndAlso(left, right);
                return Expression.Lambda<Func<Album, bool>>(and, parameter);
            });

            query = query.Where(finalPredicate);
        }
        return query;
    }

    private static IQueryable<Album> ApplyCreatedAtFilter(IQueryable<Album> query, FilterOperatorInfo filter)
    {
        if (filter.Value is not Instant instantValue)
        {
            return query;
        }

        return filter.Operator switch
        {
            FilterOperator.GreaterThanOrEquals => query.Where(a => a.CreatedAt >= instantValue),
            FilterOperator.GreaterThan => query.Where(a => a.CreatedAt > instantValue),
            FilterOperator.LessThanOrEquals => query.Where(a => a.CreatedAt <= instantValue),
            FilterOperator.LessThan => query.Where(a => a.CreatedAt < instantValue),
            FilterOperator.Equals => query.Where(a => a.CreatedAt == instantValue),
            _ => query
        };
    }

    private static Expression<Func<Album, bool>>? GetCreatedAtPredicate(FilterOperatorInfo filter)
    {
        if (filter.Value is not Instant instantValue)
        {
            return null;
        }

        return filter.Operator switch
        {
            FilterOperator.GreaterThanOrEquals => a => a.CreatedAt >= instantValue,
            FilterOperator.GreaterThan => a => a.CreatedAt > instantValue,
            FilterOperator.LessThanOrEquals => a => a.CreatedAt <= instantValue,
            FilterOperator.LessThan => a => a.CreatedAt < instantValue,
            FilterOperator.Equals => a => a.CreatedAt == instantValue,
            _ => null
        };
    }


    /// <summary>
    /// Get all genres from albums and songs for OpenSubsonic API
    /// </summary>
    public async Task<MelodeeModels.OperationResult<Dictionary<string, (int songCount, int albumCount)>>> GetGenresAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

        return await CacheManager.GetAsync(CacheKeyGenres, async () =>
        {
            var overallStopwatch = Stopwatch.StartNew();

            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            // Get all albums and songs with their genres using EF Core
            var dbStopwatch = Stopwatch.StartNew();
            var albums = await scopedContext.Albums
                .AsNoTracking()
                .Where(a => a.Genres != null && a.Genres.Length > 0)
                .Select(a => a.Genres)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            var songs = await scopedContext.Songs
                .AsNoTracking()
                .Where(s => s.Genres != null && s.Genres.Length > 0)
                .Select(s => s.Genres)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            dbStopwatch.Stop();

            // Flatten and collect all unique genres
            var genreCounts = new Dictionary<string, (int songCount, int albumCount)>();

            // Process album genres
            foreach (var albumGenres in albums.Where(g => g != null))
            {
                foreach (var genre in albumGenres!)
                {
                    if (genreCounts.TryGetValue(genre, out var current))
                    {
                        genreCounts[genre] = (current.songCount, current.albumCount + 1);
                    }
                    else
                    {
                        genreCounts[genre] = (0, 1);
                    }
                }
            }

            // Process song genres  
            foreach (var songGenres in songs.Where(g => g != null))
            {
                foreach (var genre in songGenres!)
                {
                    if (genreCounts.TryGetValue(genre, out var current))
                    {
                        genreCounts[genre] = (current.songCount + 1, current.albumCount);
                    }
                    else
                    {
                        genreCounts[genre] = (1, 0);
                    }
                }
            }

            overallStopwatch.Stop();
            Logger.Debug("GetGenresAsync MISS: DB: {DbMs}ms, Total: {TotalMs}ms, GenreCount: {GenreCount}",
                dbStopwatch.ElapsedMilliseconds, overallStopwatch.ElapsedMilliseconds, genreCounts.Count);

            return new MelodeeModels.OperationResult<Dictionary<string, (int songCount, int albumCount)>>
            {
                Data = genreCounts
            };
        }, cancellationToken, configuration.CacheDuration(), Album.CacheRegion);
    }

    public async Task<MelodeeModels.OperationResult<(long totalCount, AlbumList[] albums)>> GetAlbumListAsync(
        GetAlbumListRequest albumListRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        long totalCount = 0;
        AlbumList[] data = [];

        try
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var albumsQuery = scopedContext.Albums
                .Include(a => a.Artist).ThenInclude(x => x.Library)
                .Include(x => x.UserAlbums)
                .AsQueryable();

            // Filter by genre
            if (!string.IsNullOrWhiteSpace(albumListRequest.Genre))
            {
                albumsQuery = albumsQuery.Where(a => a.Genres != null && a.Genres.Contains(albumListRequest.Genre));
            }

            // Filter by year
            if (albumListRequest.FromYear.HasValue)
            {
                albumsQuery = albumsQuery.Where(a => a.ReleaseDate.Year >= albumListRequest.FromYear.Value);
            }

            if (albumListRequest.ToYear.HasValue)
            {
                albumsQuery = albumsQuery.Where(a => a.ReleaseDate.Year <= albumListRequest.ToYear.Value);
            }

            // Sorting
            switch (albumListRequest.Type)
            {
                case ListType.Random:
                    albumsQuery = albumsQuery.OrderBy(a => Guid.NewGuid());
                    break;
                case ListType.Newest:
                    albumsQuery = albumsQuery.OrderByDescending(a => a.CreatedAt);
                    break;
                case ListType.Highest:
                    albumsQuery = albumsQuery.OrderByDescending(a => a.CalculatedRating);
                    break;
                case ListType.Frequent:
                    albumsQuery = albumsQuery.OrderByDescending(a => a.PlayedCount);
                    break;
                case ListType.Recent:
                    albumsQuery = albumsQuery.OrderByDescending(a => a.ReleaseDate);
                    break;
                case ListType.AlphabeticalByName:
                    albumsQuery = albumsQuery.OrderBy(a => a.Name);
                    break;
                case ListType.AlphabeticalByArtist:
                    albumsQuery = albumsQuery.OrderBy(a => a.Artist.Name);
                    break;
                case ListType.Starred:
                    albumsQuery = albumsQuery.Where(a => a.UserAlbums.Any(ua => ua.UserId == userId && ua.IsStarred));
                    break;
                case ListType.ByYear:
                    // Already filtered above
                    break;
                case ListType.ByGenre:
                    // Already filtered above
                    break;
            }

            totalCount = await albumsQuery.CountAsync(cancellationToken);

            // Paging
            albumsQuery = albumsQuery
                .Skip(albumListRequest.OffsetValue)
                .Take(albumListRequest.SizeValue);

            var albums = await albumsQuery.ToListAsync(cancellationToken);

            data = albums.Select(a => new AlbumList
            {
                Id = a.ToApiKey(),
                Album = a.Name,
                Title = a.Name,
                Name = a.Name,
                CoverArt = a.ToCoverArtId(),
                SongCount = a.SongCount ?? 0,
                CreatedRaw = a.CreatedAt,
                Duration = SafeParser.ToNumber<int>(a.Duration / 1000),
                PlayedCount = a.PlayedCount,
                ArtistId = a.Artist.ToApiKey(),
                Artist = a.Artist.Name,
                Year = a.ReleaseDate.Year,
                Genres = a.Genres,
                UserRating = a.UserAlbums
                                 .FirstOrDefault(ua => ua.UserId == userId)
                                 ?.Rating ??
                             0,
                AverageRating = a.CalculatedRating,
                Parent = a.Artist.Library.ToApiKey()
            }).ToArray();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to get AlbumList Request");
            return new MelodeeModels.OperationResult<(long totalCount, AlbumList[] albums)>("Failed to get AlbumList")
            {
                Type = MelodeeModels.OperationResponseType.Error,
                Data = (0, [])
            };
        }

        return new MelodeeModels.OperationResult<(long totalCount, AlbumList[] albums)>
        {
            Data = (totalCount, data)
        };
    }

    public async Task<MelodeeModels.OperationResult<(long totalCount, AlbumList2[] albums)>> GetAlbumList2Async(
        GetAlbumListRequest albumListRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        long totalCount = 0;
        AlbumList2[] data = [];

        try
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            var albumsQuery = scopedContext.Albums
                .Include(a => a.Artist).ThenInclude(x => x.Library)
                .Include(a => a.UserAlbums)
                .AsQueryable();

            // Filter by genre
            if (!string.IsNullOrWhiteSpace(albumListRequest.Genre))
            {
                albumsQuery = albumsQuery.Where(a => a.Genres != null && a.Genres.Contains(albumListRequest.Genre));
            }

            // Filter by year
            if (albumListRequest.FromYear.HasValue)
            {
                albumsQuery = albumsQuery.Where(a => a.ReleaseDate.Year >= albumListRequest.FromYear.Value);
            }

            if (albumListRequest.ToYear.HasValue)
            {
                albumsQuery = albumsQuery.Where(a => a.ReleaseDate.Year <= albumListRequest.ToYear.Value);
            }

            // Starred filter
            if (albumListRequest.Type == ListType.Starred)
            {
                albumsQuery = albumsQuery.Where(a => a.UserAlbums.Any(ua => ua.UserId == userId && ua.IsStarred));
            }

            // Sorting
            switch (albumListRequest.Type)
            {
                case ListType.Random:
                    albumsQuery = albumsQuery.OrderBy(a => Guid.NewGuid());
                    break;
                case ListType.Newest:
                    albumsQuery = albumsQuery.OrderByDescending(a => a.CreatedAt);
                    break;
                case ListType.Highest:
                    albumsQuery = albumsQuery.OrderByDescending(a => a.CalculatedRating);
                    break;
                case ListType.Frequent:
                    albumsQuery = albumsQuery.OrderByDescending(a => a.PlayedCount);
                    break;
                case ListType.Recent:
                    albumsQuery = albumsQuery.OrderByDescending(a => a.ReleaseDate);
                    break;
                case ListType.AlphabeticalByName:
                    albumsQuery = albumsQuery.OrderBy(a => a.Name);
                    break;
                case ListType.AlphabeticalByArtist:
                    albumsQuery = albumsQuery.OrderBy(a => a.Artist.Name);
                    break;
                    // ByYear and ByGenre already filtered above
            }

            totalCount = await albumsQuery.CountAsync(cancellationToken);

            // Paging
            albumsQuery = albumsQuery
                .Skip(albumListRequest.OffsetValue)
                .Take(albumListRequest.SizeValue);

            var albums = await albumsQuery.ToListAsync(cancellationToken);

            data = albums.Select(a => new AlbumList2
            {
                Id = a.ToApiKey(),
                Album = a.Name,
                Title = a.Name,
                Name = a.Name,
                CoverArt = a.ToCoverArtId(),
                SongCount = a.SongCount ?? 0,
                CreatedRaw = a.CreatedAt,
                Duration = SafeParser.ToNumber<int>(a.Duration / 1000),
                PlayedCount = a.PlayedCount,
                ArtistId = a.Artist.ToApiKey(),
                Artist = a.Artist.Name,
                Year = a.ReleaseDate.Year,
                Genres = a.Genres,
                UserRating = a.UserAlbums
                                 .FirstOrDefault(ua => ua.UserId == userId)
                                 ?.Rating ??
                             0,
                Parent = a.Artist.Library.ToApiKey()
            }).ToArray();
        }
        catch (Exception e)
        {
            Logger.Error(e, "Failed to get AlbumList2 LINQ Request");
            return new MelodeeModels.OperationResult<(long totalCount, AlbumList2[] albums)>("Failed to get AlbumList2")
            {
                Type = MelodeeModels.OperationResponseType.Error,
                Data = (0, [])
            };
        }

        return new MelodeeModels.OperationResult<(long totalCount, AlbumList2[] albums)>
        {
            Data = (totalCount, data)
        };
    }

    /// <summary>
    /// List starred albums for a user with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<AlbumDataInfo>> ListStarredAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserAlbums
            .Where(ua => ua.UserId == userId && ua.IsStarred)
            .Include(ua => ua.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var albumCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        AlbumDataInfo[] albums = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserAlbums = await baseQuery
                .OrderByDescending(ua => ua.StarredAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            albums = rawUserAlbums.Select(ua => new AlbumDataInfo(
                ua.Album.Id,
                ua.Album.ApiKey,
                ua.Album.IsLocked,
                ua.Album.Name,
                ua.Album.NameNormalized,
                ua.Album.AlternateNames,
                ua.Album.Artist.ApiKey,
                ua.Album.Artist.Name,
                ua.Album.SongCount ?? 0,
                ua.Album.Duration,
                ua.Album.CreatedAt,
                ua.Album.Tags,
                ua.Album.ReleaseDate,
                (short)ua.Album.AlbumStatus,
                ua.Album.LastPlayedAt,
                ua.Album.PlayedCount,
                ua.Album.CalculatedRating
            )
            {
                UserStarred = ua.IsStarred,
                UserRating = ua.Rating
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<AlbumDataInfo>
        {
            TotalCount = albumCount,
            TotalPages = pagedRequest.TotalPages(albumCount),
            Data = albums
        };
    }

    /// <summary>
    /// List hated albums for a user with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<AlbumDataInfo>> ListHatedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserAlbums
            .Where(ua => ua.UserId == userId && ua.IsHated)
            .Include(ua => ua.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var albumCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        AlbumDataInfo[] albums = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserAlbums = await baseQuery
                .OrderByDescending(ua => ua.LastUpdatedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            albums = rawUserAlbums.Select(ua => new AlbumDataInfo(
                ua.Album.Id,
                ua.Album.ApiKey,
                ua.Album.IsLocked,
                ua.Album.Name,
                ua.Album.NameNormalized,
                ua.Album.AlternateNames,
                ua.Album.Artist.ApiKey,
                ua.Album.Artist.Name,
                ua.Album.SongCount ?? 0,
                ua.Album.Duration,
                ua.Album.CreatedAt,
                ua.Album.Tags,
                ua.Album.ReleaseDate,
                (short)ua.Album.AlbumStatus,
                ua.Album.LastPlayedAt,
                ua.Album.PlayedCount,
                ua.Album.CalculatedRating
            )
            {
                UserStarred = ua.IsStarred,
                UserRating = ua.Rating
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<AlbumDataInfo>
        {
            TotalCount = albumCount,
            TotalPages = pagedRequest.TotalPages(albumCount),
            Data = albums
        };
    }

    /// <summary>
    /// List top-rated albums (4+ stars) for a user with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<AlbumDataInfo>> ListTopRatedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        int minRating = 4,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserAlbums
            .Where(ua => ua.UserId == userId && ua.Rating >= minRating)
            .Include(ua => ua.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var albumCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        AlbumDataInfo[] albums = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserAlbums = await baseQuery
                .OrderByDescending(ua => ua.Rating)
                .ThenByDescending(ua => ua.LastUpdatedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            albums = rawUserAlbums.Select(ua => new AlbumDataInfo(
                ua.Album.Id,
                ua.Album.ApiKey,
                ua.Album.IsLocked,
                ua.Album.Name,
                ua.Album.NameNormalized,
                ua.Album.AlternateNames,
                ua.Album.Artist.ApiKey,
                ua.Album.Artist.Name,
                ua.Album.SongCount ?? 0,
                ua.Album.Duration,
                ua.Album.CreatedAt,
                ua.Album.Tags,
                ua.Album.ReleaseDate,
                (short)ua.Album.AlbumStatus,
                ua.Album.LastPlayedAt,
                ua.Album.PlayedCount,
                ua.Album.CalculatedRating
            )
            {
                UserStarred = ua.IsStarred,
                UserRating = ua.Rating
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<AlbumDataInfo>
        {
            TotalCount = albumCount,
            TotalPages = pagedRequest.TotalPages(albumCount),
            Data = albums
        };
    }

    /// <summary>
    /// List all rated albums for a user with pagination, sorted by rating descending
    /// </summary>
    public async Task<MelodeeModels.PagedResult<AlbumDataInfo>> ListRatedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserAlbums
            .Where(ua => ua.UserId == userId && ua.Rating > 0)
            .Include(ua => ua.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var albumCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        AlbumDataInfo[] albums = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserAlbums = await baseQuery
                .OrderByDescending(ua => ua.Rating)
                .ThenByDescending(ua => ua.LastUpdatedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            albums = rawUserAlbums.Select(ua => new AlbumDataInfo(
                ua.Album.Id,
                ua.Album.ApiKey,
                ua.Album.IsLocked,
                ua.Album.Name,
                ua.Album.NameNormalized,
                ua.Album.AlternateNames,
                ua.Album.Artist.ApiKey,
                ua.Album.Artist.Name,
                ua.Album.SongCount ?? 0,
                ua.Album.Duration,
                ua.Album.CreatedAt,
                ua.Album.Tags,
                ua.Album.ReleaseDate,
                (short)ua.Album.AlbumStatus,
                ua.Album.LastPlayedAt,
                ua.Album.PlayedCount,
                ua.Album.CalculatedRating
            )
            {
                UserStarred = ua.IsStarred,
                UserRating = ua.Rating
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<AlbumDataInfo>
        {
            TotalCount = albumCount,
            TotalPages = pagedRequest.TotalPages(albumCount),
            Data = albums
        };
    }

    /// <summary>
    /// List recently played albums for a user with pagination, sorted by last played descending
    /// </summary>
    public async Task<MelodeeModels.PagedResult<AlbumDataInfo>> ListRecentlyPlayedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserAlbums
            .Where(ua => ua.UserId == userId && ua.LastPlayedAt != null)
            .Include(ua => ua.Album)
            .ThenInclude(a => a.Artist)
            .AsNoTracking();

        var albumCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        AlbumDataInfo[] albums = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserAlbums = await baseQuery
                .OrderByDescending(ua => ua.LastPlayedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            albums = rawUserAlbums.Select(ua => new AlbumDataInfo(
                ua.Album.Id,
                ua.Album.ApiKey,
                ua.Album.IsLocked,
                ua.Album.Name,
                ua.Album.NameNormalized,
                ua.Album.AlternateNames,
                ua.Album.Artist.ApiKey,
                ua.Album.Artist.Name,
                ua.Album.SongCount ?? 0,
                ua.Album.Duration,
                ua.Album.CreatedAt,
                ua.Album.Tags,
                ua.Album.ReleaseDate,
                (short)ua.Album.AlbumStatus,
                ua.LastPlayedAt,
                ua.PlayedCount,
                ua.Album.CalculatedRating
            )
            {
                UserStarred = ua.IsStarred,
                UserRating = ua.Rating
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<AlbumDataInfo>
        {
            TotalCount = albumCount,
            TotalPages = pagedRequest.TotalPages(albumCount),
            Data = albums
        };
    }

    public async Task<MelodeeModels.OperationResult<Album[]>> ListByGenreAsync(
        string[] genres,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (genres == null || genres.Length == 0)
        {
            return new MelodeeModels.OperationResult<Album[]> { Data = [] };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var normalizedGenres = genres.Select(g => g.ToUpperInvariant()).ToArray();

        var albums = await scopedContext.Albums
            .AsNoTracking()
            .Include(a => a.Artist)
            .Where(a => a.Genres != null && a.Genres.Any(g => normalizedGenres.Contains(g.ToUpper())))
            .OrderByDescending(a => a.PlayedCount)
            .ThenBy(a => a.Name)
            .Take(limit)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<Album[]> { Data = albums };
    }
}
