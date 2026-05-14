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
using Melodee.Common.Imaging;
using Melodee.Common.MessageBus.Events;
using Melodee.Common.Models.Collection;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Extensions;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Rebus.Bus;
using Serilog;
using SmartFormat;
using MelodeeModels = Melodee.Common.Models;

namespace Melodee.Common.Services;

public class ArtistService(
    ILogger logger,
    ICacheManager cacheManager,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    ISerializer serializer,
    IHttpClientFactory httpClientFactory,
    IImageProcessor imageProcessor,
    AlbumService albumService,
    IBus bus,
    IFileSystemService fileSystemService)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string CacheKeyDetailByApiKeyTemplate = "urn:artist:apikey:{0}";
    private const string CacheKeyDetailByNameNormalizedTemplate = "urn:artist:namenormalized:{0}";
    private const string CacheKeyDetailByMusicBrainzIdTemplate = "urn:artist:musicbrainzid:{0}";
    private const string CacheKeyDetailTemplate = "urn:artist:{0}";
    private const string CacheKeyArtistImageBytesAndEtagTemplate = "urn:artist:imagebytesandetag:{0}:{1}";

    /// <summary>
    /// Duration tolerance in milliseconds for considering songs as equal during merge operations.
    /// Songs with duration difference within this threshold are considered identical.
    /// </summary>
    private const double SongDurationToleranceMs = 1000;

    public async Task<MelodeeModels.PagedResult<ArtistDataInfo>> ListAsync(
        MelodeeModels.PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Build the base query with performance optimizations
        var baseQuery = scopedContext.Artists
            .AsNoTracking()
            .Include(a => a.Library);

        // Apply filters using EF Core instead of raw SQL
        var filteredQuery = ApplyFilters(baseQuery, pagedRequest);

        // Get count efficiently
        var artistCount = await filteredQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        ArtistDataInfo[] artists = [];
        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            // Apply ordering, skip, and take with projection to ArtistDataInfo
            var orderedQuery = ApplyOrdering(filteredQuery, pagedRequest);

            artists = await orderedQuery
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .Select(a => new ArtistDataInfo(
                    a.Id,
                    a.ApiKey,
                    a.IsLocked,
                    a.LibraryId,
                    a.Library.Path, // LibraryPath
                    a.Name,
                    a.NameNormalized,
                    a.AlternateNames ?? string.Empty,
                    a.Directory,
                    a.AlbumCount,
                    a.SongCount,
                    a.CreatedAt,
                    a.Tags ?? string.Empty,
                    a.LastUpdatedAt,
                    a.LastPlayedAt,
                    a.PlayedCount,
                    a.CalculatedRating))
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return new MelodeeModels.PagedResult<ArtistDataInfo>
        {
            TotalCount = artistCount,
            TotalPages = pagedRequest.TotalPages(artistCount),
            Data = artists
        };
    }

    private static IQueryable<Artist> ApplyFilters(IQueryable<Artist> query, MelodeeModels.PagedRequest pagedRequest)
    {
        if (pagedRequest.FilterBy == null || pagedRequest.FilterBy.Length == 0)
        {
            return query;
        }

        // If there's only one filter, apply it directly
        if (pagedRequest.FilterBy.Length == 1)
        {
            var filter = pagedRequest.FilterBy[0];
            var filterValue = filter.Value.ToString().ToNormalizedString() ?? string.Empty;

            return filter.PropertyName.ToLowerInvariant() switch
            {
                "name" or "namenormalized" => filter.Operator switch
                {
                    FilterOperator.Contains => query.Where(a => a.NameNormalized.Contains(filterValue)),
                    FilterOperator.Equals => query.Where(a => a.NameNormalized == filterValue),
                    FilterOperator.StartsWith => query.Where(a => a.NameNormalized.StartsWith(filterValue)),
                    _ => query
                },
                "alternatenames" => filter.Operator switch
                {
                    FilterOperator.Contains => query.Where(a => a.AlternateNames != null && a.AlternateNames.Contains(filterValue)),
                    _ => query
                },
                "islocked" => filter.Operator switch
                {
                    FilterOperator.Equals when bool.TryParse(filterValue, out var boolValue) =>
                        query.Where(a => a.IsLocked == boolValue),
                    _ => query
                },
                "createdat" => ApplyCreatedAtFilter(query, filter),
                _ => query
            };
        }

        // For multiple filters, combine them with OR logic
        var filterPredicates = new List<Expression<Func<Artist, bool>>>();

        foreach (var filter in pagedRequest.FilterBy)
        {
            var filterValue = filter.Value.ToString().ToNormalizedString() ?? string.Empty;

            var predicate = filter.PropertyName.ToLowerInvariant() switch
            {
                "name" or "namenormalized" => filter.Operator switch
                {
                    FilterOperator.Contains => (Expression<Func<Artist, bool>>)(a => a.NameNormalized.Contains(filterValue)),
                    FilterOperator.Equals => (Expression<Func<Artist, bool>>)(a => a.NameNormalized == filterValue),
                    FilterOperator.StartsWith => (Expression<Func<Artist, bool>>)(a => a.NameNormalized.StartsWith(filterValue)),
                    _ => null
                },
                "alternatenames" => filter.Operator switch
                {
                    FilterOperator.Contains => (Expression<Func<Artist, bool>>)(a => a.AlternateNames != null && a.AlternateNames.Contains(filterValue)),
                    _ => null
                },
                "islocked" => filter.Operator switch
                {
                    FilterOperator.Equals when bool.TryParse(filterValue, out var boolValue) =>
                        (Expression<Func<Artist, bool>>)(a => a.IsLocked == boolValue),
                    _ => null
                },
                "createdat" => GetCreatedAtPredicate(filter),
                _ => null
            };

            if (predicate != null)
            {
                filterPredicates.Add(predicate);
            }
        }

        // If we have predicates, combine them with OR logic
        if (filterPredicates.Count > 0)
        {
            var combinedPredicate = filterPredicates.Aggregate((prev, next) =>
            {
                var parameter = Expression.Parameter(typeof(Artist), "a");
                var left = Expression.Invoke(prev, parameter);
                var right = Expression.Invoke(next, parameter);
                var or = Expression.OrElse(left, right);
                return Expression.Lambda<Func<Artist, bool>>(or, parameter);
            });

            query = query.Where(combinedPredicate);
        }

        return query;
    }

    private static IQueryable<Artist> ApplyCreatedAtFilter(IQueryable<Artist> query, FilterOperatorInfo filter)
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

    private static Expression<Func<Artist, bool>>? GetCreatedAtPredicate(FilterOperatorInfo filter)
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

    private static IQueryable<Artist> ApplyOrdering(IQueryable<Artist> query, MelodeeModels.PagedRequest pagedRequest)
    {
        // Use the existing OrderByValue method from PagedRequest
        var orderByClause = pagedRequest.OrderByValue("Name", MelodeeModels.PagedRequest.OrderAscDirection);

        // Parse the order by clause to determine field and direction
        var isDescending = orderByClause.Contains("DESC", StringComparison.OrdinalIgnoreCase);
        var fieldName = orderByClause.Split(' ')[0].Trim('"').ToLowerInvariant();

        return fieldName switch
        {
            "alternatenames" => isDescending ? query.OrderByDescending(a => a.AlternateNames) : query.OrderBy(a => a.AlternateNames),
            "name" or "namenormalized" => isDescending ? query.OrderByDescending(a => a.SortName).ThenByDescending(x => x.Name) : query.OrderBy(a => a.SortName).ThenBy(x => x.Name),
            "createdat" => isDescending ? query.OrderByDescending(a => a.CreatedAt) : query.OrderBy(a => a.CreatedAt),
            "lastupdatedat" => isDescending ? query.OrderByDescending(a => a.LastUpdatedAt) : query.OrderBy(a => a.LastUpdatedAt),
            "albumcount" => isDescending ? query.OrderByDescending(a => a.AlbumCount) : query.OrderBy(a => a.AlbumCount),
            "directory" => isDescending ? query.OrderByDescending(a => a.Directory) : query.OrderBy(a => a.Directory),
            "songcount" => isDescending ? query.OrderByDescending(a => a.SongCount) : query.OrderBy(a => a.SongCount),
            "lastplayedat" => isDescending ? query.OrderByDescending(a => a.LastPlayedAt) : query.OrderBy(a => a.LastPlayedAt),
            "playedcount" => isDescending ? query.OrderByDescending(a => a.PlayedCount) : query.OrderBy(a => a.PlayedCount),
            "calculatedrating" => isDescending ? query.OrderByDescending(a => a.CalculatedRating) : query.OrderBy(a => a.CalculatedRating),
            _ => query.OrderBy(a => a.Name)
        };
    }

    public async Task<MelodeeModels.OperationResult<Artist?>> GetAsync(
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
                    .Artists
                    .Include(x => x.Library)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                    .ConfigureAwait(false);
            }
        }, cancellationToken, region: Artist.CacheRegion);
        return new MelodeeModels.OperationResult<Artist?>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<Artist?>> GetByMusicBrainzIdAsync(
        Guid musicBrainzId,
        CancellationToken cancellationToken = default)
    {
        var id = await CacheManager.GetAsync(
            CacheKeyDetailByMusicBrainzIdTemplate.FormatSmart(musicBrainzId.ToString()), async () =>
            {
                await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

                return await scopedContext.Artists
                    .AsNoTracking()
                    .Where(a => a.MusicBrainzId == musicBrainzId)
                    .Select(a => (int?)a.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken, region: Artist.CacheRegion);

        if (id == null)
        {
            return new MelodeeModels.OperationResult<Artist?>("Unknown artist.")
            {
                Data = null
            };
        }

        return await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<Artist?>> GetByNameNormalized(
        string nameNormalized,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(nameNormalized, nameof(nameNormalized));

        var id = await CacheManager.GetAsync(CacheKeyDetailByNameNormalizedTemplate.FormatSmart(nameNormalized),
            async () =>
            {
                await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

                return await scopedContext.Artists
                    .AsNoTracking()
                    .Where(a => a.NameNormalized == nameNormalized)
                    .Select(a => (int?)a.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken, region: Artist.CacheRegion);

        if (id == null)
        {
            return new MelodeeModels.OperationResult<Artist?>("Unknown artist.")
            {
                Data = null
            };
        }

        return await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    ///     Find the Artist using various given Ids.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<Artist?>> FindArtistAsync(
        int? byId,
        Guid byApiKey,
        string? byName,
        Guid? byMusicBrainzId,
        string? bySpotifyId,
        CancellationToken cancellationToken = default)
    {
        int? id = null;

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Try to find by ID first (most efficient)
            if (byId.HasValue)
            {
                id = await scopedContext.Artists
                    .AsNoTracking()
                    .Where(a => a.Id == byId.Value)
                    .Select(a => (int?)a.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            // Try to find by API key
            if (id == null && byApiKey != Guid.Empty)
            {
                id = await scopedContext.Artists
                    .AsNoTracking()
                    .Where(a => a.ApiKey == byApiKey)
                    .Select(a => (int?)a.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            // Try to find by MusicBrainz ID or Spotify ID
            if (id == null && (byMusicBrainzId != null || bySpotifyId != null))
            {
                id = await scopedContext.Artists
                    .AsNoTracking()
                    .Where(a => (byMusicBrainzId != null && a.MusicBrainzId == byMusicBrainzId) ||
                                (bySpotifyId != null && a.SpotifyId == bySpotifyId))
                    .Select(a => (int?)a.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            // Finally try to find by normalized name
            if (id == null && !string.IsNullOrEmpty(byName))
            {
                id = await scopedContext.Artists
                    .AsNoTracking()
                    .Where(a => a.NameNormalized == byName)
                    .Select(a => (int?)a.Id)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e,
                "[{ServiceName}] attempting to Find Artist id [{Id}], apiKey [{ApiKey}], name [{Name}] musicbrainzId [{MbId}] spotifyId [{SpotifyId}]",
                nameof(ArtistService),
                byId,
                byApiKey,
                byName,
                byMusicBrainzId,
                bySpotifyId);
        }

        if (id == null)
        {
            return new MelodeeModels.OperationResult<Artist?>("Unknown artist.")
            {
                Data = null
            };
        }

        return await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<Artist?>> GetByApiKeyAsync(
        Guid apiKey,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x == Guid.Empty, apiKey, nameof(apiKey));

        var id = await CacheManager.GetAsync(CacheKeyDetailByApiKeyTemplate.FormatSmart(apiKey), async () =>
        {
            await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            return await scopedContext.Artists
                .AsNoTracking()
                .Where(a => a.ApiKey == apiKey)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }, cancellationToken, region: Artist.CacheRegion);

        if (id == null)
        {
            return new MelodeeModels.OperationResult<Artist?>("Unknown artist.")
            {
                Data = null
            };
        }

        return await GetAsync(id.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearCacheAsync(Artist artist, CancellationToken cancellationToken)
    {
        CacheManager.Remove(CacheKeyDetailByApiKeyTemplate.FormatSmart(artist.ApiKey), Artist.CacheRegion);
        CacheManager.Remove(CacheKeyDetailByNameNormalizedTemplate.FormatSmart(artist.NameNormalized), Artist.CacheRegion);
        CacheManager.Remove(CacheKeyDetailTemplate.FormatSmart(artist.Id), Artist.CacheRegion);
        if (artist.MusicBrainzId != null)
        {
            CacheManager.Remove(CacheKeyDetailByMusicBrainzIdTemplate.FormatSmart(artist.MusicBrainzId.Value.ToString()), Artist.CacheRegion);
        }

        CacheManager.Remove(CacheKeyArtistImageBytesAndEtagTemplate.FormatSmart(artist.ApiKey, ImageSize.Thumbnail), Artist.CacheRegion);
        CacheManager.Remove(CacheKeyArtistImageBytesAndEtagTemplate.FormatSmart(artist.ApiKey, ImageSize.Small), Artist.CacheRegion);
        CacheManager.Remove(CacheKeyArtistImageBytesAndEtagTemplate.FormatSmart(artist.ApiKey, ImageSize.Medium), Artist.CacheRegion);
        CacheManager.Remove(CacheKeyArtistImageBytesAndEtagTemplate.FormatSmart(artist.ApiKey, ImageSize.Large), Artist.CacheRegion);

        await albumService.ClearCacheForArtist(artist.Id, cancellationToken);
    }

    public async Task ClearCacheAsync(int artistId, CancellationToken cancellationToken)
    {
        var artist = await GetAsync(artistId, cancellationToken).ConfigureAwait(false);
        await ClearCacheAsync(artist.Data!, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<bool>> RescanAsync(int[] artistIds, CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(artistIds, nameof(artistIds));

        foreach (var artistId in artistIds)
        {
            var artistResult = await GetAsync(artistId, cancellationToken).ConfigureAwait(false);
            if (!artistResult.IsSuccess || artistResult.Data == null)
            {
                return new MelodeeModels.OperationResult<bool>("Unknown artist.")
                {
                    Data = false
                };
            }

            await bus.SendLocal(new ArtistRescanEvent(artistResult.Data.Id,
                    Path.Combine(artistResult.Data.Library.Path,
                        artistResult.Data.Directory)))
                .ConfigureAwait(false);
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = true
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(
        int[] artistIds,
        CancellationToken cancellationToken = default)
    {
        return await DeleteAsync(artistIds, deleteFiles: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAsync(
        int[] artistIds,
        bool deleteFiles,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(artistIds, nameof(artistIds));

        bool result;

        var libraryIds = new List<int>();

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var artistId in artistIds)
            {
                var artist = await GetAsync(artistId, cancellationToken).ConfigureAwait(false);
                if (!artist.IsSuccess)
                {
                    return new MelodeeModels.OperationResult<bool>("Unknown artist.")
                    {
                        Data = false
                    };
                }
            }

            foreach (var artistId in artistIds)
            {
                var artist = await scopedContext
                    .Artists.Include(x => x.Library)
                    .FirstAsync(x => x.Id == artistId, cancellationToken)
                    .ConfigureAwait(false);

                if (deleteFiles)
                {
                    var artistDirectory = Path.Combine(artist.Library.Path, artist.Directory);
                    if (fileSystemService.DirectoryExists(artistDirectory))
                    {
                        fileSystemService.DeleteDirectory(artistDirectory, true);
                    }
                }

                var artistContributors = await scopedContext.Contributors.Where(x => x.ArtistId == artistId)
                    .ToListAsync(cancellationToken).ConfigureAwait(false);
                if (artistContributors.Count > 0)
                {
                    foreach (var artistContributor in artistContributors)
                    {
                        scopedContext.Contributors.Remove(artistContributor);
                    }
                }

                scopedContext.Artists.Remove(artist);
                libraryIds.Add(artist.LibraryId);
            }

            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            foreach (var libraryId in libraryIds.Distinct())
            {
                await UpdateLibraryAggregateStatsByIdAsync(libraryId, cancellationToken).ConfigureAwait(false);
            }

            Logger.Information("Deleted artists [{ArtistIds}] (files deleted: {DeleteFiles}).", artistIds, deleteFiles);
            result = true;
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> UpdateAsync(
        Artist artist,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(artist, nameof(artist));

        var validationResult = ValidateModel(artist);
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
        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var dbDetail = await scopedContext
                .Artists
                .Include(x => x.Library)
                .FirstOrDefaultAsync(x => x.Id == artist.Id, cancellationToken)
                .ConfigureAwait(false);

            if (dbDetail == null)
            {
                return new MelodeeModels.OperationResult<bool>
                {
                    Data = false,
                    Type = MelodeeModels.OperationResponseType.NotFound
                };
            }

            dbDetail.Directory = artist.Directory;

            var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);

            var newArtistDirectory = artist.ToMelodeeArtistModel().ToDirectoryName(configuration.GetValue<int>(SettingRegistry.ProcessingMaximumArtistDirectoryNameLength));
            var newDirectory = Path.Combine(dbDetail.Library.Path, newArtistDirectory);
            var originalDirectoryPath = Path.Combine(dbDetail.Library.Path, dbDetail.Directory);

            // Check if we need to move the directory
            if (originalDirectoryPath != newDirectory)
            {
                fileSystemService.MoveDirectory(originalDirectoryPath, newDirectory);
                dbDetail.Directory = newArtistDirectory;
            }

            dbDetail.AlternateNames = artist.AlternateNames;
            dbDetail.AmgId = artist.AmgId;
            dbDetail.Biography = artist.Biography.Nullify();
            dbDetail.Description = artist.Description;

            dbDetail.DeezerId = artist.DeezerId;
            dbDetail.DiscogsId = artist.DiscogsId;
            dbDetail.ImageCount = artist.ImageCount;
            dbDetail.IsLocked = artist.IsLocked;
            dbDetail.ItunesId = artist.ItunesId;
            dbDetail.LastFmId = artist.LastFmId;
            dbDetail.LastUpdatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
            dbDetail.LibraryId = artist.LibraryId;
            dbDetail.MusicBrainzId = artist.MusicBrainzId;
            dbDetail.Name = artist.Name;
            dbDetail.NameNormalized = artist.NameNormalized;
            dbDetail.Notes = artist.Notes;
            dbDetail.RealName = artist.RealName;
            dbDetail.Roles = artist.Roles;
            dbDetail.SortName = artist.SortName;
            dbDetail.SortOrder = artist.SortOrder;
            dbDetail.SpotifyId = artist.SpotifyId;
            dbDetail.Tags = artist.Tags;
            dbDetail.WikiDataId = artist.WikiDataId;

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

            if (result)
            {
                await ClearCacheAsync(dbDetail, cancellationToken).ConfigureAwait(false);
            }
        }


        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<MelodeeModels.OperationResult<Artist?>> AddArtistAsync(
        Artist artist,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(artist, nameof(artist));

        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);

        artist.ApiKey = Guid.NewGuid();
        artist.Directory = artist.ToMelodeeArtistModel()
            .ToDirectoryName(configuration.GetValue<int>(SettingRegistry.ProcessingMaximumArtistDirectoryNameLength));
        artist.CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow);
        artist.MetaDataStatus = (int)MetaDataModelStatus.ReadyToProcess;
        artist.NameNormalized = artist.NameNormalized.Nullify() ?? artist.Name.ToNormalizedString() ?? artist.Name;

        var validationResult = ValidateModel(artist);
        if (!validationResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<Artist?>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage)).Select(x => x.ErrorMessage!).ToArray() ?? [])
            {
                Data = null,
                Type = MelodeeModels.OperationResponseType.ValidationFailure
            };
        }

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            scopedContext.Artists.Add(artist);
            var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            if (result > 0)
            {
                await UpdateLibraryAggregateStatsByIdAsync(artist.LibraryId, cancellationToken).ConfigureAwait(false);
            }
        }

        return await GetAsync(artist.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<MelodeeModels.OperationResult<bool>> SaveImageAsArtistImageAsync(
        int artistId,
        bool deleteAllImages,
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));
        Guard.Against.NullOrEmpty(imageBytes, nameof(imageBytes));

        var artist = await GetAsync(artistId, cancellationToken);
        if (!artist.IsSuccess || artist.Data == null)
        {
            return new MelodeeModels.OperationResult<bool>("Unknown artist.")
            {
                Data = false
            };
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = await SaveImageBytesAsArtistImageAsync(
                    artist.Data,
                    deleteAllImages,
                    imageBytes,
                    cancellationToken)
                .ConfigureAwait(false)
        };
    }

    private async Task<bool> SaveImageBytesAsArtistImageAsync(
        Artist artist,
        bool deleteAllImages,
        byte[] imageBytes,
        CancellationToken cancellationToken = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);
        var imageConvertor = new ImageConvertor(imageProcessor, configuration);
        var artistDirectory = artist.ToFileSystemDirectoryInfo();
        var artistImages = artistDirectory.FileInfosForExtension("jpg", false).ToArray();
        if (deleteAllImages && artistImages.Length != 0)
        {
            foreach (var fileInAlbumDirectory in artistImages)
            {
                fileInAlbumDirectory.Delete();
            }

            artistImages = artistDirectory.FileInfosForExtension("jpg", false).ToArray();
        }

        var totalArtistImageCount = artistImages.Length == 1 ? 1 : artistImages.Length + 1;
        var artistImageFileName = Path.Combine(artistDirectory.Path, deleteAllImages ? "01-Band.image" : $"{totalArtistImageCount}-Band.image");
        var artistImageFileInfo = new FileInfo(artistImageFileName).ToFileSystemInfo();

        await fileSystemService.WriteAllBytesAsync(artistImageFileInfo.FullName(artistDirectory), imageBytes, cancellationToken).ConfigureAwait(false);
        await imageConvertor.ProcessFileAsync(
            artistDirectory,
            artistImageFileInfo,
            cancellationToken).ConfigureAwait(false);
        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            await scopedContext.Artists
                .Where(x => x.Id == artist.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LastUpdatedAt, now)
                    .SetProperty(x => x.ImageCount, totalArtistImageCount), cancellationToken)
                .ConfigureAwait(false);
        }

        await ClearCacheAsync(artist, cancellationToken).ConfigureAwait(false);
        Logger.Information("Saved image for artist [{ArtistId}] with {ImageCount} images.",
            artist.Id, totalArtistImageCount);
        return true;
    }

    public async Task<MelodeeModels.OperationResult<bool>> SaveImageUrlAsArtistImageAsync(
        int artistId,
        string imageUrl,
        bool deleteAllImages,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));
        Guard.Against.NullOrEmpty(imageUrl, nameof(imageUrl));

        var artist = await GetAsync(artistId, cancellationToken);
        if (!artist.IsSuccess || artist.Data == null)
        {
            return new MelodeeModels.OperationResult<bool>("Unknown artist.")
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
                cancellationToken).ConfigureAwait(false);
            if (imageBytes != null)
            {
                result = await SaveImageBytesAsArtistImageAsync(
                    artist.Data,
                    deleteAllImages,
                    imageBytes,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Error attempting to download mage Url [{Url}] for artist [{Artist}]", imageUrl,
                artist.Data.ToString());
        }

        return new MelodeeModels.OperationResult<bool>("An error has occured. OH NOES!")
        {
            Data = result
        };
    }


    /// <summary>
    ///     Merge all artists to merge into the merge into artist
    /// </summary>
    /// <param name="artistIdToMergeInfo">The artist to merge the other artists into.</param>
    /// <param name="artistIdsToMerge">Artists to merge.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task<MelodeeModels.OperationResult<bool>> MergeArtistsAsync(int artistIdToMergeInfo,
        int[] artistIdsToMerge, CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistIdToMergeInfo, nameof(artistIdToMergeInfo));
        Guard.Against.NullOrEmpty(artistIdsToMerge, nameof(artistIdsToMerge));

        Logger.Debug("Starting merge operation: Target artist ID [{TargetArtistId}], Source artist IDs [{SourceArtistIds}]",
            artistIdToMergeInfo, string.Join(", ", artistIdsToMerge));

        await using (var scopedContext =
                     await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken);

            var dbArtistToMergeInto = await scopedContext
                .Artists
                .Include(x => x.Library)
                .Include(x => x.Albums)
                .FirstOrDefaultAsync(x => x.Id == artistIdToMergeInfo, cancellationToken)
                .ConfigureAwait(false);

            if (dbArtistToMergeInto == null)
            {
                Logger.Warning("Merge failed: Target artist [{TargetArtistId}] not found", artistIdToMergeInfo);
                return new MelodeeModels.OperationResult<bool>($"Unknown artist to merge into [{artistIdToMergeInfo}].")
                {
                    Data = false
                };
            }

            Logger.Debug("Target artist: [{ArtistName}] (ID: {ArtistId}), Library: [{LibraryName}], Albums: {AlbumCount}",
                dbArtistToMergeInto.Name, dbArtistToMergeInto.Id, dbArtistToMergeInto.Library.Name, dbArtistToMergeInto.Albums.Count);

            var dbArtistToMergeIntoDirectoryPath = dbArtistToMergeInto.ToFileSystemDirectoryInfo().FullName();
            if (!fileSystemService.DirectoryExists(dbArtistToMergeIntoDirectoryPath))
            {
                Logger.Debug("Creating target artist directory: [{Directory}]", dbArtistToMergeIntoDirectoryPath);
                fileSystemService.CreateDirectory(dbArtistToMergeIntoDirectoryPath);
            }

            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            var libraryIdsToUpdate = new List<int>();
            var artistAlternateNamesToMerge = new List<string>();
            foreach (var artistApiKeyToMerge in artistIdsToMerge)
            {
                Logger.Debug("Processing source artist ID [{SourceArtistId}]", artistApiKeyToMerge);

                var dbArtist = await scopedContext
                    .Artists
                    .Include(x => x.Library)
                    .Include(x => x.Albums)
                    .Include(x => x.UserArtists)
                    .FirstOrDefaultAsync(x => x.Id == artistApiKeyToMerge, cancellationToken)
                    .ConfigureAwait(false);
                if (dbArtist == null)
                {
                    Logger.Warning("Merge failed: Source artist [{SourceArtistId}] not found", artistApiKeyToMerge);
                    return new MelodeeModels.OperationResult<bool>($"Unknown artist to merge [{artistApiKeyToMerge}].")
                    {
                        Data = false
                    };
                }

                Logger.Debug("Source artist: [{ArtistName}] (ID: {ArtistId}), Library: [{LibraryName}], Albums: {AlbumCount}, UserArtists: {UserArtistCount}",
                    dbArtist.Name, dbArtist.Id, dbArtist.Library.Name, dbArtist.Albums.Count, dbArtist.UserArtists.Count);

                artistAlternateNamesToMerge.Add(dbArtist.NameNormalized);
                artistAlternateNamesToMerge.AddRange(dbArtist.AlternateNames.ToTags() ?? []);
                Logger.Debug("Adding alternate names from source artist: [{AlternateNames}]",
                    string.Join(", ", dbArtist.AlternateNames.ToTags() ?? []));

                var artistPinType = (int)UserPinType.Artist;
                var existingUserPinUserIds = await scopedContext.UserPins
                    .Where(up => up.PinType == artistPinType && up.PinId == dbArtistToMergeInto.Id)
                    .Select(up => up.UserId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var userPins = await scopedContext.UserPins
                    .Where(x => x.PinType == artistPinType && x.PinId == dbArtist.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                Logger.Debug("Processing {UserPinCount} user pins from source artist, target already has {ExistingPinCount} pin user IDs",
                    userPins.Count, existingUserPinUserIds.Count);

                var pinsTransferred = 0;
                var pinsRemoved = 0;
                foreach (var userPin in userPins)
                {
                    if (existingUserPinUserIds.Contains(userPin.UserId))
                    {
                        scopedContext.UserPins.Remove(userPin);
                        pinsRemoved++;
                    }
                    else
                    {
                        userPin.PinId = dbArtistToMergeInto.Id;
                        userPin.LastUpdatedAt = now;
                        existingUserPinUserIds.Add(userPin.UserId);
                        pinsTransferred++;
                    }
                }
                Logger.Debug("User pins: {Transferred} transferred, {Removed} removed (duplicates)", pinsTransferred, pinsRemoved);

                // Get existing album names (and normalized names) for target artist to check for conflicts
                var existingAlbumNames = dbArtistToMergeInto.Albums
                    .Select(a => a.NameNormalized)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                Logger.Debug("Target artist has {ExistingAlbumCount} existing albums: [{AlbumNames}]",
                    existingAlbumNames.Count, string.Join(", ", dbArtistToMergeInto.Albums.Select(a => a.Name)));

                foreach (var albumToMerge in dbArtist.Albums.ToList())
                {
                    try
                    {
                        Logger.Debug("Processing album [{AlbumName}] (ID: {AlbumId}, NameNormalized: [{NameNormalized}])",
                            albumToMerge.Name, albumToMerge.Id, albumToMerge.NameNormalized);

                        // Check if target artist already has an album with this name
                        if (existingAlbumNames.Contains(albumToMerge.NameNormalized))
                        {
                            Logger.Debug("Album name conflict detected: Target artist already has album [{AlbumName}]", albumToMerge.Name);

                            // Target artist already has an album with the same name
                            // We need to merge the songs from this album into the existing album
                            var existingAlbum = dbArtistToMergeInto.Albums
                                .FirstOrDefault(a => string.Equals(a.NameNormalized, albumToMerge.NameNormalized, StringComparison.OrdinalIgnoreCase));

                            if (existingAlbum != null)
                            {
                                Logger.Debug("Merging albums: Source album ID {SourceAlbumId} into target album ID {TargetAlbumId}",
                                    albumToMerge.Id, existingAlbum.Id);

                                // Transfer songs from source album to existing album if not duplicates
                                var existingSongNumbers = await scopedContext.Songs
                                    .Where(s => s.AlbumId == existingAlbum.Id)
                                    .Select(s => s.SongNumber)
                                    .ToListAsync(cancellationToken)
                                    .ConfigureAwait(false);

                                var songsToTransfer = await scopedContext.Songs
                                    .Where(s => s.AlbumId == albumToMerge.Id)
                                    .ToListAsync(cancellationToken)
                                    .ConfigureAwait(false);

                                Logger.Debug("Source album has {SourceSongCount} songs, target album has {TargetSongCount} existing song numbers",
                                    songsToTransfer.Count, existingSongNumbers.Count);

                                var songsTransferred = 0;
                                var songsRemoved = 0;
                                foreach (var song in songsToTransfer)
                                {
                                    if (existingSongNumbers.Contains(song.SongNumber))
                                    {
                                        Logger.Debug("Removing duplicate song: [{SongTitle}] (SongNumber: {SongNumber})", song.Title, song.SongNumber);
                                        scopedContext.Songs.Remove(song);
                                        songsRemoved++;
                                    }
                                    else
                                    {
                                        Logger.Debug("Transferring song: [{SongTitle}] (SongNumber: {SongNumber}) to target album", song.Title, song.SongNumber);
                                        song.AlbumId = existingAlbum.Id;
                                        song.LastUpdatedAt = now;
                                        existingSongNumbers.Add(song.SongNumber);
                                        songsTransferred++;
                                    }
                                }
                                Logger.Debug("Album merge songs: {Transferred} transferred, {Removed} removed (duplicates)", songsTransferred, songsRemoved);

                                // Transfer contributors from source album's songs
                                var contributorsToTransfer = await scopedContext.Contributors
                                    .Where(c => c.AlbumId == albumToMerge.Id)
                                    .ToListAsync(cancellationToken)
                                    .ConfigureAwait(false);

                                Logger.Debug("Transferring {ContributorCount} contributors from source album to target album", contributorsToTransfer.Count);
                                foreach (var contributor in contributorsToTransfer)
                                {
                                    contributor.AlbumId = existingAlbum.Id;
                                    contributor.LastUpdatedAt = now;
                                }

                                // Handle file system merge
                                var albumToMergeDirectory = Path.Combine(dbArtist.Library.Path, dbArtist.Directory,
                                    albumToMerge.Directory);
                                var existingAlbumDirectory = Path.Combine(dbArtistToMergeInto.Library.Path,
                                    dbArtistToMergeInto.Directory, existingAlbum.Directory);

                                Logger.Debug("File system merge: Source [{SourceDir}] -> Target [{TargetDir}]",
                                    albumToMergeDirectory, existingAlbumDirectory);

                                if (fileSystemService.DirectoryExists(albumToMergeDirectory))
                                {
                                    var albumJsonFiles = fileSystemService.GetFiles(
                                        albumToMergeDirectory,
                                        MelodeeModels.Album.JsonFileName);
                                    if (albumJsonFiles.Length > 0)
                                    {
                                        Logger.Debug("Found album JSON file, processing existing directory merge");
                                        var album = await fileSystemService.DeserializeAlbumAsync(albumJsonFiles[0],
                                            cancellationToken).ConfigureAwait(false);
                                        if (album != null)
                                        {
                                            await ProcessExistingDirectoryMoveMergeAsync(configuration, serializer, imageProcessor, album,
                                                existingAlbumDirectory, cancellationToken).ConfigureAwait(false);
                                        }
                                    }
                                }
                                else
                                {
                                    Logger.Debug("Source album directory does not exist: [{Directory}]", albumToMergeDirectory);
                                }

                                // Remove the source album (it's being merged into existing)
                                Logger.Debug("Removing source album from database: [{AlbumName}] (ID: {AlbumId})", albumToMerge.Name, albumToMerge.Id);
                                scopedContext.Albums.Remove(albumToMerge);
                                Logger.Information("Merged album [{AlbumName}] into existing album on target artist [{ArtistName}]",
                                    albumToMerge.Name, dbArtistToMergeInto.Name);
                            }
                            continue;
                        }

                        // No name conflict - transfer the album to the target artist
                        Logger.Debug("No album name conflict - transferring album [{AlbumName}] to target artist", albumToMerge.Name);

                        var albumToMergeDirectory2 = Path.Combine(dbArtist.Library.Path, dbArtist.Directory,
                            albumToMerge.Directory);
                        var albumToMergeNewDirectory = Path.Combine(dbArtistToMergeInto.Library.Path,
                            dbArtistToMergeInto.Directory, albumToMerge.Directory);

                        Logger.Debug("Album directory move: [{SourceDir}] -> [{TargetDir}]", albumToMergeDirectory2, albumToMergeNewDirectory);

                        if (fileSystemService.DirectoryExists(albumToMergeDirectory2) && !fileSystemService.DirectoryExists(albumToMergeNewDirectory))
                        {
                            Logger.Debug("Moving album directory to new location");
                            fileSystemService.MoveDirectory(albumToMergeDirectory2, albumToMergeNewDirectory);
                        }
                        else if (fileSystemService.DirectoryExists(albumToMergeNewDirectory))
                        {
                            Logger.Debug("Target directory already exists, processing merge");
                            var albumJsonFiles = fileSystemService.GetFiles(
                                albumToMergeNewDirectory,
                                MelodeeModels.Album.JsonFileName);
                            if (albumJsonFiles.Length > 0)
                            {
                                var album = await fileSystemService.DeserializeAlbumAsync(albumJsonFiles[0],
                                    cancellationToken).ConfigureAwait(false);
                                if (album != null)
                                {
                                    await ProcessExistingDirectoryMoveMergeAsync(configuration, serializer, imageProcessor, album,
                                        albumToMergeDirectory2, cancellationToken).ConfigureAwait(false);
                                }
                            }
                        }
                        else
                        {
                            Logger.Debug("Source album directory does not exist: [{Directory}]", albumToMergeDirectory2);
                        }

                        albumToMerge.ArtistId = dbArtistToMergeInto.Id;
                        albumToMerge.LastUpdatedAt = now;
                        existingAlbumNames.Add(albumToMerge.NameNormalized);
                        Logger.Debug("Album [{AlbumName}] transferred to target artist", albumToMerge.Name);
                    }
                    catch (Exception e)
                    {
                        Logger.Error(e, "Error attempting to merge album [{Album}] into artist [{Artist}]",
                            albumToMerge.Directory, dbArtistToMergeInto.Name);
                    }
                }

                // Handle UserArtists - check for duplicates before transferring
                var existingUserArtistUserIds = await scopedContext.UserArtists
                    .Where(ua => ua.ArtistId == dbArtistToMergeInto.Id)
                    .Select(ua => ua.UserId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                Logger.Debug("Processing {UserArtistCount} UserArtists from source, target has {ExistingCount} existing",
                    dbArtist.UserArtists.Count, existingUserArtistUserIds.Count);

                var userArtistsTransferred = 0;
                var userArtistsRemoved = 0;
                foreach (var userArtistToMerge in dbArtist.UserArtists)
                {
                    if (existingUserArtistUserIds.Contains(userArtistToMerge.UserId))
                    {
                        // User already has a relationship with the target artist - remove the duplicate
                        scopedContext.UserArtists.Remove(userArtistToMerge);
                        userArtistsRemoved++;
                    }
                    else
                    {
                        userArtistToMerge.ArtistId = dbArtistToMergeInto.Id;
                        userArtistToMerge.LastUpdatedAt = now;
                        userArtistsTransferred++;
                    }
                }
                Logger.Debug("UserArtists: {Transferred} transferred, {Removed} removed (duplicates)", userArtistsTransferred, userArtistsRemoved);

                // Handle Contributors - need to check for unique constraint (ArtistId, MetaTagIdentifier, SongId)
                var existingContributorKeys = await scopedContext.Contributors
                    .Where(c => c.ArtistId == dbArtistToMergeInto.Id)
                    .Select(c => new { c.MetaTagIdentifier, c.SongId })
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var contributorsToMerge = await scopedContext.Contributors
                    .Where(c => c.ArtistId == dbArtist.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                Logger.Debug("Processing {ContributorCount} contributors from source artist, target has {ExistingCount} existing contributor keys",
                    contributorsToMerge.Count, existingContributorKeys.Count);

                var contributorsTransferred = 0;
                var contributorsRemoved = 0;
                foreach (var contributor in contributorsToMerge)
                {
                    var wouldViolateConstraint = existingContributorKeys
                        .Any(e => e.MetaTagIdentifier == contributor.MetaTagIdentifier && e.SongId == contributor.SongId);

                    if (wouldViolateConstraint)
                    {
                        Logger.Debug("Removing duplicate contributor: MetaTagIdentifier={MetaTag}, SongId={SongId}",
                            contributor.MetaTagIdentifier, contributor.SongId);
                        scopedContext.Contributors.Remove(contributor);
                        contributorsRemoved++;
                    }
                    else
                    {
                        contributor.ArtistId = dbArtistToMergeInto.Id;
                        contributor.LastUpdatedAt = now;
                        existingContributorKeys.Add(new { contributor.MetaTagIdentifier, contributor.SongId });
                        contributorsTransferred++;
                    }
                }
                Logger.Debug("Contributors: {Transferred} transferred, {Removed} removed (duplicates)", contributorsTransferred, contributorsRemoved);

                // Transfer ArtistRelations where merged artist is the source (ArtistId)
                var existingOutboundRelations = await scopedContext.ArtistRelation
                    .Where(ar => ar.ArtistId == dbArtistToMergeInto.Id)
                    .Select(ar => ar.RelatedArtistId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var outboundRelations = await scopedContext.ArtistRelation
                    .Where(ar => ar.ArtistId == dbArtist.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                Logger.Debug("Processing {OutboundCount} outbound artist relations from source, target has {ExistingCount} existing",
                    outboundRelations.Count, existingOutboundRelations.Count);

                var outboundTransferred = 0;
                var outboundRemoved = 0;
                foreach (var relation in outboundRelations)
                {
                    // Skip if target artist already has this relation, or if it would create self-reference
                    if (existingOutboundRelations.Contains(relation.RelatedArtistId) ||
                        relation.RelatedArtistId == dbArtistToMergeInto.Id)
                    {
                        Logger.Debug("Removing outbound relation: RelatedArtistId={RelatedArtistId} (duplicate or self-reference)",
                            relation.RelatedArtistId);
                        scopedContext.ArtistRelation.Remove(relation);
                        outboundRemoved++;
                    }
                    else
                    {
                        relation.ArtistId = dbArtistToMergeInto.Id;
                        relation.LastUpdatedAt = now;
                        existingOutboundRelations.Add(relation.RelatedArtistId);
                        outboundTransferred++;
                    }
                }
                Logger.Debug("Outbound relations: {Transferred} transferred, {Removed} removed", outboundTransferred, outboundRemoved);

                // Transfer ArtistRelations where merged artist is the target (RelatedArtistId)
                var existingInboundRelations = await scopedContext.ArtistRelation
                    .Where(ar => ar.RelatedArtistId == dbArtistToMergeInto.Id)
                    .Select(ar => ar.ArtistId)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var inboundRelations = await scopedContext.ArtistRelation
                    .Where(ar => ar.RelatedArtistId == dbArtist.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                Logger.Debug("Processing {InboundCount} inbound artist relations from source, target has {ExistingCount} existing",
                    inboundRelations.Count, existingInboundRelations.Count);

                var inboundTransferred = 0;
                var inboundRemoved = 0;
                foreach (var relation in inboundRelations)
                {
                    // Skip if this relation already exists, or if it would create self-reference
                    if (existingInboundRelations.Contains(relation.ArtistId) ||
                        relation.ArtistId == dbArtistToMergeInto.Id)
                    {
                        Logger.Debug("Removing inbound relation: ArtistId={ArtistId} (duplicate or self-reference)",
                            relation.ArtistId);
                        scopedContext.ArtistRelation.Remove(relation);
                        inboundRemoved++;
                    }
                    else
                    {
                        relation.RelatedArtistId = dbArtistToMergeInto.Id;
                        relation.LastUpdatedAt = now;
                        existingInboundRelations.Add(relation.ArtistId);
                        inboundTransferred++;
                    }
                }
                Logger.Debug("Inbound relations: {Transferred} transferred, {Removed} removed", inboundTransferred, inboundRemoved);

                Logger.Debug("Removing source artist from database: [{ArtistName}] (ID: {ArtistId})", dbArtist.Name, dbArtist.Id);
                scopedContext.Artists.Remove(dbArtist);

                Logger.Debug("Saving changes to database for source artist [{ArtistName}]", dbArtist.Name);
                var saveResult = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                Logger.Debug("SaveChanges returned {RowsAffected} rows affected", saveResult);

                if (saveResult > 0)
                {
                    var dbArtistDirectory = dbArtist.ToFileSystemDirectoryInfo();
                    if ((dbArtistToMergeInto.ImageCount ?? 0) == 0 && fileSystemService.DirectoryExists(dbArtistDirectory.FullName()))
                    {
                        Logger.Debug("Target artist has no images, checking source artist directory for images");
                        dbArtistToMergeInto.ImageCount = dbArtistToMergeInto.ImageCount ?? 0;
                        var jpgFiles = fileSystemService.GetFiles(dbArtistDirectory.FullName(), "*.jpg");
                        Logger.Debug("Found {ImageCount} jpg files in source artist directory", jpgFiles.Length);
                        foreach (var jpgFile in jpgFiles)
                        {
                            var fileName = fileSystemService.GetFileName(jpgFile);
                            var newPath = fileSystemService.CombinePath(dbArtistToMergeIntoDirectoryPath, fileName);
                            Logger.Debug("Moving image: [{Source}] -> [{Target}]", jpgFile, newPath);
                            fileSystemService.MoveDirectory(jpgFile, newPath);
                            dbArtistToMergeInto.ImageCount++;
                        }

                        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (fileSystemService.DirectoryExists(dbArtistDirectory.FullName()))
                    {
                        Logger.Debug("Deleting source artist directory: [{Directory}]", dbArtistDirectory.FullName());
                        fileSystemService.DeleteDirectory(dbArtistDirectory.FullName(), true);
                    }
                }

                libraryIdsToUpdate.Add(dbArtist.Library.Id);
                Logger.Information("Successfully merged artist [{SourceArtist}] (ID: {SourceId}) into [{TargetArtist}] (ID: {TargetId})",
                    dbArtist.Name, dbArtist.Id, dbArtistToMergeInto.Name, dbArtistToMergeInto.Id);
            }

            // Add existing alternate names from target artist (fix: was checking == null but should be != null)
            if (dbArtistToMergeInto.AlternateNames != null)
            {
                artistAlternateNamesToMerge.AddRange(dbArtistToMergeInto.AlternateNames.ToTags() ?? []);
            }

            var distinctAlternateNames = artistAlternateNamesToMerge.Distinct().ToList();
            Logger.Debug("Setting {Count} alternate names on target artist: [{AlternateNames}]",
                distinctAlternateNames.Count, string.Join(", ", distinctAlternateNames));

            dbArtistToMergeInto.AlternateNames = "".AddTags(distinctAlternateNames, doNormalize: true);
            dbArtistToMergeInto.LastUpdatedAt = now;
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            Logger.Debug("Updating aggregate values for target artist ID {ArtistId}", dbArtistToMergeInto.Id);
            await UpdateArtistAggregateValuesByIdAsync(dbArtistToMergeInto.Id, cancellationToken).ConfigureAwait(false);

            foreach (var libraryId in libraryIdsToUpdate.Distinct())
            {
                Logger.Debug("Updating library aggregate stats for library ID {LibraryId}", libraryId);
                await UpdateLibraryAggregateStatsByIdAsync(libraryId, cancellationToken).ConfigureAwait(false);
            }

            // To clear the entire cache is unusual, but here we have (likely) deleted many artists, safer to clear all cache and let repopulate as needed.
            Logger.Debug("Clearing cache after merge operation");
            CacheManager.Clear();

            Logger.Information("Merge operation completed successfully. Merged {SourceCount} artist(s) into [{TargetArtist}]",
                artistIdsToMerge.Length, dbArtistToMergeInto.Name);

            return new MelodeeModels.OperationResult<bool>
            {
                Data = true
            };
        }
    }

    public async Task<MelodeeModels.OperationResult<bool>> LockUnlockArtistAsync(
        int artistId,
        bool doLock,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));

        var artistResult = await GetAsync(artistId, cancellationToken).ConfigureAwait(false);
        if (!artistResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<bool>($"Unknown artist to lock [{artistId}].")
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

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAlbumsForArtist(
        int artistId,
        int[] albumIdsToDelete,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));

        var artistResult = await GetAsync(artistId, cancellationToken).ConfigureAwait(false);
        if (!artistResult.IsSuccess)
        {
            return new MelodeeModels.OperationResult<bool>($"Unknown artist [{artistId}].")
            {
                Data = false
            };
        }

        var deleteResult = await albumService.DeleteAsync(albumIdsToDelete, cancellationToken).ConfigureAwait(false);
        var result = deleteResult.IsSuccess;
        if (deleteResult.IsSuccess)
        {
            await ClearCacheAsync(artistResult.Data!, cancellationToken).ConfigureAwait(false);
        }

        return new MelodeeModels.OperationResult<bool>
        {
            Data = result
        };
    }

    /// <summary>
    /// Detect conflicts when merging albums
    /// </summary>
    public async Task<MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeConflictDetectionResult>> DetectAlbumMergeConflictsAsync(
        int artistId,
        int targetAlbumId,
        int[] sourceAlbumIds,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));
        Guard.Against.Expression(x => x < 1, targetAlbumId, nameof(targetAlbumId));
        Guard.Against.NullOrEmpty(sourceAlbumIds, nameof(sourceAlbumIds));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Validate artist exists
        var artist = await scopedContext.Artists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == artistId, cancellationToken)
            .ConfigureAwait(false);

        if (artist == null)
        {
            return new MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeConflictDetectionResult>($"Unknown artist [{artistId}].")
            {
                Data = null!
            };
        }

        // Load target album with songs
        var targetAlbum = await scopedContext.Albums
            .Include(a => a.Songs)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == targetAlbumId && x.ArtistId == artistId, cancellationToken)
            .ConfigureAwait(false);

        if (targetAlbum == null)
        {
            return new MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeConflictDetectionResult>($"Unknown target album [{targetAlbumId}] for artist [{artistId}].")
            {
                Data = null!
            };
        }

        // Load source albums with songs
        var sourceAlbums = await scopedContext.Albums
            .Include(a => a.Songs)
            .AsNoTracking()
            .Where(x => sourceAlbumIds.Contains(x.Id) && x.ArtistId == artistId)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sourceAlbums.Length != sourceAlbumIds.Length)
        {
            return new MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeConflictDetectionResult>("One or more source albums not found or do not belong to the artist.")
            {
                Data = null!
            };
        }

        var conflicts = new List<MelodeeModels.AlbumMerge.AlbumMergeConflict>();

        // Detect album-level field conflicts
        DetectAlbumFieldConflicts(targetAlbum, sourceAlbums, conflicts);

        // Detect track conflicts
        DetectTrackConflicts(targetAlbum, sourceAlbums, conflicts);

        // Detect metadata conflicts
        DetectMetadataConflicts(targetAlbum, sourceAlbums, conflicts);

        var result = new MelodeeModels.AlbumMerge.AlbumMergeConflictDetectionResult
        {
            TargetAlbumId = targetAlbumId,
            SourceAlbumIds = sourceAlbumIds,
            Conflicts = conflicts.ToArray(),
            MergeSummary = $"Merging {sourceAlbums.Length} album(s) into '{targetAlbum.Name}'. Total songs to merge: {sourceAlbums.Sum(a => a.Songs.Count)}"
        };

        return new MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeConflictDetectionResult>
        {
            Data = result
        };
    }

    private void DetectAlbumFieldConflicts(Album targetAlbum, Album[] sourceAlbums, List<MelodeeModels.AlbumMerge.AlbumMergeConflict> conflicts)
    {
        // Check year conflicts
        var distinctYears = sourceAlbums.Select(a => a.ReleaseDate.Year).Distinct().ToArray();
        if (distinctYears.Length > 1 || (distinctYears.Length == 1 && distinctYears[0] != targetAlbum.ReleaseDate.Year))
        {
            var sourceValues = sourceAlbums.ToDictionary(a => a.Id, a => a.ReleaseDate.Year.ToString());
            // Use deterministic conflict ID based on target album ID
            conflicts.Add(new MelodeeModels.AlbumMerge.AlbumMergeConflict
            {
                ConflictId = $"field_year_{targetAlbum.Id}",
                ConflictType = Enums.AlbumMergeConflictType.AlbumFieldConflict,
                Description = "Albums have different release years",
                FieldName = "ReleaseYear",
                TargetValue = targetAlbum.ReleaseDate.Year.ToString(),
                SourceValues = sourceValues,
                IsRequired = true
            });
        }

        // Check title conflicts
        var distinctTitles = sourceAlbums.Select(a => a.Name).Distinct().ToArray();
        if (distinctTitles.Length > 1 || (distinctTitles.Length == 1 && distinctTitles[0] != targetAlbum.Name))
        {
            var sourceValues = sourceAlbums.ToDictionary(a => a.Id, a => a.Name);
            // Use deterministic conflict ID based on target album ID
            conflicts.Add(new MelodeeModels.AlbumMerge.AlbumMergeConflict
            {
                ConflictId = $"field_title_{targetAlbum.Id}",
                ConflictType = Enums.AlbumMergeConflictType.AlbumFieldConflict,
                Description = "Albums have different titles",
                FieldName = "Title",
                TargetValue = targetAlbum.Name,
                SourceValues = sourceValues,
                IsRequired = true
            });
        }
    }

    private void DetectTrackConflicts(Album targetAlbum, Album[] sourceAlbums, List<MelodeeModels.AlbumMerge.AlbumMergeConflict> conflicts)
    {
        var targetSongs = targetAlbum.Songs.ToArray();

        foreach (var sourceAlbum in sourceAlbums)
        {
            foreach (var sourceSong in sourceAlbum.Songs)
            {
                // Check for track number collision
                var targetSongSameNumber = targetSongs.FirstOrDefault(s => s.SongNumber == sourceSong.SongNumber);
                if (targetSongSameNumber != null)
                {
                    // Different songs with same track number
                    if (!AreSongsEqual(targetSongSameNumber, sourceSong))
                    {
                        // Use deterministic conflict ID based on track number and source album ID
                        conflicts.Add(new MelodeeModels.AlbumMerge.AlbumMergeConflict
                        {
                            ConflictId = $"track_number_{sourceSong.SongNumber}_{sourceAlbum.Id}",
                            ConflictType = Enums.AlbumMergeConflictType.TrackNumberCollision,
                            Description = $"Track {sourceSong.SongNumber} exists in both albums with different content",
                            TrackNumber = sourceSong.SongNumber,
                            TargetValue = $"{targetSongSameNumber.Title} ({FormatDuration(targetSongSameNumber.Duration)})",
                            SourceValues = new Dictionary<int, string>
                            {
                                { sourceAlbum.Id, $"{sourceSong.Title} ({FormatDuration(sourceSong.Duration)})" }
                            },
                            TrackIds = new Dictionary<int, int>
                            {
                                { 0, targetSongSameNumber.Id },
                                { sourceAlbum.Id, sourceSong.Id }
                            },
                            IsRequired = true
                        });
                    }
                    // If songs are equal, no conflict (will be skipped during merge)
                }
                else
                {
                    // Check for duplicate title at different number (compilation case)
                    var targetSongSameTitle = targetSongs.FirstOrDefault(s =>
                        string.Equals(s.TitleNormalized, sourceSong.TitleNormalized, StringComparison.OrdinalIgnoreCase));

                    if (targetSongSameTitle != null && targetSongSameTitle.SongNumber != sourceSong.SongNumber)
                    {
                        // Use deterministic conflict ID based on normalized title and source album ID
                        conflicts.Add(new MelodeeModels.AlbumMerge.AlbumMergeConflict
                        {
                            ConflictId = $"track_title_{sourceSong.TitleNormalized}_{sourceAlbum.Id}",
                            ConflictType = Enums.AlbumMergeConflictType.DuplicateTitleDifferentNumber,
                            Description = $"Track '{sourceSong.Title}' exists at different track numbers",
                            TargetValue = $"Track {targetSongSameTitle.SongNumber}: {targetSongSameTitle.Title}",
                            SourceValues = new Dictionary<int, string>
                            {
                                { sourceAlbum.Id, $"Track {sourceSong.SongNumber}: {sourceSong.Title}" }
                            },
                            TrackIds = new Dictionary<int, int>
                            {
                                { 0, targetSongSameTitle.Id },
                                { sourceAlbum.Id, sourceSong.Id }
                            },
                            IsRequired = true
                        });
                    }
                }
            }
        }
    }

    private void DetectMetadataConflicts(Album targetAlbum, Album[] sourceAlbums, List<MelodeeModels.AlbumMerge.AlbumMergeConflict> conflicts)
    {
        // Check for genre conflicts
        var allSourceGenres = sourceAlbums.SelectMany(a => a.Genres ?? []).Distinct().ToArray();
        var targetGenres = targetAlbum.Genres ?? [];

        var newGenres = allSourceGenres.Except(targetGenres, StringComparer.OrdinalIgnoreCase).ToArray();
        if (newGenres.Any())
        {
            // Use deterministic conflict ID based on target album ID
            conflicts.Add(new MelodeeModels.AlbumMerge.AlbumMergeConflict
            {
                ConflictId = $"metadata_genres_{targetAlbum.Id}",
                ConflictType = Enums.AlbumMergeConflictType.MetadataCollision,
                Description = "Source albums have additional genres not in target",
                FieldName = "Genres",
                TargetValue = string.Join(", ", targetGenres),
                SourceValues = new Dictionary<int, string>
                {
                    { -1, string.Join(", ", newGenres) }
                },
                IsRequired = false // Can default to union
            });
        }

        // Check for mood conflicts
        var allSourceMoods = sourceAlbums.SelectMany(a => a.Moods ?? []).Distinct().ToArray();
        var targetMoods = targetAlbum.Moods ?? [];

        var newMoods = allSourceMoods.Except(targetMoods, StringComparer.OrdinalIgnoreCase).ToArray();
        if (newMoods.Any())
        {
            // Use deterministic conflict ID based on target album ID
            conflicts.Add(new MelodeeModels.AlbumMerge.AlbumMergeConflict
            {
                ConflictId = $"metadata_moods_{targetAlbum.Id}",
                ConflictType = Enums.AlbumMergeConflictType.MetadataCollision,
                Description = "Source albums have additional moods not in target",
                FieldName = "Moods",
                TargetValue = string.Join(", ", targetMoods),
                SourceValues = new Dictionary<int, string>
                {
                    { -1, string.Join(", ", newMoods) }
                },
                IsRequired = false // Can default to union
            });
        }
    }

    private bool AreSongsEqual(Song song1, Song song2)
    {
        // Compare normalized titles
        if (!string.Equals(song1.TitleNormalized, song2.TitleNormalized, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Compare duration (within tolerance threshold)
        if (Math.Abs(song1.Duration - song2.Duration) > SongDurationToleranceMs)
        {
            return false;
        }

        // If both songs have file hash, compare them for definitive equality check
        var hasHash1 = !string.IsNullOrEmpty(song1.FileHash);
        var hasHash2 = !string.IsNullOrEmpty(song2.FileHash);

        if (hasHash1 && hasHash2)
        {
            // Both hashes present - they must match for songs to be equal
            return string.Equals(song1.FileHash, song2.FileHash, StringComparison.OrdinalIgnoreCase);
        }

        // If hashes not available or only one has hash, rely on title and duration match
        return true;
    }

    private string FormatDuration(double milliseconds)
    {
        var ts = TimeSpan.FromMilliseconds(milliseconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// Merge multiple albums into a single target album with conflict resolution
    /// </summary>
    public async Task<MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeReport>> MergeAlbumsAsync(
        MelodeeModels.AlbumMerge.AlbumMergeRequest request,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(request, nameof(request));
        Guard.Against.Expression(x => x < 1, request.ArtistId, nameof(request.ArtistId));
        Guard.Against.Expression(x => x < 1, request.TargetAlbumId, nameof(request.TargetAlbumId));
        Guard.Against.NullOrEmpty(request.SourceAlbumIds, nameof(request.SourceAlbumIds));

        // First detect conflicts
        var conflictDetection = await DetectAlbumMergeConflictsAsync(
            request.ArtistId,
            request.TargetAlbumId,
            request.SourceAlbumIds,
            cancellationToken).ConfigureAwait(false);

        if (!conflictDetection.IsSuccess)
        {
            return new MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeReport>(conflictDetection.Messages)
            {
                Data = null!
            };
        }

        // Validate that all required conflicts have resolutions
        if (conflictDetection.Data.HasConflicts)
        {
            var requiredConflicts = conflictDetection.Data.Conflicts!.Where(c => c.IsRequired).ToArray();
            if (requiredConflicts.Any())
            {
                var resolutionIds = request.Resolutions?.Select(r => r.ConflictId).ToHashSet() ?? [];
                var unresolvedConflicts = requiredConflicts.Where(c => !resolutionIds.Contains(c.ConflictId)).ToArray();

                if (unresolvedConflicts.Any())
                {
                    return new MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeReport>($"Missing resolutions for {unresolvedConflicts.Length} required conflict(s).")
                    {
                        Data = null!
                    };
                }
            }
        }

        // Execute merge in transaction
        // NOTE: File system operations (moving song and image files) occur within the transaction
        // but cannot be rolled back if the database transaction fails. This is a known limitation.
        // If an error occurs after files are moved, those files will remain in the target directory
        // even though the database changes are rolled back.
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await scopedContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var report = await ExecuteMergeAsync(scopedContext, request, conflictDetection.Data, cancellationToken).ConfigureAwait(false);

            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // Clear caches
            await ClearCacheAsync(request.ArtistId, cancellationToken).ConfigureAwait(false);

            return new MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeReport>
            {
                Data = report
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            Logger.Error(ex, "Error merging albums for artist [{ArtistId}]", request.ArtistId);

            return new MelodeeModels.OperationResult<MelodeeModels.AlbumMerge.AlbumMergeReport>(ex)
            {
                Data = null!
            };
        }
    }

    private async Task<MelodeeModels.AlbumMerge.AlbumMergeReport> ExecuteMergeAsync(
        MelodeeDbContext context,
        MelodeeModels.AlbumMerge.AlbumMergeRequest request,
        MelodeeModels.AlbumMerge.AlbumMergeConflictDetectionResult conflictResult,
        CancellationToken cancellationToken)
    {
        _ = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var now = Instant.FromDateTimeUtc(DateTime.UtcNow);

        // Load target album with all necessary includes
        var targetAlbum = await context.Albums
            .Include(a => a.Songs)
                .ThenInclude(s => s.UserSongs)
            .Include(a => a.Contributors)
            .Include(a => a.Artist)
            .ThenInclude(a => a.Library)
            .FirstAsync(x => x.Id == request.TargetAlbumId, cancellationToken)
            .ConfigureAwait(false);

        // Load source albums with all song-related data
        var sourceAlbums = await context.Albums
            .Include(a => a.Songs)
                .ThenInclude(s => s.UserSongs)
            .Include(a => a.Contributors)
            .Include(a => a.UserAlbums)
            .Include(a => a.Artist)
            .ThenInclude(a => a.Library)
            .Where(x => request.SourceAlbumIds.Contains(x.Id))
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        // Get all source song IDs for bulk loading related data
        var sourceSongIds = sourceAlbums.SelectMany(a => a.Songs.Select(s => s.Id)).ToArray();
        var targetSongIds = targetAlbum.Songs.Select(s => s.Id).ToArray();
        var allSongIds = sourceSongIds.Concat(targetSongIds).ToArray();

        // Load related song data in bulk for efficiency
        var playlistSongs = await context.PlaylistSong
            .Where(ps => allSongIds.Contains(ps.SongId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var playQueues = await context.PlayQues
            .Where(pq => allSongIds.Contains(pq.SongId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var bookmarks = await context.Bookmarks
            .Where(b => allSongIds.Contains(b.SongId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var playHistory = await context.UserSongPlayHistories
            .Where(h => allSongIds.Contains(h.SongId))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var actionLog = new List<string>();
        var songsMoved = 0;
        var songsSkipped = 0;
        var songsMerged = 0;
        var imagesMoved = 0;
        var imagesSkipped = 0;
        var metadataMerged = 0;

        // Calculate target album directory once
        var targetAlbumDirectory = Path.Combine(targetAlbum.Artist.Library.Path, targetAlbum.Artist.Directory, targetAlbum.Directory);

        // Apply field resolutions
        ApplyFieldResolutions(targetAlbum, sourceAlbums, request.Resolutions, actionLog);

        // Merge songs
        foreach (var sourceAlbum in sourceAlbums)
        {
            var sourceAlbumDirectory = Path.Combine(sourceAlbum.Artist.Library.Path, sourceAlbum.Artist.Directory, sourceAlbum.Directory);

            foreach (var sourceSong in sourceAlbum.Songs.ToArray())
            {
                var resolution = GetTrackResolution(sourceSong, request.Resolutions);

                if (resolution?.Action == MelodeeModels.AlbumMerge.AlbumMergeResolutionAction.SkipSource)
                {
                    // Even when skipping, merge user data to the target song if one exists
                    var targetSongForUserData = targetAlbum.Songs.FirstOrDefault(s =>
                        s.SongNumber == sourceSong.SongNumber ||
                        (string.Equals(s.TitleNormalized, sourceSong.TitleNormalized, StringComparison.OrdinalIgnoreCase) &&
                         Math.Abs(s.Duration - sourceSong.Duration) < SongDurationToleranceMs));

                    if (targetSongForUserData != null)
                    {
                        MergeSongUserData(context, sourceSong, targetSongForUserData, playlistSongs, playQueues, bookmarks, playHistory, now, actionLog);
                        songsMerged++;
                    }

                    songsSkipped++;
                    actionLog.Add($"Skipped track {sourceSong.SongNumber}: {sourceSong.Title} from {sourceAlbum.Name}");
                    continue;
                }

                // Check if song already exists in target
                var existingSong = targetAlbum.Songs.FirstOrDefault(s =>
                    s.SongNumber == sourceSong.SongNumber ||
                    (string.Equals(s.TitleNormalized, sourceSong.TitleNormalized, StringComparison.OrdinalIgnoreCase) &&
                     Math.Abs(s.Duration - sourceSong.Duration) < SongDurationToleranceMs));

                if (existingSong != null)
                {
                    if (resolution?.Action == MelodeeModels.AlbumMerge.AlbumMergeResolutionAction.ReplaceWithSource)
                    {
                        // Merge user data from existing song to source song before removing
                        MergeSongUserData(context, existingSong, sourceSong, playlistSongs, playQueues, bookmarks, playHistory, now, actionLog);

                        // Remove existing and add source
                        context.Songs.Remove(existingSong);
                        sourceSong.AlbumId = targetAlbum.Id;
                        songsMoved++;
                        songsMerged++;
                        actionLog.Add($"Replaced track {sourceSong.SongNumber}: {existingSong.Title} with {sourceSong.Title}");
                    }
                    else
                    {
                        // Default: keep target, merge source user data to target
                        MergeSongUserData(context, sourceSong, existingSong, playlistSongs, playQueues, bookmarks, playHistory, now, actionLog);
                        songsMerged++;
                        songsSkipped++;
                        actionLog.Add($"Skipped duplicate track {sourceSong.SongNumber}: {sourceSong.Title} (user data merged)");
                        continue;
                    }
                }
                else
                {
                    // Move song to target album
                    sourceSong.AlbumId = targetAlbum.Id;
                    songsMoved++;
                    actionLog.Add($"Moved track {sourceSong.SongNumber}: {sourceSong.Title} from {sourceAlbum.Name}");
                }

                // Move the file
                try
                {
                    var sourceFilePath = Path.Combine(sourceAlbumDirectory, sourceSong.FileName);
                    var targetFilePath = Path.Combine(targetAlbumDirectory, sourceSong.FileName);

                    if (fileSystemService.FileExists(sourceFilePath))
                    {
                        if (!fileSystemService.FileExists(targetFilePath))
                        {
                            fileSystemService.MoveDirectory(sourceFilePath, targetFilePath);
                        }
                        else
                        {
                            actionLog.Add($"File move skipped for track {sourceSong.SongNumber}: target file already exists");
                        }
                    }
                    else
                    {
                        Logger.Warning("Source file not found for song [{SongId}]: {SourcePath}", sourceSong.Id, sourceFilePath);
                        actionLog.Add($"Warning: Source file missing for track {sourceSong.SongNumber}: {sourceSong.Title}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to move file for song [{SongId}]", sourceSong.Id);
                    actionLog.Add($"Failed to move file for track {sourceSong.SongNumber}: {sourceSong.Title}. See logs for details.");
                }
            }

            // Merge UserAlbums, avoiding duplicates on the target album
            foreach (var userAlbum in sourceAlbum.UserAlbums.ToList())
            {
                var existingUserAlbum = targetAlbum.UserAlbums
                    .FirstOrDefault(ua => ua.UserId == userAlbum.UserId);

                if (existingUserAlbum is not null)
                {
                    // Prefer the existing UserAlbum on the target album to avoid duplicates
                    existingUserAlbum.LastUpdatedAt = now;
                    context.UserAlbums.Remove(userAlbum);
                }
                else
                {
                    userAlbum.AlbumId = targetAlbum.Id;
                    userAlbum.LastUpdatedAt = now;
                }
            }

            // Merge images
            var sourceAlbumDir = fileSystemService.CombinePath(sourceAlbum.Artist.Library.Path, sourceAlbum.Artist.Directory, sourceAlbum.Directory);
            var targetAlbumDir = fileSystemService.CombinePath(targetAlbum.Artist.Library.Path, targetAlbum.Artist.Directory, targetAlbum.Directory);

            if (fileSystemService.DirectoryExists(sourceAlbumDir))
            {
                var imageFiles = fileSystemService.GetFiles(sourceAlbumDir, "*.jpg");
                foreach (var imageFile in imageFiles)
                {
                    var fileName = fileSystemService.GetFileName(imageFile);
                    var targetPath = fileSystemService.CombinePath(targetAlbumDir, fileName);

                    if (!fileSystemService.FileExists(targetPath))
                    {
                        try
                        {
                            fileSystemService.MoveDirectory(imageFile, targetPath);
                            imagesMoved++;
                            actionLog.Add($"Moved image file: {fileName}");
                        }
                        catch (Exception ex)
                        {
                            Logger.Warning(ex, "Failed to move image file [{File}]", imageFile);
                            imagesSkipped++;
                            actionLog.Add($"Failed to move image file: {fileName}. See logs for details.");
                        }
                    }
                    else
                    {
                        imagesSkipped++;
                        actionLog.Add($"Image file skipped (already exists): {fileName}");
                    }
                }
            }

            // Delete source album
            context.Albums.Remove(sourceAlbum);
            actionLog.Add($"Deleted source album: {sourceAlbum.Name}");
        }

        // Merge metadata (genres, moods)
        MergeMetadata(targetAlbum, sourceAlbums, request.Resolutions, ref metadataMerged);

        // Update target album timestamps and counts
        targetAlbum.LastUpdatedAt = now;
        targetAlbum.SongCount = (short?)targetAlbum.Songs.Count;
        targetAlbum.Duration = targetAlbum.Songs.Sum(s => s.Duration);
        targetAlbum.ImageCount = fileSystemService.DirectoryExists(targetAlbumDirectory)
            ? fileSystemService.GetFiles(targetAlbumDirectory, "*.jpg").Length
            : 0;

        return new MelodeeModels.AlbumMerge.AlbumMergeReport
        {
            TargetAlbumId = targetAlbum.Id,
            TargetAlbumName = targetAlbum.Name,
            SourceAlbumIds = sourceAlbums.Select(a => a.Id).ToArray(),
            SourceAlbumNames = sourceAlbums.Select(a => a.Name).ToArray(),
            SongsMoved = songsMoved,
            SongsSkipped = songsSkipped,
            SongsUserDataMerged = songsMerged,
            ImagesMoved = imagesMoved,
            ImagesSkipped = imagesSkipped,
            MetadataMerged = metadataMerged,
            ResolvedConflicts = conflictResult.Conflicts,
            AppliedResolutions = request.Resolutions,
            ActionLog = actionLog.ToArray()
        };
    }

    private void ApplyFieldResolutions(
        Album targetAlbum,
        Album[] sourceAlbums,
        MelodeeModels.AlbumMerge.AlbumMergeResolution[]? resolutions,
        List<string> actionLog)
    {
        if (resolutions == null || !resolutions.Any())
        {
            return;
        }

        // Handle ReplaceWithSource resolutions
        foreach (var resolution in resolutions.Where(r => r.Action == MelodeeModels.AlbumMerge.AlbumMergeResolutionAction.ReplaceWithSource))
        {
            var conflict = resolution.ConflictId;
            if (conflict.StartsWith("field_year_") && resolution.SelectedFromAlbumId is > 0)
            {
                var sourceAlbum = sourceAlbums.FirstOrDefault(a => a.Id == resolution.SelectedFromAlbumId);
                if (sourceAlbum != null)
                {
                    targetAlbum.ReleaseDate = sourceAlbum.ReleaseDate;
                    actionLog.Add($"Updated release year to {sourceAlbum.ReleaseDate.Year} from source album '{sourceAlbum.Name}'");
                }
            }
            else if (conflict.StartsWith("field_title_") && resolution.SelectedFromAlbumId is > 0)
            {
                var sourceAlbum = sourceAlbums.FirstOrDefault(a => a.Id == resolution.SelectedFromAlbumId);
                if (sourceAlbum != null)
                {
                    targetAlbum.Name = sourceAlbum.Name;
                    targetAlbum.NameNormalized = sourceAlbum.NameNormalized;
                    actionLog.Add($"Updated title to '{sourceAlbum.Name}' from source album ID {sourceAlbum.Id}");
                }
            }
        }

        // Handle KeepTarget resolutions - log for audit purposes
        foreach (var resolution in resolutions.Where(r => r.Action == MelodeeModels.AlbumMerge.AlbumMergeResolutionAction.KeepTarget))
        {
            var conflict = resolution.ConflictId;
            if (conflict.StartsWith("field_year_"))
            {
                actionLog.Add($"Kept existing release year {targetAlbum.ReleaseDate.Year} for '{targetAlbum.Name}'");
            }
            else if (conflict.StartsWith("field_title_"))
            {
                actionLog.Add($"Kept existing title '{targetAlbum.Name}'");
            }
        }
    }

    private MelodeeModels.AlbumMerge.AlbumMergeResolution? GetTrackResolution(
        Song song,
        MelodeeModels.AlbumMerge.AlbumMergeResolution[]? resolutions)
    {
        if (resolutions == null)
        {
            return null;
        }

        return resolutions.FirstOrDefault(r =>
            r.TrackIds != null &&
            r.TrackIds.Values.Contains(song.Id));
    }

    /// <summary>
    /// Merges user-related data from a source song to a target song.
    /// Handles UserSongs, PlaylistSongs, PlayQueues, Bookmarks, and UserSongPlayHistory.
    /// </summary>
    private void MergeSongUserData(
        MelodeeDbContext context,
        Song sourceSong,
        Song targetSong,
        List<PlaylistSong> playlistSongs,
        List<PlayQueue> playQueues,
        List<Bookmark> bookmarks,
        List<UserSongPlayHistory> playHistory,
        Instant now,
        List<string> actionLog)
    {
        var userDataMerged = 0;

        // Merge UserSongs (play counts, ratings, stars)
        foreach (var sourceUserSong in sourceSong.UserSongs.ToList())
        {
            var targetUserSong = targetSong.UserSongs.FirstOrDefault(us => us.UserId == sourceUserSong.UserId);

            if (targetUserSong != null)
            {
                // Merge: combine play counts, keep highest rating, preserve star if either has it
                targetUserSong.PlayedCount += sourceUserSong.PlayedCount;
                targetUserSong.Rating = Math.Max(targetUserSong.Rating, sourceUserSong.Rating);
                targetUserSong.IsStarred = targetUserSong.IsStarred || sourceUserSong.IsStarred;
                targetUserSong.IsHated = targetUserSong.IsHated && sourceUserSong.IsHated; // Only hated if both are hated
                targetUserSong.LastPlayedAt = targetUserSong.LastPlayedAt > sourceUserSong.LastPlayedAt
                    ? targetUserSong.LastPlayedAt
                    : sourceUserSong.LastPlayedAt;
                targetUserSong.StarredAt ??= sourceUserSong.StarredAt;
                targetUserSong.LastUpdatedAt = now;

                context.UserSongs.Remove(sourceUserSong);
                userDataMerged++;
            }
            else
            {
                // Move: re-point to target song
                sourceUserSong.SongId = targetSong.Id;
                sourceUserSong.LastUpdatedAt = now;
                userDataMerged++;
            }
        }

        // Merge PlaylistSongs - since SongId is part of composite key, we must delete and recreate
        var sourcePlaylistSongs = playlistSongs.Where(ps => ps.SongId == sourceSong.Id).ToList();
        foreach (var sourcePs in sourcePlaylistSongs)
        {
            var existsInPlaylist = playlistSongs.Any(ps => ps.SongId == targetSong.Id && ps.PlaylistId == sourcePs.PlaylistId);

            // Always remove the source playlist song entry
            context.PlaylistSong.Remove(sourcePs);

            if (!existsInPlaylist)
            {
                // Create a new entry pointing to target song (since SongId is part of composite key, we can't update it)
                var newPlaylistSong = new PlaylistSong
                {
                    SongId = targetSong.Id,
                    SongApiKey = targetSong.ApiKey,
                    PlaylistId = sourcePs.PlaylistId,
                    PlaylistOrder = sourcePs.PlaylistOrder
                };
                context.PlaylistSong.Add(newPlaylistSong);
                userDataMerged++;
            }
            // If exists, the duplicate is simply removed (no action log needed, already handled)
        }

        // Merge PlayQueues - re-point to target song
        var sourcePlayQueues = playQueues.Where(pq => pq.SongId == sourceSong.Id).ToList();
        foreach (var sourcePq in sourcePlayQueues)
        {
            var existsInQueue = playQueues.Any(pq => pq.SongId == targetSong.Id && pq.UserId == sourcePq.UserId);
            if (!existsInQueue)
            {
                sourcePq.SongId = targetSong.Id;
                sourcePq.SongApiKey = targetSong.ApiKey;
                userDataMerged++;
            }
            else
            {
                context.PlayQues.Remove(sourcePq);
            }
        }

        // Merge Bookmarks - re-point to target song
        var sourceBookmarks = bookmarks.Where(b => b.SongId == sourceSong.Id).ToList();
        foreach (var sourceBookmark in sourceBookmarks)
        {
            var existsForUser = bookmarks.Any(b => b.SongId == targetSong.Id && b.UserId == sourceBookmark.UserId);
            if (!existsForUser)
            {
                sourceBookmark.SongId = targetSong.Id;
                sourceBookmark.LastUpdatedAt = now;
                userDataMerged++;
            }
            else
            {
                context.Bookmarks.Remove(sourceBookmark);
            }
        }

        // Merge Play History - re-point all history to target song (preserves full history)
        var sourceHistory = playHistory.Where(h => h.SongId == sourceSong.Id).ToList();
        foreach (var history in sourceHistory)
        {
            history.SongId = targetSong.Id;
            userDataMerged++;
        }

        if (userDataMerged > 0)
        {
            actionLog.Add($"Merged {userDataMerged} user data items from '{sourceSong.Title}' to '{targetSong.Title}'");
        }
    }

    private void MergeMetadata(
        Album targetAlbum,
        Album[] sourceAlbums,
        MelodeeModels.AlbumMerge.AlbumMergeResolution[]? resolutions,
        ref int metadataMerged)
    {
        // Default to union for genres unless user specified otherwise
        var genreResolution = resolutions?.FirstOrDefault(r => r.ConflictId.StartsWith("metadata_genres_"));
        if (genreResolution == null || genreResolution.Action == MelodeeModels.AlbumMerge.AlbumMergeResolutionAction.KeepBoth)
        {
            var allGenres = (targetAlbum.Genres ?? [])
                .Union(sourceAlbums.SelectMany(a => a.Genres ?? []))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (allGenres.Length != (targetAlbum.Genres?.Length ?? 0))
            {
                targetAlbum.Genres = allGenres;
                metadataMerged++;
            }
        }

        // Default to union for moods unless user specified otherwise
        var moodResolution = resolutions?.FirstOrDefault(r => r.ConflictId.StartsWith("metadata_moods_"));
        if (moodResolution == null || moodResolution.Action == MelodeeModels.AlbumMerge.AlbumMergeResolutionAction.KeepBoth)
        {
            var allMoods = (targetAlbum.Moods ?? [])
                .Union(sourceAlbums.SelectMany(a => a.Moods ?? []))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (allMoods.Length != (targetAlbum.Moods?.Length ?? 0))
            {
                targetAlbum.Moods = allMoods;
                metadataMerged++;
            }
        }
    }

    public async Task<MelodeeModels.ImageBytesAndEtag> GetArtistImageBytesAndEtagAsync(Guid? apiKey, string? size = null, CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(apiKey, nameof(apiKey));
        Guard.Against.Expression(x => x == Guid.Empty, apiKey.Value, nameof(apiKey));

        var configuration = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var sizeValue = size ?? nameof(ImageSize.Large);

        // Use apiKey and size in cache key - resized images are cached separately
        var cacheKey = CacheKeyArtistImageBytesAndEtagTemplate.FormatSmart(apiKey.Value, sizeValue);
        var overallStopwatch = Stopwatch.StartNew();
        var wasCacheMiss = false;

        var result = await CacheManager.GetAsync(cacheKey, async () =>
        {
            wasCacheMiss = true;
            var badEtag = Instant.MinValue.ToEtag();

            // Database lookup only happens on cache miss
            var dbStopwatch = Stopwatch.StartNew();
            var artist = await GetByApiKeyAsync(apiKey.Value, cancellationToken).ConfigureAwait(false);
            dbStopwatch.Stop();

            if (!artist.IsSuccess || artist.Data == null)
            {
                Logger.Debug("GetArtistImageBytesAndEtagAsync: DB lookup failed for ApiKey [{ApiKey}] in {DbMs}ms", apiKey.Value, dbStopwatch.ElapsedMilliseconds);
                return new MelodeeModels.ImageBytesAndEtag(null, null);
            }

            var artistDirectory = artist.Data.ToFileSystemDirectoryInfo();
            if (!artistDirectory.Exists())
            {
                Logger.Warning("Artist directory [{Directory}] does not exist for artist [{ArtistId}]. DB: {DbMs}ms", artistDirectory.FullName(), artist.Data.Id, dbStopwatch.ElapsedMilliseconds);
                return new MelodeeModels.ImageBytesAndEtag(null, badEtag);
            }

            // Check if a pre-sized image exists on disk first
            var artistImages = artistDirectory.AllFileImageTypeFileInfos().ToArray();
            var imageFile = artistImages
                .FirstOrDefault(x => x.Name.Contains($"-{sizeValue}", StringComparison.OrdinalIgnoreCase))
                            ?? artistImages.OrderBy(x => x.Name).FirstOrDefault();

            if (imageFile is not { Exists: true })
            {
                Logger.Warning("No image found for artist [{ArtistId}]. DB: {DbMs}ms", artist.Data.Id, dbStopwatch.ElapsedMilliseconds);
                return new MelodeeModels.ImageBytesAndEtag(null, badEtag);
            }

            var fileStopwatch = Stopwatch.StartNew();
            var imageBytes = await fileSystemService.ReadAllBytesAsync(imageFile.FullName, cancellationToken).ConfigureAwait(false);
            fileStopwatch.Stop();

            var eTag = (artist.Data.LastUpdatedAt ?? artist.Data.CreatedAt).ToEtag();

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
                    imageBytes = imageProcessor.ResizeImageIfNeeded(imageBytes, targetSize, targetSize, false);
                    eTag = HashHelper.CreateSha256(eTag + targetSize);
                }
                resizeStopwatch.Stop();

                Logger.Debug("GetArtistImageBytesAndEtagAsync MISS: Artist [{ArtistId}] DB: {DbMs}ms, FileRead: {FileMs}ms, Resize: {ResizeMs}ms, Size: {Size}bytes",
                    artist.Data.Id, dbStopwatch.ElapsedMilliseconds, fileStopwatch.ElapsedMilliseconds, resizeStopwatch.ElapsedMilliseconds, imageBytes.Length);
            }
            else
            {
                Logger.Debug("GetArtistImageBytesAndEtagAsync MISS: Artist [{ArtistId}] DB: {DbMs}ms, FileRead: {FileMs}ms, Size: {Size}bytes",
                    artist.Data.Id, dbStopwatch.ElapsedMilliseconds, fileStopwatch.ElapsedMilliseconds, imageBytes.Length);
            }

            return new MelodeeModels.ImageBytesAndEtag(imageBytes, eTag);
        }, cancellationToken, configuration.CacheDuration(), Artist.CacheRegion);

        overallStopwatch.Stop();

        if (!wasCacheMiss)
        {
            Logger.Debug("GetArtistImageBytesAndEtagAsync HIT: ApiKey [{ApiKey}] Total: {TotalMs}ms", apiKey.Value, overallStopwatch.ElapsedMilliseconds);
        }
        else
        {
            Logger.Debug("GetArtistImageBytesAndEtagAsync MISS Total: ApiKey [{ApiKey}] Total: {TotalMs}ms", apiKey.Value, overallStopwatch.ElapsedMilliseconds);
        }

        return result;
    }

    /// <summary>
    /// Get artist with similar artists for OpenSubsonic API
    /// </summary>
    public async Task<MelodeeModels.OperationResult<(Artist artist, Artist[] similarArtists)>> GetArtistWithRelatedAsync(
        Guid apiKey,
        int? numberOfSimilarArtists = null,
        ArtistRelationType? artistRelationType = null,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var artist = await scopedContext.Artists
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == apiKey, cancellationToken)
            .ConfigureAwait(false);

        if (artist == null)
        {
            return new MelodeeModels.OperationResult<(Artist, Artist[])>("Artist not found.")
            {
                Data = (null!, []),
                Type = MelodeeModels.OperationResponseType.NotFound
            };
        }

        Artist[] similarArtists = [];

        if (numberOfSimilarArtists > 0)
        {
            var similarArtistRelationType = SafeParser.ToNumber<int>(artistRelationType ?? ArtistRelationType.Similar);
            var similarDbArtists = await scopedContext.ArtistRelation
                .Include(x => x.RelatedArtist)
                .Where(x => x.ArtistId == artist.Id)
                .Where(x => similarArtistRelationType == 0 || x.ArtistRelationType == similarArtistRelationType)
                .OrderBy(x => x.Artist.SortName)
                .Take(numberOfSimilarArtists.Value)
                .AsNoTracking()
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            if (similarDbArtists.Any())
            {
                similarArtists = similarDbArtists.Select(x => x.RelatedArtist).ToArray();
            }
        }

        return new MelodeeModels.OperationResult<(Artist, Artist[])>
        {
            Data = (artist, similarArtists)
        };
    }

    public async Task<MelodeeModels.OperationResult<bool>> AddArtistRelationAsync(
        int artistId,
        int relatedArtistId,
        ArtistRelationType relationType,
        int? sortOrder = null,
        DateTime? relationStart = null,
        DateTime? relationEnd = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));
        Guard.Against.Expression(x => x < 1, relatedArtistId, nameof(relatedArtistId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var exists = await scopedContext.ArtistRelation.AnyAsync(ar => ar.ArtistId == artistId && ar.RelatedArtistId == relatedArtistId, cancellationToken).ConfigureAwait(false);
        if (exists)
        {
            return new MelodeeModels.OperationResult<bool>("Relation already exists") { Data = false, Type = MelodeeModels.OperationResponseType.ValidationFailure };
        }

        scopedContext.ArtistRelation.Add(new Data.Models.ArtistRelation
        {
            ArtistId = artistId,
            RelatedArtistId = relatedArtistId,
            ArtistRelationType = SafeParser.ToNumber<int>(relationType),
            SortOrder = sortOrder ?? 0,
            RelationStart = relationStart != null ? NodaTime.Instant.FromDateTimeUtc(DateTime.SpecifyKind(relationStart.Value, DateTimeKind.Utc)) : null,
            RelationEnd = relationEnd != null ? NodaTime.Instant.FromDateTimeUtc(DateTime.SpecifyKind(relationEnd.Value, DateTimeKind.Utc)) : null,
            CreatedAt = NodaTime.SystemClock.Instance.GetCurrentInstant()
        });

        var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;

        if (result)
        {
            await ClearCacheAsync(artistId, cancellationToken).ConfigureAwait(false);
            await ClearCacheAsync(relatedArtistId, cancellationToken).ConfigureAwait(false);
        }

        return new MelodeeModels.OperationResult<bool> { Data = result };
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteArtistRelationAsync(
        int artistId,
        int relatedArtistId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));
        Guard.Against.Expression(x => x < 1, relatedArtistId, nameof(relatedArtistId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var relations = await scopedContext.ArtistRelation
            .Where(ar => ar.ArtistId == artistId && ar.RelatedArtistId == relatedArtistId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!relations.Any())
        {
            return new MelodeeModels.OperationResult<bool>("Relation not found") { Data = false, Type = MelodeeModels.OperationResponseType.NotFound };
        }

        scopedContext.ArtistRelation.RemoveRange(relations);
        var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        if (result)
        {
            await ClearCacheAsync(artistId, cancellationToken).ConfigureAwait(false);
            await ClearCacheAsync(relatedArtistId, cancellationToken).ConfigureAwait(false);
        }

        return new MelodeeModels.OperationResult<bool> { Data = result };
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAllArtistRelationsAsync(
        int artistId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var relations = await scopedContext.ArtistRelation
            .Where(ar => ar.ArtistId == artistId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!relations.Any())
        {
            return new MelodeeModels.OperationResult<bool> { Data = true };
        }

        var relatedIds = relations.Select(r => r.RelatedArtistId).Distinct().ToArray();
        scopedContext.ArtistRelation.RemoveRange(relations);
        var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        if (result)
        {
            await ClearCacheAsync(artistId, cancellationToken).ConfigureAwait(false);
            foreach (var rid in relatedIds)
            {
                await ClearCacheAsync(rid, cancellationToken).ConfigureAwait(false);
            }
        }

        return new MelodeeModels.OperationResult<bool> { Data = result };
    }

    public async Task<MelodeeModels.OperationResult<int>> CountArtistRelationsAsync(
        int artistId,
        ArtistRelationType? relationType = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var typeNumber = SafeParser.ToNumber<int>(relationType ?? ArtistRelationType.NotSet);
        var query = scopedContext.ArtistRelation
            .AsNoTracking()
            .Where(x => x.ArtistId == artistId);
        if (relationType != null && typeNumber != 0)
        {
            query = query.Where(x => x.ArtistRelationType == typeNumber);
        }
        var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        return new MelodeeModels.OperationResult<int> { Data = count };
    }

    public async Task<MelodeeModels.OperationResult<int>> CountArtistRelationsInboundAsync(
        int relatedArtistId,
        ArtistRelationType? relationType = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, relatedArtistId, nameof(relatedArtistId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var typeNumber = SafeParser.ToNumber<int>(relationType ?? ArtistRelationType.NotSet);
        var query = scopedContext.ArtistRelation
            .AsNoTracking()
            .Where(x => x.RelatedArtistId == relatedArtistId);
        if (relationType != null && typeNumber != 0)
        {
            query = query.Where(x => x.ArtistRelationType == typeNumber);
        }
        var count = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        return new MelodeeModels.OperationResult<int> { Data = count };
    }

    public async Task<MelodeeModels.OperationResult<ArtistRelation[]>> ListArtistRelationsAsync(
        int artistId,
        ArtistRelationType? relationType = null,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var typeNumber = SafeParser.ToNumber<int>(relationType ?? ArtistRelationType.NotSet);

        var query = scopedContext.ArtistRelation
            .Include(x => x.RelatedArtist)
            .Where(x => x.ArtistId == artistId)
            .AsNoTracking();

        if (relationType != null && typeNumber != 0)
        {
            query = query.Where(x => x.ArtistRelationType == typeNumber);
        }

        query = query.OrderBy(x => x.RelatedArtist.SortName).ThenBy(x => x.RelatedArtist.Name);
        if (take.HasValue && take.Value > 0)
        {
            query = query.Take(take.Value);
        }

        var relations = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new MelodeeModels.OperationResult<ArtistRelation[]> { Data = relations };
    }

    public async Task<MelodeeModels.OperationResult<ArtistRelation[]>> ListArtistRelationsInboundAsync(
        int relatedArtistId,
        ArtistRelationType? relationType = null,
        int? take = null,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, relatedArtistId, nameof(relatedArtistId));

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var typeNumber = SafeParser.ToNumber<int>(relationType ?? ArtistRelationType.NotSet);

        var query = scopedContext.ArtistRelation
            .Include(x => x.Artist)
            .Where(x => x.RelatedArtistId == relatedArtistId)
            .AsNoTracking();

        if (relationType != null && typeNumber != 0)
        {
            query = query.Where(x => x.ArtistRelationType == typeNumber);
        }

        query = query.OrderBy(x => x.Artist.SortName).ThenBy(x => x.Artist.Name);
        if (take.HasValue && take.Value > 0)
        {
            query = query.Take(take.Value);
        }

        var relations = await query.ToArrayAsync(cancellationToken).ConfigureAwait(false);
        return new MelodeeModels.OperationResult<ArtistRelation[]> { Data = relations };
    }

    public async Task<MelodeeModels.OperationResult<bool>> DeleteAllArtistImagesAsync(
        int artistId,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Expression(x => x < 1, artistId, nameof(artistId));

        var artist = await GetAsync(artistId, cancellationToken).ConfigureAwait(false);
        if (!artist.IsSuccess || artist.Data == null)
        {
            return new MelodeeModels.OperationResult<bool>("Unknown artist.") { Data = false };
        }

        var artistDir = artist.Data.ToFileSystemDirectoryInfo();
        fileSystemService.DeleteAllFilesForExtension(artistDir, "*.jpg");

        await using (var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var now = Instant.FromDateTimeUtc(DateTime.UtcNow);
            await scopedContext.Artists
                .Where(x => x.Id == artist.Data.Id)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.LastUpdatedAt, now)
                    .SetProperty(x => x.ImageCount, artistDir.ImageFilesFound), cancellationToken)
                .ConfigureAwait(false);
        }

        await ClearCacheAsync(artist.Data, cancellationToken).ConfigureAwait(false);
        return new MelodeeModels.OperationResult<bool> { Data = true };
    }

    /// <summary>
    /// List starred artists for a user with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<ArtistDataInfo>> ListStarredAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserArtists
            .Where(ua => ua.UserId == userId && ua.IsStarred)
            .Include(ua => ua.Artist)
            .ThenInclude(a => a.Library)
            .AsNoTracking();

        var artistCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        ArtistDataInfo[] artists = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserArtists = await baseQuery
                .OrderByDescending(ua => ua.StarredAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            artists = rawUserArtists.Select(ua => new ArtistDataInfo(
                ua.Artist.Id,
                ua.Artist.ApiKey,
                ua.Artist.IsLocked,
                ua.Artist.LibraryId,
                ua.Artist.Library.Path,
                ua.Artist.Name,
                ua.Artist.NameNormalized,
                ua.Artist.AlternateNames ?? string.Empty,
                ua.Artist.Directory,
                ua.Artist.AlbumCount,
                ua.Artist.SongCount,
                ua.Artist.CreatedAt,
                ua.Artist.Tags ?? string.Empty,
                ua.Artist.LastUpdatedAt,
                ua.Artist.LastPlayedAt,
                ua.Artist.PlayedCount,
                ua.Artist.CalculatedRating
            )
            {
                UserStarred = ua.IsStarred,
                UserRating = ua.Rating,
                Biography = ua.Artist.Biography
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<ArtistDataInfo>
        {
            TotalCount = artistCount,
            TotalPages = pagedRequest.TotalPages(artistCount),
            Data = artists
        };
    }

    /// <summary>
    /// List hated artists for a user with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<ArtistDataInfo>> ListHatedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserArtists
            .Where(ua => ua.UserId == userId && ua.IsHated)
            .Include(ua => ua.Artist)
            .ThenInclude(a => a.Library)
            .AsNoTracking();

        var artistCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        ArtistDataInfo[] artists = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserArtists = await baseQuery
                .OrderByDescending(ua => ua.LastUpdatedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            artists = rawUserArtists.Select(ua => new ArtistDataInfo(
                ua.Artist.Id,
                ua.Artist.ApiKey,
                ua.Artist.IsLocked,
                ua.Artist.LibraryId,
                ua.Artist.Library.Path,
                ua.Artist.Name,
                ua.Artist.NameNormalized,
                ua.Artist.AlternateNames ?? string.Empty,
                ua.Artist.Directory,
                ua.Artist.AlbumCount,
                ua.Artist.SongCount,
                ua.Artist.CreatedAt,
                ua.Artist.Tags ?? string.Empty,
                ua.Artist.LastUpdatedAt,
                ua.Artist.LastPlayedAt,
                ua.Artist.PlayedCount,
                ua.Artist.CalculatedRating
            )
            {
                UserStarred = ua.IsStarred,
                UserRating = ua.Rating,
                Biography = ua.Artist.Biography
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<ArtistDataInfo>
        {
            TotalCount = artistCount,
            TotalPages = pagedRequest.TotalPages(artistCount),
            Data = artists
        };
    }

    /// <summary>
    /// List top-rated artists (4+ stars) for a user with pagination
    /// </summary>
    public async Task<MelodeeModels.PagedResult<ArtistDataInfo>> ListTopRatedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        int minRating = 4,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserArtists
            .Where(ua => ua.UserId == userId && ua.Rating >= minRating)
            .Include(ua => ua.Artist)
            .ThenInclude(a => a.Library)
            .AsNoTracking();

        var artistCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        ArtistDataInfo[] artists = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserArtists = await baseQuery
                .OrderByDescending(ua => ua.Rating)
                .ThenByDescending(ua => ua.LastUpdatedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            artists = rawUserArtists.Select(ua => new ArtistDataInfo(
                ua.Artist.Id,
                ua.Artist.ApiKey,
                ua.Artist.IsLocked,
                ua.Artist.LibraryId,
                ua.Artist.Library.Path,
                ua.Artist.Name,
                ua.Artist.NameNormalized,
                ua.Artist.AlternateNames ?? string.Empty,
                ua.Artist.Directory,
                ua.Artist.AlbumCount,
                ua.Artist.SongCount,
                ua.Artist.CreatedAt,
                ua.Artist.Tags ?? string.Empty,
                ua.Artist.LastUpdatedAt,
                ua.Artist.LastPlayedAt,
                ua.Artist.PlayedCount,
                ua.Artist.CalculatedRating
            )
            {
                UserStarred = ua.IsStarred,
                UserRating = ua.Rating,
                Biography = ua.Artist.Biography
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<ArtistDataInfo>
        {
            TotalCount = artistCount,
            TotalPages = pagedRequest.TotalPages(artistCount),
            Data = artists
        };
    }

    /// <summary>
    /// List all rated artists for a user with pagination, sorted by rating descending
    /// </summary>
    public async Task<MelodeeModels.PagedResult<ArtistDataInfo>> ListRatedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var baseQuery = scopedContext.UserArtists
            .Where(ua => ua.UserId == userId && ua.Rating > 0)
            .Include(ua => ua.Artist)
            .ThenInclude(a => a.Library)
            .AsNoTracking();

        var artistCount = await baseQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        ArtistDataInfo[] artists = [];

        if (!pagedRequest.IsTotalCountOnlyRequest)
        {
            var rawUserArtists = await baseQuery
                .OrderByDescending(ua => ua.Rating)
                .ThenByDescending(ua => ua.LastUpdatedAt)
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToArrayAsync(cancellationToken)
                .ConfigureAwait(false);

            artists = rawUserArtists.Select(ua => new ArtistDataInfo(
                ua.Artist.Id,
                ua.Artist.ApiKey,
                ua.Artist.IsLocked,
                ua.Artist.LibraryId,
                ua.Artist.Library.Path,
                ua.Artist.Name,
                ua.Artist.NameNormalized,
                ua.Artist.AlternateNames ?? string.Empty,
                ua.Artist.Directory,
                ua.Artist.AlbumCount,
                ua.Artist.SongCount,
                ua.Artist.CreatedAt,
                ua.Artist.Tags ?? string.Empty,
                ua.Artist.LastUpdatedAt,
                ua.Artist.LastPlayedAt,
                ua.Artist.PlayedCount,
                ua.Artist.CalculatedRating
            )
            {
                UserStarred = ua.IsStarred,
                UserRating = ua.Rating,
                Biography = ua.Artist.Biography
            }).ToArray();
        }

        return new MelodeeModels.PagedResult<ArtistDataInfo>
        {
            TotalCount = artistCount,
            TotalPages = pagedRequest.TotalPages(artistCount),
            Data = artists
        };
    }

    /// <summary>
    /// List recently played artists for a user with pagination.
    /// Artists are considered "recently played" based on songs the user has played from that artist.
    /// </summary>
    public async Task<MelodeeModels.PagedResult<ArtistDataInfo>> ListRecentlyPlayedAsync(
        MelodeeModels.PagedRequest pagedRequest,
        int userId,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        // Get distinct artists from user's recently played songs
        var recentlyPlayedArtistIds = await scopedContext.UserSongs
            .Where(us => us.UserId == userId && us.LastPlayedAt != null)
            .Include(us => us.Song)
            .ThenInclude(s => s.Album)
            .Select(us => new { us.Song.Album.ArtistId, us.LastPlayedAt })
            .GroupBy(x => x.ArtistId)
            .Select(g => new { ArtistId = g.Key, LastPlayedAt = g.Max(x => x.LastPlayedAt) })
            .OrderByDescending(x => x.LastPlayedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var artistCount = recentlyPlayedArtistIds.Count;

        ArtistDataInfo[] artists = [];

        if (!pagedRequest.IsTotalCountOnlyRequest && artistCount > 0)
        {
            var pagedArtistData = recentlyPlayedArtistIds
                .Skip(pagedRequest.SkipValue)
                .Take(pagedRequest.TakeValue)
                .ToList();

            var artistIds = pagedArtistData.Select(x => x.ArtistId).ToArray();

            var rawArtists = await scopedContext.Artists
                .Where(a => artistIds.Contains(a.Id))
                .Include(a => a.Library)
                .Include(a => a.UserArtists.Where(ua => ua.UserId == userId))
                .AsNoTracking()
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            // Preserve the order from recentlyPlayedArtistIds
            var artistDict = rawArtists.ToDictionary(a => a.Id);
            artists = pagedArtistData
                .Where(x => artistDict.ContainsKey(x.ArtistId))
                .Select(x =>
                {
                    var artist = artistDict[x.ArtistId];
                    var userArtist = artist.UserArtists.FirstOrDefault();
                    return new ArtistDataInfo(
                        artist.Id,
                        artist.ApiKey,
                        artist.IsLocked,
                        artist.LibraryId,
                        artist.Library.Path,
                        artist.Name,
                        artist.NameNormalized,
                        artist.AlternateNames ?? string.Empty,
                        artist.Directory,
                        artist.AlbumCount,
                        artist.SongCount,
                        artist.CreatedAt,
                        artist.Tags ?? string.Empty,
                        artist.LastUpdatedAt,
                        x.LastPlayedAt,
                        artist.PlayedCount,
                        artist.CalculatedRating
                    )
                    {
                        UserStarred = userArtist?.IsStarred ?? false,
                        UserRating = userArtist?.Rating ?? 0,
                        Biography = artist.Biography
                    };
                }).ToArray();
        }

        return new MelodeeModels.PagedResult<ArtistDataInfo>
        {
            TotalCount = artistCount,
            TotalPages = pagedRequest.TotalPages(artistCount),
            Data = artists
        };
    }

    public async Task<MelodeeModels.OperationResult<Artist[]>> ListByGenreAsync(
        string[] genres,
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (genres == null || genres.Length == 0)
        {
            return new MelodeeModels.OperationResult<Artist[]> { Data = [] };
        }

        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var normalizedGenres = genres.Select(g => g.ToUpperInvariant()).ToArray();

        // Artists don't have genres directly, so find artists via their albums
        var artistIds = await scopedContext.Albums
            .AsNoTracking()
            .Where(a => a.Genres != null && a.Genres.Any(g => normalizedGenres.Contains(g.ToUpper())))
            .Select(a => a.ArtistId)
            .Distinct()
            .Take(limit)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        var artists = await scopedContext.Artists
            .AsNoTracking()
            .Where(a => artistIds.Contains(a.Id))
            .OrderByDescending(a => a.PlayedCount)
            .ThenBy(a => a.Name)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return new MelodeeModels.OperationResult<Artist[]> { Data = artists };
    }

    /// <summary>
    /// Validates all albums for a given artist by checking their melodee.json files and directory structure.
    /// </summary>
    public async Task<MelodeeModels.OperationResult<Plugins.Validation.Models.ArtistAlbumsValidationResult?>> ValidateArtistAlbumsAsync(
        Guid artistApiKey,
        CancellationToken cancellationToken = default)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        var artist = await scopedContext.Artists
            .AsNoTracking()
            .Include(a => a.Library)
            .Include(a => a.Albums)
            .FirstOrDefaultAsync(a => a.ApiKey == artistApiKey, cancellationToken)
            .ConfigureAwait(false);

        if (artist == null)
        {
            return new MelodeeModels.OperationResult<Plugins.Validation.Models.ArtistAlbumsValidationResult?>([$"Artist with ApiKey [{artistApiKey}] not found."])
            {
                Data = null
            };
        }

        var config = await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        var albumValidator = new Plugins.Validation.AlbumValidator(config);

        var albumResults = new List<Plugins.Validation.Models.AlbumValidationDetail>();
        var validCount = 0;
        var invalidCount = 0;

        foreach (var dbAlbum in artist.Albums)
        {
            var albumDirectory = Path.Combine(artist.Library.Path, artist.Directory, dbAlbum.Directory);
            var melodeeFilePath = Path.Combine(albumDirectory, MelodeeModels.Album.JsonFileName);
            var directoryExists = Directory.Exists(albumDirectory);
            var hasCoverImage = false;

            MelodeeModels.Album? album = null;
            Plugins.Validation.Models.AlbumValidationResult? validationResult = null;
            var messages = new List<MelodeeModels.Validation.ValidationResultMessage>();

            if (!directoryExists)
            {
                messages.Add(new MelodeeModels.Validation.ValidationResultMessage
                {
                    Message = $"Album directory does not exist: {albumDirectory}",
                    Severity = MelodeeModels.Validation.ValidationResultMessageSeverity.Critical
                });
                invalidCount++;
            }
            else
            {
                if (File.Exists(melodeeFilePath))
                {
                    try
                    {
                        album = await MelodeeModels.Album.DeserializeAndInitializeAlbumAsync(serializer, melodeeFilePath, cancellationToken).ConfigureAwait(false);
                        if (album != null)
                        {
                            var result = albumValidator.ValidateAlbum(album);
                            validationResult = result.Data;
                            if (result.Data?.Messages != null)
                            {
                                messages.AddRange(result.Data.Messages);
                            }
                        }
                        else
                        {
                            messages.Add(new MelodeeModels.Validation.ValidationResultMessage
                            {
                                Message = "Failed to deserialize melodee.json file",
                                Severity = MelodeeModels.Validation.ValidationResultMessageSeverity.Critical
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning(ex, "Failed to validate album [{AlbumName}] at [{Path}]", dbAlbum.Name, melodeeFilePath);
                        messages.Add(new MelodeeModels.Validation.ValidationResultMessage
                        {
                            Message = $"Error reading melodee.json: {ex.Message}",
                            Severity = MelodeeModels.Validation.ValidationResultMessageSeverity.Critical
                        });
                    }
                }
                else
                {
                    messages.Add(new MelodeeModels.Validation.ValidationResultMessage
                    {
                        Message = $"melodee.json file not found at: {melodeeFilePath}",
                        Severity = MelodeeModels.Validation.ValidationResultMessageSeverity.Critical
                    });
                }

                hasCoverImage = (dbAlbum.ImageCount ?? 0) > 0 ||
                                (album?.Images?.Count() ?? 0) > 0 ||
                                Directory.GetFiles(albumDirectory, "*.jpg", SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(albumDirectory, "*.jpeg", SearchOption.TopDirectoryOnly))
                    .Concat(Directory.GetFiles(albumDirectory, "*.png", SearchOption.TopDirectoryOnly))
                    .Concat(Directory.GetFiles(albumDirectory, "*.webp", SearchOption.TopDirectoryOnly))
                    .Any();

                if (!hasCoverImage)
                {
                    messages.Add(new MelodeeModels.Validation.ValidationResultMessage
                    {
                        Message = "Album does not have cover image",
                        Severity = MelodeeModels.Validation.ValidationResultMessageSeverity.Undesired
                    });
                }

                var isValid = directoryExists &&
                              messages.All(m => m.Severity != MelodeeModels.Validation.ValidationResultMessageSeverity.Critical) &&
                              (validationResult?.AlbumStatus == Enums.AlbumStatus.Ok || validationResult == null && messages.Count == 0);

                if (isValid)
                {
                    validCount++;
                }
                else
                {
                    invalidCount++;
                }
            }

            albumResults.Add(new Plugins.Validation.Models.AlbumValidationDetail
            {
                AlbumApiKey = dbAlbum.ApiKey,
                AlbumName = dbAlbum.Name,
                ReleaseYear = dbAlbum.ReleaseDate.Year > 0 ? dbAlbum.ReleaseDate.Year : null,
                IsValid = directoryExists && messages.All(m => m.Severity != MelodeeModels.Validation.ValidationResultMessageSeverity.Critical),
                Status = validationResult?.AlbumStatus ?? (directoryExists ? Enums.AlbumStatus.New : Enums.AlbumStatus.Invalid),
                StatusReasons = validationResult?.AlbumStatusReasons ?? (directoryExists ? Enums.AlbumNeedsAttentionReasons.NotSet : Enums.AlbumNeedsAttentionReasons.AlbumCannotBeLoaded),
                Messages = messages,
                DirectoryExists = directoryExists,
                HasCoverImage = hasCoverImage,
                DirectoryPath = albumDirectory
            });
        }

        var result2 = new Plugins.Validation.Models.ArtistAlbumsValidationResult
        {
            ArtistApiKey = artistApiKey,
            ArtistName = artist.Name,
            TotalAlbums = artist.Albums.Count,
            ValidAlbums = validCount,
            InvalidAlbums = invalidCount,
            AlbumResults = albumResults.OrderBy(a => a.IsValid).ThenBy(a => a.AlbumName).ToList()
        };

        Logger.Information("Validated {Total} albums for artist [{ArtistName}]: {Valid} valid, {Invalid} invalid",
            result2.TotalAlbums, artist.Name, result2.ValidAlbums, result2.InvalidAlbums);

        return new MelodeeModels.OperationResult<Plugins.Validation.Models.ArtistAlbumsValidationResult?> { Data = result2 };
    }
}
