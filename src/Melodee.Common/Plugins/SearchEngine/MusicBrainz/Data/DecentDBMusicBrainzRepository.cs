using System.Collections.Concurrent;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using Album = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Album;
using Artist = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Artist;
using Directory = System.IO.Directory;

namespace Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;

/// <summary>
///     DecentDB backend database created from MusicBrainz data dumps using Entity Framework Core.
///     Uses pure EF Core queries for search (no Lucene dependency).
///     <remarks>
///         See https://metabrainz.org/datasets/postgres-dumps#musicbrainz
///     </remarks>
/// </summary>
public class DecentDBMusicBrainzRepository(
    ILogger logger,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MusicBrainzDbContext> dbContextFactory) : IMusicBrainzRepository
{
    private const int CacheMaxSize = 10000;
    private const int CacheExpirationMinutes = 60;

    private static readonly ConcurrentDictionary<string, CachedSearchResult> SearchCache = new();

    private sealed record CachedSearchResult(PagedResult<ArtistSearchResult> Result, DateTime CachedAt);

    private static void CleanExpiredCache()
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-CacheExpirationMinutes);
        var expiredKeys = SearchCache.Where(kvp => kvp.Value.CachedAt < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var key in expiredKeys)
        {
            SearchCache.TryRemove(key, out _);
        }

        if (SearchCache.Count > CacheMaxSize)
        {
            var toRemove = SearchCache.OrderBy(kvp => kvp.Value.CachedAt).Take(SearchCache.Count - CacheMaxSize + 100).Select(kvp => kvp.Key).ToList();
            foreach (var key in toRemove)
            {
                SearchCache.TryRemove(key, out _);
            }
        }
    }

    public async Task<Album?> GetAlbumByMusicBrainzId(Guid musicBrainzId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var musicBrainzIdRaw = musicBrainzId.ToString();

        return await context.Albums
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.MusicBrainzIdRaw == musicBrainzIdRaw, cancellationToken);
    }

    public async Task<PagedResult<ArtistSearchResult>> SearchArtist(
        ArtistQuery query,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var startTicks = Stopwatch.GetTimestamp();
        var maxSearchResults = 10;

        var cacheKey = $"{query.NameNormalized}:{query.MusicBrainzIdValue}:{maxResults}";
        if (SearchCache.TryGetValue(cacheKey, out var cached) &&
            cached.CachedAt > DateTime.UtcNow.AddMinutes(-CacheExpirationMinutes))
        {
            logger.Debug("[{RepoName}] Cache HIT for [{Query}]", nameof(DecentDBMusicBrainzRepository), LogSanitizer.Sanitize(query.NameNormalized));
            return cached.Result;
        }

        var data = new List<ArtistSearchResult>();
        var totalCount = 0;

        try
        {
            using (Operation.At(LogEventLevel.Debug).Time("[{Name}] SearchArtist [{ArtistQuery}]",
                       nameof(DecentDBMusicBrainzRepository), query))
            {
                await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

                await MusicBrainzSchemaInitializer.EnsureArtistAliasTableAsync(context, cancellationToken);

                Artist[] foundArtists = [];
                var mbIdRaw = query.MusicBrainzIdValue?.ToString();

                if (!string.IsNullOrEmpty(query.NameNormalized))
                {
                    foundArtists = await SearchByNameAsync(context, query, maxSearchResults, cancellationToken);

                    if (foundArtists.Length > 0 && !string.IsNullOrEmpty(mbIdRaw))
                    {
                        var exactIdMatch = foundArtists.FirstOrDefault(a => a.MusicBrainzIdRaw == mbIdRaw);
                        if (exactIdMatch != null)
                        {
                            foundArtists = [exactIdMatch];
                        }
                    }
                }

                if (foundArtists.Length == 0 && !string.IsNullOrEmpty(mbIdRaw))
                {
                    foundArtists = await context.Artists
                        .AsNoTracking()
                        .Where(a => a.MusicBrainzIdRaw == mbIdRaw)
                        .Take(maxSearchResults)
                        .ToArrayAsync(cancellationToken);
                }

                logger.Debug("[{RepoName}] Search found [{Count}] artists for [{NameNormalized}]",
                    nameof(DecentDBMusicBrainzRepository), foundArtists.Length, LogSanitizer.Sanitize(query.NameNormalized));

                if (foundArtists.Length > 0)
                {
                    var artistIds = foundArtists.Select(a => a.MusicBrainzArtistId).ToArray();
                    var allAlbums = await context.Albums
                        .AsNoTracking()
                        .Where(a => artistIds.Contains(a.MusicBrainzArtistId) && a.ReleaseDate > DateTime.MinValue)
                        .ToArrayAsync(cancellationToken);

                    var albumsByArtist = allAlbums
                        .GroupBy(a => a.MusicBrainzArtistId)
                        .ToDictionary(g => g.Key, g => g
                            .GroupBy(x => x.ReleaseGroupMusicBrainzIdRaw)
                            .Select(rg => rg.OrderBy(x => x.ReleaseDate).First())
                            .ToArray());
                    var aliasValuesByArtist = await LoadAliasValuesByArtistAsync(context, artistIds, cancellationToken);

                    foreach (var artist in foundArtists)
                    {
                        var alternateNamesValues = artist.AlternateNamesValues
                            .Concat(aliasValuesByArtist.GetValueOrDefault(artist.MusicBrainzArtistId, []))
                            .Distinct(StringComparer.Ordinal)
                            .ToArray();
                        var rank = artist.NameNormalized == query.NameNormalized ? 10 : 1;
                        if (alternateNamesValues.Contains(query.NameNormalized))
                        {
                            rank++;
                        }

                        if (alternateNamesValues.Contains(query.Name.CleanString().ToNormalizedString()))
                        {
                            rank++;
                        }

                        if (alternateNamesValues.Contains(query.NameNormalizedReversed))
                        {
                            rank++;
                        }

                        var artistAlbums = albumsByArtist.GetValueOrDefault(artist.MusicBrainzArtistId, []);
                        rank += artistAlbums.Length;

                        if (query.AlbumKeyValues != null)
                        {
                            rank += artistAlbums.Length;
                            foreach (var albumKeyValues in query.AlbumKeyValues)
                            {
                                rank += artistAlbums.Count(x =>
                                    x.ReleaseDate.Year.ToString() == albumKeyValues.Key &&
                                    x.NameNormalized == albumKeyValues.Value.ToNormalizedString());
                            }
                        }

                        data.Add(new ArtistSearchResult
                        {
                            AlternateNames = alternateNamesValues,
                            FromPlugin =
                                $"{nameof(MusicBrainzArtistSearchEnginePlugin)}:{nameof(DecentDBMusicBrainzRepository)}",
                            UniqueId = SafeParser.Hash(artist.MusicBrainzId.ToString()),
                            Rank = rank,
                            Name = artist.Name,
                            SortName = artist.SortName,
                            MusicBrainzId = artist.MusicBrainzId,
                            AlbumCount = artistAlbums.Count(x => x.ReleaseDate > DateTime.MinValue),
                            Releases = artistAlbums
                                .Where(x => x.ReleaseDate > DateTime.MinValue)
                                .OrderBy(x => x.ReleaseDate)
                                .ThenBy(x => x.SortName).Select(x => new AlbumSearchResult
                                {
                                    AlbumType = SafeParser.ToEnum<AlbumType>(x.ReleaseType),
                                    ReleaseDate = x.ReleaseDate.ToString("o", CultureInfo.InvariantCulture),
                                    UniqueId = SafeParser.Hash(x.MusicBrainzId.ToString()),
                                    Name = x.Name,
                                    NameNormalized = x.NameNormalized,
                                    MusicBrainzResourceGroupId = x.ReleaseGroupMusicBrainzId,
                                    SortName = x.SortName,
                                    MusicBrainzId = x.MusicBrainzId
                                }).ToArray()
                        });
                    }

                    totalCount = foundArtists.Length;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.Error(e, "[DecentDBMusicBrainzRepository] Search Engine Exception ArtistQuery [{Query}]", query.ToString());
        }

        var elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;

        var result = new PagedResult<ArtistSearchResult>
        {
            OperationTime = (long)elapsedMs * 1000,
            TotalCount = totalCount,
            TotalPages = maxResults > 0 ? SafeParser.ToNumber<int>((totalCount + maxResults - 1) / maxResults) : 0,
            Data = data.OrderByDescending(x => x.Rank).Take(Math.Max(0, maxResults)).ToArray()
        };

        SearchCache[cacheKey] = new CachedSearchResult(result, DateTime.UtcNow);

        if (SearchCache.Count > CacheMaxSize / 10 && Random.Shared.Next(100) == 0)
        {
            CleanExpiredCache();
        }

        if (data.Count > 0)
        {
            logger.Debug("[{RepoName}] SearchArtist COMPLETE: Found [{Count}] results for [{Query}] in {ElapsedMs:F1}ms. Top result: [{TopArtist}]",
                nameof(DecentDBMusicBrainzRepository), data.Count, LogSanitizer.Sanitize(query.NameNormalized), elapsedMs, LogSanitizer.Sanitize(data.First().Name));
        }
        else
        {
            logger.Debug("[{RepoName}] SearchArtist COMPLETE: NO RESULTS for [{Query}] in {ElapsedMs:F1}ms",
                nameof(DecentDBMusicBrainzRepository), LogSanitizer.Sanitize(query.NameNormalized), elapsedMs);
        }

        return result;
    }

    public async Task<OperationResult<bool>> ImportData(
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await ImportData(new MusicBrainzImportRequest(), progressCallback, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<OperationResult<bool>> ImportData(
        MusicBrainzImportRequest request,
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDBMusicBrainzRepository: ImportData (Streaming)"))
        {
            var configuration =
                await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

            var storagePath = request.StoragePath ??
                              configuration.GetValue<string>(SettingRegistry.SearchEngineMusicBrainzStoragePath);
            if (storagePath == null || !Directory.Exists(storagePath))
            {
                logger.Warning("MusicBrainz storage path is invalid [{KeyName}]",
                    SettingRegistry.SearchEngineMusicBrainzStoragePath);
                return new OperationResult<bool>
                {
                    Data = false,
                    Type = OperationResponseType.Error,
                    Errors = [new DirectoryNotFoundException(
                        $"MusicBrainz storage path does not exist: {storagePath ?? "(null)"}")]
                };
            }

            try
            {
                var importer = new DecentDBStreamingMusicBrainzImporter(logger);

                await importer.ImportAsync(
                    ct => CreateImportContextAsync(request.TargetDatabasePath, ct),
                    storagePath,
                    progressCallback,
                    cancellationToken);

                await using var context = await CreateImportContextAsync(request.TargetDatabasePath, cancellationToken)
                    .ConfigureAwait(false);
                var artistCount = await context.Artists.CountAsync(cancellationToken);
                var albumCount = await context.Albums.CountAsync(cancellationToken);

                logger.Information(
                    "DecentDBMusicBrainzRepository: Streaming import complete. Artists: {ArtistCount:N0}, Albums: {AlbumCount:N0}",
                    artistCount, albumCount);

                return new OperationResult<bool>
                {
                    Data = artistCount > 0 && albumCount > 0
                };
            }
            catch (OperationCanceledException)
            {
                logger.Warning("DecentDBMusicBrainzRepository: Import was cancelled");
                throw;
            }
            catch (Exception e)
            {
                var importException = CreateImportFailureException(e);
                logger.Error("DecentDBMusicBrainzRepository: Import failed - {Message}", importException.Message);
                logger.Debug(e, "DecentDBMusicBrainzRepository: Import failure details");
                return new OperationResult<bool>
                {
                    Data = false,
                    Type = OperationResponseType.Error,
                    Errors = [importException]
                };
            }
        }
    }

    private static Exception CreateImportFailureException(Exception exception)
    {
        if (exception is InvalidOperationException invalidOperationException &&
            invalidOperationException.Message.Contains("at most 1000 values in an IN list", StringComparison.OrdinalIgnoreCase))
        {
            return new InvalidOperationException(
                "MusicBrainz import exceeded the DecentDB IN-list limit during artist materialization. " +
                "Rebuild the CLI or server binaries and rerun with the latest importer changes.",
                exception);
        }

        return new InvalidOperationException($"MusicBrainz import failed: {exception.Message}", exception);
    }

    private async Task<MusicBrainzDbContext> CreateImportContextAsync(
        string? targetDatabasePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetDatabasePath))
        {
            return await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        }

        await using var baseContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var connectionString = baseContext.Database.GetConnectionString()
                               ?? throw new InvalidOperationException("MusicBrainzDbContext has no connection string configured.");
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };
        builder["Data Source"] = targetDatabasePath;

        var options = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB(builder.ConnectionString)
            .Options;

        return new MusicBrainzDbContext(options);
    }

    /// <summary>
    /// Multi-step pure EF Core search: exact → reversed → indexed alias lookup.
    /// </summary>
    private static async Task<Artist[]> SearchByNameAsync(
        MusicBrainzDbContext context,
        ArtistQuery query,
        int maxResults,
        CancellationToken cancellationToken)
    {
        // Step 1: Exact match on NameNormalized (index-backed)
        var artists = await context.Artists
            .AsNoTracking()
            .Where(a => a.NameNormalized == query.NameNormalized)
            .OrderBy(a => a.SortName)
            .Take(maxResults)
            .ToArrayAsync(cancellationToken);

        if (artists.Length > 0)
        {
            return artists;
        }

        // Step 2: Exact match on reversed name (index-backed)
        if (query.NameNormalizedReversed != query.NameNormalized)
        {
            artists = await context.Artists
                .AsNoTracking()
                .Where(a => a.NameNormalized == query.NameNormalizedReversed)
                .OrderBy(a => a.SortName)
                .Take(maxResults)
                .ToArrayAsync(cancellationToken);

            if (artists.Length > 0)
            {
                return artists;
            }
        }

        // Step 3: Exact alias lookup through the dedicated alias table.
        var aliasTerms = query.NameNormalizedReversed != query.NameNormalized
            ? new[] { query.NameNormalized, query.NameNormalizedReversed }
            : new[] { query.NameNormalized };

        var aliasArtistIds = await context.ArtistAliases
            .AsNoTracking()
            .Where(a => aliasTerms.Contains(a.NameNormalized))
            .Select(a => a.MusicBrainzArtistId)
            .ToArrayAsync(cancellationToken);

        if (aliasArtistIds.Length == 0)
        {
            return [];
        }

        var distinctAliasArtistIds = aliasArtistIds
            .Distinct()
            .Take(maxResults)
            .ToArray();

        return await context.Artists
            .AsNoTracking()
            .Where(a => distinctAliasArtistIds.Contains(a.MusicBrainzArtistId))
            .OrderBy(a => a.SortName)
            .Take(maxResults)
            .ToArrayAsync(cancellationToken);
    }

    private static async Task<Dictionary<long, string[]>> LoadAliasValuesByArtistAsync(
        MusicBrainzDbContext context,
        long[] artistIds,
        CancellationToken cancellationToken)
    {
        var aliasRows = await context.ArtistAliases
            .AsNoTracking()
            .Where(alias => artistIds.Contains(alias.MusicBrainzArtistId))
            .Select(alias => new
            {
                alias.MusicBrainzArtistId,
                alias.NameNormalized
            })
            .ToArrayAsync(cancellationToken);

        return aliasRows
            .GroupBy(alias => alias.MusicBrainzArtistId)
            .ToDictionary(
                grouping => grouping.Key,
                grouping => grouping
                    .Select(alias => alias.NameNormalized)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(alias => alias, StringComparer.Ordinal)
                    .ToArray());
    }
}
