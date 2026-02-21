using System.Collections.Concurrent;
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
public class DecentDbMusicBrainzRepository(
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
            logger.Debug("[{RepoName}] Cache HIT for [{Query}]", nameof(DecentDbMusicBrainzRepository), LogSanitizer.Sanitize(query.NameNormalized));
            return cached.Result;
        }

        var data = new List<ArtistSearchResult>();
        var totalCount = 0;

        try
        {
            using (Operation.At(LogEventLevel.Debug).Time("[{Name}] SearchArtist [{ArtistQuery}]",
                       nameof(DecentDbMusicBrainzRepository), query))
            {
                await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);

                Artist[] foundArtists;

                if (query.MusicBrainzIdValue != null)
                {
                    var mbIdRaw = query.MusicBrainzIdValue.Value.ToString();
                    foundArtists = await context.Artists
                        .AsNoTracking()
                        .Where(a => a.MusicBrainzIdRaw == mbIdRaw)
                        .Take(maxSearchResults)
                        .ToArrayAsync(cancellationToken);
                }
                else if (!string.IsNullOrEmpty(query.NameNormalized))
                {
                    foundArtists = await SearchByNameAsync(context, query, maxSearchResults, cancellationToken);
                }
                else
                {
                    foundArtists = [];
                }

                logger.Debug("[{RepoName}] Search found [{Count}] artists for [{NameNormalized}]",
                    nameof(DecentDbMusicBrainzRepository), foundArtists.Length, LogSanitizer.Sanitize(query.NameNormalized));

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

                    foreach (var artist in foundArtists)
                    {
                        var rank = artist.NameNormalized == query.NameNormalized ? 10 : 1;
                        if (artist.AlternateNamesValues.Contains(query.NameNormalized))
                        {
                            rank++;
                        }

                        if (artist.AlternateNamesValues.Contains(query.Name.CleanString().ToNormalizedString()))
                        {
                            rank++;
                        }

                        if (artist.AlternateNamesValues.Contains(query.NameNormalizedReversed))
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
                            AlternateNames = artist.AlternateNames?.ToTags()?.ToArray() ?? [],
                            FromPlugin =
                                $"{nameof(MusicBrainzArtistSearchEnginePlugin)}:{nameof(DecentDbMusicBrainzRepository)}",
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
            logger.Error(e, "[DecentDbMusicBrainzRepository] Search Engine Exception ArtistQuery [{Query}]", query.ToString());
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
                nameof(DecentDbMusicBrainzRepository), data.Count, LogSanitizer.Sanitize(query.NameNormalized), elapsedMs, LogSanitizer.Sanitize(data.First().Name));
        }
        else
        {
            logger.Debug("[{RepoName}] SearchArtist COMPLETE: NO RESULTS for [{Query}] in {ElapsedMs:F1}ms",
                nameof(DecentDbMusicBrainzRepository), LogSanitizer.Sanitize(query.NameNormalized), elapsedMs);
        }

        return result;
    }

    public async Task<OperationResult<bool>> ImportData(
        ImportProgressCallback? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        using (Operation.At(LogEventLevel.Debug).Time("DecentDbMusicBrainzRepository: ImportData (Streaming)"))
        {
            var configuration =
                await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

            var storagePath = configuration.GetValue<string>(SettingRegistry.SearchEngineMusicBrainzStoragePath);
            if (storagePath == null || !Directory.Exists(storagePath))
            {
                logger.Warning("MusicBrainz storage path is invalid [{KeyName}]",
                    SettingRegistry.SearchEngineMusicBrainzStoragePath);
                return new OperationResult<bool>
                {
                    Data = false
                };
            }

            await using var context = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await context.Database.EnsureCreatedAsync(cancellationToken);

            try
            {
                var importer = new DecentDbStreamingMusicBrainzImporter(logger);

                await importer.ImportAsync(
                    context,
                    storagePath,
                    progressCallback,
                    cancellationToken);

                var artistCount = await context.Artists.CountAsync(cancellationToken);
                var albumCount = await context.Albums.CountAsync(cancellationToken);

                logger.Information(
                    "DecentDbMusicBrainzRepository: Streaming import complete. Artists: {ArtistCount:N0}, Albums: {AlbumCount:N0}",
                    artistCount, albumCount);

                return new OperationResult<bool>
                {
                    Data = artistCount > 0 && albumCount > 0
                };
            }
            catch (Exception e)
            {
                logger.Error(e, "DecentDbMusicBrainzRepository: Import failed");
                return new OperationResult<bool>
                {
                    Data = false
                };
            }
        }
    }

    /// <summary>
    /// Multi-step pure EF Core search: exact → reversed → alternate names → word tokenized.
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

        // Step 3: Search in alternate names
        artists = await context.Artists
            .AsNoTracking()
            .Where(a => a.AlternateNames != null && a.AlternateNames.Contains(query.NameNormalized))
            .OrderBy(a => a.SortName)
            .Take(maxResults)
            .ToArrayAsync(cancellationToken);

        if (artists.Length > 0)
        {
            return artists;
        }

        // Step 4: Word tokenization search for multi-word queries
        var words = ExtractWordsFromNormalized(query.NameNormalized);
        if (words.Length > 1)
        {
            var significantWords = words.Where(w => w.Length >= 4).Take(3).ToArray();
            if (significantWords.Length > 0)
            {
                var wordResults = new List<Artist>();
                foreach (var word in significantWords)
                {
                    var wordArtists = await context.Artists
                        .AsNoTracking()
                        .Where(a => a.NameNormalized.Contains(word) ||
                                    (a.AlternateNames != null && a.AlternateNames.Contains(word)))
                        .OrderBy(a => a.SortName)
                        .Take(maxResults)
                        .ToArrayAsync(cancellationToken);

                    wordResults.AddRange(wordArtists);
                }

                artists = wordResults
                    .DistinctBy(a => a.Id)
                    .Take(maxResults)
                    .ToArray();
            }
        }

        return artists;
    }

    /// <summary>
    /// Extracts individual words from a normalized artist name for tokenized search.
    /// </summary>
    /// <example>
    /// "SMOKEYROBINSONMIRACLES" -> ["SMOKEY", "ROBINSON", "MIRACLES"]
    /// "ARMINVANBUURENANDDJSHAH" -> ["ARMIN", "VAN", "BUUREN", "AND", "DJ", "SHAH"]
    /// </example>
    private static string[] ExtractWordsFromNormalized(string normalizedName)
    {
        if (string.IsNullOrEmpty(normalizedName) || normalizedName.Length < 4)
        {
            return [];
        }

        var words = new List<string>();

        string[] commonPatterns =
        [
            "AND", "THE", "FEAT", "FEATURING", "WITH", "VS", "VERSUS",
            "DJ", "MC", "DR", "MR", "MRS", "MS",
            "VAN", "VON", "DE", "LA", "LE", "EL",
            "BAND", "GROUP", "TRIO", "QUARTET", "QUINTET", "ORCHESTRA", "ENSEMBLE",
            "PROJECT", "EXPERIENCE", "COLLECTIVE", "FAMILY", "BROTHERS", "SISTERS"
        ];

        var remaining = normalizedName;

        foreach (var pattern in commonPatterns.OrderByDescending(p => p.Length))
        {
            var idx = remaining.IndexOf(pattern, StringComparison.Ordinal);
            if (idx >= 0)
            {
                if (idx > 0)
                {
                    var before = remaining[..idx];
                    if (before.Length >= 3)
                    {
                        words.Add(before);
                    }
                }

                words.Add(pattern);

                if (idx + pattern.Length < remaining.Length)
                {
                    var after = remaining[(idx + pattern.Length)..];
                    if (after.Length >= 3)
                    {
                        words.AddRange(ExtractWordsFromNormalized(after));
                    }
                }

                return words.Distinct().Where(w => w.Length >= 3).ToArray();
            }
        }

        if (normalizedName.Length <= 12)
        {
            return [normalizedName];
        }

        words.Add(normalizedName);

        foreach (var splitLen in new[] { 5, 6, 7, 8 })
        {
            if (normalizedName.Length > splitLen + 3)
            {
                var firstPart = normalizedName[..splitLen];
                var secondPart = normalizedName[splitLen..];

                if (firstPart.Length >= 4 && secondPart.Length >= 4)
                {
                    words.Add(firstPart);
                    words.Add(secondPart);
                }
            }
        }

        return words.Distinct().Where(w => w.Length >= 3).ToArray();
    }
}
