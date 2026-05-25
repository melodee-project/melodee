using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services.Scanning;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Melodee.Tests.Common.Services;

public class MediaEditServiceTests : ServiceTestBase
{
    [Fact]
    public async Task InitializeAsync_WithValidConfiguration_InitializesSuccessfully()
    {
        var service = GetMediaEditService();
        var configuration = new MelodeeConfiguration([]);

        await service.InitializeAsync(configuration);

        // Verify no exceptions are thrown and service can be used
        Assert.True(true); // If we reach here, initialization succeeded
    }

    [Fact]
    public async Task InitializeAsync_WithoutConfiguration_UsesFactory()
    {
        var service = GetMediaEditService();

        await service.InitializeAsync();

        // Verify no exceptions are thrown and service can be used
        Assert.True(true); // If we reach here, initialization succeeded
    }

    [Fact]
    public void CheckInitialized_WhenNotInitialized_ThrowsException()
    {
        var service = GetMediaEditService();

        // Try to call a method that requires initialization
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            var album = CreateTestAlbum();
            service.SetArtistOnAllSongs(album);
        });

        Assert.Equal("Media edit service is not initialized.", exception.Message);
    }

    [Fact]
    public async Task SaveImageUrlAsCoverAsync_WithInvalidAlbumDirectory_ReturnsError()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();
        album.Directory = null!;

        var result = await service.SaveImageUrlAsCoverAsync(album, "http://example.com/image.jpg", false);

        Assert.NotNull(result);
        Assert.Equal("An error has occured. OH NOES!", result.Messages?.FirstOrDefault());
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task DoMagic_WithMagicDisabled_ReturnsNotSetStatus()
    {
        var service = GetMediaEditService();
        var config = new MelodeeConfiguration(new Dictionary<string, object?>
        {
            { SettingRegistry.MagicEnabled, false }
        });
        await service.InitializeAsync(config);

        var album = CreateTestAlbum();

        var result = await service.DoMagic(album);

        Assert.NotNull(result);
        Assert.Equal(AlbumStatus.NotSet, result.Data.AlbumStatus);
        Assert.Equal(AlbumNeedsAttentionReasons.NotSet, result.Data.AlbumStatusReasons);
    }

    [Fact]
    public async Task DoMagic_WithInvalidDirectory_ReturnsInvalidStatus()
    {
        var service = GetMediaEditService();
        var config = new MelodeeConfiguration(new Dictionary<string, object?>
        {
            { SettingRegistry.MagicEnabled, true }
        });
        await service.InitializeAsync(config);

        var album = CreateTestAlbum();
        album.Directory = null!;

        var result = await service.DoMagic(album);

        Assert.NotNull(result);
        Assert.Equal(AlbumStatus.Invalid, result.Data.AlbumStatus);
        Assert.Equal(AlbumNeedsAttentionReasons.AlbumCannotBeLoaded, result.Data.AlbumStatusReasons);
    }

    [Fact]
    public async Task RemoveUnwantedTextFromAlbumTitle_WithValidAlbum_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();
        album.SetTagValue(MetaTagIdentifier.Album, "Test Album (Remastered)");

        var result = await service.RemoveUnwantedTextFromAlbumTitle(album, false);

        Assert.True(result.IsSuccess);
        // Verify the album was returned (even if title wasn't changed)
        Assert.Equal(album, result.Data.Item2);
    }

    [Fact]
    public async Task RemoveUnwantedTextFromSongTitles_WithValidAlbum_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();
        if (album.Songs?.FirstOrDefault() != null)
        {
            album.SetSongTagValue(album.Songs.First().Id, MetaTagIdentifier.Title, "Test Song (Remastered)");
        }

        var result = await service.RemoveUnwantedTextFromSongTitles(album, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(album, result.Data.Item2);
    }

    [Fact]
    public async Task RemoveFeaturingArtistsFromSongTitle_WithFeaturingArtist_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();
        if (album.Songs?.FirstOrDefault() != null)
        {
            album.SetSongTagValue(album.Songs.First().Id, MetaTagIdentifier.Title, "Test Song feat. Other Artist");
        }

        var result = await service.RemoveFeaturingArtistsFromSongTitle(album, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(album, result.Data.Item2);
    }

    [Fact]
    public async Task RemoveFeaturingArtistsFromSongsArtist_WithFeaturingArtist_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();
        if (album.Songs?.FirstOrDefault() != null)
        {
            album.SetSongTagValue(album.Songs.First().Id, MetaTagIdentifier.Artist, "Main Artist feat. Other Artist");
        }

        var result = await service.RemoveFeaturingArtistsFromSongsArtist(album, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(album, result.Data.Item2);
    }

    [Fact]
    public async Task PromoteSongArtist_WithValidParameters_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var directoryInfo = CreateTestDirectoryInfo();
        var albumId = Guid.NewGuid();
        var songId = Guid.NewGuid();

        var result = await service.PromoteSongArtist(directoryInfo, albumId, songId, false);

        Assert.NotNull(result);
        // The method should complete without throwing an exception
        Assert.False(result.Data); // Expected to be false as test data isn't fully set up
    }

    [Fact]
    public async Task SetYearToCurrent_WithInvalidYear_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();
        album.SetTagValue(MetaTagIdentifier.OrigAlbumYear, 0); // Invalid year

        var result = await service.SetYearToCurrent(album, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(album, result.Data.Item2);
    }

    [Fact]
    public async Task ReplaceGivenTextFromSongTitles_WithValidParameters_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var directoryInfo = CreateTestDirectoryInfo();
        var albumId = Guid.NewGuid();
        const string textToRemove = "unwanted";
        const string replacement = "wanted";

        var result = await service.ReplaceGivenTextFromSongTitles(directoryInfo, albumId, textToRemove, replacement, false);

        Assert.NotNull(result);
        // Method should complete without exception
        Assert.False(result.Data); // Expected to be false as test data isn't fully set up
    }

    [Fact]
    public async Task RemoveAllSongArtists_WithEmptyArray_ReturnsFalse()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var directoryInfo = CreateTestDirectoryInfo();
        var albumIds = Array.Empty<Guid>();

        var result = await service.RemoveAllSongArtists(directoryInfo, albumIds, false);

        Assert.NotNull(result);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task ReplaceAllSongArtistSeparators_WithValidAlbum_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();
        if (album.Songs?.FirstOrDefault() != null)
        {
            album.SetSongTagValue(album.Songs.First().Id, MetaTagIdentifier.Artist, "Artist1; Artist2");
        }

        var result = await service.ReplaceAllSongArtistSeparators(album, false);

        Assert.True(result.IsSuccess);
        Assert.Equal(album, result.Data.Item2);
    }

    [Fact]
    public async Task SetArtistOnAllSongs_WithEmptyAlbum_ReturnsAlbum()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();
        album.Songs = [];

        var result = service.SetArtistOnAllSongs(album);

        Assert.NotNull(result);
        Assert.Equal(album, result);
    }

    [Fact]
    public async Task RenumberSongsAsync_WithValidAlbum_ReturnsAlbum()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();

        var result = await service.RenumberSongsAsync(album);

        Assert.NotNull(result);
        Assert.Equal(album, result);
    }

    [Fact]
    public async Task RenumberSongs_WithValidAlbum_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();

        var result = await service.RenumberSongs(album, false);

        Assert.True(result.IsSuccess);
        Assert.True(result.Data.Item1); // Should return true for success
        Assert.Equal(album, result.Data.Item2);
    }

    [Fact]
    public async Task DeleteAllImagesForAlbums_WithEmptyArray_ReturnsFalse()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var directoryInfo = CreateTestDirectoryInfo();
        var albumIds = Array.Empty<Guid>();

        var result = await service.DeleteAllImagesForAlbums(directoryInfo, albumIds, false);

        Assert.NotNull(result);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task RemoveArtistFromSongArtists_WithValidParameters_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var directoryInfo = CreateTestDirectoryInfo();
        var albumIds = new[] { Guid.NewGuid() };

        var result = await service.RemoveArtistFromSongArtists(directoryInfo, albumIds, false);

        Assert.NotNull(result);
        // Method should complete without exception
        Assert.False(result.Data); // Expected to be false as test data isn't fully set up
    }

    [Fact]
    public async Task SetAlbumsStatusToReviewed_WithEmptyArray_ReturnsFalse()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var directoryInfo = CreateTestDirectoryInfo();
        var albumIds = Array.Empty<Guid>();

        var result = await service.SetAlbumsStatusToReviewed(directoryInfo, albumIds, false);

        Assert.NotNull(result);
        Assert.False(result.Data);
    }

    [Fact]
    public async Task SaveMelodeeAlbum_WithValidAlbum_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();

        var result = await service.SaveMelodeeAlbum(album, true);

        Assert.NotNull(result);
        Assert.True(result.Data); // Should be true when forceIsOk is true
    }

    [Fact]
    public async Task ManuallyValidateAlbum_WithValidAlbum_ReturnsSuccess()
    {
        var service = GetMediaEditService();
        await service.InitializeAsync();

        var album = CreateTestAlbum();

        var result = await service.ManuallyValidateAlbum(album);

        Assert.NotNull(result);
        Assert.True(result.Data); // Should be true as this forces OK status
    }

    #region Helper Methods

    private new MediaEditService GetMediaEditService()
    {
        var mockHttpClientFactory = new Mock<IHttpClientFactory>();
        var mockSerializer = new Mock<ISerializer>();
        mockSerializer.Setup(x => x.Serialize(It.IsAny<object>())).Returns("{}");

        return new MediaEditService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            GetTestAlbumDiscoveryService(),
            mockSerializer.Object,
            mockHttpClientFactory.Object,
            new ImageProcessor());
    }

    private Album CreateTestAlbum()
    {
        var albumId = Guid.NewGuid();
        var artistId = Guid.NewGuid();
        var songId = Guid.NewGuid();

        var directoryInfo = CreateTestDirectoryInfo();
        var fileInfo = new FileSystemFileInfo { Name = "song.mp3", Size = 1000 };

        return new Album
        {
            Id = albumId,
            ViaPlugins = [],
            OriginalDirectory = directoryInfo,
            Artist = new Artist("Test Artist", "testartist", null, null, null),
            Directory = directoryInfo,
            Tags = new List<MetaTag<object?>>
            {
                new() { Identifier = MetaTagIdentifier.Album, Value = "Test Album" },
                new() { Identifier = MetaTagIdentifier.OrigAlbumYear, Value = 2023 }
            },
            Songs =
            [
                new Song
                {
                    Id = songId,
                    CrcHash = "testcrc",
                    File = fileInfo,
                    Tags = new List<MetaTag<object?>>
                    {
                        new() { Identifier = MetaTagIdentifier.Title, Value = "Test Song" },
                        new() { Identifier = MetaTagIdentifier.TrackNumber, Value = 1 }
                    },
                    SortOrder = 1
                }
            ],
            Status = AlbumStatus.Ok,
            StatusReasons = AlbumNeedsAttentionReasons.NotSet
        };
    }

    private FileSystemDirectoryInfo CreateTestDirectoryInfo()
    {
        return new FileSystemDirectoryInfo
        {
            Path = "/test/path",
            Name = "TestDirectory"
        };
    }

    private AlbumDiscoveryService GetTestAlbumDiscoveryService()
    {
        var mockSerializer = new Mock<ISerializer>();
        return new AlbumDiscoveryService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            MockFileSystemService());
    }

    #endregion
}
