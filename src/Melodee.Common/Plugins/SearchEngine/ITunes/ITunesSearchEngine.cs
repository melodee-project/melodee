using System.Text.RegularExpressions;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Utility;
using Polly;
using Polly.Retry;
using Serilog;

namespace Melodee.Common.Plugins.SearchEngine.ITunes;

/// <summary>
///     Search apple itunes using the api
///     <remarks>
///         https://performance-partners.apple.com/search-api
///     </remarks>
/// </summary>
public class ITunesSearchEngine(
ILogger logger,
ISerializer serializer,
IHttpClientFactory httpClientFactory,
ICacheManager cacheManager)
: IAlbumImageSearchEnginePlugin, IArtistSearchEnginePlugin, IArtistImageSearchEnginePlugin
{
    private static readonly int Width = 1200;
    private static readonly int Height = 1200;
    public bool StopProcessing { get; } = false;

    public string Id => "36E4DB7A-18BB-4A7D-86F7-A3C653A21611";

    public string DisplayName => "ITunes Search Engine";

    public bool IsEnabled { get; set; }

    public int SortOrder { get; } = 3;

    public async Task<OperationResult<ImageSearchResult[]?>> DoAlbumImageSearch(AlbumQuery query, int maxResults,
        CancellationToken cancellationToken = default)
    {
        //https://itunes.apple.com/search?term=Cargo&entity=album&country=US&media=music

        var results = new List<ImageSearchResult>();

        if (string.IsNullOrWhiteSpace(query.Name))
        {
            return new OperationResult<ImageSearchResult[]?>
            {
                Data = []
            };
        }

        var httpClient = httpClientFactory.CreateClient();
        var requestUri =
            $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query.Name.Trim())}&entity=album&country={query.Country}&media=music&limit={maxResults}";

        try
        {
            var response = await httpClient.GetAsync(requestUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new OperationResult<ImageSearchResult[]?>
                {
                    Data = []
                };
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResult = serializer.Deserialize<ITunesSearchResult>(jsonResponse);
            if (searchResult?.Results?.Any() ?? false)
            {
                var na = query.Artist.ToNormalizedString();
                foreach (var sr in searchResult.Results.Where(x => x is
                { ArtworkUrl100: not null, ArtworkUrl60: not null }))
                {
                    if (sr.ArtistName.ToNormalizedString() == na)
                    {
                        short rank = 10;
                        if (sr.ReleaseDate?.Year == query.Year)
                        {
                            rank = 20;
                        }

                        var mediaUrl = sr.ArtworkUrl100!.Replace("100x100bb", "600x600bb");
                        results.Add(new ImageSearchResult
                        {
                            ArtistAmgId = sr.AmgArtistId?.ToString(),
                            ArtistItunesId = sr.ArtistId?.ToString(),
                            FromPlugin = DisplayName,
                            Height = 600,
                            ItunesId = sr.CollectionId?.ToString(),
                            MediaUrl = mediaUrl,
                            Rank = rank,
                            ReleaseDate = sr.ReleaseDate,
                            ThumbnailUrl = sr.ArtworkUrl100!,
                            Title = sr.CollectionName,
                            UniqueId = sr.CollectionId ?? sr.ArtistId ?? 0,
                            Width = 600
                        });
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
            logger.Error(ex, "Error searching for album image url [{Url}] query [{Query}]", requestUri,
                query.ToString());
        }

        return new OperationResult<ImageSearchResult[]?>
        {
            Data = results.OrderBy(x => x.Rank).ToArray()
        };
    }

    public async Task<OperationResult<ImageSearchResult[]?>> DoArtistImageSearch(ArtistQuery query, int maxResults,
        CancellationToken cancellationToken = default)
    {
        //https://itunes.apple.com/search?term=Colin%2BHay&entity=allArtist&country=US&media=all

        var results = new List<ImageSearchResult>();

        if (string.IsNullOrWhiteSpace(query.Name))
        {
            return new OperationResult<ImageSearchResult[]?>
            {
                Data = []
            };
        }

        var httpClient = httpClientFactory.CreateClient();
        var requestUri =
            $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query.Name.Trim())}&entity=allArtist&country={query.Country}&media=all&limit={maxResults}";

        try
        {
            var response = await httpClient.GetAsync(requestUri, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new OperationResult<ImageSearchResult[]?>
                {
                    Data = []
                };
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResult = serializer.Deserialize<ITunesSearchResult>(jsonResponse);
            if (searchResult?.Results?.FirstOrDefault()?.ArtistId != null)
            {
                var sr = searchResult.Results.First();
                var mediaUrl = await GetArtistArtworkUrlAsync(httpClient,
                    searchResult.Results.First().ArtistId.ToString()!, cancellationToken);
                if (mediaUrl.Nullify() != null)
                {
                    results.Add(new ImageSearchResult
                    {
                        ArtistAmgId = sr.AmgArtistId?.ToString(),
                        ArtistItunesId = sr.ArtistId?.ToString(),
                        FromPlugin = DisplayName,
                        Height = 600,
                        ItunesId = sr.CollectionId?.ToString(),
                        MediaUrl = mediaUrl!,
                        Rank = 1,
                        ThumbnailUrl = sr.ArtworkUrl100!,
                        Title = sr.CollectionName,
                        UniqueId = sr.CollectionId ?? sr.ArtistId ?? 0,
                        Width = 600
                    });
                }

                if (results.Count > 0)
                {
                    logger.Debug("[{DisplayName}] found [{ImageCount}] for Artist [{Query}]",
                        DisplayName,
                        results.Count,
                        query.ToString());
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error searching for artist image url [{Url}] query [{Query}]", requestUri,
                query.ToString());
        }

        return new OperationResult<ImageSearchResult[]?>
        {
            Data = results.OrderBy(x => x.Rank).ToArray()
        };
    }

    public async Task<PagedResult<ArtistSearchResult>> DoArtistSearchAsync(ArtistQuery query, int maxResults,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query.Name))
        {
            return new PagedResult<ArtistSearchResult>(["Query Name value is invalid."]) { Data = [] };
        }

        var cacheKey = $"itunes:artist:{query.NameNormalized}:{query.Country}:{maxResults}";

        return await cacheManager.GetAsync(cacheKey, async () =>
        {
            var results = new List<ArtistSearchResult>();
            var httpClient = httpClientFactory.CreateClient();
            var requestUri =
                $"https://itunes.apple.com/search?term={Uri.EscapeDataString(query.Name.Trim())}&entity=musicArtist&country={query.Country}&media=music&limit={maxResults}";

            try
            {
                var response = await httpClient.GetAsync(requestUri, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return new PagedResult<ArtistSearchResult>([$"iTunes search failed with status {response.StatusCode}"])
                    {
                        Data = []
                    };
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                var searchResult = serializer.Deserialize<ITunesSearchResult>(jsonResponse);
                if (searchResult?.Results?.Any() ?? false)
                {
                    var normalizedQuery = query.NameNormalized;
                    foreach (var sr in searchResult.Results)
                    {
                        if (sr.ArtistName.Nullify() == null)
                        {
                            continue;
                        }

                        var isExact = sr.ArtistName.ToNormalizedString() == normalizedQuery;
                        var rank = isExact ? 10 : 1;

                        results.Add(new ArtistSearchResult
                        {
                            Name = sr.ArtistName!,
                            SortName = sr.ArtistName?.ToNormalizedString(),
                            FromPlugin = DisplayName,
                            AlbumCount = sr.TrackCount,
                            UniqueId = SafeParser.Hash($"{sr.ArtistId}:{sr.AmgArtistId}:{sr.ArtistName}"),
                            Rank = rank,
                            ImageUrl = sr.ArtworkUrl100,
                            ThumbnailUrl = sr.ArtworkUrl60 ?? sr.ArtworkUrl100,
                            AmgId = sr.AmgArtistId?.ToString(),
                            ItunesId = sr.ArtistId?.ToString(),
                            AlternateNames = sr.CollectionName.Nullify() != null
                                ? [sr.CollectionName.ToNormalizedString() ?? sr.CollectionName ?? string.Empty]
                                : null
                        });
                    }

                    if (results.Count > 0)
                    {
                        logger.Debug("[{DisplayName}] found [{Count}] artists for [{Query}]",
                            DisplayName,
                            results.Count,
                            query.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Error searching for artist url [{Url}] query [{Query}]", requestUri, query.ToString());
                return new PagedResult<ArtistSearchResult>(["iTunes search failed."]) { Data = [] };
            }

            var ordered = results.OrderByDescending(x => x.Rank).ThenBy(x => x.SortName ?? x.Name).Take(maxResults)
                .ToArray();

            return new PagedResult<ArtistSearchResult>
            {
                Data = ordered,
                TotalCount = ordered.Length,
                TotalPages = 1,
                CurrentPage = 1
            };
        }, cancellationToken, TimeSpan.FromHours(12), ServiceBase.CacheName);
    }

    private async Task<string?> GetArtistArtworkUrlAsync(HttpClient client, string artistId,
        CancellationToken cancellationToken = default)
    {
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMinutes(2)
            })
            .Build();

        return await pipeline.ExecuteAsync(
            static async (state, token) => await GetArtistArtworkUrlAsyncAction(state.client, state.artistId),
            (client, artistId),
            cancellationToken);
    }

    private static async Task<string?> GetArtistArtworkUrlAsyncAction(HttpClient client, string artistId)
    {
        var url = $"https://music.apple.com/de/artist/{artistId}";

        var document = await client.GetStringAsync(url);

        var regex = new Regex(
            "<meta property=\"og:image\" content=\"(.*png)\"",
            RegexOptions.None,
            TimeSpan.FromSeconds(5));
        var match = regex.Match(document);

        if (match.Success)
        {
            var rawImageUrl = match.Groups[1].Value;
            var imageUrl = Regex.Replace(
                rawImageUrl,
                @"[\d]+x[\d]+(cw)+",
                $"{Width}x{Height}cc",
                RegexOptions.None,
                TimeSpan.FromSeconds(5)
            );
            return imageUrl;
        }

        return null;
    }
}
