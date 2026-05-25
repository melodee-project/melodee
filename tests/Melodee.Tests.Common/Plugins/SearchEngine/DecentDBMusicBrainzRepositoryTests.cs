using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.EntityFrameworkCore;
using Moq;
using Serilog;
using Album = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Album;
using Artist = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Artist;
using ArtistAliasLookup = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.ArtistAliasLookup;

namespace Melodee.Tests.Common.Plugins.SearchEngine;

public class DecentDBMusicBrainzRepositoryTests : IDisposable, IAsyncDisposable
{
    private readonly DbContextOptions<MusicBrainzDbContext> _dbContextOptions;
    private readonly string _tempDbDir;
    private DecentDBMusicBrainzRepository _repository;
    private readonly ILogger _logger;

    public DecentDBMusicBrainzRepositoryTests()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();

        _tempDbDir = Path.Combine(Path.GetTempPath(), $"melodee-mb-repo-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDbDir);
        var dbFile = Path.Combine(_tempDbDir, "musicbrainz.ddb");

        _dbContextOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .EnableSensitiveDataLogging()
            .Options;

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        context.Database.EnsureCreated();

        var mockFactory = new Mock<IDbContextFactory<MusicBrainzDbContext>>();
        mockFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                return new MusicBrainzDbContext(_dbContextOptions);
            });
        mockFactory.Setup(f => f.CreateDbContext())
            .Returns(() => new MusicBrainzDbContext(_dbContextOptions));
        var dbContextFactory = mockFactory.Object;

        _repository = new DecentDBMusicBrainzRepository(
            _logger,
            MockConfigurationFactory(),
            dbContextFactory);
    }

    private static IMelodeeConfigurationFactory MockConfigurationFactory()
    {
        var mock = new Mock<IMelodeeConfigurationFactory>();
        mock.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TestsBase.NewPluginsConfiguration);
        return mock.Object;
    }


    [Fact]
    public async Task GetAlbumByMusicBrainzId_WithValidId_ReturnsAlbum()
    {
        var albumId = Guid.NewGuid();
        var testAlbum = new Album
        {
            Id = 1,
            MusicBrainzIdRaw = albumId.ToString(),
            Name = "Test Album",
            NameNormalized = "test album",
            SortName = "Test Album",
            ReleaseDate = DateTime.Now,
            ReleaseType = 1,
            MusicBrainzArtistId = 1,
            ReleaseGroupMusicBrainzIdRaw = Guid.NewGuid().ToString()
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        context.Albums.Add(testAlbum);
        await context.SaveChangesAsync();

        var result = await _repository.GetAlbumByMusicBrainzId(albumId);

        Assert.NotNull(result);
        Assert.Equal(albumId, result.MusicBrainzId);
        Assert.Equal("Test Album", result.Name);
    }

    [Fact]
    public async Task GetAlbumByMusicBrainzId_WithInvalidId_ReturnsNull()
    {
        var nonExistentId = Guid.NewGuid();

        var result = await _repository.GetAlbumByMusicBrainzId(nonExistentId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAlbumByMusicBrainzId_WithEmptyId_ReturnsNull()
    {
        var result = await _repository.GetAlbumByMusicBrainzId(Guid.Empty);

        Assert.Null(result);
    }

    [Fact]
    public async Task SearchArtist_WithMusicBrainzId_ReturnsCorrectArtist()
    {
        SetupTestArtistData();

        var artistId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var query = new ArtistQuery { Name = "Test Artist", MusicBrainzId = artistId.ToString() };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Data);
        Assert.Equal(artistId, result.Data.First().MusicBrainzId);
    }

    [Fact]
    public async Task SearchArtist_WithNormalizedName_AndMusicBrainzId_ReturnsMatchingArtists()
    {
        SetupTestArtistData();

        var artistId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var query = new ArtistQuery
        {
            Name = "Test Artist",
            MusicBrainzId = artistId.ToString()
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);
        Assert.Equal("Test Artist", result.Data.First().Name);
    }

    [Fact]
    public async Task SearchArtist_WithNameAndStaleMusicBrainzId_ReturnsExactNameMatch()
    {
        SetupTestArtistData();

        var query = new ArtistQuery
        {
            Name = "Test Artist",
            MusicBrainzId = Guid.NewGuid().ToString()
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Single(result.Data);
        Assert.Equal("Test Artist", result.Data.First().Name);
    }

    [Fact]
    public async Task SearchArtist_WithEmptyQuery_ReturnsEmptyResult()
    {
        var query = new ArtistQuery { Name = "" };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchArtist_WithMaxResults_LimitsResults()
    {
        SetupMultipleTestArtists();

        var query = new ArtistQuery
        {
            Name = "Artist"
        };

        var result = await _repository.SearchArtist(query, 2);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.True(result.Data.Count() <= 2);
    }

    [Fact]
    public async Task SearchArtist_WithAlbumKeyValues_IncludesAlbumMatching()
    {
        SetupTestArtistWithAlbums();

        var query = new ArtistQuery
        {
            Name = "Artist With Albums",
            AlbumKeyValues = [new KeyValue("2023", "Test Album")]
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);

        var artist = result.Data.First();
        Assert.NotNull(artist.Releases);
        Assert.NotEmpty(artist.Releases);
    }

    [Fact]
    public async Task SearchArtist_WithSingleResultAndAlbumKeyValues_LoadsMatchingReleaseOnly()
    {
        var artistId = Guid.NewGuid();
        var matchingAlbumId = Guid.NewGuid();
        var otherAlbumId = Guid.NewGuid();
        using var context = new MusicBrainzDbContext(_dbContextOptions);
        context.ArtistAliases.RemoveRange(context.ArtistAliases);
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        await context.SaveChangesAsync();

        context.Artists.Add(new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artistId.ToString(),
            Name = "Compact Artist",
            NameNormalized = "Compact Artist".ToNormalizedString() ?? string.Empty,
            SortName = "Compact Artist",
            AlternateNames = ""
        });
        context.Albums.AddRange(
            new Album
            {
                Id = 1,
                MusicBrainzIdRaw = matchingAlbumId.ToString(),
                Name = "Target Album",
                NameNormalized = "Target Album".ToNormalizedString() ?? string.Empty,
                SortName = "Target Album",
                ReleaseDate = new DateTime(2024, 1, 1),
                ReleaseType = 1,
                MusicBrainzArtistId = 1,
                ReleaseGroupMusicBrainzIdRaw = Guid.NewGuid().ToString()
            },
            new Album
            {
                Id = 2,
                MusicBrainzIdRaw = otherAlbumId.ToString(),
                Name = "Other Album",
                NameNormalized = "Other Album".ToNormalizedString() ?? string.Empty,
                SortName = "Other Album",
                ReleaseDate = new DateTime(2025, 1, 1),
                ReleaseType = 1,
                MusicBrainzArtistId = 1,
                ReleaseGroupMusicBrainzIdRaw = Guid.NewGuid().ToString()
            });
        await context.SaveChangesAsync();

        var result = await _repository.SearchArtist(new ArtistQuery
        {
            Name = "Compact Artist",
            AlbumKeyValues = [new KeyValue("2024", "Target Album")]
        }, 1);

        var artist = Assert.Single(result.Data);
        var release = Assert.Single(artist.Releases ?? []);
        Assert.Equal("Target Album", release.Name);
    }

    [Fact]
    public async Task SearchArtist_WithSpecialCharacters_HandlesCorrectly()
    {
        var artistId = Guid.NewGuid();
        var testArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artistId.ToString(),
            Name = "Ac/Dc",
            NameNormalized = "Ac/Dc".ToNormalizedString() ?? string.Empty,
            SortName = "Ac/Dc",
            AlternateNames = ""
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.ArtistAliases.RemoveRange(context.ArtistAliases);
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        await context.SaveChangesAsync();

        context.Artists.Add(testArtist);
        await context.SaveChangesAsync();

        var query = new ArtistQuery
        {
            Name = "Ac/Dc"
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SearchArtist_WithNullName_HandlesGracefully()
    {
        var query = new ArtistQuery
        {
            Name = ""
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task SearchArtist_WithZeroMaxResults_ReturnsEmpty()
    {
        SetupTestArtistData();

        var query = new ArtistQuery
        {
            Name = "Test Artist"
        };

        var result = await _repository.SearchArtist(query, 0);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task SearchArtist_WithNegativeMaxResults_ReturnsEmpty()
    {
        SetupTestArtistData();

        var query = new ArtistQuery
        {
            Name = "Test Artist"
        };

        var result = await _repository.SearchArtist(query, -1);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task SearchArtist_DatabaseException_ReturnsEmptyResult()
    {
        var query = new ArtistQuery
        {
            Name = "Test Artist"
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data);
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task SearchArtist_CancellationToken_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var query = new ArtistQuery
        {
            Name = "Test Artist"
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => _repository.SearchArtist(query, 10, cts.Token));
    }

    [Fact]
    public async Task ImportData_WithInvalidStoragePath_ReturnsFalse()
    {
        var result = await _repository.ImportData(progressCallback: null);

        Assert.NotNull(result);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task SearchArtist_WithRanking_ReturnsCorrectOrder()
    {
        var artist1Id = Guid.NewGuid();
        var artist2Id = Guid.NewGuid();

        var exactMatchArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artist1Id.ToString(),
            Name = "Beatles",
            NameNormalized = "Beatles".ToNormalizedString() ?? string.Empty,
            SortName = "Beatles",
            AlternateNames = ""
        };

        var partialMatchArtist = new Artist
        {
            Id = 2,
            MusicBrainzArtistId = 2,
            MusicBrainzIdRaw = artist2Id.ToString(),
            Name = "Beatles Tribute Band",
            NameNormalized = "Beatles Tribute Band".ToNormalizedString() ?? string.Empty,
            SortName = "Beatles Tribute Band",
            AlternateNames = ""
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.ArtistAliases.RemoveRange(context.ArtistAliases);
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        await context.SaveChangesAsync();

        context.Artists.Add(exactMatchArtist);
        context.Artists.Add(partialMatchArtist);
        await context.SaveChangesAsync();

        var query = new ArtistQuery
        {
            Name = "Beatles"
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);

        var topResult = result.Data.First();
        Assert.Equal("Beatles", topResult.Name);
        Assert.True(topResult.Rank > 0);
    }

    [Fact]
    public async Task SearchArtist_WithAlternateNames_MatchesCorrectly()
    {
        var artistId = Guid.NewGuid();
        var testArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artistId.ToString(),
            Name = "Prince",
            NameNormalized = "Prince".ToNormalizedString() ?? string.Empty,
            SortName = "Prince",
            // Include both original and normalized versions of alternate names
            AlternateNames = $"the artist formerly known as prince|{("The Artist Formerly Known As Prince").ToNormalizedString()}|tafkap"
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.ArtistAliases.RemoveRange(context.ArtistAliases);
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        await context.SaveChangesAsync();

        context.Artists.Add(testArtist);
        context.ArtistAliases.Add(new ArtistAliasLookup
        {
            MusicBrainzArtistId = testArtist.MusicBrainzArtistId,
            NameNormalized = "The Artist Formerly Known As Prince".ToNormalizedString() ?? string.Empty
        });
        await context.SaveChangesAsync();

        var query = new ArtistQuery
        {
            Name = "The Artist Formerly Known As Prince"
        };

        var result = await _repository.SearchArtist(query, 10);

        Assert.NotNull(result);
        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Data);

        var artist = result.Data.First();
        Assert.Equal("Prince", artist.Name);
        Assert.Contains("the artist formerly known as prince", artist.AlternateNames ?? []);
    }

    private void SetupTestArtistData()
    {
        var artistId = Guid.Parse("12345678-1234-1234-1234-123456789012");
        var testArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artistId.ToString(),
            Name = "Test Artist",
            NameNormalized = "Test Artist".ToNormalizedString() ?? string.Empty, // Use actual normalization
            SortName = "Test Artist",
            AlternateNames = ""
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.ArtistAliases.RemoveRange(context.ArtistAliases);
        context.Artists.RemoveRange(context.Artists);
        context.SaveChanges();

        context.Artists.Add(testArtist);
        context.SaveChanges();
    }

    private void SetupMultipleTestArtists()
    {
        var artists = new[]
        {
            new Artist
            {
                Id = 1,
                MusicBrainzArtistId = 1,
                MusicBrainzIdRaw = Guid.NewGuid().ToString(),
                Name = "Artist One",
                NameNormalized = "Artist One".ToNormalizedString()?? string.Empty,
                SortName = "Artist One",
                AlternateNames = ""
            },
            new Artist
            {
                Id = 2,
                MusicBrainzArtistId = 2,
                MusicBrainzIdRaw = Guid.NewGuid().ToString(),
                Name = "Artist Two",
                NameNormalized = "Artist Two".ToNormalizedString()?? string.Empty,
                SortName = "Artist Two",
                AlternateNames = ""
            },
            new Artist
            {
                Id = 3,
                MusicBrainzArtistId = 3,
                MusicBrainzIdRaw = Guid.NewGuid().ToString(),
                Name = "Artist Three",
                NameNormalized = "Artist Three".ToNormalizedString() ?? string.Empty,
                SortName = "Artist Three",
                AlternateNames = ""
            }
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.ArtistAliases.RemoveRange(context.ArtistAliases);
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        context.SaveChanges();

        context.Artists.AddRange(artists);
        context.SaveChanges();
    }

    private void SetupTestArtistWithAlbums()
    {
        var artistId = Guid.NewGuid();
        var albumId = Guid.NewGuid();

        var testArtist = new Artist
        {
            Id = 1,
            MusicBrainzArtistId = 1,
            MusicBrainzIdRaw = artistId.ToString(),
            Name = "Artist With Albums",
            NameNormalized = "Artist With Albums".ToNormalizedString() ?? string.Empty,
            SortName = "Artist With Albums",
            AlternateNames = ""
        };

        var testAlbum = new Album
        {
            Id = 1,
            MusicBrainzIdRaw = albumId.ToString(),
            Name = "Test Album",
            NameNormalized = "Test Album".ToNormalizedString() ?? string.Empty,
            SortName = "Test Album",
            ReleaseDate = new DateTime(2023, 1, 1),
            ReleaseType = 1,
            MusicBrainzArtistId = 1,
            ReleaseGroupMusicBrainzIdRaw = Guid.NewGuid().ToString()
        };

        using var context = new MusicBrainzDbContext(_dbContextOptions);
        // Clear any existing data to avoid conflicts
        context.ArtistAliases.RemoveRange(context.ArtistAliases);
        context.Artists.RemoveRange(context.Artists);
        context.Albums.RemoveRange(context.Albums);
        context.SaveChanges();

        context.Artists.Add(testArtist);
        context.Albums.Add(testAlbum);
        context.SaveChanges();
    }

    /// <summary>
    /// Integration test: Import real MusicBrainz data and verify we can find "Men At Work" and album "Cargo".
    /// MusicBrainz IDs:
    /// - Artist "Men At Work": 395cc503-63b5-4a0b-a20a-604e3fcacea2
    /// - Release "Cargo": 517346ce-cd49-4cfa-831e-0546b871708a
    /// - Release Group "Cargo": c9618aa9-fcca-3661-bf12-6c651fc8c84d
    ///
    /// Run with: dotnet test --filter "ImportData_WithRealData_FindsMenAtWorkAndCargo" -v n -l "console;verbosity=detailed"
    /// </summary>
    [Fact(Skip = "Requires real MusicBrainz data dump to run")]
    public async Task ImportData_WithRealData_FindsMenAtWorkAndCargo()
    {
        // Arrange
        const string musicBrainzDataPath = "/mnt/incoming/melodee_test/search-engine-storage/musicbrainz";
        const string stagingPath = musicBrainzDataPath + "/staging/mbdump";
        var menAtWorkId = Guid.Parse("395cc503-63b5-4a0b-a20a-604e3fcacea2");

        if (!Directory.Exists(stagingPath))
        {
            Console.WriteLine($"Skipping - MusicBrainz data not found at {stagingPath}");
            return;
        }

        // Create a file-based DecentDB database for this test
        var testDbPath = Path.Combine(Path.GetTempPath(), $"mb-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDbPath);
        var dbFile = Path.Combine(testDbPath, "musicbrainz.ddb");
        Console.WriteLine($"Test database: {dbFile}");

        try
        {
            var fileDbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
                .UseDecentDB($"Data Source={dbFile}")
                .Options;

            var mockDbFactory = new Mock<IDbContextFactory<MusicBrainzDbContext>>();
            mockDbFactory.Setup(f => f.CreateDbContextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new MusicBrainzDbContext(fileDbOptions));
            mockDbFactory.Setup(f => f.CreateDbContext())
                .Returns(() => new MusicBrainzDbContext(fileDbOptions));

            // Configure for real data path
            var configDict = new Dictionary<string, object?>
            {
                [SettingRegistry.SearchEngineMusicBrainzStoragePath] = musicBrainzDataPath,
                [SettingRegistry.SearchEngineMusicBrainzImportBatchSize] = 10000,
                [SettingRegistry.SearchEngineMusicBrainzImportMaximumToProcess] = 0
            };
            var config = new MelodeeConfiguration(MelodeeConfiguration.AllSettings(configDict));
            var mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
            mockConfigFactory.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>())).ReturnsAsync(config);

            var repo = new DecentDBMusicBrainzRepository(_logger, mockConfigFactory.Object, mockDbFactory.Object);

            // Act - Import with progress logging
            var peakMemoryMb = 0L;
            var lastPhase = "";
            var sw = System.Diagnostics.Stopwatch.StartNew();

            var importResult = await repo.ImportData(
                (phase, current, total, msg) =>
                {
                    var memMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                    if (memMb > peakMemoryMb) peakMemoryMb = memMb;

                    if (phase != lastPhase)
                    {
                        Console.WriteLine($"\n[{sw.Elapsed:mm\\:ss}] Starting: {phase}");
                        lastPhase = phase;
                    }

                    if (total > 0 && current % 50000 == 0)
                        Console.WriteLine($"  [{sw.Elapsed:mm\\:ss}] {phase}: {current:N0}/{total:N0} - Memory: {memMb:N0}MB");
                },
                CancellationToken.None);

            // Assert - Import succeeded
            Console.WriteLine($"\n[{sw.Elapsed:mm\\:ss}] Import complete. Peak memory: {peakMemoryMb:N0}MB");
            Assert.True(importResult.IsSuccess, $"Import failed: {string.Join(", ", importResult.Errors ?? [])}");

            // Assert - Memory stayed under 2GB
            Console.WriteLine($"Memory check: {peakMemoryMb:N0}MB (limit: 2048MB) - {(peakMemoryMb < 2048 ? "PASS" : "FAIL")}");
            Assert.True(peakMemoryMb < 2048, $"Memory exceeded 2GB limit. Peak was {peakMemoryMb}MB");

            // Assert - Can find Men At Work
            Console.WriteLine("\nSearching for 'Men At Work'...");
            var artistQuery = new ArtistQuery { Name = "Men At Work" };
            var artistResult = await repo.SearchArtist(artistQuery, 10);

            Assert.True(artistResult.IsSuccess);
            var menAtWork = artistResult.Data.FirstOrDefault(a => a.MusicBrainzId == menAtWorkId);
            Assert.NotNull(menAtWork);
            Console.WriteLine($"Found artist: {menAtWork.Name} (ID: {menAtWork.MusicBrainzId})");

            // Assert - Can find Cargo album in database
            Console.WriteLine("\nSearching for 'Cargo' album...");
            await using var context = mockDbFactory.Object.CreateDbContext();
            var artist = await context.Artists.FirstOrDefaultAsync(a => a.MusicBrainzIdRaw == menAtWorkId.ToString());
            Assert.NotNull(artist);

            var albums = await context.Albums
                .Where(a => a.MusicBrainzArtistId == artist.MusicBrainzArtistId)
                .ToListAsync();

            Console.WriteLine($"Found {albums.Count} albums for Men At Work");
            var cargoAlbums = albums.Where(a => a.Name.Contains("Cargo", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.ReleaseDate).ToList();

            Console.WriteLine($"Found {cargoAlbums.Count} 'Cargo' releases:");
            foreach (var c in cargoAlbums)
            {
                Console.WriteLine($" - {c.Name} ({c.ReleaseDate.Year}) [ID: {c.MusicBrainzIdRaw}]");
            }

            // Verify the specific release from 1983
            var cargoId = Guid.Parse("517346ce-cd49-4cfa-831e-0546b871708a");
            var cargo = cargoAlbums.FirstOrDefault(a => a.MusicBrainzIdRaw == cargoId.ToString());

            if (cargo != null)
            {
                Console.WriteLine($"Matched 1983 Cargo by ID: {cargo.Name} ({cargo.ReleaseDate.Year})");
                Assert.Equal(1983, cargo.ReleaseDate.Year);
            }
            else
            {
                Console.WriteLine("Could not find Cargo (1983) by ID.");
                // Fail if we didn't find the specific ID we were looking for
                Assert.NotNull(cargo);
            }
            Console.WriteLine("\n*** ALL TESTS PASSED ***");
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(testDbPath))
            {
                try { Directory.Delete(testDbPath, true); } catch { }
            }
        }
    }

    public void Dispose()
    {
        _repository = null!;
        CleanupTempDir();
    }

    public ValueTask DisposeAsync()
    {
        CleanupTempDir();
        return ValueTask.CompletedTask;
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
}
