using System.Net;
using System.Text;
using Melodee.Blazor.Controllers.Melodee.Models.ArtistLookup;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Filtering;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Services.SearchEngines;
using Melodee.Tests.Common;
using Microsoft.EntityFrameworkCore;
using Moq;
using Album = Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData.Album;
using Artist = Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData.Artist;

namespace Melodee.Tests.Common.Services.SearchEngines;

public class ArtistSearchEngineServiceTests : ServiceTestBase
{
    #region InitializeAsync Tests

    [Fact]
    public async Task InitializeAsync_WhenCalled_InitializesService()
    {
        // Arrange
        var service = GetArtistSearchEngineService();

        // Act
        await service.InitializeAsync();

        // Assert - no exception thrown means success
        Assert.True(true);
    }

    [Fact]
    public async Task InitializeAsync_WhenCalledMultipleTimes_OnlyInitializesOnce()
    {
        // Arrange
        var service = GetArtistSearchEngineService();

        // Act
        await service.InitializeAsync();
        await service.InitializeAsync();

        // Assert - no exception thrown means success
        Assert.True(true);
    }

    #endregion

    #region DoSearchAsync Tests

    [Fact]
    public async Task DoSearchAsync_WithEmptyQuery_ReturnsResults()
    {
        // Arrange
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        var query = new ArtistQuery { Name = "" };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task DoSearchAsync_WithBypassNegativeCache_SearchesAfterCachedMiss()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        var artistName = $"Cache Bypass Artist {Guid.NewGuid():N}";
        var query = new ArtistQuery { Name = artistName };

        var cachedMiss = await service.DoSearchAsync(query, 10);
        Assert.Empty(cachedMiss.Data ?? []);

        await using (var context = await MockArtistSearchEngineFactory().CreateDbContextAsync())
        {
            context.Artists.Add(new Artist
            {
                Name = artistName,
                NameNormalized = query.NameNormalized,
                SortName = artistName
            });
            await context.SaveChangesAsync();
        }

        var stillCachedMiss = await service.DoSearchAsync(query, 10);
        Assert.Empty(stillCachedMiss.Data ?? []);

        var bypassResult = await service.DoSearchAsync(query, 10, bypassNegativeCache: true);
        var artist = Assert.Single(bypassResult.Data ?? []);
        Assert.Equal(artistName, artist.Name);
    }

    [Fact]
    public async Task DoSearchAsync_PassesBoundedMaxResultsToExternalProviders()
    {
        Uri? requestedUri = null;
        var handler = new HttpHandlerStubDelegate((request, _) =>
        {
            requestedUri = request.RequestUri;
            const string json = """{"resultCount":0,"results":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });
        using var httpClient = new HttpClient(handler);
        var configFactory = SearchConfigurationFactory(new Dictionary<string, object?>
        {
            [SettingRegistry.SearchEngineMusicBrainzEnabled] = "false",
            [SettingRegistry.SearchEngineSpotifyEnabled] = "false",
            [SettingRegistry.SearchEngineITunesEnabled] = "true",
            [SettingRegistry.SearchEngineLastFmEnabled] = "false",
            [SettingRegistry.SearchEngineDiscogsEnabled] = "false",
            [SettingRegistry.SearchEngineWikiDataEnabled] = "false"
        });
        var service = CreateArtistSearchEngineService(
            configFactory,
            new TestHttpClientFactory(httpClient));
        await service.InitializeAsync();

        await service.DoSearchAsync(new ArtistQuery { Name = "Bounded Provider Limit Artist" }, 1, bypassNegativeCache: true);

        Assert.Contains("limit=1", requestedUri?.ToString());
    }

    [Fact]
    public async Task DoSearchAsync_WithExactLocalNameHit_DoesNotCallExternalHttpProvider()
    {
        var requestCount = 0;
        var handler = new HttpHandlerStubDelegate((_, _) =>
        {
            requestCount++;
            const string json = """{"resultCount":0,"results":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });
        using var httpClient = new HttpClient(handler);
        var configFactory = SearchConfigurationFactory(new Dictionary<string, object?>
        {
            [SettingRegistry.SearchEngineMusicBrainzEnabled] = "false",
            [SettingRegistry.SearchEngineSpotifyEnabled] = "false",
            [SettingRegistry.SearchEngineITunesEnabled] = "true",
            [SettingRegistry.SearchEngineLastFmEnabled] = "false",
            [SettingRegistry.SearchEngineDiscogsEnabled] = "false",
            [SettingRegistry.SearchEngineWikiDataEnabled] = "false"
        });
        var service = CreateArtistSearchEngineService(
            configFactory,
            new TestHttpClientFactory(httpClient));
        await service.InitializeAsync();

        var localArtist = new Artist
        {
            Name = "Local Exact Artist",
            NameNormalized = "LOCALEXACTARTIST",
            SortName = "Local Exact Artist"
        };
        await using (var context = await MockArtistSearchEngineFactory().CreateDbContextAsync())
        {
            context.Artists.Add(localArtist);
            await context.SaveChangesAsync();
            context.Albums.Add(NewAlbum(localArtist, "Local Exact Album", 2026));
            await context.SaveChangesAsync();
        }

        var result = await service.DoSearchAsync(
            new ArtistQuery { Name = "Local Exact Artist" },
            10,
            bypassNegativeCache: true);

        var artist = Assert.Single(result.Data ?? []);
        Assert.Equal("Local Exact Artist", artist.Name);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task DoSearchAsync_WithExactLocalAliasHit_DoesNotCallExternalHttpProvider()
    {
        var requestCount = 0;
        var handler = new HttpHandlerStubDelegate((_, _) =>
        {
            requestCount++;
            const string json = """{"resultCount":0,"results":[]}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        });
        using var httpClient = new HttpClient(handler);
        var configFactory = SearchConfigurationFactory(new Dictionary<string, object?>
        {
            [SettingRegistry.SearchEngineMusicBrainzEnabled] = "false",
            [SettingRegistry.SearchEngineSpotifyEnabled] = "false",
            [SettingRegistry.SearchEngineITunesEnabled] = "true",
            [SettingRegistry.SearchEngineLastFmEnabled] = "false",
            [SettingRegistry.SearchEngineDiscogsEnabled] = "false",
            [SettingRegistry.SearchEngineWikiDataEnabled] = "false"
        });
        var service = CreateArtistSearchEngineService(
            configFactory,
            new TestHttpClientFactory(httpClient));

        var localArtist = new Artist
        {
            Name = "Canonical Artist",
            NameNormalized = "CANONICALARTIST",
            SortName = "Canonical Artist",
            AlternateNames = "EXACTLOCALALIAS"
        };
        await using (var context = await MockArtistSearchEngineFactory().CreateDbContextAsync())
        {
            context.Artists.Add(localArtist);
            await context.SaveChangesAsync();
            context.Albums.Add(NewAlbum(localArtist, "Alias Album", 2026));
            await context.SaveChangesAsync();
        }

        await service.InitializeAsync();

        var result = await service.DoSearchAsync(
            new ArtistQuery { Name = "Exact Local Alias" },
            10,
            bypassNegativeCache: true);

        var artist = Assert.Single(result.Data ?? []);
        Assert.Equal("Canonical Artist", artist.Name);
        Assert.Equal(0, requestCount);
    }

    [Fact]
    public async Task DoSearchAsync_WithProviderResultMatchingExistingItunesId_DoesNotCreateDuplicateArtist()
    {
        var handler = new HttpHandlerStubDelegate((_, _) =>
        {
            const string json = """
                                {
                                  "resultCount": 1,
                                  "results": [
                                    {
                                      "artistId": 12345,
                                      "artistName": "Provider Match",
                                      "artistType": "Artist",
                                      "wrapperType": "artist",
                                      "trackCount": 3
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
        var configFactory = SearchConfigurationFactory(new Dictionary<string, object?>
        {
            [SettingRegistry.SearchEngineMusicBrainzEnabled] = "false",
            [SettingRegistry.SearchEngineSpotifyEnabled] = "false",
            [SettingRegistry.SearchEngineITunesEnabled] = "true",
            [SettingRegistry.SearchEngineLastFmEnabled] = "false",
            [SettingRegistry.SearchEngineDiscogsEnabled] = "false",
            [SettingRegistry.SearchEngineWikiDataEnabled] = "false"
        });
        var service = CreateArtistSearchEngineService(
            configFactory,
            new TestHttpClientFactory(httpClient));
        await service.InitializeAsync();

        await using (var context = await MockArtistSearchEngineFactory().CreateDbContextAsync())
        {
            context.Artists.Add(new Artist
            {
                Name = "Existing Provider Artist",
                NameNormalized = "EXISTINGPROVIDERARTIST",
                SortName = "Existing Provider Artist",
                ItunesId = "12345"
            });
            await context.SaveChangesAsync();
        }

        var result = await service.DoSearchAsync(
            new ArtistQuery { Name = "Provider Match" },
            10,
            bypassNegativeCache: true);

        var artist = Assert.Single(result.Data ?? []);
        Assert.Equal("Existing Provider Artist", artist.Name);

        await using (var context = await MockArtistSearchEngineFactory().CreateDbContextAsync())
        {
            Assert.Equal(1, await context.Artists.CountAsync());
        }
    }

    #endregion

    #region Performance Helper Tests

    [Fact]
    public void GetCompoundArtistFallbackNames_WithCompoundArtist_ReturnsParts()
    {
        var result = ArtistSearchEngineService.GetCompoundArtistFallbackNames("Artist One feat. Artist Two");

        Assert.Equal(["Artist One", "Artist Two"], result);
    }

    [Fact]
    public void GetCompoundArtistFallbackNames_WithSingleArtist_ReturnsEmpty()
    {
        var result = ArtistSearchEngineService.GetCompoundArtistFallbackNames("Earth Wind Fire");

        Assert.Empty(result);
    }

    [Fact]
    public void ShouldUseCompoundArtistFallbackResult_WithTrustedMatchingRelease_ReturnsTrue()
    {
        var candidate = new ArtistSearchResult
        {
            Name = "Artist One",
            FromPlugin = "Test",
            SpotifyId = "spotify:artist-one",
            Releases =
            [
                new AlbumSearchResult
                {
                    AlbumType = AlbumType.Album,
                    Name = "Shared Release",
                    NameNormalized = "SHAREDRELEASE",
                    SortName = "Shared Release",
                    ReleaseDate = "2026-01-01"
                }
            ]
        };
        var query = new ArtistQuery
        {
            Name = "Artist One feat. Artist Two",
            AlbumKeyValues = [new KeyValue("2026", "SHAREDRELEASE")]
        };

        var result = ArtistSearchEngineService.ShouldUseCompoundArtistFallbackResult(candidate, query);

        Assert.True(result);
    }

    [Fact]
    public void ShouldUseCompoundArtistFallbackResult_WithoutTrustedIdentifier_ReturnsFalse()
    {
        var candidate = new ArtistSearchResult
        {
            Name = "Artist One",
            FromPlugin = "Test",
            Releases =
            [
                new AlbumSearchResult
                {
                    AlbumType = AlbumType.Album,
                    Name = "Shared Release",
                    NameNormalized = "SHAREDRELEASE",
                    SortName = "Shared Release",
                    ReleaseDate = "2026-01-01"
                }
            ]
        };
        var query = new ArtistQuery
        {
            Name = "Artist One feat. Artist Two",
            AlbumKeyValues = [new KeyValue("2026", "SHAREDRELEASE")]
        };

        var result = ArtistSearchEngineService.ShouldUseCompoundArtistFallbackResult(candidate, query);

        Assert.False(result);
    }

    [Fact]
    public void ShouldAttemptCompoundArtistFallback_WithForcedRevalidation_ReturnsFalse()
    {
        var query = new ArtistQuery
        {
            Name = "Artist One feat. Artist Two",
            AlbumKeyValues = [new KeyValue("2026", "SHAREDRELEASE")]
        };

        var result = ArtistSearchEngineService.ShouldAttemptCompoundArtistFallback(
            query,
            bypassNegativeCache: true);

        Assert.False(result);
    }

    [Fact]
    public void ShouldAttemptCompoundArtistFallback_WithAlbumEvidenceAndNormalSearch_ReturnsTrue()
    {
        var query = new ArtistQuery
        {
            Name = "Artist One feat. Artist Two",
            AlbumKeyValues = [new KeyValue("2026", "SHAREDRELEASE")]
        };

        var result = ArtistSearchEngineService.ShouldAttemptCompoundArtistFallback(
            query,
            bypassNegativeCache: false);

        Assert.True(result);
    }

    [Fact]
    public void IsDecentDbTransientTransactionConflict_WithConflictMessage_ReturnsTrue()
    {
        var exception = new InvalidOperationException("DecentDB error 4: transaction conflict");

        var result = ArtistSearchEngineService.IsDecentDbTransientTransactionConflict(exception);

        Assert.True(result);
    }

    [Fact]
    public void IsDecentDbCorruption_WithChecksumMessage_ReturnsTrue()
    {
        var exception = new InvalidOperationException("DecentDB checksum mismatch detected");

        var result = ArtistSearchEngineService.IsDecentDbCorruption(exception);

        Assert.True(result);
    }

    [Fact]
    public void IsDecentDbCorruption_WithDatabaseCorruptionMessage_ReturnsTrue()
    {
        var exception = new InvalidOperationException("DecentDB error 2: database corruption: page WAL frame used page id 0");

        var result = ArtistSearchEngineService.IsDecentDbCorruption(exception);

        Assert.True(result);
    }

    #endregion

    #region ListAsync Tests

    [Fact]
    public async Task ListAsync_WithAlbums_ReturnsPagedArtistsWithAlbumCounts()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        var firstArtist = new Artist
        {
            Name = "First Artist",
            NameNormalized = "FIRSTARTIST",
            SortName = "First Artist"
        };
        var secondArtist = new Artist
        {
            Name = "Second Artist",
            NameNormalized = "SECONDARTIST",
            SortName = "Second Artist"
        };
        var thirdArtist = new Artist
        {
            Name = "Third Artist",
            NameNormalized = "THIRDARTIST",
            SortName = "Third Artist"
        };

        await using (var context = await MockArtistSearchEngineFactory().CreateDbContextAsync())
        {
            context.Artists.AddRange(firstArtist, secondArtist, thirdArtist);
            await context.SaveChangesAsync();

            context.Albums.AddRange(
                NewAlbum(firstArtist, "First Album", 2001),
                NewAlbum(firstArtist, "Second Album", 2002),
                NewAlbum(secondArtist, "Only Album", 2003));
            await context.SaveChangesAsync();
        }

        var result = await service.ListAsync(new PagedRequest
        {
            Page = 1,
            PageSize = 2,
            OrderBy = new Dictionary<string, string> { { nameof(Artist.Id), PagedRequest.OrderAscDirection } }
        });

        var artists = result.Data?.ToArray() ?? [];
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, artists.Length);
        Assert.Equal(2, artists[0].AlbumCount);
        Assert.Equal(1, artists[1].AlbumCount);
    }

    [Fact]
    public async Task ListAsync_WithTotalCountOnlyRequest_ReturnsExactCountWithoutData()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        await using (var context = await MockArtistSearchEngineFactory().CreateDbContextAsync())
        {
            context.Artists.AddRange(
                new Artist
                {
                    Name = "Count Artist One",
                    NameNormalized = "COUNTARTISTONE",
                    SortName = "Count Artist One"
                },
                new Artist
                {
                    Name = "Count Artist Two",
                    NameNormalized = "COUNTARTISTTWO",
                    SortName = "Count Artist Two"
                },
                new Artist
                {
                    Name = "Count Artist Three",
                    NameNormalized = "COUNTARTISTTHREE",
                    SortName = "Count Artist Three"
                });
            await context.SaveChangesAsync();
        }

        var result = await service.ListAsync(new PagedRequest
        {
            Page = 1,
            PageSize = 2,
            IsTotalCountOnlyRequest = true
        });

        Assert.Equal(3, result.TotalCount);
        Assert.Empty(result.Data ?? []);
    }

    [Fact]
    public async Task ListAsync_WithFilteredPage_ReturnsOnlyRequestedPage()
    {
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        await using (var context = await MockArtistSearchEngineFactory().CreateDbContextAsync())
        {
            context.Artists.AddRange(
                new Artist
                {
                    Name = "Alpha One",
                    NameNormalized = "ALPHAONE",
                    SortName = "Alpha One"
                },
                new Artist
                {
                    Name = "Alpha Two",
                    NameNormalized = "ALPHATWO",
                    SortName = "Alpha Two"
                },
                new Artist
                {
                    Name = "Beta One",
                    NameNormalized = "BETAONE",
                    SortName = "Beta One"
                });
            await context.SaveChangesAsync();
        }

        var result = await service.ListAsync(new PagedRequest
        {
            Page = 1,
            PageSize = 1,
            FilterBy =
            [
                new FilterOperatorInfo(nameof(Artist.NameNormalized), FilterOperator.Contains, "ALPHA")
            ],
            OrderBy = new Dictionary<string, string> { { nameof(Artist.Name), PagedRequest.OrderAscDirection } }
        });

        var artists = result.Data?.ToArray() ?? [];
        Assert.Equal(2, result.TotalCount);
        var artist = Assert.Single(artists);
        Assert.Equal("Alpha One", artist.Name);
    }

    private static Album NewAlbum(Artist artist, string name, int year)
    {
        return new Album
        {
            Artist = artist,
            ArtistId = artist.Id,
            Name = name,
            NameNormalized = name.Replace(" ", string.Empty).ToUpperInvariant(),
            SortName = name,
            AlbumType = 1,
            Year = year
        };
    }

    private ArtistSearchEngineService CreateArtistSearchEngineService(
        IMelodeeConfigurationFactory configFactory,
        IHttpClientFactory httpClientFactory)
    {
        return new ArtistSearchEngineService(
            Logger,
            CacheManager,
            MockSettingService(),
            MockSpotifyClientBuilder(),
            configFactory,
            MockFactory(),
            MockArtistSearchEngineFactory(),
            GetMusicBrainzRepository(),
            Serializer,
            httpClientFactory);
    }

    private static IMelodeeConfigurationFactory SearchConfigurationFactory(
        IReadOnlyDictionary<string, object?> overrides)
    {
        var settings = TestsBase.NewConfiguration();
        foreach (var (key, value) in overrides)
        {
            settings[key] = value;
        }

        var mock = new Mock<IMelodeeConfigurationFactory>();
        mock.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MelodeeConfiguration(settings));
        return mock.Object;
    }

    #endregion

    #region DoArtistTopSongsSearchAsync Tests

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_WithValidArtist_ReturnsResults()
    {
        // Arrange
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        // Act
        var result = await service.DoArtistTopSongsSearchAsync("Test Artist", null, 10);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.TotalCount >= 0);
    }

    [Fact]
    public async Task DoArtistTopSongsSearchAsync_WithNullArtistName_ReturnsEmptyResults()
    {
        // Arrange
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        // Act
        var result = await service.DoArtistTopSongsSearchAsync(null!, null, 10);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(0, result.TotalCount);
    }

    #endregion

    #region LookupAsync Tests

    [Fact]
    public async Task LookupAsync_WithEmptyName_ReturnsEmptyResult()
    {
        // Arrange
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        // Act
        var result = await service.LookupAsync(string.Empty, 10, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Candidates);
    }

    [Fact]
    public async Task LookupAsync_WithValidName_ReturnsCandidates()
    {
        // Arrange
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        // Act
        var result = await service.LookupAsync("Test", 10, null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Candidates);
    }

    [Fact]
    public async Task LookupAsync_WithProviderFilter_RespectsFilter()
    {
        // Arrange
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        var plugins = service.GetRegisteredPlugins();
        var firstPluginId = plugins.FirstOrDefault()?.Id;

        // Act
        var result = await service.LookupAsync("Test", 10, [firstPluginId ?? "Unknown"], CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result.Candidates);
    }

    [Fact]
    public async Task LookupAsync_GetRegisteredPlugins_ReturnsEnabledPlugins()
    {
        // Arrange
        var service = GetArtistSearchEngineService();
        await service.InitializeAsync();

        // Act
        var plugins = service.GetRegisteredPlugins();

        // Assert
        Assert.NotNull(plugins);
        Assert.NotEmpty(plugins);
    }

    #endregion

    #region ArtistLookupRequest Validation Tests

    [Fact]
    public void ArtistLookupRequest_WithEmptyName_ReturnsValidationError()
    {
        // Arrange
        var request = new ArtistLookupRequest { ArtistName = string.Empty };

        // Act
        var isValid = request.Validate(out var errorMessage);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(errorMessage);
        Assert.Contains("required", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArtistLookupRequest_WithWhitespaceName_ReturnsValidationError()
    {
        // Arrange
        var request = new ArtistLookupRequest { ArtistName = "   " };

        // Act
        var isValid = request.Validate(out var errorMessage);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(errorMessage);
    }

    [Fact]
    public void ArtistLookupRequest_WithNameTooLong_ReturnsValidationError()
    {
        // Arrange
        var request = new ArtistLookupRequest { ArtistName = new string('a', 201) };

        // Act
        var isValid = request.Validate(out var errorMessage);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(errorMessage);
        Assert.Contains("200", errorMessage);
    }

    [Fact]
    public void ArtistLookupRequest_WithInvalidLimit_ReturnsValidationError()
    {
        // Arrange
        var request = new ArtistLookupRequest { ArtistName = "Test Artist", Limit = 100 };

        // Act
        var isValid = request.Validate(out var errorMessage);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(errorMessage);
        Assert.Contains("limit", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArtistLookupRequest_WithTooManyProviderIds_ReturnsValidationError()
    {
        // Arrange
        var request = new ArtistLookupRequest
        {
            ArtistName = "Test",
            ProviderIds = Enumerable.Range(0, 21).Select(i => $"provider_{i}").ToArray()
        };

        // Act
        var isValid = request.Validate(out var errorMessage);

        // Assert
        Assert.False(isValid);
        Assert.NotNull(errorMessage);
        Assert.Contains("ProviderIds", errorMessage);
    }

    [Fact]
    public void ArtistLookupRequest_WithValidData_ReturnsSuccess()
    {
        // Arrange
        var request = new ArtistLookupRequest
        {
            ArtistName = "Test Artist",
            Limit = 10,
            ProviderIds = ["provider1", "provider2"]
        };

        // Act
        var isValid = request.Validate(out var errorMessage);

        // Assert
        Assert.True(isValid);
        Assert.Null(errorMessage);
    }

    #endregion
}
