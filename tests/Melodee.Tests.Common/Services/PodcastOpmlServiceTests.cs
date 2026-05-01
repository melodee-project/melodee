using DecentDB.EntityFrameworkCore;
using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Security;
using Microsoft.EntityFrameworkCore;
using Moq;
using NodaTime;
using Serilog;

namespace Melodee.Tests.Common.Services;

public class PodcastOpmlServiceTests : IAsyncDisposable
{
    private readonly string _tempDbDir;
    private readonly DbContextOptions<MelodeeDbContext> _dbOptions;
    private readonly ILogger _logger;
    private readonly ICacheManager _cacheManager;
    private readonly Mock<IMelodeeConfigurationFactory> _configFactoryMock;
    private readonly Mock<LibraryService> _libraryServiceMock;
    private readonly Mock<ISsrfValidator> _ssrfValidatorMock;
    private readonly PodcastHttpClient _podcastHttpClient;
    private readonly IDbContextFactory<MelodeeDbContext> _contextFactory;

    public PodcastOpmlServiceTests()
    {
        _tempDbDir = Path.Combine(Path.GetTempPath(), $"melodee-opml-test-{Guid.NewGuid():N}");
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
        _configFactoryMock.Setup(x => x.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(configMock.Object);

        _libraryServiceMock = new Mock<LibraryService>();

        _ssrfValidatorMock = new Mock<ISsrfValidator>();
        _ssrfValidatorMock.Setup(x => x.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SsrfValidationResult.Valid([]));

        _podcastHttpClient = new PodcastHttpClient(
            _logger, _ssrfValidatorMock.Object, _configFactoryMock.Object);

        _contextFactory = CreateContextFactory();
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

    private PodcastService CreatePodcastService() =>
        new(_logger, _cacheManager, _contextFactory, _configFactoryMock.Object,
            _libraryServiceMock.Object, _ssrfValidatorMock.Object, _podcastHttpClient);

    private PodcastOpmlService CreateOpmlService(PodcastService podcastService) =>
        new(_logger, _cacheManager, _contextFactory, podcastService);

    [Fact]
    public async Task ExportAsync_WithChannels_ReturnsValidOpml()
    {
        var podcastService = CreatePodcastService();
        var opmlService = CreateOpmlService(podcastService);

        await using var context = await _contextFactory.CreateDbContextAsync();
        context.PodcastChannels.Add(new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example1.com/feed.rss",
            Title = "Podcast One",
            Description = "Test podcast description",
            SiteUrl = "https://example1.com",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
        context.PodcastChannels.Add(new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example2.com/feed.rss",
            Title = "Podcast Two",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
        await context.SaveChangesAsync();

        var result = await opmlService.ExportAsync(1);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Contain("<opml");
        result.Data.Should().Contain("Podcast One");
        result.Data.Should().Contain("Podcast Two");
        result.Data.Should().Contain("https://example1.com/feed.rss");
        result.Data.Should().Contain("https://example2.com/feed.rss");
    }

    [Fact]
    public async Task ExportAsync_WithNoChannels_ReturnsEmptyOpml()
    {
        var podcastService = CreatePodcastService();
        var opmlService = CreateOpmlService(podcastService);

        var result = await opmlService.ExportAsync(999);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().Contain("<opml");
        result.Data.Should().Contain("<outline text=\"Podcasts\"");
    }

    [Fact]
    public async Task ImportAsync_WithValidOpml_ImportsChannels()
    {
        var podcastService = CreatePodcastService();
        var opmlService = CreateOpmlService(podcastService);

        const string opml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <opml version="2.0">
                <head>
                    <title>Test Podcasts</title>
                </head>
                <body>
                    <outline text="Podcasts">
                        <outline type="rss" text="Test Podcast" xmlUrl="https://import-test1.com/feed.rss" />
                        <outline type="rss" text="Another Podcast" xmlUrl="https://import-test2.com/feed.rss" />
                    </outline>
                </body>
            </opml>
            """;

        var result = await opmlService.ImportAsync(1, opml);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Imported.Should().Be(2);
        result.Data.ImportedFeeds.Should().Contain("https://import-test1.com/feed.rss");
        result.Data.ImportedFeeds.Should().Contain("https://import-test2.com/feed.rss");
    }

    [Fact]
    public async Task ImportAsync_WithDuplicates_SkipsExistingChannels()
    {
        var podcastService = CreatePodcastService();
        var opmlService = CreateOpmlService(podcastService);

        await using var context = await _contextFactory.CreateDbContextAsync();
        context.PodcastChannels.Add(new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://existing.com/feed.rss",
            Title = "Existing Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
        await context.SaveChangesAsync();

        const string opml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <opml version="2.0">
                <body>
                    <outline type="rss" text="Existing" xmlUrl="https://existing.com/feed.rss" />
                    <outline type="rss" text="New" xmlUrl="https://new-import.com/feed.rss" />
                </body>
            </opml>
            """;

        var result = await opmlService.ImportAsync(1, opml);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Imported.Should().Be(1);
        result.Data.Skipped.Should().Be(1);
        result.Data.Duplicates.Should().Contain("https://existing.com/feed.rss");
    }

    [Fact]
    public async Task ImportAsync_WithInvalidXml_ReturnsError()
    {
        var podcastService = CreatePodcastService();
        var opmlService = CreateOpmlService(podcastService);

        const string invalidOpml = "not valid xml at all";

        var result = await opmlService.ImportAsync(1, invalidOpml);

        result.IsSuccess.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("Invalid OPML format"));
    }

    [Fact]
    public async Task ImportAsync_WithNoFeeds_ReturnsError()
    {
        var podcastService = CreatePodcastService();
        var opmlService = CreateOpmlService(podcastService);

        const string emptyOpml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <opml version="2.0">
                <body>
                    <outline text="Empty folder" />
                </body>
            </opml>
            """;

        var result = await opmlService.ImportAsync(1, emptyOpml);

        result.IsSuccess.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("No podcast feeds found"));
    }
}
