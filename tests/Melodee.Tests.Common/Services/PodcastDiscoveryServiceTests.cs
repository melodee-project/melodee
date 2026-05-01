using System.Net;
using DecentDB.EntityFrameworkCore;
using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Microsoft.EntityFrameworkCore;
using Moq;
using Moq.Protected;
using Serilog;

namespace Melodee.Tests.Common.Services;

public class PodcastDiscoveryServiceTests : IAsyncDisposable
{
    private readonly string _tempDbDir;
    private readonly DbContextOptions<MelodeeDbContext> _dbOptions;
    private readonly ILogger _logger;
    private readonly ICacheManager _cacheManager;
    private readonly Mock<IMelodeeConfigurationFactory> _configFactoryMock;
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;

    public PodcastDiscoveryServiceTests()
    {
        _tempDbDir = Path.Combine(Path.GetTempPath(), $"melodee-podcast-disc-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDbDir);
        var dbFile = Path.Combine(_tempDbDir, "melodee.ddb");

        _dbOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseDecentDB($"Data Source={dbFile}", x => x.UseNodaTime())
            .Options;

        using (var context = new MelodeeDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
        }

        _logger = new LoggerConfiguration().CreateLogger();
        _cacheManager = new FakeCacheManager(_logger, TimeSpan.FromMinutes(5), new Melodee.Common.Serialization.Serializer(_logger));

        _configFactoryMock = new Mock<IMelodeeConfigurationFactory>();
        var configMock = new Mock<IMelodeeConfiguration>();
        configMock.Setup(x => x.GetValue<bool>(SettingRegistry.PodcastEnabled)).Returns(true);
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(configMock.Object);

        _contextFactory = CreateContextFactory();

        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
    }

    private IDbContextFactory<MelodeeDbContext> CreateContextFactory()
    {
        var factoryMock = new Mock<IDbContextFactory<MelodeeDbContext>>();
        factoryMock.Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MelodeeDbContext(_dbOptions));
        return factoryMock.Object;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(_tempDbDir))
            {
                Directory.Delete(_tempDbDir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }

        return ValueTask.CompletedTask;
    }

    private PodcastDiscoveryService CreateService(HttpClient? httpClient = null)
    {
        if (httpClient != null)
        {
            _httpClientFactoryMock.Setup(x => x.CreateClient("PodcastDiscovery"))
                .Returns(httpClient);
        }

        return new(_logger, _cacheManager, _contextFactory, _configFactoryMock.Object, _httpClientFactoryMock.Object);
    }

    private static HttpClient CreateMockHttpClient(string responseContent, HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(responseContent)
            });

        return new HttpClient(handlerMock.Object);
    }

    [Fact]
    public async Task SearchAsync_WithEmptyQuery_ReturnsError()
    {
        var service = CreateService(CreateMockHttpClient("{}"));

        var result = await service.SearchAsync("");

        result.IsSuccess.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("query is required"));
    }

    [Fact]
    public async Task SearchAsync_WhenPodcastDisabled_ReturnsError()
    {
        var disabledConfigMock = new Mock<IMelodeeConfiguration>();
        disabledConfigMock.Setup(x => x.GetValue<bool>(SettingRegistry.PodcastEnabled)).Returns(false);

        var configFactoryMock = new Mock<IMelodeeConfigurationFactory>();
        configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(disabledConfigMock.Object);

        var service = new PodcastDiscoveryService(
            _logger, _cacheManager, _contextFactory, configFactoryMock.Object, _httpClientFactoryMock.Object);

        var result = await service.SearchAsync("test");

        result.IsSuccess.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("disabled"));
    }

    [Fact]
    public async Task SearchAsync_WithValidResponse_ReturnsResults()
    {
        const string itunesResponse = """
            {
                "resultCount": 2,
                "results": [
                    {
                        "collectionId": 123456,
                        "collectionName": "Test Podcast",
                        "artistName": "Test Artist",
                        "feedUrl": "https://example.com/feed.rss",
                        "artworkUrl600": "https://example.com/art.jpg",
                        "primaryGenreName": "Technology",
                        "trackCount": 50
                    },
                    {
                        "collectionId": 789012,
                        "collectionName": "Another Podcast",
                        "artistName": "Another Artist",
                        "feedUrl": "https://example2.com/feed.rss"
                    }
                ]
            }
            """;

        var httpClient = CreateMockHttpClient(itunesResponse);
        var service = CreateService(httpClient);

        var result = await service.SearchAsync("test podcast");

        result.IsSuccess.Should().BeTrue();
        result.Data!.TotalResults.Should().Be(2);
        result.Data.Results.Should().HaveCount(2);
        result.Data.Results[0].Title.Should().Be("Test Podcast");
        result.Data.Results[0].Artist.Should().Be("Test Artist");
        result.Data.Results[0].FeedUrl.Should().Be("https://example.com/feed.rss");
    }

    [Fact]
    public async Task SearchAsync_WithHttpError_ReturnsError()
    {
        var httpClient = CreateMockHttpClient("", HttpStatusCode.ServiceUnavailable);
        var service = CreateService(httpClient);

        var result = await service.SearchAsync("test");

        result.IsSuccess.Should().BeFalse();
        result.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LookupByItunesIdAsync_WithEmptyId_ReturnsError()
    {
        var service = CreateService(CreateMockHttpClient("{}"));

        var result = await service.LookupByItunesIdAsync("");

        result.IsSuccess.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("iTunes ID is required"));
    }

    [Fact]
    public async Task LookupByItunesIdAsync_WithValidId_ReturnsPodcast()
    {
        const string lookupResponse = """
            {
                "resultCount": 1,
                "results": [
                    {
                        "collectionId": 123456,
                        "collectionName": "Found Podcast",
                        "artistName": "Found Artist",
                        "feedUrl": "https://found.com/feed.rss",
                        "primaryGenreName": "Comedy"
                    }
                ]
            }
            """;

        var httpClient = CreateMockHttpClient(lookupResponse);
        var service = CreateService(httpClient);

        var result = await service.LookupByItunesIdAsync("123456");

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.Title.Should().Be("Found Podcast");
        result.Data.ItunesId.Should().Be("123456");
    }

    [Fact]
    public async Task LookupByItunesIdAsync_WithNotFound_ReturnsNotFound()
    {
        const string emptyResponse = """
            {
                "resultCount": 0,
                "results": []
            }
            """;

        var httpClient = CreateMockHttpClient(emptyResponse);
        var service = CreateService(httpClient);

        var result = await service.LookupByItunesIdAsync("nonexistent");

        result.IsSuccess.Should().BeFalse();
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task GetTrendingAsync_CallsSearchWithDefaultQuery()
    {
        const string searchResponse = """
            {
                "resultCount": 1,
                "results": [
                    {
                        "collectionId": 111,
                        "collectionName": "Trending Podcast",
                        "feedUrl": "https://trending.com/feed.rss"
                    }
                ]
            }
            """;

        var httpClient = CreateMockHttpClient(searchResponse);
        var service = CreateService(httpClient);

        var result = await service.GetTrendingAsync(10);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Results.Should().HaveCount(1);
    }
}
