using Melodee.Common.Services;
using Melodee.Tests.Common.TestHelpers;
using NodaTime;
using System.Text;

namespace Melodee.Tests.Common.Services;

/// <summary>
/// Basic tests for PlaylistImportService functionality.
/// </summary>
public class PlaylistImportServiceBasicTests : ServiceTestBase
{
    private PlaylistImportService GetPlaylistImportService()
    {
        return new PlaylistImportService(
            Logger,
            CacheManager,
            MockFactory(),
            Serializer);
    }

    [Fact]
    public async Task ImportPlaylistAsync_WithValidM3U_CreatesPlaylist()
    {
        // Arrange
        var service = GetPlaylistImportService();
        var context = await MockFactory().CreateDbContextAsync();

        var user = TestDataFactory.CreateTestUser();
        context.Users.Add(user);

        var artist = TestDataFactory.CreateTestArtist();
        context.Artists.Add(artist);

        var album = TestDataFactory.CreateTestAlbum(artist);
        context.Albums.Add(album);

        var song1 = TestDataFactory.CreateTestSong(album, "Song One", "song1.mp3", 1);
        var song2 = TestDataFactory.CreateTestSong(album, "Song Two", "song2.mp3", 2);
        context.Songs.AddRange(song1, song2);
        await context.SaveChangesAsync();

        var m3uContent = """
            #EXTM3U
            #EXTINF:180,Test Artist - Song One
            song1.mp3
            #EXTINF:200,Test Artist - Song Two
            song2.mp3
            """;
        var fileContent = Encoding.UTF8.GetBytes(m3uContent);

        // Act
        var result = await service.ImportPlaylistAsync(
            user.Id,
            "test.m3u",
            fileContent,
            "Test Playlist");

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.TotalEntries);
        Assert.Equal(2, result.Data.MatchedCount);
        Assert.Equal(0, result.Data.MissingCount);
    }

    [Fact]
    public async Task ImportPlaylistAsync_WithEmptyFile_ReturnsValidationError()
    {
        // Arrange
        var service = GetPlaylistImportService();
        var context = await MockFactory().CreateDbContextAsync();

        var user = TestDataFactory.CreateTestUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var m3uContent = """
            #EXTM3U
            
            
            """;
        var fileContent = Encoding.UTF8.GetBytes(m3uContent);

        // Act
        var result = await service.ImportPlaylistAsync(
            user.Id,
            "empty.m3u",
            fileContent);

        // Assert
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task ImportPlaylistAsync_WithCommentsAndBlankLines_IgnoresThem()
    {
        // Arrange
        var service = GetPlaylistImportService();
        var context = await MockFactory().CreateDbContextAsync();

        var user = TestDataFactory.CreateTestUser();
        context.Users.Add(user);

        var artist = TestDataFactory.CreateTestArtist();
        context.Artists.Add(artist);

        var album = TestDataFactory.CreateTestAlbum(artist);
        context.Albums.Add(album);

        var song = TestDataFactory.CreateTestSong(album, "Test Song", "test.mp3", 1);
        context.Songs.Add(song);
        await context.SaveChangesAsync();

        var m3uContent = """
            #EXTM3U
            # This is a comment
            
            #EXTINF:180,Test Artist - Test Song
            test.mp3
            
            # Another comment
            """;
        var fileContent = Encoding.UTF8.GetBytes(m3uContent);

        // Act
        var result = await service.ImportPlaylistAsync(
            user.Id,
            "comments.m3u",
            fileContent);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.TotalEntries);
        Assert.Equal(1, result.Data.MatchedCount);
    }
}
