using DecentDB.EntityFrameworkCore;
using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Security;
using Microsoft.EntityFrameworkCore;
using Moq;
using NodaTime;
using Serilog;

namespace Melodee.Tests.Common.Services;

public class PodcastServiceTests : IAsyncDisposable
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

    public PodcastServiceTests()
    {
        _tempDbDir = Path.Combine(Path.GetTempPath(), $"melodee-podcast-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDbDir);
        var dbFile = Path.Combine(_tempDbDir, "melodee.ddb");

        _dbOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseDecentDB($"Data Source={dbFile}", x => x.UseNodaTime())
            .Options;

        using (var context = new MelodeeDbContext(_dbOptions))
        {
            context.Database.EnsureCreated();
            context.SaveChanges();
        }

        _logger = new LoggerConfiguration().CreateLogger();
        _cacheManager = new FakeCacheManager(_logger, TimeSpan.FromMinutes(5), new Melodee.Common.Serialization.Serializer(_logger));

        _configFactoryMock = new Mock<IMelodeeConfigurationFactory>();
        var configMock = new Mock<IMelodeeConfiguration>();
        configMock.Setup(x => x.GetValue<int>(SettingRegistry.PodcastRefreshMaxItemsPerChannel)).Returns(50);
        configMock.Setup(x => x.GetValue<long>(SettingRegistry.PodcastHttpMaxFeedBytes)).Returns(10 * 1024 * 1024);
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

    private PodcastService CreateService() =>
        new(_logger, _cacheManager, _contextFactory, _configFactoryMock.Object,
            _libraryServiceMock.Object, _ssrfValidatorMock.Object, _podcastHttpClient);

    [Fact]
    public async Task CreateChannelAsync_WithValidUrl_CreatesChannel()
    {
        var service = CreateService();
        const int userId = 1;
        const string feedUrl = "https://example.com/podcast.rss";

        var result = await service.CreateChannelAsync(userId, feedUrl);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().NotBeNull();
        result.Data!.UserId.Should().Be(userId);
        result.Data.FeedUrl.Should().Be(feedUrl);
    }

    [Fact]
    public async Task CreateChannelAsync_WithSsrfViolation_ReturnsError()
    {
        _ssrfValidatorMock.Setup(x => x.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SsrfValidationResult.Invalid("Access to private IP not allowed"));

        var service = CreateService();

        var result = await service.CreateChannelAsync(1, "https://192.168.1.1/podcast.rss");

        result.IsSuccess.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("private"));
    }

    [Fact]
    public async Task CreateChannelAsync_DuplicateFeedUrl_ReturnsError()
    {
        var service = CreateService();
        const int userId = 1;
        const string feedUrl = "https://example.com/podcast.rss";

        await service.CreateChannelAsync(userId, feedUrl);
        var result = await service.CreateChannelAsync(userId, feedUrl);

        result.IsSuccess.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("already exists"));
    }

    [Fact]
    public async Task ListChannelsAsync_ReturnsUserChannelsOnly()
    {
        var service = CreateService();

        await using var context = await _contextFactory.CreateDbContextAsync();
        context.PodcastChannels.Add(new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example1.com/feed",
            Title = "Podcast 1",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
        context.PodcastChannels.Add(new PodcastChannel
        {
            UserId = 2,
            FeedUrl = "https://example2.com/feed",
            Title = "Podcast 2",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
        await context.SaveChangesAsync();

        var result = await service.ListChannelsAsync(1, limit: 100);

        result.Data.Should().HaveCount(1);
        result.Data!.First().Title.Should().Be("Podcast 1");
    }

    [Fact]
    public async Task ListChannelsAsync_ExcludesDeletedChannels()
    {
        var service = CreateService();

        await using var context = await _contextFactory.CreateDbContextAsync();
        context.PodcastChannels.Add(new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example1.com/feed",
            Title = "Active Podcast",
            IsDeleted = false,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
        context.PodcastChannels.Add(new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example2.com/feed",
            Title = "Deleted Podcast",
            IsDeleted = true,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
        await context.SaveChangesAsync();

        var result = await service.ListChannelsAsync(1, limit: 100);

        result.Data.Should().HaveCount(1);
        result.Data!.First().Title.Should().Be("Active Podcast");
    }

    [Fact]
    public async Task DeleteChannelAsync_SoftDelete_MarksAsDeleted()
    {
        var service = CreateService();

        await using var context = await _contextFactory.CreateDbContextAsync();
        var channel = new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example.com/feed",
            Title = "Test Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastChannels.Add(channel);
        await context.SaveChangesAsync();
        var channelId = channel.Id;

        var result = await service.DeleteChannelAsync(channelId, 1, softDelete: true);

        result.IsSuccess.Should().BeTrue();

        await using var verifyContext = await _contextFactory.CreateDbContextAsync();
        // Must use IgnoreQueryFilters() to find soft-deleted records since there's a global query filter
        var deletedChannel = await verifyContext.PodcastChannels
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == channelId);
        deletedChannel.Should().NotBeNull();
        deletedChannel!.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteChannelAsync_HardDelete_RemovesChannel()
    {
        var service = CreateService();

        await using var context = await _contextFactory.CreateDbContextAsync();
        var channel = new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example.com/feed",
            Title = "Test Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastChannels.Add(channel);
        await context.SaveChangesAsync();
        var channelId = channel.Id;

        var result = await service.DeleteChannelAsync(channelId, 1, softDelete: false);

        result.IsSuccess.Should().BeTrue();

        await using var verifyContext = await _contextFactory.CreateDbContextAsync();
        var deletedChannel = await verifyContext.PodcastChannels.FindAsync(channelId);
        deletedChannel.Should().BeNull();
    }

    [Fact]
    public async Task DeleteChannelAsync_WrongUser_ReturnsError()
    {
        var service = CreateService();

        await using var context = await _contextFactory.CreateDbContextAsync();
        var channel = new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example.com/feed",
            Title = "Test Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastChannels.Add(channel);
        await context.SaveChangesAsync();
        var channelId = channel.Id;

        var result = await service.DeleteChannelAsync(channelId, userId: 999);

        result.IsSuccess.Should().BeFalse();
        result.Messages.Should().Contain(m => m.Contains("not found"));
    }

    [Fact]
    public async Task QueueDownloadAsync_ValidEpisode_SetsStatusToQueued()
    {
        var service = CreateService();

        await using var context = await _contextFactory.CreateDbContextAsync();
        var channel = new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example.com/feed",
            Title = "Test Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastChannels.Add(channel);
        await context.SaveChangesAsync();

        var episode = new PodcastEpisode
        {
            PodcastChannelId = channel.Id,
            EpisodeKey = "ep1",
            Title = "Episode 1",
            EnclosureUrl = "https://example.com/ep1.mp3",
            DownloadStatus = PodcastEpisodeDownloadStatus.None,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastEpisodes.Add(episode);
        await context.SaveChangesAsync();

        var result = await service.QueueDownloadAsync(episode.Id, 1);

        result.IsSuccess.Should().BeTrue();

        await using var verifyContext = await _contextFactory.CreateDbContextAsync();
        var updatedEpisode = await verifyContext.PodcastEpisodes.FindAsync(episode.Id);
        updatedEpisode!.DownloadStatus.Should().Be(PodcastEpisodeDownloadStatus.Queued);
    }

    [Fact]
    public async Task QueueDownloadAsync_AlreadyDownloaded_ReturnsSuccess()
    {
        var service = CreateService();

        await using var context = await _contextFactory.CreateDbContextAsync();
        var channel = new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example.com/feed",
            Title = "Test Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastChannels.Add(channel);
        await context.SaveChangesAsync();

        var episode = new PodcastEpisode
        {
            PodcastChannelId = channel.Id,
            EpisodeKey = "ep1",
            Title = "Episode 1",
            EnclosureUrl = "https://example.com/ep1.mp3",
            DownloadStatus = PodcastEpisodeDownloadStatus.Downloaded,
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastEpisodes.Add(episode);
        await context.SaveChangesAsync();

        var result = await service.QueueDownloadAsync(episode.Id, 1);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task GetNewestEpisodesAsync_ReturnsEpisodesForUser()
    {
        var service = CreateService();

        // Keep context open during test (consistent with other working tests)
        await using var context = await _contextFactory.CreateDbContextAsync();
        var channel = new PodcastChannel
        {
            UserId = 1,
            FeedUrl = "https://example.com/feed",
            Title = "Test Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastChannels.Add(channel);
        await context.SaveChangesAsync();

        var episodes = new[]
        {
            new PodcastEpisode
            {
                PodcastChannelId = channel.Id,
                EpisodeKey = "ep1",
                Title = "Episode 1",
                EnclosureUrl = "https://example.com/ep1.mp3",
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            },
            new PodcastEpisode
            {
                PodcastChannelId = channel.Id,
                EpisodeKey = "ep2",
                Title = "Episode 2",
                EnclosureUrl = "https://example.com/ep2.mp3",
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            }
        };
        context.PodcastEpisodes.AddRange(episodes);
        await context.SaveChangesAsync();

        var result = await service.GetNewestEpisodesAsync(1, count: 10);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().HaveCount(2);
        result.Data!.Select(e => e.Title).Should().Contain("Episode 1");
        result.Data!.Select(e => e.Title).Should().Contain("Episode 2");
    }

    [Theory]
    [InlineData(0, 15)]   // Initial: 15 min * 2^0 = 15 min
    [InlineData(1, 30)]   // 15 min * 2^1 = 30 min
    [InlineData(2, 60)]   // 15 min * 2^2 = 60 min
    [InlineData(3, 120)]  // 15 min * 2^3 = 120 min
    [InlineData(4, 240)]  // 15 min * 2^4 = 240 min
    [InlineData(10, 1440)] // Capped at 24 hours (1440 min)
    public void CalculateNextSyncTime_ExponentialBackoff_CorrectMinutes(int failureCount, int expectedMinutes)
    {
        // Access via reflection since it's a private method
        var method = typeof(PodcastService).GetMethod("CalculateNextSyncTime",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        var result = method?.Invoke(null, [failureCount]) as Instant?;

        result.Should().NotBeNull();

        // Calculate expected: min(15 * 2^failureCount, 1440) minutes from now
        var now = SystemClock.Instance.GetCurrentInstant();
        var expectedDuration = Duration.FromMinutes(expectedMinutes);

        // Allow 1 second tolerance for timing
        var diff = (result!.Value - now - expectedDuration).TotalSeconds;
        Math.Abs(diff).Should().BeLessThan(1);
    }

    [Fact]
    public async Task ListEpisodesAsync_WithPlayHistory_PopulatesPlayedData()
    {
        var service = CreateService();
        const int userId = 1;

        await using var context = await _contextFactory.CreateDbContextAsync();

        // Create a user with all required fields
        context.Users.Add(new User
        {
            Id = userId,
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = Guid.NewGuid().ToString(),
            PasswordEncrypted = "encrypted_password",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });

        var channel = new PodcastChannel
        {
            UserId = userId,
            FeedUrl = "https://example.com/feed",
            Title = "Test Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastChannels.Add(channel);
        await context.SaveChangesAsync();

        var playedEpisode = new PodcastEpisode
        {
            PodcastChannelId = channel.Id,
            EpisodeKey = "played-ep",
            Title = "Played Episode",
            EnclosureUrl = "https://example.com/played.mp3",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        var unplayedEpisode = new PodcastEpisode
        {
            PodcastChannelId = channel.Id,
            EpisodeKey = "unplayed-ep",
            Title = "Unplayed Episode",
            EnclosureUrl = "https://example.com/unplayed.mp3",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastEpisodes.AddRange(playedEpisode, unplayedEpisode);
        await context.SaveChangesAsync();

        // Add play history for the first episode
        var playedAt = SystemClock.Instance.GetCurrentInstant();
        context.UserPodcastEpisodePlayHistories.AddRange(
            new UserPodcastEpisodePlayHistory
            {
                UserId = userId,
                PodcastEpisodeId = playedEpisode.Id,
                PlayedAt = playedAt.Minus(Duration.FromHours(2)),
                IsNowPlaying = false
            },
            new UserPodcastEpisodePlayHistory
            {
                UserId = userId,
                PodcastEpisodeId = playedEpisode.Id,
                PlayedAt = playedAt,
                IsNowPlaying = false
            }
        );
        await context.SaveChangesAsync();

        var request = new Melodee.Common.Models.PagedRequest { Page = 1, PageSize = 10 };
        var result = await service.ListEpisodesAsync(request, userId, channel.Id);

        result.Data.Should().HaveCount(2);

        var played = result.Data!.First(x => x.Title == "Played Episode");
        played.LastPlayedAt.Should().NotBeNull();
        played.PlayedCount.Should().Be(2);

        var unplayed = result.Data!.First(x => x.Title == "Unplayed Episode");
        unplayed.LastPlayedAt.Should().BeNull();
        unplayed.PlayedCount.Should().Be(0);
    }

    [Fact]
    public async Task ListEpisodesAsync_ExcludesNowPlayingFromPlayCount()
    {
        var service = CreateService();
        const int userId = 1;

        await using var context = await _contextFactory.CreateDbContextAsync();

        context.Users.Add(new User
        {
            Id = userId,
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@example.com",
            EmailNormalized = "TEST@EXAMPLE.COM",
            PublicKey = Guid.NewGuid().ToString(),
            PasswordEncrypted = "encrypted_password",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });

        var channel = new PodcastChannel
        {
            UserId = userId,
            FeedUrl = "https://example.com/feed",
            Title = "Test Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastChannels.Add(channel);
        await context.SaveChangesAsync();

        var episode = new PodcastEpisode
        {
            PodcastChannelId = channel.Id,
            EpisodeKey = "ep1",
            Title = "Test Episode",
            EnclosureUrl = "https://example.com/ep1.mp3",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastEpisodes.Add(episode);
        await context.SaveChangesAsync();

        // Add completed plays and one currently playing
        var playedAt = SystemClock.Instance.GetCurrentInstant();
        context.UserPodcastEpisodePlayHistories.AddRange(
            new UserPodcastEpisodePlayHistory
            {
                UserId = userId,
                PodcastEpisodeId = episode.Id,
                PlayedAt = playedAt.Minus(Duration.FromHours(1)),
                IsNowPlaying = false
            },
            new UserPodcastEpisodePlayHistory
            {
                UserId = userId,
                PodcastEpisodeId = episode.Id,
                PlayedAt = playedAt,
                IsNowPlaying = true  // Currently playing - should NOT count
            }
        );
        await context.SaveChangesAsync();

        var request = new Melodee.Common.Models.PagedRequest { Page = 1, PageSize = 10 };
        var result = await service.ListEpisodesAsync(request, userId, channel.Id);

        result.Data.Should().HaveCount(1);
        var episodeData = result.Data!.First();

        // Should only count completed plays (1), not the IsNowPlaying entry
        episodeData.PlayedCount.Should().Be(1);
    }

    [Fact]
    public async Task SearchEpisodesAsync_WithMatchingTitle_ReturnsEpisodes()
    {
        var service = CreateService();

        await using var context = await _contextFactory.CreateDbContextAsync();

        var user = new User
        {
            UserName = "SearchTestUser",
            UserNameNormalized = "SEARCHTESTUSER",
            Email = "test-searchepisodes@test.com",
            EmailNormalized = "TEST-SEARCHEPISODES@TEST.COM",
            PublicKey = Guid.NewGuid().ToString(),
            PasswordEncrypted = "encrypted_password",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var channel = new PodcastChannel
        {
            UserId = user.Id,
            FeedUrl = "https://example.com/search-test",
            Title = "Test Search Podcast",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.PodcastChannels.Add(channel);
        await context.SaveChangesAsync();

        context.PodcastEpisodes.Add(new PodcastEpisode
        {
            PodcastChannelId = channel.Id,
            EpisodeKey = "ep1",
            Title = "Introduction to Programming",
            TitleNormalized = "introduction to programming",
            EnclosureUrl = "https://example.com/ep1.mp3",
            PublishDate = Instant.FromUtc(2025, 1, 1, 0, 0),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
        context.PodcastEpisodes.Add(new PodcastEpisode
        {
            PodcastChannelId = channel.Id,
            EpisodeKey = "ep2",
            Title = "Advanced Topics",
            TitleNormalized = "advanced topics",
            EnclosureUrl = "https://example.com/ep2.mp3",
            PublishDate = Instant.FromUtc(2025, 1, 2, 0, 0),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        });
        await context.SaveChangesAsync();

        var request = new Melodee.Common.Models.PagedRequest { Page = 1, PageSize = 10 };
        var result = await service.SearchEpisodesAsync("programming", user.Id, request);

        result.Data.Should().HaveCount(1);
        result.Data!.First().Title.Should().Be("Introduction to Programming");
    }

    [Fact]
    public async Task SearchEpisodesAsync_WithEmptyQuery_ReturnsEmptyResult()
    {
        var service = CreateService();

        var request = new Melodee.Common.Models.PagedRequest { Page = 1, PageSize = 10 };
        var result = await service.SearchEpisodesAsync("", 1, request);

        result.TotalCount.Should().Be(0);
        result.Data.Should().BeEmpty();
    }

    [Fact]
    public async Task ChannelAutoDownloadEnabled_DefaultsFalse()
    {
        var service = CreateService();

        var result = await service.CreateChannelAsync(1, "https://example.com/auto-download-test.rss");

        result.IsSuccess.Should().BeTrue();
        result.Data!.AutoDownloadEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task ChannelRefreshIntervalHours_DefaultsNull()
    {
        var service = CreateService();

        var result = await service.CreateChannelAsync(1, "https://example.com/refresh-interval-test.rss");

        result.IsSuccess.Should().BeTrue();
        result.Data!.RefreshIntervalHours.Should().BeNull();
    }

    [Fact]
    public async Task UpdateChannelAsync_WithValidSettings_UpdatesChannel()
    {
        var service = CreateService();

        var createResult = await service.CreateChannelAsync(1, "https://example.com/update-test.rss");
        createResult.IsSuccess.Should().BeTrue();
        var channelId = createResult.Data!.Id;

        var updateResult = await service.UpdateChannelAsync(
            channelId,
            userId: 1,
            autoDownloadEnabled: true,
            refreshIntervalHours: 6,
            maxDownloadedEpisodes: 10,
            maxStorageBytes: 1073741824);

        updateResult.IsSuccess.Should().BeTrue();
        updateResult.Data!.AutoDownloadEnabled.Should().BeTrue();
        updateResult.Data.RefreshIntervalHours.Should().Be(6);
        updateResult.Data.MaxDownloadedEpisodes.Should().Be(10);
        updateResult.Data.MaxStorageBytes.Should().Be(1073741824);
    }

    [Fact]
    public async Task UpdateChannelAsync_WithZeroRefreshInterval_SetsToNull()
    {
        var service = CreateService();

        var createResult = await service.CreateChannelAsync(1, "https://example.com/update-zero-interval.rss");
        var channelId = createResult.Data!.Id;

        await service.UpdateChannelAsync(channelId, 1, refreshIntervalHours: 6);
        var updateResult = await service.UpdateChannelAsync(channelId, 1, refreshIntervalHours: 0);

        updateResult.IsSuccess.Should().BeTrue();
        updateResult.Data!.RefreshIntervalHours.Should().BeNull();
    }

    [Fact]
    public async Task UpdateChannelAsync_WithWrongUser_ReturnsNotFound()
    {
        var service = CreateService();

        var createResult = await service.CreateChannelAsync(1, "https://example.com/wrong-user-update.rss");
        var channelId = createResult.Data!.Id;

        var updateResult = await service.UpdateChannelAsync(channelId, userId: 999, autoDownloadEnabled: true);

        updateResult.IsSuccess.Should().BeFalse();
        updateResult.Type.Should().Be(Melodee.Common.Models.OperationResponseType.NotFound);
    }

    [Fact]
    public async Task UpdateChannelAsync_WithNonExistentChannel_ReturnsNotFound()
    {
        var service = CreateService();

        var updateResult = await service.UpdateChannelAsync(channelId: 99999, userId: 1, autoDownloadEnabled: true);

        updateResult.IsSuccess.Should().BeFalse();
        updateResult.Type.Should().Be(Melodee.Common.Models.OperationResponseType.NotFound);
    }

    [Fact]
    public async Task UpdateChannelAsync_PartialUpdate_OnlyChangesSpecifiedFields()
    {
        var service = CreateService();

        var createResult = await service.CreateChannelAsync(1, "https://example.com/partial-update.rss");
        var channelId = createResult.Data!.Id;

        await service.UpdateChannelAsync(channelId, 1, autoDownloadEnabled: true, refreshIntervalHours: 12);

        var partialUpdate = await service.UpdateChannelAsync(channelId, 1, refreshIntervalHours: 24);

        partialUpdate.IsSuccess.Should().BeTrue();
        partialUpdate.Data!.AutoDownloadEnabled.Should().BeTrue();
        partialUpdate.Data.RefreshIntervalHours.Should().Be(24);
    }
}
