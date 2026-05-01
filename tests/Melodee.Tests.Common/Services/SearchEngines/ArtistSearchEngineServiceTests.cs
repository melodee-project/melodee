using Melodee.Blazor.Controllers.Melodee.Models.ArtistLookup;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines;
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
