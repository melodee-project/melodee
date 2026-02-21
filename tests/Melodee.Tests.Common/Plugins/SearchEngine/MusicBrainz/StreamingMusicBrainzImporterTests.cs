using System.Diagnostics;
using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;

namespace Melodee.Tests.Common.Plugins.SearchEngine.MusicBrainz;

/// <summary>
/// Unit and performance tests for DecentDBStreamingMusicBrainzImporter using synthetic test data.
/// </summary>
public class StreamingMusicBrainzImporterTests : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _testDataPath;
    private readonly string _testDbPath;

    public StreamingMusicBrainzImporterTests()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();

        _testDataPath = Path.Combine(Path.GetTempPath(), $"mb-test-data-{Guid.NewGuid():N}");
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mb-test-db-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_testDataPath);
        Directory.CreateDirectory(_testDbPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDataPath))
            {
                Directory.Delete(_testDataPath, true);
            }
            if (Directory.Exists(_testDbPath))
            {
                Directory.Delete(_testDbPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void GenerateTestData_CreatesAllRequiredFiles()
    {
        var mbDumpPath = Path.Combine(_testDataPath, "mbdump");

        var stats = MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, artistCount: 100);

        stats.ArtistCount.Should().Be(100);
        File.Exists(Path.Combine(mbDumpPath, "artist")).Should().BeTrue();
        File.Exists(Path.Combine(mbDumpPath, "artist_alias")).Should().BeTrue();
        File.Exists(Path.Combine(mbDumpPath, "artist_credit")).Should().BeTrue();
        File.Exists(Path.Combine(mbDumpPath, "artist_credit_name")).Should().BeTrue();
        File.Exists(Path.Combine(mbDumpPath, "release_group")).Should().BeTrue();
        File.Exists(Path.Combine(mbDumpPath, "release_group_meta")).Should().BeTrue();
        File.Exists(Path.Combine(mbDumpPath, "release")).Should().BeTrue();
        File.Exists(Path.Combine(mbDumpPath, "release_country")).Should().BeTrue();
        File.Exists(Path.Combine(mbDumpPath, "link")).Should().BeTrue();
        File.Exists(Path.Combine(mbDumpPath, "l_artist_artist")).Should().BeTrue();
    }

    [Fact]
    public void GenerateTestData_IsDeterministic_WithSameSeed()
    {
        var path1 = Path.Combine(_testDataPath, "run1", "mbdump");
        var path2 = Path.Combine(_testDataPath, "run2", "mbdump");

        var stats1 = MusicBrainzTestDataGenerator.GenerateTestData(path1, artistCount: 50, seed: 12345);
        var stats2 = MusicBrainzTestDataGenerator.GenerateTestData(path2, artistCount: 50, seed: 12345);

        stats1.ArtistCount.Should().Be(stats2.ArtistCount);
        stats1.ReleaseCount.Should().Be(stats2.ReleaseCount);

        var artists1 = File.ReadAllText(Path.Combine(path1, "artist"));
        var artists2 = File.ReadAllText(Path.Combine(path2, "artist"));
        artists1.Should().Be(artists2);
    }

    [Fact]
    public async Task ImportAsync_WithSmallTestData_ImportsSuccessfully()
    {
        var mbDumpPath = Path.Combine(_testDataPath, "staging", "mbdump");
        var dbFile = Path.Combine(_testDbPath, "musicbrainz.ddb");

        var stats = MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, artistCount: 100, albumsPerArtist: 3);

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);
        var progressMessages = new List<string>();

        await importer.ImportAsync(
            context,
            _testDataPath,
            (phase, current, total, msg) => progressMessages.Add($"{phase}: {msg}"),
            CancellationToken.None);

        var artistCount = await context.Artists.CountAsync();
        var albumCount = await context.Albums.CountAsync();

        artistCount.Should().Be(stats.ArtistCount);
        albumCount.Should().BeGreaterThan(0);
        progressMessages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ImportAsync_WithMediumTestData_CompletesInReasonableTime()
    {
        var mbDumpPath = Path.Combine(_testDataPath, "staging", "mbdump");
        var dbFile = Path.Combine(_testDbPath, "musicbrainz.ddb");

        var stats = MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, artistCount: 1000, albumsPerArtist: 5);

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);
        var sw = Stopwatch.StartNew();

        await importer.ImportAsync(
            context,
            _testDataPath,
            null,
            CancellationToken.None);

        sw.Stop();

        var artistCount = await context.Artists.CountAsync();
        var albumCount = await context.Albums.CountAsync();

        artistCount.Should().Be(stats.ArtistCount);
        albumCount.Should().BeGreaterThan(0);

        // Should complete in under 30 seconds for 1000 artists
        sw.Elapsed.TotalSeconds.Should().BeLessThan(30,
            $"Import took {sw.Elapsed.TotalSeconds:F1}s which exceeds 30s threshold");
    }

    [Fact]
    public async Task ImportAsync_CancellationToken_StopsImport()
    {
        var mbDumpPath = Path.Combine(_testDataPath, "staging", "mbdump");
        var dbFile = Path.Combine(_testDbPath, "musicbrainz.ddb");

        MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, artistCount: 500);

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);
        using var cts = new CancellationTokenSource();

        var importTask = importer.ImportAsync(
            context,
            _testDataPath,
            (phase, current, total, msg) =>
            {
                if (current > 100)
                {
                    cts.Cancel();
                }
            },
            cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importTask);
    }

    [Fact]
    public async Task ImportAsync_WithMissingFiles_HandlesGracefully()
    {
        var emptyPath = Path.Combine(_testDataPath, "empty", "mbdump");
        Directory.CreateDirectory(emptyPath);
        var dbFile = Path.Combine(_testDbPath, "musicbrainz.ddb");

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);

        await importer.ImportAsync(
            context,
            Path.Combine(_testDataPath, "empty"),
            null,
            CancellationToken.None);

        var artistCount = await context.Artists.CountAsync();
        artistCount.Should().Be(0);
    }

    [Theory]
    [InlineData(100, 3)]
    [InlineData(500, 5)]
    [InlineData(1000, 5)]
    public async Task ImportAsync_ScalesLinearly_WithDataSize(int artistCount, int albumsPerArtist)
    {
        var storagePath = Path.Combine(_testDataPath, $"storage-{artistCount}");
        var mbDumpPath = Path.Combine(storagePath, "staging", "mbdump");
        var dbFile = Path.Combine(_testDbPath, $"musicbrainz-{artistCount}.ddb");

        var stats = MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, artistCount, albumsPerArtist);

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);
        var sw = Stopwatch.StartNew();

        await importer.ImportAsync(
            context,
            storagePath,
            null,
            CancellationToken.None);

        sw.Stop();

        var importedArtists = await context.Artists.CountAsync();
        var importedAlbums = await context.Albums.CountAsync();

        importedArtists.Should().Be(artistCount);
        importedAlbums.Should().BeGreaterThan(0);

        var totalRecords = stats.ArtistCount + stats.AliasCount + stats.ReleaseCount;
        var recordsPerSecond = totalRecords / sw.Elapsed.TotalSeconds;

        Console.WriteLine($"[{artistCount} artists] Time: {sw.Elapsed.TotalSeconds:F2}s, " +
                          $"Records: {totalRecords:N0}, Rate: {recordsPerSecond:N0}/sec");

        recordsPerSecond.Should().BeGreaterThan(400,
            $"Performance below threshold: {recordsPerSecond:N0} records/sec");
    }

    [Fact]
    public async Task FullImportWorkflow_WithRepository_WorksEndToEnd()
    {
        var storagePath = _testDataPath;
        var mbDumpPath = Path.Combine(storagePath, "staging", "mbdump");
        var dbFile = Path.Combine(storagePath, "musicbrainz.ddb");

        var stats = MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, artistCount: 200, albumsPerArtist: 4);

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        var mockDbFactory = new Mock<IDbContextFactory<MusicBrainzDbContext>>();
        mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MusicBrainzDbContext(dbOptions));
        mockDbFactory.Setup(f => f.CreateDbContext())
            .Returns(() => new MusicBrainzDbContext(dbOptions));

        var configDict = new Dictionary<string, object?>
        {
            [SettingRegistry.SearchEngineMusicBrainzStoragePath] = storagePath,
            [SettingRegistry.SearchEngineMusicBrainzImportBatchSize] = 5000,
            [SettingRegistry.SearchEngineMusicBrainzImportMaximumToProcess] = 0
        };
        var config = new MelodeeConfiguration(MelodeeConfiguration.AllSettings(configDict));
        var mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
        mockConfigFactory.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(config);

        var repo = new DecentDBMusicBrainzRepository(_logger, mockConfigFactory.Object, mockDbFactory.Object);

        var result = await repo.ImportData(
            (phase, current, total, msg) => Console.WriteLine($"{phase}: {current}/{total} - {msg}"),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data.Should().BeTrue();

        await using var context = mockDbFactory.Object.CreateDbContext();
        var artistCount = await context.Artists.CountAsync();
        var albumCount = await context.Albums.CountAsync();

        artistCount.Should().Be(stats.ArtistCount);
        albumCount.Should().BeGreaterThan(0);
    }
}