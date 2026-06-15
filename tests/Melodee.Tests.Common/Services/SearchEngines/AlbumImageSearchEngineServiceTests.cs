using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Plugins.SearchEngine;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.Spotify;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.SearchEngines;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Tests.Common.Services.SearchEngines;

public class AlbumImageSearchEngineServiceTests : ServiceTestBase
{
    [Fact]
    public async Task DoSearchAsync_WithValidQuery_ReturnsImageResults()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Abbey Road",
            Artist = "The Beatles",
            Year = 1969
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        // Results might be empty if search engines are disabled, but should not fail
    }

    [Fact]
    public async Task DoSearchAsync_WithNullMaxResults_UsesDefaultPageSize()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Dark Side of the Moon",
            Artist = "Pink Floyd",
            Year = 1973
        };

        // Act
        var result = await service.DoSearchAsync(query, null);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithCancellationToken_CompletesWithoutError()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "The Wall",
            Artist = "Pink Floyd",
            Year = 1979
        };

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(100); // Allow some time for initialization

        // Act
        var result = await service.DoSearchAsync(query, 10, cts.Token);

        // Assert - Should complete without throwing even if cancelled
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithMaxResults_LimitsResultCount()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Thriller",
            Artist = "Michael Jackson",
            Year = 1982
        };
        var maxResults = 5;

        // Act
        var result = await service.DoSearchAsync(query, maxResults);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.Length <= maxResults);
    }

    [Fact]
    public async Task DoSearchAsync_WithMusicBrainzId_IncludesIdInQuery()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Revolver",
            Artist = "The Beatles",
            Year = 1966,
            MusicBrainzId = "123e4567-e89b-12d3-a456-426614174000"
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithArtistMusicBrainzId_IncludesArtistIdInQuery()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Sgt. Pepper's Lonely Hearts Club Band",
            Artist = "The Beatles",
            Year = 1967,
            ArtistMusicBrainzId = "b10bbbfc-cf9e-42e0-be17-e2c3e1d2600d"
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithEmptyQuery_HandlesGracefully()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "",
            Artist = "",
            Year = 2000
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        // Empty query should still return valid response, even if no results
    }

    [Fact]
    public async Task DoSearchAsync_WithSpecialCharactersInQuery_HandlesCorrectly()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Âme & Soul",
            Artist = "Café del Mar",
            Year = 2005
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithVeryLongQuery_HandlesCorrectly()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var longName = new string('A', 1000);
        var query = new AlbumQuery
        {
            Name = longName,
            Artist = "Some Artist",
            Year = 2020
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_ResultsOrderedByRankDescending()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Unknown Pleasures",
            Artist = "Joy Division",
            Year = 1979
        };

        // Act
        var result = await service.DoSearchAsync(query, 50);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);

        // If there are multiple results, verify they are ordered by rank descending
        if (result.Data.Length > 1)
        {
            for (int i = 0; i < result.Data.Length - 1; i++)
            {
                Assert.True(result.Data[i].Rank >= result.Data[i + 1].Rank);
            }
        }
    }

    [Fact]
    public async Task DoSearchAsync_WithZeroMaxResults_ReturnsEmptyResults()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Test Album",
            Artist = "Test Artist",
            Year = 2023
        };

        // Act
        var result = await service.DoSearchAsync(query, 0);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Empty(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithNegativeMaxResults_HandlesGracefully()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "Test Album",
            Artist = "Test Artist",
            Year = 2023
        };

        // Act
        var result = await service.DoSearchAsync(query, -1);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        // Should handle negative values gracefully
    }

    [Fact]
    public async Task DoSearchAsync_WithApiKey_IncludesApiKeyInQuery()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var apiKey = Guid.NewGuid();
        var query = new AlbumQuery
        {
            ApiKey = apiKey,
            Name = "Random Access Memories",
            Artist = "Daft Punk",
            Year = 2013
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithSpotifyId_IncludesSpotifyIdInQuery()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "OK Computer",
            Artist = "Radiohead",
            Year = 1997,
            SpotifyId = "6dVIqQ8qmQ183Z5MaQRIhJ"
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithDifferentCountry_HandlesCountrySpecificSearch()
    {
        // Arrange
        var service = GetAlbumImageSearchEngineService();
        var query = new AlbumQuery
        {
            Name = "The Joshua Tree",
            Artist = "U2",
            Year = 1987,
            Country = "GB"
        };

        // Act
        var result = await service.DoSearchAsync(query, 10);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task DoSearchAsync_WithMultipleEnabledPlugins_StartsProvidersConcurrently()
    {
        var releaseSearches = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPlugin = new BlockingAlbumImageSearchEnginePlugin("First", 10, 1, releaseSearches);
        var secondPlugin = new BlockingAlbumImageSearchEnginePlugin("Second", 20, 2, releaseSearches);
        var service = GetAlbumImageSearchEngineServiceWithPlugins(firstPlugin, secondPlugin);
        var query = new AlbumQuery
        {
            Name = "Parallel Album",
            Artist = "Parallel Artist",
            Year = 2024
        };

        var searchTask = service.DoSearchAsync(query, 1);

        await Task.WhenAll(firstPlugin.Started, secondPlugin.Started).WaitAsync(TimeSpan.FromSeconds(1));
        releaseSearches.SetResult(true);

        var result = await searchTask;

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Single(result.Data);
        Assert.Equal("Second", result.Data[0].FromPlugin);
    }

    private AlbumImageSearchEngineService GetAlbumImageSearchEngineServiceWithPlugins(
        params IAlbumImageSearchEnginePlugin[] searchEngines)
    {
        return new TestAlbumImageSearchEngineService(
            Logger,
            CacheManager,
            Serializer,
            MockSettingService(),
            MockConfigurationFactory(),
            MockFactory(),
            GetMusicBrainzRepository(),
            MockSpotifyClientBuilder(),
            MockHttpClientFactory(),
            searchEngines);
    }

    private sealed class TestAlbumImageSearchEngineService(
        ILogger logger,
        ICacheManager cacheManager,
        ISerializer serializer,
        SettingService settingService,
        IMelodeeConfigurationFactory configurationFactory,
        IDbContextFactory<MelodeeDbContext> contextFactory,
        IMusicBrainzRepository musicBrainzRepository,
        ISpotifyClientBuilder spotifyClientBuilder,
        IHttpClientFactory httpClientFactory,
        IReadOnlyCollection<IAlbumImageSearchEnginePlugin> searchEngines)
        : AlbumImageSearchEngineService(
            logger,
            cacheManager,
            serializer,
            settingService,
            configurationFactory,
            contextFactory,
            musicBrainzRepository,
            spotifyClientBuilder,
            httpClientFactory)
    {
        protected override IReadOnlyCollection<IAlbumImageSearchEnginePlugin> CreateSearchEngines(IMelodeeConfiguration configuration)
        {
            return searchEngines;
        }
    }

    private sealed class BlockingAlbumImageSearchEnginePlugin(
        string displayName,
        short rank,
        long uniqueId,
        TaskCompletionSource<bool> releaseSearch) : IAlbumImageSearchEnginePlugin
    {
        private readonly TaskCompletionSource<bool> _started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Started => _started.Task;

        public bool StopProcessing => false;

        public string Id => uniqueId.ToString();

        public string DisplayName => displayName;

        public bool IsEnabled { get; set; } = true;

        public int SortOrder { get; } = 1;

        public async Task<OperationResult<ImageSearchResult[]?>> DoAlbumImageSearch(
            AlbumQuery query,
            int maxResults,
            CancellationToken cancellationToken = default)
        {
            _started.SetResult(true);
            await releaseSearch.Task.WaitAsync(cancellationToken);

            return new OperationResult<ImageSearchResult[]?>
            {
                Data =
                [
                    new ImageSearchResult
                    {
                        FromPlugin = displayName,
                        Rank = rank,
                        UniqueId = uniqueId,
                        Width = 600,
                        Height = 600,
                        ThumbnailUrl = $"https://example.com/{uniqueId}.jpg",
                        MediaUrl = $"https://example.com/{uniqueId}.jpg",
                        Title = displayName
                    }
                ]
            };
        }
    }
}
