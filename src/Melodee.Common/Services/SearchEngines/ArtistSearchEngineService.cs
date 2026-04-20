using System.Diagnostics;
using Ardalis.GuardClauses;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData.Extension;
using Melodee.Common.Models.SpecialArtists;
using Melodee.Common.Plugins.SearchEngine;
using Melodee.Common.Plugins.SearchEngine.Discogs;
using Melodee.Common.Plugins.SearchEngine.ITunes;
using Melodee.Common.Plugins.SearchEngine.LastFm;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.Spotify;
using Melodee.Common.Plugins.SearchEngine.WikiData;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using SerilogTimings;
using Album = Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData.Album;
using Artist = Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData.Artist;
using StringExtensions = Melodee.Common.Extensions.StringExtensions;

namespace Melodee.Common.Services.SearchEngines;

public class ArtistSearchEngineService(
    ILogger logger,
    ICacheManager cacheManager,
    SettingService settingService,
    ISpotifyClientBuilder spotifyClientBuilder,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> melodeeDbContextFactory,
    IDbContextFactory<ArtistSearchEngineServiceDbContext> artistSearchEngineServiceDbContextFactory,
    IMusicBrainzRepository musicBrainzRepository,
    ISerializer serializer,
    IHttpClientFactory httpClientFactory)
    : ServiceBase(logger, cacheManager, melodeeDbContextFactory)
{
    private IArtistSearchEnginePlugin[] _artistSearchEnginePlugins = [];
    private IArtistTopSongsSearchEnginePlugin[] _artistTopSongsSearchEnginePlugins = [];
    private IMelodeeConfiguration _configuration = new MelodeeConfiguration([]);
    private bool _initialized;
    private readonly ArtistSearchCache _searchCache = new();

    /// <summary>
    /// MusicBrainz rate limit: 1 request per second.
    /// </summary>
    private const string MusicBrainzProvider = "MusicBrainz";
    private static readonly TimeSpan MusicBrainzRateLimit = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Spotify concurrency limit (conservative default).
    /// </summary>
    private const string SpotifyProvider = "Spotify";
    private const int SpotifyMaxConcurrency = 2;

    public async Task InitializeAsync(IMelodeeConfiguration? configuration = null,
        CancellationToken cancellationToken = default)
    {
        _configuration = configuration ?? await configurationFactory.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);

        // Subscribe to configuration changes to reload plugins
        configurationFactory.ConfigurationChanged += OnConfigurationChanged;

        await InitializePluginsAsync(cancellationToken).ConfigureAwait(false);

        await using (var scopedContext = await artistSearchEngineServiceDbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            await scopedContext.Database.EnsureCreatedAsync(cancellationToken);
            await EnsureHousekeepingIndexesAsync(scopedContext, cancellationToken).ConfigureAwait(false);
        }

        _initialized = true;
    }

    private static async Task EnsureHousekeepingIndexesAsync(
        ArtistSearchEngineServiceDbContext context,
        CancellationToken cancellationToken)
    {
        if (!context.Database.IsRelational())
        {
            return;
        }

        await context.Database.ExecuteSqlRawAsync(
            """
            CREATE INDEX IF NOT EXISTS "IX_Artists_IsLocked_LastRefreshed"
            ON "Artists" ("IsLocked", "LastRefreshed")
            """,
            cancellationToken);
    }

    private void OnConfigurationChanged(object? sender, EventArgs e) => _ = OnConfigurationChangedAsync(sender, e);

    private async Task OnConfigurationChangedAsync(object? sender, EventArgs e)
    {
        try
        {
            Logger.Information("[{Name}] Configuration changed, reinitializing artist search engine plugins",
                nameof(ArtistSearchEngineService));

            _configuration = await configurationFactory.GetConfigurationAsync().ConfigureAwait(false);

            await InitializePluginsAsync(CancellationToken.None).ConfigureAwait(false);

            _searchCache.Clear();

            Logger.Information("[{Name}] Artist search engine plugins reinitialized after configuration change",
                nameof(ArtistSearchEngineService));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "[{Name}] Failed to reinitialize artist search engine plugins after configuration change",
                nameof(ArtistSearchEngineService));
        }
    }

    private async Task InitializePluginsAsync(CancellationToken cancellationToken)
    {
        _artistSearchEnginePlugins =
        [
            new MelodeeArtistSearchEnginePlugin(ContextFactory),
            new MusicBrainzArtistSearchEnginePlugin(musicBrainzRepository)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.SearchEngineMusicBrainzEnabled)
            },
            new Spotify(Log.Logger, _configuration, CacheManager, spotifyClientBuilder, settingService, ContextFactory)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.SearchEngineSpotifyEnabled)
            },
            new ITunesSearchEngine(Log.Logger, serializer, httpClientFactory, CacheManager)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.SearchEngineITunesEnabled)
            },
            new LastFm(Log.Logger, _configuration, serializer, httpClientFactory, CacheManager)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.SearchEngineLastFmEnabled)
            },
            new DiscogsArtistSearchEnginePlugin(Log.Logger, _configuration, httpClientFactory, CacheManager)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.SearchEngineDiscogsEnabled)
            },
            new WikiDataArtistSearchEnginePlugin(Log.Logger, _configuration, httpClientFactory, CacheManager)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.SearchEngineWikiDataEnabled)
            }
        ];

        _artistTopSongsSearchEnginePlugins =
        [
            new MelodeeArtistSearchEnginePlugin(ContextFactory),
            new Spotify(Log.Logger, _configuration, CacheManager, spotifyClientBuilder, settingService, ContextFactory)
            {
                IsEnabled = _configuration.GetValue<bool>(SettingRegistry.SearchEngineSpotifyEnabled)
            }
        ];
    }

    private void CheckInitialized()
    {
        if (!_initialized)
        {
            throw new InvalidOperationException($"{nameof(ArtistSearchEngineService)} is not initialized.");
        }
    }

    public async Task<PagedResult<Artist>> ListAsync(
        PagedRequest pagedRequest,
        CancellationToken cancellationToken = default)
    {
        int totalCount;
        Artist[] artists = [];

        using (Operation.At(LogEventLevel.Debug)
                   .Time("[{ServiceName}:{ServiceMethod}] : Data [{EventData}]", nameof(ArtistSearchEngineService), nameof(ListAsync), pagedRequest.ToString()))
        {
            await using (var scopedContext = await artistSearchEngineServiceDbContextFactory
                             .CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                // Build the base query with filters
                var query = scopedContext.Artists.AsNoTracking();

                // Apply filters from PagedRequest.FilterBy if any
                if (pagedRequest.FilterBy?.Length > 0)
                {
                    foreach (var filter in pagedRequest.FilterBy)
                    {
                        var filterValue = filter.Value?.ToString() ?? string.Empty;
                        var filterValueLower = filterValue.ToLowerInvariant();

                        // Apply filters based on property name and operator
                        query = filter.PropertyName.ToLower() switch
                        {
                            "name" => filter.OperatorValue.ToUpper() switch
                            {
                                "LIKE" => ApplyLikeFilter(query, x => x.Name, filter.Operator, filterValueLower),
                                "=" => query.Where(x => x.Name == filterValue),
                                "!=" => query.Where(x => x.Name != filterValue),
                                _ => query
                            },
                            "namenormalized" => filter.OperatorValue.ToUpper() switch
                            {
                                "LIKE" => ApplyLikeFilter(query, x => x.NameNormalized, filter.Operator,
                                    filterValueLower),
                                "=" => query.Where(x => x.NameNormalized == filterValue),
                                "!=" => query.Where(x => x.NameNormalized != filterValue),
                                _ => query
                            },
                            "sortname" => filter.OperatorValue.ToUpper() switch
                            {
                                "LIKE" => ApplyLikeFilter(query, x => x.SortName, filter.Operator, filterValueLower),
                                "=" => query.Where(x => x.SortName == filterValue),
                                "!=" => query.Where(x => x.SortName != filterValue),
                                _ => query
                            },
                            "musicbrainzid" => filter.OperatorValue.ToUpper() switch
                            {
                                "=" => query.Where(x => x.MusicBrainzId.ToString() == filterValue),
                                "!=" => query.Where(x => x.MusicBrainzId.ToString() != filterValue),
                                _ => query
                            },
                            "spotifyid" => filter.OperatorValue.ToUpper() switch
                            {
                                "=" => query.Where(x => x.SpotifyId == filterValue),
                                "!=" => query.Where(x => x.SpotifyId != filterValue),
                                "LIKE" => ApplyLikeFilter(query, x => x.SpotifyId!, filter.Operator, filterValueLower),
                                _ => query
                            },
                            _ => query
                        };
                    }
                }

                // Get total count
                totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

                if (!pagedRequest.IsTotalCountOnlyRequest)
                {
                    // Apply ordering
                    if (pagedRequest.OrderBy?.Count > 0)
                    {
                        var firstOrderBy = pagedRequest.OrderBy.First();
                        var isDescending = firstOrderBy.Value.ToUpper() == "DESC";

                        query = firstOrderBy.Key.ToLower() switch
                        {
                            "id" => isDescending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id),
                            "name" => isDescending ? query.OrderByDescending(x => x.Name) : query.OrderBy(x => x.Name),
                            "namenormalized" => isDescending
                                ? query.OrderByDescending(x => x.NameNormalized)
                                : query.OrderBy(x => x.NameNormalized),
                            "sortname" => isDescending
                                ? query.OrderByDescending(x => x.SortName)
                                : query.OrderBy(x => x.SortName),
                            _ => isDescending ? query.OrderByDescending(x => x.Id) : query.OrderBy(x => x.Id)
                        };

                        // Apply additional ordering if present
                        foreach (var orderBy in pagedRequest.OrderBy.Skip(1))
                        {
                            var isDesc = orderBy.Value.ToUpper() == "DESC";
                            var orderedQuery = (IOrderedQueryable<Artist>)query;

                            query = orderBy.Key.ToLower() switch
                            {
                                "id" => isDesc
                                    ? orderedQuery.ThenByDescending(x => x.Id)
                                    : orderedQuery.ThenBy(x => x.Id),
                                "name" => isDesc
                                    ? orderedQuery.ThenByDescending(x => x.Name)
                                    : orderedQuery.ThenBy(x => x.Name),
                                "namenormalized" => isDesc
                                    ? orderedQuery.ThenByDescending(x => x.NameNormalized)
                                    : orderedQuery.ThenBy(x => x.NameNormalized),
                                "sortname" => isDesc
                                    ? orderedQuery.ThenByDescending(x => x.SortName)
                                    : orderedQuery.ThenBy(x => x.SortName),
                                _ => orderedQuery
                            };
                        }
                    }
                    else
                    {
                        query = query.OrderBy(x => x.Id);
                    }

                    // Apply pagination and get results with album counts in a single query
                    artists = await query
                        .Skip(pagedRequest.SkipValue)
                        .Take(pagedRequest.TakeValue)
                        .Select(x => new Artist
                        {
                            Id = x.Id,
                            Name = x.Name,
                            NameNormalized = x.NameNormalized,
                            SortName = x.SortName,
                            AlternateNames = x.AlternateNames,
                            ItunesId = x.ItunesId,
                            AmgId = x.AmgId,
                            DiscogsId = x.DiscogsId,
                            WikiDataId = x.WikiDataId,
                            MusicBrainzId = x.MusicBrainzId,
                            LastFmId = x.LastFmId,
                            SpotifyId = x.SpotifyId,
                            IsLocked = x.IsLocked,
                            LastRefreshed = x.LastRefreshed,
                            AlbumCount =
                                scopedContext.Albums.Count(a => a.ArtistId == x.Id) // This will be optimized by EF Core
                        })
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }

        return new PagedResult<Artist>
        {
            TotalCount = totalCount,
            TotalPages = pagedRequest.TotalPages(totalCount),
            Data = artists
        };
    }

    public async Task<PagedResult<SongSearchResult>> DoArtistTopSongsSearchAsync(string artistName, int? artistId,
        int? maxResults, CancellationToken cancellationToken = default)
    {
        CheckInitialized();

        var maxResultsValue = maxResults ?? _configuration.GetValue<int>(SettingRegistry.SearchEngineDefaultPageSize);
        var totalCount = 0;
        long operationTime = 0;

        var artistIdValue = artistId;
        if (artistIdValue == null)
        {
            var searchResult = await DoSearchAsync(new ArtistQuery { Name = artistName }, maxResults, cancellationToken)
                .ConfigureAwait(false);
            artistIdValue = searchResult.Data.FirstOrDefault(x => x.Id != null)?.Id;
        }

        if (artistIdValue == null)
        {
            return new PagedResult<SongSearchResult>([$"No artist found for [{artistName}]"])
            {
                Data = []
            };
        }

        var result = new List<SongSearchResult>();

        foreach (var plugin in _artistTopSongsSearchEnginePlugins.Where(x => x.IsEnabled).OrderBy(x => x.SortOrder))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var pluginResult = await plugin
                .DoArtistTopSongsSearchAsync(artistIdValue.Value, maxResultsValue, cancellationToken)
                .ConfigureAwait(false);
            if (pluginResult is { IsSuccess: true, Data: not null })
            {
                result.AddRange(pluginResult.Data);
                totalCount += pluginResult.TotalCount;
                operationTime += pluginResult.OperationTime ?? 0;
            }

            if (result.Count > maxResultsValue)
            {
                break;
            }
        }

        return new PagedResult<SongSearchResult>
        {
            OperationTime = operationTime,
            CurrentPage = 1,
            TotalCount = totalCount,
            TotalPages = 1,
            Data = result.OrderBy(x => x.SortOrder).ToArray()
        };
    }

    private async Task<ArtistSearchResult?> GetArtistFromSearchProviders(ArtistQuery query, int maxResultsValue,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Normalize query for better matching
            var normalizedQueryName = Utility.UnicodeNormalizer.NormalizeForSearch(query.NameNormalized);

            var variousArtist = new VariousArtist();
            if (normalizedQueryName.IsSimilar(variousArtist.Name.ToNormalizedString()))
            {
                // Various artist is a mess and has hundreds of thousands of albums. Make the admin manually validate various artists.
                Logger.Warning("[{Name}]:[{MethodName}] various artists albums require manual validation.",
                    nameof(ArtistSearchEngineService),
                    nameof(GetArtistFromSearchProviders));
                return null;
            }

            var theater = new Theater();
            if (normalizedQueryName.IsSimilar(theater.Name.ToNormalizedString()))
            {
                Logger.Warning("[{Name}]:[{MethodName}] theater albums require manual validation.",
                    nameof(ArtistSearchEngineService),
                    nameof(GetArtistFromSearchProviders));
                return null;
            }

            var pluginsResult = new List<ArtistSearchResult>();

            // Execute search engines sequentially in SortOrder - stop early if we find a good match
            // This prevents unnecessary API calls to rate-limited services like Spotify
            foreach (var plugin in _artistSearchEnginePlugins.Where(x => x.IsEnabled).OrderBy(x => x.SortOrder))
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var startTicks = Stopwatch.GetTimestamp();
                var pluginResult = await plugin.DoArtistSearchAsync(query, int.MaxValue, cancellationToken).ConfigureAwait(false);
                if (pluginResult is { IsSuccess: true, Data: not null })
                {
                    foreach (var d in pluginResult.Data)
                    {
                        if (d.Name.ToNormalizedString().IsSimilar(query.NameNormalized))
                        {
                            pluginsResult.Add(d);
                        }
                    }
                }

                Logger.Debug("[{Plugin}] performed artist search. Elapsed [{ElapsedTime}]",
                    plugin.DisplayName,
                    Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds);

                // Process all enabled providers to merge results from all sources
                // Only stop if cancellation is requested or plugin signals StopProcessing
                if (cancellationToken.IsCancellationRequested || plugin.StopProcessing)
                {
                    break;
                }
            }

            if (pluginsResult.Count > 0)
            {
                var startTicks = Stopwatch.GetTimestamp();
                var artistsFromSearchResult = new List<ArtistSearchResult>();
                foreach (var pluginResult in pluginsResult)
                {
                    var artistFromSearchResult = new ArtistSearchResult
                    {
                        AmgId = pluginResult.AmgId,
                        DiscogsId = pluginResult.DiscogsId,
                        FromPlugin = nameof(ArtistSearchEngineService),
                        ItunesId = pluginResult.ItunesId,
                        LastFmId = pluginResult.LastFmId,
                        MusicBrainzId = pluginResult.MusicBrainzId,
                        Name = pluginResult.Name,
                        Rank = 1,
                        SortName = pluginResult.SortName,
                        SpotifyId = pluginResult.SpotifyId,
                        WikiDataId = pluginResult.WikiDataId
                    };
                    var seenArtist = artistsFromSearchResult.FirstOrDefault(x =>
                        x.MusicBrainzId == artistFromSearchResult.MusicBrainzId
                        || x.SpotifyId == artistFromSearchResult.SpotifyId);
                    if (seenArtist == null)
                    {
                        artistFromSearchResult = pluginsResult.Aggregate(artistFromSearchResult, (current, r) =>
                            current with
                            {
                                AmgId = current.AmgId ?? r.AmgId,
                                DiscogsId = current.DiscogsId ?? r.DiscogsId,
                                ItunesId = current.ItunesId ?? r.ItunesId,
                                LastFmId = current.LastFmId ?? r.LastFmId,
                                Name = current.Name.Nullify() ?? r.Name,
                                MusicBrainzId = current.MusicBrainzId ?? r.MusicBrainzId,
                                Rank = current.Rank + 1,
                                SpotifyId = current.SpotifyId ?? r.SpotifyId,
                                WikiDataId = current.WikiDataId ?? r.WikiDataId
                            });
                    }
                    else
                    {
                        artistFromSearchResult = pluginsResult.Aggregate(seenArtist, (current, r) => current with
                        {
                            AmgId = current.AmgId ?? r.AmgId,
                            DiscogsId = current.DiscogsId ?? r.DiscogsId,
                            ItunesId = current.ItunesId ?? r.ItunesId,
                            LastFmId = current.LastFmId ?? r.LastFmId,
                            Name = current.Name.Nullify() ?? r.Name,
                            MusicBrainzId = current.MusicBrainzId ?? r.MusicBrainzId,
                            Rank = current.Rank + 1,
                            SpotifyId = current.SpotifyId ?? r.SpotifyId,
                            WikiDataId = current.WikiDataId ?? r.WikiDataId
                        });
                        artistsFromSearchResult.Remove(seenArtist);
                    }

                    var combinedNewArtistReleases = new List<AlbumSearchResult>();
                    foreach (var arRelease in pluginResult.Releases ?? [])
                    {
                        var seenAlbumRelease = combinedNewArtistReleases
                            .OrderBy(x => x.Year)
                            .FirstOrDefault(x =>
                                x.Year == arRelease.Year && x.NameNormalized == arRelease.NameNormalized);
                        if (seenAlbumRelease == null)
                        {
                            combinedNewArtistReleases.Add(arRelease);
                        }
                        else
                        {
                            seenAlbumRelease.MusicBrainzId ??= arRelease.MusicBrainzId;
                            seenAlbumRelease.MusicBrainzResourceGroupId ??= arRelease.MusicBrainzResourceGroupId;
                            seenAlbumRelease.SpotifyId ??= arRelease.SpotifyId;
                            seenAlbumRelease.CoverUrl ??= arRelease.CoverUrl;
                        }
                    }

                    artistFromSearchResult.Releases = combinedNewArtistReleases
                        .Where(x => x.AlbumType is AlbumType.Album or AlbumType.EP).ToArray();
                    artistsFromSearchResult.Add(artistFromSearchResult);

                    if (artistFromSearchResult.SpotifyId == null && artistFromSearchResult.MusicBrainzId == null)
                    {
                        Logger.Warning("[{Name}]:[{MethodName}] unable to find artist for provided query.",
                            nameof(ArtistSearchEngineService),
                            nameof(GetArtistFromSearchProviders));
                        return null;
                    }
                }

                foreach (var artistFromSearchResult in artistsFromSearchResult)
                {
                    if (artistFromSearchResult.Releases?.Length > 0 && query.AlbumKeyValues?.Length > 0)
                    {
                        foreach (var queryAlbum in query.AlbumKeyValues)
                        {
                            var matchingAlbumsOnName = artistFromSearchResult.Releases
                                .Where(x => x.NameNormalized == queryAlbum.Value).ToArray();
                            if (matchingAlbumsOnName.Any())
                            {
                                artistFromSearchResult.Rank += matchingAlbumsOnName.Length;
                                var matchingAlbumsOnYear = matchingAlbumsOnName
                                    .Where(x => x.Year.ToString() == queryAlbum.Key).ToArray();
                                if (matchingAlbumsOnYear.Any())
                                {
                                    artistFromSearchResult.Rank += matchingAlbumsOnYear.Length;
                                }
                            }
                        }

                        artistFromSearchResult.Releases =
                            artistFromSearchResult.Releases.OrderByDescending(x => x.Rank).ToArray();
                    }
                }

                var newArtist = artistsFromSearchResult.OrderByDescending(x => x.Rank).FirstOrDefault();

                Logger.Debug(
                    "[{Name}]:[{MethodName}] artist search completed with [{AlbumCount}] albums in [{ElapsedTime}] ms.",
                    nameof(ArtistSearchEngineService),
                    nameof(GetArtistFromSearchProviders),
                    newArtist?.Releases?.Length,
                    Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds
                );
                return newArtist;
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "[{Name}] GetArtistFromSearchProviders failed.", nameof(ArtistSearchEngineService));
        }

        return null;
    }

    /// <summary>
    ///     Performs artist search with directory-run caching and request coalescing.
    ///     When a runContext is provided, uses the run-scoped cache to avoid duplicate API calls.
    /// </summary>
    public async Task<PagedResult<ArtistSearchResult>> DoSearchAsync(
        ArtistQuery query,
        int? maxResults,
        DirectoryRunContext? runContext,
        CancellationToken cancellationToken = default)
    {
        if (runContext == null)
        {
            return await DoSearchAsync(query, maxResults, cancellationToken).ConfigureAwait(false);
        }

        var startTicks = Stopwatch.GetTimestamp();

        var (results, wasHit, wasCoalesced) = await runContext.ArtistSearchCache.GetOrCreateAsync(
            query,
            async (q, ct) =>
            {
                var searchResult = await DoSearchAsync(q, maxResults, ct).ConfigureAwait(false);
                return searchResult.Data.ToArray();
            },
            cancellationToken).ConfigureAwait(false);

        var elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;
        runContext.AddEnrichmentTime((long)elapsedMs);

        Logger.Debug(
            "[{Name}] Artist search for [{Artist}]: cacheHit={Hit}, coalesced={Coalesced}, duration={Duration}ms",
            nameof(ArtistSearchEngineService),
            query.Name,
            wasHit,
            wasCoalesced,
            elapsedMs);

        return new PagedResult<ArtistSearchResult>
        {
            Data = results ?? [],
            TotalCount = results?.Length ?? 0,
            TotalPages = 1
        };
    }

    public async Task<PagedResult<ArtistSearchResult>> DoSearchAsync(ArtistQuery query, int? maxResults,
        CancellationToken cancellationToken = default)
    {
        CheckInitialized();

        // Normalize the artist name to handle special characters
        var normalizedQuery = query with
        {
            Name = Utility.UnicodeNormalizer.Normalize(query.Name)
        };

        // Check cache first
        if (_searchCache.TryGetCachedResult(normalizedQuery, out var wasFound, out var cachedArtistId))
        {
            if (!wasFound)
            {
                Logger.Debug("[{Name}] Artist [{Artist}] not found (cached negative result)",
                    nameof(ArtistSearchEngineService), normalizedQuery.Name);
                return new PagedResult<ArtistSearchResult>
                {
                    Data = [],
                    TotalCount = 0,
                    TotalPages = 0
                };
            }

            if (cachedArtistId.HasValue)
            {
                Logger.Debug("[{Name}] Artist [{Artist}] found in cache (ID: {Id})",
                    nameof(ArtistSearchEngineService), normalizedQuery.Name, cachedArtistId.Value);
                // Will be retrieved from database below
            }
        }

        var result = new List<ArtistSearchResult>();

        var maxResultsValue = maxResults ?? _configuration.GetValue<int>(SettingRegistry.SearchEngineDefaultPageSize);

        long operationTime = 0;
        var totalCount = 0;

        try
        {
            using (Operation.At(LogEventLevel.Debug)
                       .Time("[{Name}] DoSearchAsync [{Query}]", nameof(ArtistSearchEngineService), normalizedQuery))
            {
                // See if found in DbContext if not then query plugins, add to context and return results
                await using (var scopedContext = await artistSearchEngineServiceDbContextFactory
                                 .CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
                {
                    var firstTag = $"{normalizedQuery.NameNormalized}{StringExtensions.TagsSeparator}";
                    var inTag =
                        $"{StringExtensions.TagsSeparator}{normalizedQuery.NameNormalized}{StringExtensions.TagsSeparator}";
                    var outerTag = $"{StringExtensions.TagsSeparator}{normalizedQuery.NameNormalized}";
                    var artists = await scopedContext
                        .Artists.Include(x => x.Albums)
                        .Where(x => x.NameNormalized == normalizedQuery.NameNormalized ||
                                    (x.MusicBrainzId != null && normalizedQuery.MusicBrainzId != null &&
                                     x.MusicBrainzId == normalizedQuery.MusicBrainzIdValue) ||
                                    (x.AlternateNames != null && (x.AlternateNames.Contains(firstTag) ||
                                                                  x.AlternateNames.Contains(inTag) ||
                                                                  x.AlternateNames.Contains(outerTag))) ||
                                    (x.SpotifyId != null && normalizedQuery.SpotifyId != null && x.SpotifyId == normalizedQuery.SpotifyId))
                        .ToArrayAsync(cancellationToken)
                        .ConfigureAwait(false);

                    if (artists.Length > 0 && normalizedQuery.AlbumKeyValues?.Length > 0)
                    {
                        foreach (var ar in artists)
                        {
                            // If any album is given then rank artist if any album matches
                            foreach (var album in ar.Albums)
                            {
                                foreach (var albumKey in normalizedQuery.AlbumKeyValues)
                                {
                                    var isAlbumMatch = album.Year.ToString() == albumKey.Key &&
                                                       album.NameNormalized == albumKey.Value;
                                    if (isAlbumMatch)
                                    {
                                        ar.Rank++;
                                    }
                                }
                            }
                        }
                    }

                    var artist = artists.OrderByDescending(x => x.Rank).FirstOrDefault();

                    if (artist != null)
                    {
                        if (artist.Albums.Count == 0)
                        {
                            // Artist in local db doesn't have any albums refresh get and update
                            Trace.WriteLine(
                                $"[{nameof(ArtistSearchEngineService)}] artist [{artist.NameNormalized}] has no albums. Refreshing from search engine.");
                            var newArtist =
                                await GetArtistFromSearchProviders(normalizedQuery, maxResultsValue, cancellationToken)
                                    .ConfigureAwait(false);
                            if (newArtist?.Releases?.Length > 0)
                            {
                                var albumsToAdd = new List<Album>();
                                foreach (var ar in newArtist.Releases)
                                {
                                    var newAlbum = new Album
                                    {
                                        AlbumType = (int)ar.AlbumType,
                                        Artist = artist,
                                        ArtistId = artist.Id,
                                        SortName = ar.SortName,
                                        Name = ar.Name,
                                        NameNormalized = ar.NameNormalized,
                                        Year = SafeParser.ToDateTime(ar.ReleaseDate)?.Year ?? 0,
                                        MusicBrainzId = ar.MusicBrainzId,
                                        MusicBrainzReleaseGroupId = ar.MusicBrainzResourceGroupId,
                                        SpotifyId = ar.SpotifyId,
                                        CoverUrl = ar.CoverUrl
                                    };

                                    var alreadyInList = albumsToAdd.FirstOrDefault(x =>
                                        x.NameNormalized == newAlbum.NameNormalized && x.Year == newAlbum.Year);
                                    if (alreadyInList == null)
                                    {
                                        albumsToAdd.Add(newAlbum);
                                    }
                                    else
                                    {
                                        alreadyInList!.MusicBrainzId ??= alreadyInList?.MusicBrainzId;
                                        alreadyInList!.MusicBrainzReleaseGroupId ??=
                                            alreadyInList?.MusicBrainzReleaseGroupId;
                                        alreadyInList!.SpotifyId ??= alreadyInList?.SpotifyId;
                                    }
                                }

                                artist.Albums = albumsToAdd.ToArray();
                                try
                                {
                                    await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                                    Logger.Debug("[{Name}] Updated existing artist [{Artist}] added [{Count}] albums.",
                                        nameof(ArtistSearchEngineService),
                                        artist,
                                        artist.Albums.Count
                                    );
                                    var artistId = artist.Id;
                                    artist = await scopedContext
                                        .Artists
                                        .Include(x => x.Albums)
                                        .FirstAsync(x => x.Id == artistId, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                catch (DbUpdateException ex) when (IsAlbumUniqueConstraint(ex))
                                {
                                    Logger.Warning(ex,
                                        "[{Name}] Duplicate album detected while updating artist [{Artist}] - using existing records.",
                                        nameof(ArtistSearchEngineService),
                                        artist.NameNormalized);
                                    scopedContext.ChangeTracker.Clear();
                                    artist = await scopedContext
                                        .Artists
                                        .Include(x => x.Albums)
                                        .FirstAsync(x => x.Id == artist.Id, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                            }
                        }

                        result.Add(artist.ToArtistSearchResult(nameof(ArtistSearchEngineService)));
                        Trace.WriteLine(
                            $"[{nameof(ArtistSearchEngineService)}] Found artist [{artist}] in database for query [{normalizedQuery}].");
                    }
                    else
                    {
                        var newArtist = await GetArtistFromSearchProviders(normalizedQuery, maxResultsValue, cancellationToken)
                            .ConfigureAwait(false);
                        if (newArtist != null)
                        {
                            var nameNormalized = newArtist.Name.ToNormalizedString() ?? newArtist.Name;

                            artist = await scopedContext
                                .Artists
                                .Where(x => x.NameNormalized == nameNormalized ||
                                            (x.AlternateNames != null && (x.AlternateNames.Contains(firstTag) ||
                                                                          x.AlternateNames.Contains(inTag) ||
                                                                          x.AlternateNames.Contains(outerTag))) ||
                                            (x.AmgId != null && x.AmgId == newArtist.AmgId) ||
                                            (x.DiscogsId != null && x.DiscogsId == newArtist.DiscogsId) ||
                                            (x.ItunesId != null && x.ItunesId == newArtist.ItunesId) ||
                                            (x.LastFmId != null && x.LastFmId == newArtist.LastFmId) ||
                                            (x.MusicBrainzId != null && x.MusicBrainzId == newArtist.MusicBrainzId) ||
                                            (x.SpotifyId != null && x.SpotifyId == newArtist.SpotifyId) ||
                                            (x.WikiDataId != null && x.WikiDataId == newArtist.WikiDataId))
                                .FirstOrDefaultAsync(cancellationToken)
                                .ConfigureAwait(false);

                            if (artist != null)
                            {
                                result.Add(artist.ToArtistSearchResult(nameof(ArtistSearchEngineService)));
                                Trace.WriteLine(
                                    $"[{nameof(ArtistSearchEngineService)}] Found artist [{artist}] in database for query [{query}].");
                            }
                            else
                            {
                                var newDbArtist = new Artist
                                {
                                    AmgId = newArtist.AmgId,
                                    AlternateNames = "".AddTags(newArtist.AlternateNames, doNormalize: true),
                                    DiscogsId = newArtist.DiscogsId,
                                    ItunesId = newArtist.ItunesId,
                                    LastFmId = newArtist.LastFmId,
                                    MusicBrainzId = newArtist.MusicBrainzId,
                                    Name = newArtist.Name,
                                    NameNormalized = newArtist.Name.ToNormalizedString() ?? newArtist.Name,
                                    SortName = newArtist.SortName ?? newArtist.Name,
                                    SpotifyId = newArtist.SpotifyId,
                                    WikiDataId = newArtist.WikiDataId
                                };
                                Album[] albums = [];
                                if (newArtist.Releases?.Length > 0)
                                {
                                    var albumsToAdd = new List<Album>();
                                    foreach (var ar in newArtist.Releases)
                                    {
                                        var newAlbum = new Album
                                        {
                                            AlbumType = (int)ar.AlbumType,
                                            Artist = newDbArtist,
                                            ArtistId = newDbArtist.Id,
                                            SortName = ar.SortName,
                                            Name = ar.Name,
                                            NameNormalized = ar.NameNormalized,
                                            Year = SafeParser.ToDateTime(ar.ReleaseDate)?.Year ?? 0,
                                            MusicBrainzId = ar.MusicBrainzId,
                                            MusicBrainzReleaseGroupId = ar.MusicBrainzResourceGroupId,
                                            SpotifyId = ar.SpotifyId,
                                            CoverUrl = ar.CoverUrl
                                        };

                                        var alreadyInList = albumsToAdd.FirstOrDefault(x =>
                                            x.NameNormalized == newAlbum.NameNormalized && x.Year == newAlbum.Year);
                                        if (alreadyInList == null)
                                        {
                                            albumsToAdd.Add(newAlbum);
                                        }
                                        else
                                        {
                                            alreadyInList!.MusicBrainzId ??= alreadyInList?.MusicBrainzId;
                                            alreadyInList!.MusicBrainzReleaseGroupId ??=
                                                alreadyInList?.MusicBrainzReleaseGroupId;
                                            alreadyInList!.SpotifyId ??= alreadyInList?.SpotifyId;
                                        }
                                    }

                                    newDbArtist.Albums = albumsToAdd.ToArray();
                                }

                                scopedContext.Artists.Add(newDbArtist);
                                try
                                {
                                    await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                                }
                                catch (DbUpdateException ex) when (IsAlbumUniqueConstraint(ex))
                                {
                                    Logger.Warning(ex,
                                        "[{Name}] Duplicate album detected while adding artist [{Artist}], reloading existing artist.",
                                        nameof(ArtistSearchEngineService),
                                        newDbArtist.NameNormalized);
                                    scopedContext.ChangeTracker.Clear();
                                    var existingArtist = await scopedContext
                                        .Artists
                                        .Include(x => x.Albums)
                                        .FirstOrDefaultAsync(
                                            x => x.NameNormalized == newDbArtist.NameNormalized,
                                            cancellationToken)
                                        .ConfigureAwait(false);
                                    if (existingArtist != null)
                                    {
                                        newDbArtist = existingArtist;
                                    }
                                    else
                                    {
                                        scopedContext.Artists.Add(newDbArtist);
                                        await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                                    }
                                }
                                newArtist = newArtist with
                                {
                                    UniqueId = newDbArtist.Id,
                                    Releases = albums.Select(x =>
                                            x.ToAlbumSearchResult(x.Artist, nameof(ArtistSearchEngineService)))
                                        .ToArray()
                                };
                                result.Add(newArtist);
                                Trace.WriteLine(
                                    $"[{nameof(ArtistSearchEngineService)}] Added artist [{newArtist}] with [{newDbArtist.Albums.Count}] albums to database.");
                            }
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Logger.Error(e, "Attempting to Search [{Artist}]", normalizedQuery.Name);
        }

        // Cache results
        if (result.Any())
        {
            var firstResult = result.First();
            if (firstResult.Id.HasValue)
            {
                _searchCache.CachePositiveResult(normalizedQuery, firstResult.Id.Value);
            }
        }
        else
        {
            _searchCache.CacheNegativeResult(normalizedQuery);
        }

        return new PagedResult<ArtistSearchResult>
        {
            OperationTime = operationTime,
            CurrentPage = 1,
            TotalCount = totalCount,
            TotalPages = 1,
            Data = result?.OrderByDescending(x => x.Rank).ThenBy(x => x.SortName).ToArray() ?? []
        };
    }

    /// <summary>
    ///     Look up artists by AMG ID using the iTunes lookup API.
    ///     This method bypasses other search providers and directly queries iTunes.
    /// </summary>
    /// <param name="amgArtistId">The AMG Artist ID (digits only)</param>
    /// <param name="maxResults">Maximum number of results to return (default: 10)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Paged result with ArtistSearchResult candidates</returns>
    public async Task<PagedResult<ArtistSearchResult>> LookupByAmgIdAsync(
        string amgArtistId,
        int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        // Validate AMG ID - must be digits only
        var validationResult = SearchEngineQueryNormalization.ValidateAmgIdResult(amgArtistId);
        if (!validationResult.IsSuccess)
        {
            return new PagedResult<ArtistSearchResult>(validationResult.Errors?.Select(e => e.Message).ToArray())
            {
                Data = []
            };
        }

        try
        {
            var httpClient = httpClientFactory.CreateClient();
            var userAgent = _configuration.GetValue<string>(SettingRegistry.SearchEngineUserAgent);
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

            var requestUri = $"https://itunes.apple.com/lookup?amgArtistId={amgArtistId}&limit={maxResults}";
            var response = await httpClient.GetAsync(requestUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new PagedResult<ArtistSearchResult>(
                    [$"iTunes AMG lookup failed with status {response.StatusCode}"])
                {
                    Data = []
                };
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResult = serializer.Deserialize<ITunesSearchResult>(jsonResponse);

            var results = new List<ArtistSearchResult>();
            if (searchResult?.Results?.Any() ?? false)
            {
                foreach (var sr in searchResult.Results)
                {
                    if (sr.ArtistName.Nullify() == null)
                    {
                        continue;
                    }

                    results.Add(new ArtistSearchResult
                    {
                        Name = sr.ArtistName!,
                        SortName = sr.ArtistName?.ToNormalizedString(),
                        FromPlugin = "iTunes",
                        AlbumCount = sr.TrackCount,
                        UniqueId = SafeParser.Hash($"{sr.ArtistId}:{sr.AmgArtistId}:{sr.ArtistName}"),
                        Rank = 10,
                        ImageUrl = sr.ArtworkUrl100,
                        ThumbnailUrl = sr.ArtworkUrl60 ?? sr.ArtworkUrl100,
                        AmgId = sr.AmgArtistId?.ToString(),
                        ItunesId = sr.ArtistId?.ToString()
                    });
                }
            }

            Logger.Debug("[{Name}] AMG lookup for [{AmgId}] returned [{Count}] results",
                nameof(ArtistSearchEngineService), amgArtistId, results.Count);

            return new PagedResult<ArtistSearchResult>
            {
                Data = results.OrderByDescending(x => x.Rank).Take(maxResults).ToArray(),
                TotalCount = results.Count,
                TotalPages = 1,
                CurrentPage = 1
            };
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error performing AMG lookup for [{AmgId}]", amgArtistId);
            return new PagedResult<ArtistSearchResult>(["AMG lookup failed."]) { Data = [] };
        }
    }

    public async Task<OperationResult<bool>> RefreshArtistAlbums(Artist[] selectedArtists,
        CancellationToken cancellationToken = default)
    {
        var result = false;

        await using (var scopedContext = await artistSearchEngineServiceDbContextFactory
                         .CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            // Delete albums for selected artists using EF Core
            foreach (var artist in selectedArtists)
            {
                var albumsToDelete = await scopedContext.Albums
                    .Where(a => a.ArtistId == artist.Id)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                scopedContext.Albums.RemoveRange(albumsToDelete);
            }
            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            var now = DateTime.UtcNow;
            foreach (var artist in selectedArtists)
            {
                await DoSearchAsync(new ArtistQuery
                {
                    Name = artist.Name,
                    MusicBrainzId = artist.MusicBrainzId?.ToString(),
                    SpotifyId = artist.SpotifyId
                }, null, cancellationToken).ConfigureAwait(false);
                var dbArtist = await scopedContext
                    .Artists
                    .FirstOrDefaultAsync(x => x.Id == artist.Id, cancellationToken)
                    .ConfigureAwait(false);
                if (dbArtist != null)
                {
                    dbArtist.LastRefreshed = now;
                    result = dbArtist.AlbumCount > 0;
                }
            }

            await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return new OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<OperationResult<Artist?>> GetById(int artistId, CancellationToken cancellationToken = default)
    {
        Guard.Against.NegativeOrZero(artistId, nameof(artistId));

        await using (var scopedContext = await artistSearchEngineServiceDbContextFactory
                         .CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var artist = await scopedContext
                .Artists
                .Include(x => x.Albums)
                .FirstOrDefaultAsync(x => x.Id == artistId, cancellationToken)
                .ConfigureAwait(false);

            return new OperationResult<Artist?>
            {
                Data = artist
            };
        }
    }

    public async Task<OperationResult<Artist?>> AddArtistAsync(Artist artist,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(artist, nameof(artist));

        artist.NameNormalized = artist.NameNormalized.Nullify() ?? artist.Name.ToNormalizedString() ?? artist.Name;

        var validationResult = ValidateModel(artist);
        if (!validationResult.IsSuccess)
        {
            return new OperationResult<Artist?>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage)).Select(x => x.ErrorMessage!).ToArray() ?? [])
            {
                Data = null,
                Type = OperationResponseType.ValidationFailure
            };
        }

        await using (var scopedContext = await artistSearchEngineServiceDbContextFactory
                         .CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            scopedContext.Artists.Add(artist);
            var result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return await GetById(artist.Id, cancellationToken);
    }

    public async Task<OperationResult<bool>> UpdateArtistAsync(Artist artist,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.Null(artist, nameof(artist));

        var validationResult = ValidateModel(artist);
        if (!validationResult.IsSuccess)
        {
            return new OperationResult<bool>(validationResult.Data.Item2
                ?.Where(x => !string.IsNullOrWhiteSpace(x.ErrorMessage))
                .Select(x => LogSanitizer.Sanitize(x.ErrorMessage)!)
                .ToArray() ?? [])
            {
                Data = false,
                Type = OperationResponseType.ValidationFailure
            };
        }

        bool result;
        await using (var scopedContext = await artistSearchEngineServiceDbContextFactory
                         .CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            var dbDetail = await scopedContext
                .Artists
                .FirstOrDefaultAsync(x => x.Id == artist.Id, cancellationToken)
                .ConfigureAwait(false);

            if (dbDetail == null)
            {
                return new OperationResult<bool>
                {
                    Data = false,
                    Type = OperationResponseType.NotFound
                };
            }

            dbDetail.AlternateNames = artist.AlternateNames;
            dbDetail.AmgId = artist.AmgId;
            dbDetail.DiscogsId = artist.DiscogsId;
            dbDetail.IsLocked = artist.IsLocked ?? artist.IsLockedValue;
            dbDetail.ItunesId = artist.ItunesId;
            dbDetail.LastFmId = artist.LastFmId;
            dbDetail.MusicBrainzId = artist.MusicBrainzId;
            dbDetail.Name = artist.Name;
            dbDetail.NameNormalized = artist.NameNormalized;
            dbDetail.SortName = artist.SortName;
            dbDetail.SpotifyId = artist.SpotifyId;
            dbDetail.WikiDataId = artist.WikiDataId;

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        }

        return new OperationResult<bool>
        {
            Data = result
        };
    }

    public async Task<OperationResult<bool>> DeleteArtistsAsync(int[] artistIds,
        CancellationToken cancellationToken = default)
    {
        Guard.Against.NullOrEmpty(artistIds, nameof(artistIds));

        bool result;

        await using (var scopedContext = await artistSearchEngineServiceDbContextFactory
                         .CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
        {
            foreach (var artistId in artistIds)
            {
                var artist = await GetById(artistId, cancellationToken).ConfigureAwait(false);
                if (!artist.IsSuccess)
                {
                    return new OperationResult<bool>("Unknown artist.")
                    {
                        Data = false
                    };
                }
            }

            foreach (var artistId in artistIds)
            {
                var artist = await scopedContext
                    .Artists
                    .FirstAsync(x => x.Id == artistId, cancellationToken)
                    .ConfigureAwait(false);
                scopedContext.Artists.Remove(artist);
            }

            result = await scopedContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false) > 0;
        }

        return new OperationResult<bool>
        {
            Data = result
        };
    }

    public IArtistSearchEnginePlugin[] GetRegisteredPlugins()
    {
        CheckInitialized();
        return _artistSearchEnginePlugins;
    }

    public async Task<ArtistLookupResult> LookupAsync(
        string artistName,
        int? maxResults,
        string[]? providerIds,
        CancellationToken cancellationToken = default)
    {
        CheckInitialized();

        var normalizedQuery = new ArtistQuery
        {
            Name = Utility.UnicodeNormalizer.Normalize(artistName)
        };

        var enabledPlugins = _artistSearchEnginePlugins.Where(x => x.IsEnabled).ToArray();

        var filteredPlugins = providerIds != null && providerIds.Length > 0
            ? enabledPlugins.Where(p => providerIds.Contains(p.Id)).ToArray()
            : enabledPlugins;

        var candidates = new List<ArtistSearchResult>();
        var failedProviders = new List<(string ProviderId, string ErrorMessage)>();
        var totalOperationTime = 0L;

        foreach (var plugin in filteredPlugins.OrderBy(x => x.SortOrder))
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var startTicks = Stopwatch.GetTimestamp();
                var pluginResult = await plugin.DoArtistSearchAsync(
                    normalizedQuery,
                    maxResults ?? _configuration.GetValue<int>(SettingRegistry.SearchEngineDefaultPageSize),
                    cancellationToken).ConfigureAwait(false);

                totalOperationTime += Stopwatch.GetElapsedTime(startTicks).Milliseconds;

                if (pluginResult is { IsSuccess: true, Data: not null })
                {
                    foreach (var result in pluginResult.Data)
                    {
                        candidates.Add(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warning(ex,
                    "[{Plugin}] Search failed during artist lookup for query [{ArtistName}]",
                    plugin.DisplayName,
                    artistName);
                failedProviders.Add((plugin.Id, "Search failed"));
            }
        }

        var uniqueCandidates = new Dictionary<string, ArtistSearchResult>();
        foreach (var candidate in candidates.OrderByDescending(x => x.Rank).ThenBy(x => x.Name))
        {
            var key = GetCandidateKey(candidate);
            if (!uniqueCandidates.ContainsKey(key))
            {
                uniqueCandidates[key] = candidate;
            }
        }

        return new ArtistLookupResult
        {
            Candidates = uniqueCandidates.Values
                .Take(maxResults ?? _configuration.GetValue<int>(SettingRegistry.SearchEngineDefaultPageSize))
                .ToArray(),
            HasPartialFailures = failedProviders.Count > 0,
            FailedProviderIds = failedProviders.Select(x => x.ProviderId).ToArray(),
            OperationTime = totalOperationTime
        };
    }

    private static string GetCandidateKey(ArtistSearchResult result)
    {
        return string.Join("|",
            result.MusicBrainzId?.ToString() ?? string.Empty,
            result.SpotifyId ?? string.Empty,
            result.Name.ToNormalizedString() ?? string.Empty);
    }

    private static bool IsAlbumUniqueConstraint(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase) &&
               message.Contains("Albums", StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<Artist> ApplyLikeFilter(
        IQueryable<Artist> query,
        System.Linq.Expressions.Expression<Func<Artist, string?>> propertySelector,
        Filtering.FilterOperator filterOperator,
        string filterValueLower)
    {
        var parameter = propertySelector.Parameters[0];
        var propertyAccess = propertySelector.Body;

        // Build: property != null && property.ToLower().Contains/StartsWith/EndsWith(filterValueLower)
        var nullCheck = System.Linq.Expressions.Expression.NotEqual(propertyAccess, System.Linq.Expressions.Expression.Constant(null, typeof(string)));
        var toLowerMethod = typeof(string).GetMethod("ToLower", Type.EmptyTypes)!;
        var toLowerCall = System.Linq.Expressions.Expression.Call(propertyAccess, toLowerMethod);

        System.Linq.Expressions.MethodCallExpression stringOperation;
        var filterConstant = System.Linq.Expressions.Expression.Constant(filterValueLower);

        switch (filterOperator)
        {
            case Filtering.FilterOperator.StartsWith:
                var startsWithMethod = typeof(string).GetMethod("StartsWith", [typeof(string)])!;
                stringOperation = System.Linq.Expressions.Expression.Call(toLowerCall, startsWithMethod, filterConstant);
                break;
            case Filtering.FilterOperator.EndsWith:
                var endsWithMethod = typeof(string).GetMethod("EndsWith", [typeof(string)])!;
                stringOperation = System.Linq.Expressions.Expression.Call(toLowerCall, endsWithMethod, filterConstant);
                break;
            case Filtering.FilterOperator.DoesNotContain:
                var containsMethod = typeof(string).GetMethod("Contains", [typeof(string)])!;
                var containsCall = System.Linq.Expressions.Expression.Call(toLowerCall, containsMethod, filterConstant);
                var notContains = System.Linq.Expressions.Expression.Not(containsCall);
                var orNullCheck = System.Linq.Expressions.Expression.Equal(propertyAccess, System.Linq.Expressions.Expression.Constant(null, typeof(string)));
                var doesNotContainBody = System.Linq.Expressions.Expression.OrElse(orNullCheck, notContains);
                var doesNotContainLambda = System.Linq.Expressions.Expression.Lambda<Func<Artist, bool>>(doesNotContainBody, parameter);
                return query.Where(doesNotContainLambda);
            default: // Contains
                var containsMethodDefault = typeof(string).GetMethod("Contains", [typeof(string)])!;
                stringOperation = System.Linq.Expressions.Expression.Call(toLowerCall, containsMethodDefault, filterConstant);
                break;
        }

        var andExpression = System.Linq.Expressions.Expression.AndAlso(nullCheck, stringOperation);
        var lambda = System.Linq.Expressions.Expression.Lambda<Func<Artist, bool>>(andExpression, parameter);

        return query.Where(lambda);
    }
}
