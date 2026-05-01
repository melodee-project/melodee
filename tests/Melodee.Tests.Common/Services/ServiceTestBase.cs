using System.Net;
using DecentDB.EntityFrameworkCore;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Metadata;
using Melodee.Common.Models;
using Melodee.Common.Models.OpenSubsonic.Requests;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Models.Scrobbling;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.Conversion.Image;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Scrobbling;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.Spotify;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Services.ScriptEvaluation;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Services.Security;
using Microsoft.EntityFrameworkCore;
using Moq;
using Quartz;
using Rebus.Bus;
using Serilog;

namespace Melodee.Tests.Common.Services;

public abstract class ServiceTestBase : IDisposable, IAsyncDisposable
{
    private readonly DbContextOptions<ArtistSearchEngineServiceDbContext> _dbArtistSearchEngineContextOptions;
    private readonly string _tempDbDir;

    private readonly DbContextOptions<MelodeeDbContext> _dbContextOptions;

    protected ServiceTestBase()
    {
        Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();
        Serializer = new Serializer(Logger);
        CacheManager = new FakeCacheManager(Logger, TimeSpan.FromDays(1), Serializer);

        _tempDbDir = Path.Combine(Path.GetTempPath(), $"melodee-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDbDir);

        var melodeeDbFile = Path.Combine(_tempDbDir, "melodee.ddb");
        var artistSearchDbFile = Path.Combine(_tempDbDir, "artist-search.ddb");
        var musicBrainzDbFile = Path.Combine(_tempDbDir, "musicbrainz.ddb");

        _dbContextOptions = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseDecentDB($"Data Source={melodeeDbFile}", x => x.UseNodaTime())
            .Options;

        _dbArtistSearchEngineContextOptions = new DbContextOptionsBuilder<ArtistSearchEngineServiceDbContext>()
            .UseDecentDB($"Data Source={artistSearchDbFile}")
            .Options;

        using (var context = new MelodeeDbContext(_dbContextOptions))
        {
            context.Database.EnsureCreated();
            context.SaveChanges();
        }

        using (var context = new ArtistSearchEngineServiceDbContext(_dbArtistSearchEngineContextOptions))
        {
            context.Database.EnsureCreated();
            context.SaveChanges();
        }

        var musicBrainzDbContextOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={musicBrainzDbFile}")
            .Options;
        using (var context = new MusicBrainzDbContext(musicBrainzDbContextOptions))
        {
            context.Database.EnsureCreated();
            context.SaveChanges();
        }
    }

    protected ILogger Logger { get; }

    protected Serializer Serializer { get; set; }

    protected ICacheManager CacheManager { get; }

    public virtual ValueTask DisposeAsync()
    {
        CleanupTempDir();
        return ValueTask.CompletedTask;
    }

    public virtual void Dispose()
    {
        CleanupTempDir();
    }

    private void CleanupTempDir()
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
    }

    protected IFileSystemService MockFileSystemService() => new MockFileSystemService();

    protected AlbumDiscoveryService GetAlbumDiscoveryService()
    {
        return new AlbumDiscoveryService(
            Log.Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            new MockFileSystemService());
    }

    protected MediaEditService GetMediaEditService()
    {
        return new MediaEditService(
            Log.Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            GetAlbumDiscoveryService(),
            Serializer,
            MockHttpClientFactory());
    }

    protected IDbContextFactory<MelodeeDbContext> MockFactory()
    {
        var mockFactory = new Mock<IDbContextFactory<MelodeeDbContext>>();
        mockFactory.Setup(f
            => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new MelodeeDbContext(_dbContextOptions));
        return mockFactory.Object;
    }

    protected IDbContextFactory<ArtistSearchEngineServiceDbContext> MockArtistSearchEngineFactory()
    {
        var mockFactory = new Mock<IDbContextFactory<ArtistSearchEngineServiceDbContext>>();
        mockFactory.Setup(f
            => f.CreateDbContextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => new ArtistSearchEngineServiceDbContext(_dbArtistSearchEngineContextOptions));
        return mockFactory.Object;
    }

    protected ApiRequest GetApiRequest(string username, string salt, string password)
    {
        return new ApiRequest(
            [],
            false,
            username,
            "1.16.1",
            "json",
            null,
            null,
            password,
            salt,
            null,
            null,
            new UserPlayer(null,
                null,
                null,
                null));
    }

    protected IDbContextFactory<MusicBrainzDbContext> MockMusicBrainzDbContextFactory()
    {
        var mockFactory = new Mock<IDbContextFactory<MusicBrainzDbContext>>();
        var musicBrainzDbFile = Path.Combine(_tempDbDir, "musicbrainz.ddb");
        var dbContextOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={musicBrainzDbFile}")
            .Options;
        mockFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MusicBrainzDbContext(dbContextOptions));
        return mockFactory.Object;
    }

    protected IMusicBrainzRepository GetMusicBrainzRepository()
    {
        return new DecentDBMusicBrainzRepository(Log.Logger,
            MockConfigurationFactory(),
            MockMusicBrainzDbContextFactory());
    }

    protected ISpotifyClientBuilder MockSpotifyClientBuilder()
    {
        var mockFactory = new Mock<ISpotifyClientBuilder>();
        return mockFactory.Object;
    }

    protected ArtistSearchEngineService GetArtistSearchEngineService()
    {
        return new ArtistSearchEngineService(
            Logger,
            CacheManager,
            MockSettingService(),
            MockSpotifyClientBuilder(),
            MockConfigurationFactory(),
            MockFactory(),
            MockArtistSearchEngineFactory(),
            GetMusicBrainzRepository(),
            Serializer,
            MockHttpClientFactory());
    }

    protected ImageConvertor GetImageConvertor()
    {
        return new ImageConvertor(TestsBase.NewPluginsConfiguration());
    }

    protected IImageValidator GetImageValidator()
    {
        return new ImageValidator(TestsBase.NewPluginsConfiguration());
    }

    protected IAlbumValidator GetAlbumValidator()
    {
        return new AlbumValidator(TestsBase.NewPluginsConfiguration());
    }

    protected AlbumImageSearchEngineService GetAlbumImageSearchEngineService()
    {
        return new AlbumImageSearchEngineService(Logger,
            CacheManager,
            Serializer,
            MockSettingService(),
            MockConfigurationFactory(),
            MockFactory(),
            GetMusicBrainzRepository(),
            MockSpotifyClientBuilder(),
            MockHttpClientFactory());
    }

    protected OpenSubsonicApiService GetOpenSubsonicApiService()
    {
        return new OpenSubsonicApiService(
            Logger,
            CacheManager,
            MockFactory(),
            new DefaultImages
            {
                AlbumCoverBytes = [],
                ArtistBytes = [],
                PlaylistImageBytes = [],
                UserAvatarBytes = [],
                ChartImageBytes = []
            },
            MockConfigurationFactory(),
            GetUserService(),
            GetUserAuthenticationService(),
            GetUserProfileService(),
            GetArtistService(),
            GetAlbumService(),
            GetSongService(),
            GetSearchService(),
            CreateMockSchedulerFactory(),
            GetScrobbleService(),
            GetLibraryService(),
            GetArtistSearchEngineService(),
            GetPlaylistService(),
            GetChartService(),
            GetShareService(),
            GetRadioStationService(),
            GetUserQueueService(),
            GetStatisticsService(),
            MockBus(),
            GetLyricPlugin(),
            GetPodcastPlaybackService(),
            GetUserRatingService(),
            GetUserBookmarkService());
    }

    protected ISchedulerFactory CreateMockSchedulerFactory()
    {
        var mockScheduler = new Mock<IScheduler>();
        mockScheduler.Setup(x => x.GetCurrentlyExecutingJobs(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<IJobExecutionContext>());

        var mockSchedulerFactory = new Mock<ISchedulerFactory>();
        mockSchedulerFactory.Setup(x => x.GetScheduler(It.IsAny<CancellationToken>()))
                           .ReturnsAsync(mockScheduler.Object);

        return mockSchedulerFactory.Object;
    }

    protected ArtistService GetArtistService()
    {
        return new ArtistService(Logger,
            CacheManager,
            MockConfigurationFactory(),
            MockFactory(),
            Serializer,
            MockHttpClientFactory(),
            GetAlbumService(),
            MockBus(),
            MockFileSystemService());
    }

    protected AlbumService GetAlbumService()
    {
        return new AlbumService(Logger,
            CacheManager,
            MockConfigurationFactory(),
            MockFactory(),
            MockBus(),
            Serializer,
            MockHttpClientFactory(),
            GetMediaEditService(),
            MockFileSystemService());
    }

    protected StatisticsService GetStatisticsService()
    {
        return new StatisticsService(Logger, CacheManager, MockFactory(), GetPlaylistService());
    }

    protected LyricPlugin GetLyricPlugin()
    {
        return new LyricPlugin(Serializer, MockConfigurationFactory());
    }

    protected ShareService GetShareService()
    {
        return new ShareService(Logger, CacheManager, MockFactory());
    }

    protected UserBookmarkService GetUserBookmarkService()
    {
        return new UserBookmarkService(Logger, CacheManager, MockFactory());
    }

    protected UserRatingService GetUserRatingService()
    {
        return new UserRatingService(Logger, CacheManager, MockFactory(), GetArtistService(), GetAlbumService(), GetSongService(), GetUserProfileService());
    }

    protected SongService GetSongService()
    {
        return new SongService(Logger, CacheManager, MockFactory());
    }

    protected SearchService GetSearchService()
    {
        return new SearchService(Logger, CacheManager, MockFactory(), MockConfigurationFactory(), GetUserProfileService(), GetArtistService(), GetAlbumService(), GetSongService(), GetPodcastService(), MockMusicBrainzRepository(), MockBus());
    }

    protected PodcastService GetPodcastService()
    {
        return new PodcastService(Logger, CacheManager, MockFactory(), MockConfigurationFactory(), GetLibraryService(), MockSsrfValidator(), MockPodcastHttpClient());
    }

    protected ISsrfValidator MockSsrfValidator()
    {
        var mock = new Mock<ISsrfValidator>();
        mock.Setup(x => x.ValidateUrlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SsrfValidationResult.Valid([]));
        return mock.Object;
    }

    protected PodcastHttpClient MockPodcastHttpClient()
    {
        return new PodcastHttpClient(Logger, MockSsrfValidator(), MockConfigurationFactory());
    }

    protected PlaylistService GetPlaylistService()
    {
        return new PlaylistService(Logger, CacheManager, Serializer, MockConfigurationFactory(), MockFactory(), GetLibraryService());
    }

    protected ChartService GetChartService()
    {
        return new ChartService(Logger, CacheManager, MockFactory(), GetLibraryService());
    }

    protected INowPlayingRepository GetNowPlayingRepository()
    {
        return new NowPlayingDatabaseRepository(Logger, MockFactory());
    }

    protected LibraryService GetLibraryService()
    {
        return new LibraryService
        (
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            Serializer,
            GetMelodeeMetadataMaker()
        );
    }

    protected MelodeeMetadataMaker GetMelodeeMetadataMaker()
    {
        return new MelodeeMetadataMaker
        (
            Logger,
            MockConfigurationFactory(),
            Serializer,
            GetArtistSearchEngineService(),
            GetAlbumImageSearchEngineService(),
            MockHttpClientFactory(),
            GetMediaEditService());
    }

    protected ScrobbleService GetScrobbleService()
    {
        return new ScrobbleService(
            Logger,
            CacheManager,
            GetAlbumService(),
            MockFactory(),
            MockConfigurationFactory(),
            GetNowPlayingRepository());
    }

    protected IHttpClientFactory MockHttpClientFactory()
    {
        // var clientHandlerMock = new Mock<HttpHandlerStubDelegate>();
        // clientHandlerMock.Protected()
        //     .Setup<Task<HttpResponseMessage>>("SendAsync", ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
        //     .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK))
        //     .Verifiable();
        // clientHandlerMock.As<IDisposable>().Setup(s => s.Dispose());
        //
        // var httpClient = new HttpClient(clientHandlerMock.Object);

        // var clientFactoryMock = new Mock<IHttpClientFactory>(MockBehavior.Strict);
        // clientFactoryMock.Setup(cf => cf.CreateClient(It.IsAny<string>())).Returns(httpClient).Verifiable();
        //
        // clientFactoryMock.Verify(cf => cf.CreateClient());
        // clientHandlerMock.Protected().Verify("SendAsync", Times.Exactly(1), ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>());
        // return clientFactoryMock.Object;

        var clientHandlerStub = new HttpHandlerStubDelegate((_, _) =>
        {
            var response = new HttpResponseMessage { StatusCode = HttpStatusCode.OK };
            return Task.FromResult(response);
        });
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(m => m.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(clientHandlerStub));
        return factoryMock.Object;
    }

    protected LibraryService MockLibraryService()
    {
        var mock = new Mock<LibraryService>();
        mock.Setup(f => f.ListAsync(
                It.Is<PagedRequest>(_ => true),
                It.Is<CancellationToken>(_ => true)))
            .ReturnsAsync(TestsBase.TestLibraries());
        mock.Setup(f
            => f.GetStorageLibrariesAsync(It.Is<CancellationToken>(_ => true))).ReturnsAsync(new OperationResult<Library[]>
            {
                Data = TestsBase.TestLibraries().Data.Where(x => x.TypeValue == LibraryType.Storage).ToArray()
            });
        mock.Setup(f
            => f.GetStagingLibraryAsync(It.Is<CancellationToken>(_ => true))).ReturnsAsync(TestsBase.TestStagingLibrary());
        return mock.Object;
    }

    protected IPasswordHashService MockPasswordHashService()
    {
        return new Mock<IPasswordHashService>().Object;
    }

    protected ISecretProtector MockSecretProtector()
    {
        return new Mock<ISecretProtector>().Object;
    }

    protected UserProfileService GetUserProfileService()
    {
        return new UserProfileService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            GetLibraryService(),
            GetArtistService(),
            GetAlbumService(),
            GetSongService(),
            GetPlaylistService(),
            GetPodcastService(),
            MockBus(),
            MockPasswordHashService(),
            MockSecretProtector());
    }

    protected UserAuthenticationService GetUserAuthenticationService()
    {
        return new UserAuthenticationService(
            Logger,
            MockPasswordHashService(),
            MockSecretProtector(),
            MockBus(),
            GetUserProfileService(),
            MockConfigurationFactory());
    }

    protected UserService GetUserService()
    {
        return new UserService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            GetLibraryService(),
            GetArtistService(),
            GetAlbumService(),
            GetSongService(),
            GetPlaylistService(),
            GetPodcastService(),
            MockBus(),
            GetUserAuthenticationService(),
            GetUserProfileService());
    }

    protected IBus MockBus()
    {
        var busMock = new Mock<IBus>();
        busMock.Setup(b => b.SendLocal(It.IsAny<object>(), It.IsAny<Dictionary<string, string>>())).Returns(Task.CompletedTask);
        return busMock.Object;
    }

    protected IMusicBrainzRepository MockMusicBrainzRepository()
    {
        return new Mock<IMusicBrainzRepository>().Object;
    }

    protected IMelodeeConfigurationFactory MockConfigurationFactory()
    {
        var mock = new Mock<IMelodeeConfigurationFactory>();
        mock.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TestsBase.NewPluginsConfiguration);
        return mock.Object;
    }

    protected SettingService MockSettingService()
    {
        var mock = new Mock<SettingService>();
        mock.Setup(f => f.GetAllSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TestsBase.NewConfiguration());
        return mock.Object;
    }

    protected static void AssertResultIsSuccessful<T>(PagedResult<T> result) where T : notnull
    {
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    protected static void AssertResultIsSuccessful<T>(OperationResult<T?>? result)
    {
        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    protected RadioStationService GetRadioStationService()
    {
        return new RadioStationService(Logger, CacheManager, MockFactory());
    }

    protected UserQueueService GetUserQueueService()
    {
        return new UserQueueService(
            Logger,
            CacheManager,
            MockFactory(),
            GetUserProfileService());
    }

    protected PodcastPlaybackService GetPodcastPlaybackService()
    {
        return new PodcastPlaybackService(Logger, CacheManager, MockFactory());
    }

    protected UserDeviceProfileService GetUserDeviceProfileService()
    {
        return new UserDeviceProfileService(Logger, CacheManager, MockFactory());
    }

    protected DeviceIdentificationService GetDeviceIdentificationService()
    {
        return new DeviceIdentificationService(Logger, CacheManager, MockFactory());
    }

    protected IScriptOrchestrationService MockScriptOrchestrationService()
    {
        var mock = new Mock<IScriptOrchestrationService>();
        mock.Setup(x => x.EvaluateScriptForEventAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ScriptEvaluationResult { Result = true, IsDefault = true });
        return mock.Object;
    }

    protected IDirectoryContextProvider MockDirectoryContextProvider()
    {
        var mock = new Mock<IDirectoryContextProvider>();
        mock.Setup(x => x.BuildContextAsync(It.IsAny<FileSystemDirectoryInfo>(), It.IsAny<ISongPlugin[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectoryProcessingContext());
        return mock.Object;
    }

    protected DenyActionHandlerFactory MockDenyActionHandlerFactory()
    {
        var mockSafeDeleteService = new Mock<ISafeDeleteService>();
        mockSafeDeleteService.Setup(x => x.DeleteDirectoryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        return new DenyActionHandlerFactory(
            mockSafeDeleteService.Object,
            new SettingService(),
            Logger);
    }

    protected SettingService GetSettingService()
    {
        return new SettingService();
    }
}
