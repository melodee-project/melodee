using System.Diagnostics;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine;
using Melodee.Common.Plugins.SearchEngine.Brave;
using Melodee.Common.Plugins.SearchEngine.Deezer;
using Melodee.Common.Plugins.SearchEngine.ITunes;
using Melodee.Common.Plugins.SearchEngine.LastFm;
using Melodee.Common.Plugins.SearchEngine.MetalApi;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.Spotify;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Common.Services.SearchEngines;

/// <summary>
///     Uses enabled Image Search plugins to get images for album query.
/// </summary>
public class AlbumImageSearchEngineService(
    ILogger logger,
    ICacheManager cacheManager,
    ISerializer serializer,
    SettingService settingService,
    IMelodeeConfigurationFactory configurationFactory,
    IDbContextFactory<MelodeeDbContext> contextFactory,
    IMusicBrainzRepository musicBrainzRepository,
    ISpotifyClientBuilder spotifyClientBuilder,
    IHttpClientFactory httpClientFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private static readonly TimeSpan SearchEngineTimeout = TimeSpan.FromSeconds(10);

    protected virtual IReadOnlyCollection<IAlbumImageSearchEnginePlugin> CreateSearchEngines(IMelodeeConfiguration configuration)
    {
        return
        [
            new MusicBrainzCoverArtArchiveSearchEngine(configuration, musicBrainzRepository)
            {
                IsEnabled = configuration.GetValue<bool>(SettingRegistry.SearchEngineMusicBrainzEnabled)
            },
            new DeezerSearchEngine(Logger, serializer, httpClientFactory)
            {
                IsEnabled = configuration.GetValue<bool>(SettingRegistry.SearchEngineDeezerEnabled)
            },
            new ITunesSearchEngine(Logger, serializer, httpClientFactory, CacheManager)
            {
                IsEnabled = configuration.GetValue<bool>(SettingRegistry.SearchEngineITunesEnabled)
            },
            new Spotify(Logger, configuration, CacheManager, spotifyClientBuilder, settingService, ContextFactory)
            {
                IsEnabled = configuration.GetValue<bool>(SettingRegistry.SearchEngineSpotifyEnabled)
            },
            new LastFm(Logger, configuration, serializer, httpClientFactory, CacheManager)
            {
                IsEnabled = configuration.GetValue<bool>(SettingRegistry.SearchEngineLastFmEnabled)
            },
            new MetalApiAlbumImageSearchEngine(
                new MetalApiClient(
                    httpClientFactory.CreateClient(),
                    Logger,
                    new MetalApiOptions { Enabled = configuration.GetValue<bool>(SettingRegistry.SearchEngineMetalApiEnabled) }),
                Logger,
                new MetalApiOptions { Enabled = configuration.GetValue<bool>(SettingRegistry.SearchEngineMetalApiEnabled) })
            {
                IsEnabled = configuration.GetValue<bool>(SettingRegistry.SearchEngineMetalApiEnabled)
            },
            new BraveAlbumImageSearchEnginePlugin(Logger, httpClientFactory, configuration)
            {
                IsEnabled = configuration.GetValue<bool>(SettingRegistry.SearchEngineBraveEnabled)
            }
        ];
    }

    /// <summary>
    ///     Performs album image search with directory-run caching and request coalescing.
    ///     When a runContext is provided, uses the run-scoped cache to avoid duplicate API calls.
    /// </summary>
    public async Task<OperationResult<ImageSearchResult[]>> DoSearchAsync(
        AlbumQuery query,
        int? maxResults,
        DirectoryRunContext? runContext,
        CancellationToken token = default)
    {
        if (runContext == null)
        {
            return await DoSearchAsync(query, maxResults, token).ConfigureAwait(false);
        }

        var startTicks = Stopwatch.GetTimestamp();

        var (results, wasHit, wasCoalesced) = await runContext.AlbumImageCache.GetOrCreateAsync(
            query,
            async (q, ct) =>
            {
                var searchResult = await DoSearchAsync(q, maxResults, ct).ConfigureAwait(false);
                return searchResult.Data;
            },
            token).ConfigureAwait(false);

        var elapsedMs = Stopwatch.GetElapsedTime(startTicks).TotalMilliseconds;
        runContext.AddEnrichmentTime((long)elapsedMs);

        Logger.Debug(
            "[{Name}] Album image search for [{Artist}]/[{Album}]: cacheHit={Hit}, coalesced={Coalesced}, duration={Duration}ms",
            nameof(AlbumImageSearchEngineService),
            query.Artist,
            query.Name,
            wasHit,
            wasCoalesced,
            elapsedMs);

        return new OperationResult<ImageSearchResult[]>
        {
            Data = results ?? []
        };
    }

    public async Task<OperationResult<ImageSearchResult[]>> DoSearchAsync(AlbumQuery query, int? maxResults,
        CancellationToken token = default)
    {
        var configuration = await configurationFactory.GetConfigurationAsync(token);

        var maxResultsValue = maxResults ?? configuration.GetValue<int>(SettingRegistry.SearchEngineDefaultPageSize);
        if (maxResultsValue <= 0)
        {
            return new OperationResult<ImageSearchResult[]>
            {
                Data = []
            };
        }

        token.ThrowIfCancellationRequested();

        var enabledEngines = CreateSearchEngines(configuration).Where(x => x.IsEnabled).OrderBy(x => x.SortOrder).ToArray();

        // Sanitize query for logging to prevent log forging
        var sanitizedQuery = LogSanitizer.Sanitize(query.ToString()) ?? string.Empty;

        Logger.Debug("Starting album image search for query [{Query}] with [{Count}] enabled search engines: [{Engines}]",
            sanitizedQuery, enabledEngines.Length, string.Join(", ", enabledEngines.Select(x => x.DisplayName)));

        var searchResults = await Task.WhenAll(enabledEngines.Select(x => SearchAsync(x, query, maxResultsValue, sanitizedQuery, token))).ConfigureAwait(false);
        var result = searchResults
            .SelectMany(x => x)
            .OrderByDescending(x => x.Rank)
            .Take(maxResultsValue)
            .ToArray();

        Logger.Debug("Album image search completed for query [{Query}] with [{Count}] total result(s)", sanitizedQuery, result.Length);

        return new OperationResult<ImageSearchResult[]>
        {
            Data = result
        };
    }

    private async Task<ImageSearchResult[]> SearchAsync(
        IAlbumImageSearchEnginePlugin searchEngine,
        AlbumQuery query,
        int maxResults,
        string sanitizedQuery,
        CancellationToken token)
    {
        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutTokenSource.CancelAfter(SearchEngineTimeout);

        try
        {
            Logger.Debug("[{Plugin}] searching for album images with query [{Query}]", searchEngine.DisplayName, sanitizedQuery);
            var searchResult = await searchEngine.DoAlbumImageSearch(query, maxResults, timeoutTokenSource.Token).ConfigureAwait(false);
            if (searchResult.IsSuccess)
            {
                var foundCount = searchResult.Data?.Length ?? 0;
                if (foundCount > 0)
                {
                    Logger.Debug("[{Plugin}] found [{Count}] image(s) for query [{Query}]",
                        searchEngine.DisplayName, foundCount, sanitizedQuery);
                    return searchResult.Data ?? [];
                }

                Logger.Debug("[{Plugin}] found no images for query [{Query}]", searchEngine.DisplayName, sanitizedQuery);
            }
            else
            {
                Logger.Warning("[{Plugin}] search failed for query [{Query}]: [{Errors}]",
                    searchEngine.DisplayName, sanitizedQuery, string.Join(", ", searchResult.Errors ?? []));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutTokenSource.IsCancellationRequested)
        {
            Logger.Warning("[{Plugin}] timed out after {TimeoutSeconds}s for query [{Query}]",
                searchEngine.DisplayName, SearchEngineTimeout.TotalSeconds, sanitizedQuery);
        }
        catch (Exception e)
        {
            Logger.Error(e, "[{Plugin}] threw error with query [{Query}]", searchEngine.DisplayName, sanitizedQuery);
        }

        return [];
    }
}
