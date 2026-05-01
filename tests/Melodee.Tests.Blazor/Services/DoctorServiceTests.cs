using Melodee.Common.Data;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using Quartz;

namespace Melodee.Tests.Blazor.Services;

/// <summary>
/// Tests for DoctorService.
/// Verifies health check and diagnostic functionality.
/// </summary>
public class DoctorServiceTests
{
    private readonly Mock<IDbContextFactory<MelodeeDbContext>> _dbContextFactory;
    private readonly Mock<IDbContextFactory<MusicBrainzDbContext>> _musicBrainzDbContextFactory;
    private readonly Mock<IDbContextFactory<ArtistSearchEngineServiceDbContext>> _artistSearchEngineDbContextFactory;
    private readonly Mock<LibraryService> _libraryService;
    private readonly Mock<IMelodeeConfigurationFactory> _configurationFactory;
    private readonly Mock<IWebHostEnvironment> _webHostEnvironment;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessor;
    private readonly Mock<ISchedulerFactory> _schedulerFactory;

    public DoctorServiceTests()
    {
        _dbContextFactory = new Mock<IDbContextFactory<MelodeeDbContext>>();
        _musicBrainzDbContextFactory = new Mock<IDbContextFactory<MusicBrainzDbContext>>();
        _artistSearchEngineDbContextFactory = new Mock<IDbContextFactory<ArtistSearchEngineServiceDbContext>>();
        _libraryService = new Mock<LibraryService>();
        _configurationFactory = new Mock<IMelodeeConfigurationFactory>();
        _webHostEnvironment = new Mock<IWebHostEnvironment>();
        _webHostEnvironment.Setup(x => x.EnvironmentName).Returns("Test");
        _webHostEnvironment.Setup(x => x.ContentRootPath).Returns("/test/path");
        _httpContextAccessor = new Mock<IHttpContextAccessor>();
        _schedulerFactory = new Mock<ISchedulerFactory>();

        var mockScheduler = new Mock<IScheduler>();
        mockScheduler.Setup(x => x.IsStarted).Returns(true);
        mockScheduler.Setup(x => x.IsShutdown).Returns(false);
        mockScheduler.Setup(x => x.InStandbyMode).Returns(false);
        mockScheduler.Setup(x => x.GetJobGroupNames(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        mockScheduler.Setup(x => x.GetMetaData(It.IsAny<CancellationToken>())).ReturnsAsync(new SchedulerMetaData(
            "TestScheduler", "test-instance", typeof(IScheduler), false, true, false, false,
            DateTimeOffset.UtcNow, 0, typeof(object), false, false, typeof(object), 10, "1.0"));
        _schedulerFactory.Setup(x => x.GetScheduler(It.IsAny<CancellationToken>())).ReturnsAsync(mockScheduler.Object);
    }

    [Fact]
    public async Task NeedsAttentionAsync_MissingConnectionStrings_ReturnsTrue()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>());
        var service = CreateService(configuration);

        var result = await service.NeedsAttentionAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task NeedsAttentionAsync_HealthyDecentDbConnections_ReturnsFalse()
    {
        ConfigurePrimaryDatabaseCanConnect();

        var musicBrainzPath = Path.Combine(Path.GetTempPath(), $"musicbrainz-fast-path-{Guid.NewGuid():N}.ddb");
        var artistSearchPath = Path.Combine(Path.GetTempPath(), $"artist-search-fast-path-{Guid.NewGuid():N}.ddb");

        try
        {
            var musicBrainzOptions = CreateMusicBrainzOptions(musicBrainzPath);
            await using (var musicBrainzContext = new MusicBrainzDbContext(musicBrainzOptions))
            {
                await musicBrainzContext.Database.EnsureCreatedAsync();
            }
            ConfigureMusicBrainzDatabase(musicBrainzOptions);

            var artistSearchOptions = CreateArtistSearchOptions(artistSearchPath);
            await using (var artistSearchContext = new ArtistSearchEngineServiceDbContext(artistSearchOptions))
            {
                await artistSearchContext.Database.EnsureCreatedAsync();
            }
            ConfigureArtistSearchDatabase(artistSearchOptions);

            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test",
                ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={musicBrainzPath}",
                ["ConnectionStrings:ArtistSearchEngineConnection"] = $"Data Source={artistSearchPath}"
            });
            var service = CreateService(configuration);

            var result = await service.NeedsAttentionAsync();

            Assert.False(result);
            _musicBrainzDbContextFactory.Verify(
                x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()),
                Times.Once);
            _artistSearchEngineDbContextFactory.Verify(
                x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()),
                Times.Once);
        }
        finally
        {
            DeleteDatabaseArtifacts(musicBrainzPath);
            DeleteDatabaseArtifacts(artistSearchPath);
        }
    }

    [Fact]
    public async Task NeedsAttentionAsync_WhenArtistSearchDatabaseCannotBeOpened_ReturnsTrue()
    {
        ConfigurePrimaryDatabaseCanConnect();

        var musicBrainzPath = Path.Combine(Path.GetTempPath(), $"musicbrainz-openable-{Guid.NewGuid():N}.ddb");
        var artistSearchPath = CreateNonEmptyFile("artist-search-unsupported");

        try
        {
            var musicBrainzOptions = CreateMusicBrainzOptions(musicBrainzPath);
            await using (var musicBrainzContext = new MusicBrainzDbContext(musicBrainzOptions))
            {
                await musicBrainzContext.Database.EnsureCreatedAsync();
            }
            ConfigureMusicBrainzDatabase(musicBrainzOptions);

            _artistSearchEngineDbContextFactory
                .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("unsupported database format version: 3"));

            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test",
                ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={musicBrainzPath}",
                ["ConnectionStrings:ArtistSearchEngineConnection"] = $"Data Source={artistSearchPath}"
            });
            var service = CreateService(configuration);

            var result = await service.NeedsAttentionAsync();

            Assert.True(result);
        }
        finally
        {
            DeleteDatabaseArtifacts(musicBrainzPath);
            DeleteDatabaseArtifacts(artistSearchPath);
        }
    }

    [Fact]
    public async Task IsMusicBrainzDatabaseEmptyAsync_NoConnectionString_ReturnsTrue()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>());
        var service = CreateService(configuration);

        var result = await service.IsMusicBrainzDatabaseEmptyAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task IsMusicBrainzDatabaseEmptyAsync_EmptyConnectionString_ReturnsTrue()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MusicBrainzConnection"] = ""
        });
        var service = CreateService(configuration);

        var result = await service.IsMusicBrainzDatabaseEmptyAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task IsMusicBrainzDatabaseEmptyAsync_FileDoesNotExist_ReturnsTrue()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid()}.db");
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={tempPath}"
        });
        var service = CreateService(configuration);

        var result = await service.IsMusicBrainzDatabaseEmptyAsync();

        Assert.True(result);
    }

    [Fact]
    public async Task IsMusicBrainzDatabaseEmptyAsync_EmptyFile_ReturnsTrue()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"empty-{Guid.NewGuid()}.db");
        try
        {
            File.WriteAllText(tempPath, "");
            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={tempPath}"
            });
            var service = CreateService(configuration);

            var result = await service.IsMusicBrainzDatabaseEmptyAsync();

            Assert.True(result);
        }
        finally
        {
            DeleteFileIfExists(tempPath);
        }
    }

    [Fact]
    public async Task IsMusicBrainzDatabaseEmptyAsync_InitializedDatabaseWithoutArtists_ReturnsTrue()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"musicbrainz-empty-{Guid.NewGuid():N}.ddb");

        try
        {
            var options = CreateMusicBrainzOptions(tempPath);
            await using (var context = new MusicBrainzDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
            }

            ConfigureMusicBrainzDatabase(options);

            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={tempPath}"
            });
            var service = CreateService(configuration);

            var result = await service.IsMusicBrainzDatabaseEmptyAsync();

            Assert.True(result);
        }
        finally
        {
            DeleteFileIfExists(tempPath);
        }
    }

    [Fact]
    public async Task IsMusicBrainzDatabaseEmptyAsync_DatabaseWithArtists_ReturnsFalse()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"musicbrainz-data-{Guid.NewGuid():N}.ddb");

        try
        {
            var options = CreateMusicBrainzOptions(tempPath);
            await using (var context = new MusicBrainzDbContext(options))
            {
                await context.Database.EnsureCreatedAsync();
                await context.Artists.AddAsync(new Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Artist
                {
                    MusicBrainzArtistId = 1,
                    Name = "Test Artist",
                    SortName = "Test Artist",
                    NameNormalized = "test artist",
                    MusicBrainzIdRaw = Guid.NewGuid().ToString()
                });
                await context.SaveChangesAsync();
            }

            ConfigureMusicBrainzDatabase(options);

            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={tempPath}"
            });
            var service = CreateService(configuration);

            var result = await service.IsMusicBrainzDatabaseEmptyAsync();

            Assert.False(result);
        }
        finally
        {
            DeleteFileIfExists(tempPath);
        }
    }

    [Fact]
    public async Task RunAllChecksAsync_ReturnsCheckResults()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        Assert.NotNull(results);
        Assert.NotNull(results.Checks);
        Assert.True(results.Checks.Count > 0);
    }

    [Fact]
    public async Task RunAllChecksAsync_IncludesConfigurationCheck()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        var configCheck = results.Checks.FirstOrDefault(c => c.Name == "Configuration");
        Assert.NotNull(configCheck);
    }

    [Fact]
    public async Task RunAllChecksAsync_MissingConfig_HasIssues()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>());
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        Assert.True(results.HasIssues);
    }

    [Fact]
    public async Task RunAllChecksAsync_HasIssues_WhenChecksFail()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>());
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        Assert.True(results.HasIssues);
    }

    [Fact]
    public async Task RunAllChecksAsync_CheckResultsHaveDuration()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        foreach (var check in results.Checks)
        {
            Assert.True(check.Duration >= TimeSpan.Zero, $"Check '{check.Name}' should have non-negative duration");
        }
    }

    [Fact]
    public async Task RunAllChecksAsync_ReturnsConnectionStringInfo()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        Assert.NotNull(results.ConnectionStrings);
        Assert.True(results.ConnectionStrings.Count > 0);
    }

    [Fact]
    public async Task RunAllChecksAsync_WhenDecentDbCannotBeOpened_ConnectionStringInfoReflectsFailure()
    {
        ConfigurePrimaryDatabaseCanConnect();

        var musicBrainzPath = CreateNonEmptyFile("musicbrainz-unsupported");
        var artistSearchPath = CreateNonEmptyFile("artist-search-unsupported");

        try
        {
            _musicBrainzDbContextFactory
                .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("unsupported database format version: 3"));
            _artistSearchEngineDbContextFactory
                .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("unsupported database format version: 3"));

            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test",
                ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={musicBrainzPath}",
                ["ConnectionStrings:ArtistSearchEngineConnection"] = $"Data Source={artistSearchPath}"
            });
            var service = CreateService(configuration);

            var results = await service.RunAllChecksAsync();

            var musicBrainzConnection = results.ConnectionStrings.Single(x => x.Name == "MusicBrainzConnection");
            Assert.Equal(false, musicBrainzConnection.CanConnect);
            Assert.Contains("unsupported database format version: 3", musicBrainzConnection.ConnectionError);

            var artistSearchConnection = results.ConnectionStrings.Single(x => x.Name == "ArtistSearchEngineConnection");
            Assert.Equal(false, artistSearchConnection.CanConnect);
            Assert.Contains("unsupported database format version: 3", artistSearchConnection.ConnectionError);
        }
        finally
        {
            DeleteDatabaseArtifacts(musicBrainzPath);
            DeleteDatabaseArtifacts(artistSearchPath);
        }
    }

    [Fact]
    public async Task RunAllChecksAsync_WhenArtistSearchDatabaseCannotBeQueried_SystemCheckReflectsFailure()
    {
        ConfigurePrimaryDatabaseCanConnect();

        var musicBrainzPath = Path.Combine(Path.GetTempPath(), $"musicbrainz-openable-{Guid.NewGuid():N}.ddb");
        var artistSearchPath = CreateNonEmptyFile("artist-search-unsupported");

        try
        {
            var musicBrainzOptions = CreateMusicBrainzOptions(musicBrainzPath);
            await using (var musicBrainzContext = new MusicBrainzDbContext(musicBrainzOptions))
            {
                await musicBrainzContext.Database.EnsureCreatedAsync();
            }
            ConfigureMusicBrainzDatabase(musicBrainzOptions);

            _artistSearchEngineDbContextFactory
                .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("unsupported database format version: 3"));

            var configuration = CreateConfiguration(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test",
                ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={musicBrainzPath}",
                ["ConnectionStrings:ArtistSearchEngineConnection"] = $"Data Source={artistSearchPath}"
            });
            var service = CreateService(configuration);

            var results = await service.RunAllChecksAsync();

            var artistSearchCheck = results.Checks.Single(x => x.Name == "ArtistSearchEngineDatabase");
            Assert.False(artistSearchCheck.Success);
            Assert.Contains("unsupported database format version: 3", artistSearchCheck.Details);
        }
        finally
        {
            DeleteDatabaseArtifacts(musicBrainzPath);
            DeleteDatabaseArtifacts(artistSearchPath);
        }
    }

    [Fact]
    public async Task RunAllChecksAsync_ReturnsEnvironmentVariableInfo()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        Assert.NotNull(results.EnvironmentVariables);
    }

    [Fact]
    public async Task RunAllChecksAsync_ReturnsDiskSpaceInfo()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        Assert.NotNull(results.DiskSpaceInfo);
    }

    [Fact]
    public async Task RunAllChecksAsync_ReturnsSearchEngineApiKeysInfo()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        Assert.NotNull(results.SearchEngineApiKeys);
    }

    [Fact]
    public async Task RunAllChecksAsync_IncludesDiskSpaceCheck()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        var diskSpaceCheck = results.Checks.FirstOrDefault(c => c.Name == "DiskSpace");
        Assert.NotNull(diskSpaceCheck);
    }

    [Fact]
    public async Task RunAllChecksAsync_IncludesLibraryPathOverlapCheck()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        var overlapCheck = results.Checks.FirstOrDefault(c => c.Name == "LibraryPathOverlap");
        Assert.NotNull(overlapCheck);
    }

    [Fact]
    public async Task RunAllChecksAsync_IncludesSearchEngineApiKeysCheck()
    {
        var configuration = CreateValidConfiguration();
        var service = CreateService(configuration);

        var results = await service.RunAllChecksAsync();

        var apiKeysCheck = results.Checks.FirstOrDefault(c => c.Name == "SearchEngineApiKeys");
        Assert.NotNull(apiKeysCheck);
    }

    private DoctorService CreateService(IConfiguration configuration)
    {
        return new DoctorService(
            configuration,
            _dbContextFactory.Object,
            _musicBrainzDbContextFactory.Object,
            _artistSearchEngineDbContextFactory.Object,
            _libraryService.Object,
            _configurationFactory.Object,
            _webHostEnvironment.Object,
            _httpContextAccessor.Object,
            _schedulerFactory.Object);
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static IConfiguration CreateValidConfiguration()
    {
        return CreateConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test",
            ["ConnectionStrings:MusicBrainzConnection"] = "Data Source=/tmp/test.db",
            ["ConnectionStrings:ArtistSearchEngineConnection"] = "Data Source=/tmp/test2.db"
        });
    }

    private void ConfigurePrimaryDatabaseCanConnect()
    {
        var options = new DbContextOptionsBuilder<MelodeeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using (var context = new MelodeeDbContext(options))
        {
            context.Database.EnsureCreated();
        }

        _dbContextFactory
            .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MelodeeDbContext(options));
    }

    private void ConfigureMusicBrainzDatabase(DbContextOptions<MusicBrainzDbContext> options)
    {
        _musicBrainzDbContextFactory
            .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MusicBrainzDbContext(options));
    }

    private void ConfigureArtistSearchDatabase(DbContextOptions<ArtistSearchEngineServiceDbContext> options)
    {
        _artistSearchEngineDbContextFactory
            .Setup(x => x.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ArtistSearchEngineServiceDbContext(options));
    }

    private static DbContextOptions<MusicBrainzDbContext> CreateMusicBrainzOptions(string path)
    {
        return new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={path}")
            .Options;
    }

    private static DbContextOptions<ArtistSearchEngineServiceDbContext> CreateArtistSearchOptions(string path)
    {
        return new DbContextOptionsBuilder<ArtistSearchEngineServiceDbContext>()
            .UseDecentDB($"Data Source={path}")
            .Options;
    }

    private static string CreateNonEmptyFile(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{prefix}-{Guid.NewGuid():N}.ddb");
        File.WriteAllText(path, "ready");
        return path;
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void DeleteDatabaseArtifacts(string path)
    {
        DeleteFileIfExists(path);
        DeleteFileIfExists($"{path}.wal");
        DeleteFileIfExists($"{path}-wal");
        DeleteFileIfExists($"{path}.shm");
        DeleteFileIfExists($"{path}-shm");
    }
}
