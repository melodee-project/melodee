using System.Collections.Concurrent;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;
using Microsoft.EntityFrameworkCore;
using Serilog;
using SpotifyAPI.Web;

namespace Melodee.Common.Plugins.SearchEngine.Spotify;

public class Spotify(
    ILogger logger,
    IMelodeeConfiguration configuration,
    ICacheManager cacheManager,
    ISpotifyClientBuilder spotifyClientBuilder,
    SettingService settingService,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : IArtistSearchEnginePlugin, IArtistTopSongsSearchEnginePlugin, IAlbumImageSearchEnginePlugin,
        IArtistImageSearchEnginePlugin
{
    private readonly ConcurrentDictionary<string, PagedResult<ArtistSearchResult>> _artistSearchCache = new();
    private readonly SemaphoreSlim _spotifySemaphore = new(2);
    private DateTimeOffset _cooldownUntil = DateTimeOffset.MinValue;
    private int _consecutive429;

    private bool IsCoolingDown => DateTimeOffset.UtcNow < _cooldownUntil;

    private static TimeSpan ExtractRetryAfter(APITooManyRequestsException ex)
    {
        var property = ex.GetType().GetProperty("RetryAfter");
        if (property?.GetValue(ex) is int retrySeconds && retrySeconds > 0)
        {
            return TimeSpan.FromSeconds(retrySeconds);
        }

        if (property?.GetValue(ex) is double retryDouble && retryDouble > 0)
        {
            return TimeSpan.FromSeconds(retryDouble);
        }

        return TimeSpan.Zero;
    }

    private async Task<T?> ExecuteSpotifyAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        if (IsCoolingDown)
        {
            logger.Debug("[{DisplayName}] Spotify cooling down until [{Until}] for [{Operation}]",
                DisplayName,
                _cooldownUntil,
                operationName);
            return default;
        }

        await _spotifySemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await operation(cancellationToken).ConfigureAwait(false);
            _consecutive429 = 0;
            return result;
        }
        catch (APITooManyRequestsException ex)
        {
            var retryAfter = ExtractRetryAfter(ex);
            _consecutive429++;
            var cooldown = retryAfter > TimeSpan.Zero
                ? retryAfter
                : TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, _consecutive429)));

            _cooldownUntil = DateTimeOffset.UtcNow.Add(cooldown);
            logger.Warning(ex,
                "[{DisplayName}] Spotify rate limit during [{Operation}], cooling down for {Cooldown}s",
                DisplayName,
                operationName,
                cooldown.TotalSeconds);
            return default;
        }
        finally
        {
            _spotifySemaphore.Release();
        }
    }

    public async Task<OperationResult<ImageSearchResult[]?>> DoAlbumImageSearch(AlbumQuery query, int maxResults,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ImageSearchResult>();

        try
        {
            var apiClientId = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyApiKey);
            var apiClientSecret = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyClientSecret);

            if (string.IsNullOrWhiteSpace(apiClientId) || string.IsNullOrWhiteSpace(apiClientSecret))
            {
                return new OperationResult<ImageSearchResult[]?>("Spotify API key not configured.")
                {
                    Data = []
                };
            }

            var apiAccessToken = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyAccessToken);

            if (string.IsNullOrWhiteSpace(apiAccessToken))
            {
                var request = new ClientCredentialsRequest(apiClientId, apiClientSecret);
                var tokenResponse =
                    await new OAuthClient(spotifyClientBuilder.Config).RequestToken(request, cancellationToken);
                apiAccessToken = tokenResponse.AccessToken;
                await settingService.SetAsync(SettingRegistry.SearchEngineSpotifyAccessToken, apiAccessToken,
                    cancellationToken);
            }

            var spotify = spotifyClientBuilder.BuildClient(apiAccessToken);
            SearchResponse? searchResult = null;
            try
            {
                if (spotify != null)
                {
                    searchResult = await ExecuteSpotifyAsync(
                        token => spotify.Search.Item(new SearchRequest(SearchRequest.Types.Album, query.Name), token),
                        "album-search",
                        cancellationToken);
                }
            }
            catch (APIUnauthorizedException)
            {
                var request = new ClientCredentialsRequest(apiClientId, apiClientSecret);
                var refreshResponse =
                    await new OAuthClient(spotifyClientBuilder.Config).RequestToken(request, cancellationToken);
                apiAccessToken = refreshResponse.AccessToken;
                await settingService.SetAsync(SettingRegistry.SearchEngineSpotifyAccessToken, apiAccessToken,
                    cancellationToken);
            }

            if (searchResult?.Albums?.Items?.Count > 0)
            {
                var na = query.Artist.ToNormalizedString();
                foreach (var sr in searchResult.Albums.Items)
                {
                    var artist = sr.Artists.FirstOrDefault(x => x.Name.ToNormalizedString() == na);
                    if (artist != null)
                    {
                        short rank = 10;
                        if (sr.ReleaseDate == query.Year.ToString())
                        {
                            rank = 20;
                        }

                        var biggestImageHeight = sr.Images.Max(x => x.Height);
                        var image = sr.Images.FirstOrDefault(x => x.Height == biggestImageHeight);
                        if (image != null)
                        {
                            results.Add(new ImageSearchResult
                            {
                                SpotifyId = sr.Id,
                                ArtistSpotifyId = artist.Id,
                                FromPlugin = DisplayName,
                                Height = image.Height,
                                MediaUrl = image.Url,
                                Rank = rank,
                                ThumbnailUrl = image.Url,
                                Title = sr.Name,
                                UniqueId = SafeParser.Hash(sr.Id),
                                Width = image.Width
                            });
                        }
                    }
                }

                if (results.Count > 0)
                {
                    logger.Debug("[{DisplayName}] found [{ImageCount}] for Album [{Query}]",
                        DisplayName,
                        results.Count,
                        query.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error searching for album query [{Query}]", query.ToString());
        }

        return new OperationResult<ImageSearchResult[]?>
        {
            Data = results.OrderBy(x => x.Rank).ToArray()
        };
    }

    public async Task<OperationResult<ImageSearchResult[]?>> DoArtistImageSearch(ArtistQuery query, int maxResults,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ImageSearchResult>();

        try
        {
            var apiClientId = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyApiKey);
            var apiClientSecret = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyClientSecret);

            if (string.IsNullOrWhiteSpace(apiClientId) || string.IsNullOrWhiteSpace(apiClientSecret))
            {
                return new OperationResult<ImageSearchResult[]?>("Spotify API key not configured.")
                {
                    Data = []
                };
            }

            var apiAccessToken = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyAccessToken);

            var config = SpotifyClientConfig.CreateDefault();

            if (string.IsNullOrWhiteSpace(apiAccessToken))
            {
                var request = new ClientCredentialsRequest(apiClientId, apiClientSecret);
                var tokenResponse = await new OAuthClient(config).RequestToken(request, cancellationToken);
                apiAccessToken = tokenResponse.AccessToken;
                await settingService.SetAsync(SettingRegistry.SearchEngineSpotifyAccessToken, apiAccessToken,
                    cancellationToken);
            }

            var spotify = new SpotifyClient(config.WithToken(apiAccessToken));
            SearchResponse? searchResult = null;
            try
            {
                searchResult = await ExecuteSpotifyAsync(
                    token => spotify.Search.Item(new SearchRequest(SearchRequest.Types.Artist, query.Name), token),
                    "artist-image-search",
                    cancellationToken);
            }
            catch (APIUnauthorizedException)
            {
                var request = new ClientCredentialsRequest(apiClientId, apiClientSecret);
                var refreshResponse = await new OAuthClient(config).RequestToken(request, cancellationToken);
                apiAccessToken = refreshResponse.AccessToken;
                await settingService.SetAsync(SettingRegistry.SearchEngineSpotifyAccessToken, apiAccessToken,
                    cancellationToken);
            }

            if (searchResult?.Artists?.Items?.Count > 0)
            {
                foreach (var sr in searchResult.Artists?.Items?.Where(x => x.Images?.Count > 0) ?? [])
                {
                    var biggestImageHeight = sr.Images?.Max(x => x.Height) ?? 0;

                    short rank = 10;

                    var image = sr.Images?.FirstOrDefault(x => x.Height == biggestImageHeight);
                    if (image != null)
                    {
                        results.Add(new ImageSearchResult
                        {
                            SpotifyId = sr.Id,
                            ArtistSpotifyId = sr.Id,
                            FromPlugin = DisplayName,
                            Height = image.Height,
                            MediaUrl = image.Url,
                            Rank = rank,
                            ThumbnailUrl = image.Url,
                            Title = sr.Name,
                            UniqueId = SafeParser.Hash(sr.Id),
                            Width = image.Width
                        });
                    }
                }
            }

            if (results.Count > 0)
            {
                logger.Debug("[{DisplayName}] found [{ImageCount}] for Artist [{Query}]",
                    DisplayName,
                    results.Count,
                    query.ToString());
            }
        }
        catch (APITooManyRequestsException)
        {
            // Already handled by ExecuteSpotifyAsync with cooldown - don't log as error
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error searching for artist query [{Query}]", query.ToString());
        }

        return new OperationResult<ImageSearchResult[]?>
        {
            Data = results.OrderBy(x => x.Rank).ToArray()
        };
    }

    public bool StopProcessing { get; } = false;

    public string Id => "BBAC49B7-0EDF-4D31-8A54-C9126509C2CE";

    public string DisplayName => "Spotify Service";

    public bool IsEnabled { get; set; } = false;

    public int SortOrder { get; } = 100; // Last resort - Spotify has aggressive rate limiting

    public async Task<PagedResult<ArtistSearchResult>> DoArtistSearchAsync(ArtistQuery query, int maxResults,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ArtistSearchResult>();
        var cacheKey = query.NameNormalized ?? query.Name ?? string.Empty;

        try
        {
            var apiClientId = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyApiKey);
            var apiClientSecret = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyClientSecret);

            if (string.IsNullOrWhiteSpace(apiClientId) || string.IsNullOrWhiteSpace(apiClientSecret))
            {
                return new PagedResult<ArtistSearchResult>(["Spotify API key not configured."])
                {
                    Data = []
                };
            }

            if (query.Name.Nullify() == null)
            {
                return new PagedResult<ArtistSearchResult>(["Query Name value is invalid."])
                {
                    Data = []
                };
            }

            if (!string.IsNullOrWhiteSpace(cacheKey) && _artistSearchCache.TryGetValue(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            var apiAccessToken = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyAccessToken);

            var config = SpotifyClientConfig.CreateDefault();

            if (string.IsNullOrWhiteSpace(apiAccessToken))
            {
                var request = new ClientCredentialsRequest(apiClientId, apiClientSecret);
                var tokenResponse = await new OAuthClient(config).RequestToken(request, cancellationToken);
                apiAccessToken = tokenResponse.AccessToken;
                await settingService.SetAsync(SettingRegistry.SearchEngineSpotifyAccessToken, apiAccessToken,
                    cancellationToken);
            }

            var spotify = new SpotifyClient(config.WithToken(apiAccessToken));
            SearchResponse? searchResult = null;
            try
            {
                searchResult = await ExecuteSpotifyAsync(
                    token => spotify.Search.Item(
                        new SearchRequest(SearchRequest.Types.Artist, query.Name ?? query.NameNormalized ?? string.Empty),
                        token),
                    "artist-search",
                    cancellationToken);
            }
            catch (APIUnauthorizedException)
            {
                var request = new ClientCredentialsRequest(apiClientId, apiClientSecret);
                var refreshResponse = await new OAuthClient(config).RequestToken(request, cancellationToken);
                apiAccessToken = refreshResponse.AccessToken;
                await settingService.SetAsync(SettingRegistry.SearchEngineSpotifyAccessToken, apiAccessToken,
                    cancellationToken);
            }

            if (searchResult?.Artists?.Items?.Count > 0)
            {
                foreach (var sr in searchResult.Artists.Items)
                {
                    results.Add(new ArtistSearchResult
                    {
                        Name = sr.Name,
                        FromPlugin = DisplayName,
                        AlbumCount = null,
                        UniqueId = SafeParser.Hash(sr.Id),
                        Rank = 1,
                        ImageUrl = sr.Images.FirstOrDefault()?.Url,
                        ThumbnailUrl = sr.Images.LastOrDefault()?.Url,
                        SpotifyId = sr.Id
                    });
                    if (results.Count >= maxResults)
                    {
                        break;
                    }
                }

                if (results.Count > 0)
                {
                    var newResults = new List<ArtistSearchResult>();
                    foreach (var artist in results.Where(x => x.SpotifyId != null))
                    {
                        var artistAlbumsResult = await ExecuteSpotifyAsync(
                            token => spotify.Artists.GetAlbums(artist.SpotifyId!,
                                new ArtistsAlbumsRequest(), token),
                            "artist-albums",
                            cancellationToken);
                        if (artistAlbumsResult?.Items?.Count > 0)
                        {
                            newResults.Add(artist with
                            {
                                Releases = artistAlbumsResult.Items.Select(x => new AlbumSearchResult
                                {
                                    AlbumType = x.AlbumType.ToNormalizedString() == "ALBUM"
                                        ? AlbumType.Album
                                        : AlbumType.NotSet,
                                    Name = x.Name,
                                    NameNormalized = x.Name.ToNormalizedString() ?? x.Name,
                                    ReleaseDate = x.ReleaseDate,
                                    SortName = x.Name,
                                    SpotifyId = x.Id
                                }).ToArray()
                            });
                        }

                        var matchingAlbumCount = query.AlbumKeyValues == null
                            ? 0
                            : (artistAlbumsResult?.Items ?? []).Count(x =>
                                query.AlbumKeyValues?.Any(y =>
                                    y.Key == x.ReleaseDate[..4] && y.Value == x.Name.ToNormalizedString()) ?? false);
                        artist.Rank += matchingAlbumCount;
                    }

                    if (newResults.Count > 0)
                    {
                        results = newResults;
                    }
                }
            }
        }
        catch (APITooManyRequestsException)
        {
            // Already handled by ExecuteSpotifyAsync with cooldown - don't log as error
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error searching for artist query [{Query}]", query.ToString());
        }

        var response = new PagedResult<ArtistSearchResult>
        {
            Data = results.OrderBy(x => x.Rank).ToArray()
        };

        if (!string.IsNullOrWhiteSpace(cacheKey))
        {
            _artistSearchCache.TryAdd(cacheKey, response);
        }

        return response;
    }

    public async Task<PagedResult<SongSearchResult>> DoArtistTopSongsSearchAsync(int forArtist, int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (maxResults <= 0)
        {
            return new PagedResult<SongSearchResult>(["No results requested."]) { Data = [] };
        }

        var apiClientId = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyApiKey);
        var apiClientSecret = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyClientSecret);

        if (string.IsNullOrWhiteSpace(apiClientId) || string.IsNullOrWhiteSpace(apiClientSecret))
        {
            return new PagedResult<SongSearchResult>(["Spotify API key not configured."]) { Data = [] };
        }

        var cacheKey = $"spotify:artist-top:{forArtist}:{maxResults}";

        return await cacheManager.GetAsync(cacheKey, async () =>
        {
            try
            {
                await using var scopedContext =
                    await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

                var artist = await scopedContext.Artists.AsNoTracking()
                    .Where(x => x.Id == forArtist)
                    .Select(x => new { x.Id, x.Name, x.SpotifyId })
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (artist == null)
                {
                    return new PagedResult<SongSearchResult>([$"Artist [{forArtist}] not found"]) { Data = [] };
                }

                if (artist.SpotifyId.Nullify() == null)
                {
                    logger.Warning("[{DisplayName}] artist [{Artist}] missing SpotifyId", DisplayName, artist.Name);
                    return new PagedResult<SongSearchResult>(["Artist missing SpotifyId"]) { Data = [] };
                }

                var apiAccessToken = configuration.GetValue<string>(SettingRegistry.SearchEngineSpotifyAccessToken);
                var config = SpotifyClientConfig.CreateDefault();

                if (string.IsNullOrWhiteSpace(apiAccessToken))
                {
                    var request = new ClientCredentialsRequest(apiClientId, apiClientSecret);
                    var tokenResponse = await new OAuthClient(config).RequestToken(request, cancellationToken);
                    apiAccessToken = tokenResponse.AccessToken;
                    await settingService.SetAsync(SettingRegistry.SearchEngineSpotifyAccessToken, apiAccessToken,
                        cancellationToken);
                }

                var spotify = new SpotifyClient(config.WithToken(apiAccessToken));
                SearchResponse? searchResult = null;
                var searchQuery = $"artist:{artist.Name}";
                var searchRequest = new SearchRequest(SearchRequest.Types.Track, searchQuery)
                {
                    Limit = Math.Min(maxResults, 50)
                };
                try
                {
                    searchResult = await ExecuteSpotifyAsync(
                        token => spotify.Search.Item(searchRequest, token),
                        "artist-top-tracks-search",
                        cancellationToken);
                }
                catch (APIUnauthorizedException)
                {
                    var request = new ClientCredentialsRequest(apiClientId, apiClientSecret);
                    var refreshResponse = await new OAuthClient(config).RequestToken(request, cancellationToken);
                    apiAccessToken = refreshResponse.AccessToken;
                    await settingService.SetAsync(SettingRegistry.SearchEngineSpotifyAccessToken, apiAccessToken,
                        cancellationToken);
                    spotify = new SpotifyClient(config.WithToken(apiAccessToken));
                    searchResult = await ExecuteSpotifyAsync(
                        token => spotify.Search.Item(searchRequest, token),
                        "artist-top-tracks-search",
                        cancellationToken);
                }

                var results = new List<SongSearchResult>();
                if (searchResult?.Tracks?.Items?.Any() ?? false)
                {
                    var ordered = searchResult.Tracks.Items
                        .Where(t => t.Artists?.Any(a =>
                            string.Equals(a.Name, artist.Name, StringComparison.OrdinalIgnoreCase)) ?? false)
                        .Take(maxResults)
                        .Select((track, index) => new SongSearchResult
                        {
                            ApiKey = Guid.NewGuid(),
                            Name = track.Name,
                            SortName = track.Name.ToNormalizedString() ?? track.Name,
                            SortOrder = index + 1,
                            InfoUrl = track.ExternalUrls?.FirstOrDefault().Value,
                            ImageUrl = track.Album?.Images?.FirstOrDefault()?.Url,
                            ThumbnailUrl = track.Album?.Images?.LastOrDefault()?.Url
                        }).ToArray();

                    results.AddRange(ordered);
                }

                return new PagedResult<SongSearchResult>
                {
                    Data = results.ToArray(),
                    TotalCount = results.Count,
                    TotalPages = 1,
                    CurrentPage = 1
                };
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error getting Spotify top songs for artist [{ArtistId}]", forArtist);
                return new PagedResult<SongSearchResult>(["Spotify top songs search failed."]) { Data = [] };
            }
        }, cancellationToken, TimeSpan.FromHours(6), ServiceBase.CacheName);
    }
}
