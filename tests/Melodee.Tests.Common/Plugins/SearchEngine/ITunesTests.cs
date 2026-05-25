using System.Net;
using System.Text;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine.ITunes;
using Melodee.Common.Services.Caching;

namespace Melodee.Tests.Common.Plugins.SearchEngine;

public class ITunesTests : TestsBase
{
    [Fact]
    public async Task PerformITunesAlbumSearch()
    {
        using (var httpClient = new HttpClient())
        {
            var itunes = new ITunesSearchEngine(Logger, Serializer, new TestHttpClientFactory(httpClient),
                new FakeCacheManager(Logger, TimeSpan.FromMinutes(5), Serializer));
            var result = await itunes.DoAlbumImageSearch(new AlbumQuery
            {
                Year = 1983,
                Name = "Cargo",
                Artist = "Men At Work"
            }, 10);
            Assert.NotNull(result);
        }
    }

    [Fact]
    public async Task DoArtistSearchAsync_WithLargeArtistId_DeserializesResultAndUsesRequestedLimit()
    {
        Uri? requestedUri = null;
        var handler = new HttpHandlerStubDelegate((request, _) =>
        {
            requestedUri = request.RequestUri;
            const string json = """
                                {
                                  "resultCount": 1,
                                  "results": [
                                    {
                                      "wrapperType": "artist",
                                      "artistType": "Artist",
                                      "artistName": "Large Id Artist",
                                      "artistId": 3000000000,
                                      "amgArtistId": 4000000000,
                                      "primaryGenreId": 5000000000,
                                      "primaryGenreName": "Rock"
                                    }
                                  ]
                                }
                                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });
        using var httpClient = new HttpClient(handler);
        var itunes = new ITunesSearchEngine(
            Logger,
            Serializer,
            new TestHttpClientFactory(httpClient),
            new FakeCacheManager(Logger, TimeSpan.FromMinutes(5), Serializer));

        var result = await itunes.DoArtistSearchAsync(new ArtistQuery
        {
            Name = "Large Id Artist",
            Country = "US"
        }, 5);

        var artist = Assert.Single(result.Data ?? []);
        Assert.Equal("3000000000", artist.ItunesId);
        Assert.Equal("4000000000", artist.AmgId);
        Assert.Contains("limit=5", requestedUri?.ToString());
    }
}
