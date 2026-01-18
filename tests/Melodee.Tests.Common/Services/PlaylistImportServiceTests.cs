using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Services;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using System.Text;

namespace Melodee.Tests.Common.Services;

public class PlaylistImportServiceTests : ServiceTestBase
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

        // Create test user
        var user = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@melodee.net",
            EmailNormalized = "TEST@MELODEE.NET",
            PublicKey = "testkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Users.Add(user);

        // Create test songs
        var artist = new Artist
        {
            Name = "Test Artist",
            NameNormalized = "TEST ARTIST",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Artists.Add(artist);

        var album = new Album
        {
            Name = "Test Album",
            NameNormalized = "TEST ALBUM",
            Artist = artist,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Albums.Add(album);

        var song1 = new Song
        {
            Title = "Song One",
            TitleNormalized = "SONG ONE",
            FileName = "song1.mp3",
            Album = album,
            SongNumber = 1,
            FileSize = 1000,
            FileHash = "hash1",
            Duration = 180000,
            SamplingRate = 44100,
            BitRate = 320,
            BitDepth = 16,
            BPM = 120,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        var song2 = new Song
        {
            Title = "Song Two",
            TitleNormalized = "SONG TWO",
            FileName = "song2.mp3",
            Album = album,
            SongNumber = 2,
            FileSize = 2000,
            FileHash = "hash2",
            Duration = 200000,
            SamplingRate = 44100,
            BitRate = 320,
            BitDepth = 16,
            BPM = 130,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        context.Songs.AddRange(song1, song2);
        await context.SaveChangesAsync();

        // Create M3U content
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
        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.TotalEntries);
        Assert.Equal(2, result.Data.MatchedCount);
        Assert.Equal(0, result.Data.MissingCount);

        // Verify playlist was created
        var playlist = await context.Playlists
            .Include(p => p.Songs)
            .FirstOrDefaultAsync(p => p.ApiKey == result.Data.PlaylistApiKey);
        Assert.NotNull(playlist);
        Assert.Equal("Test Playlist", playlist.Name);
        Assert.Equal(2, playlist.Songs.Count);
        Assert.Equal(user.Id, playlist.UserId);

        // Verify uploaded file record
        var uploadedFile = await context.PlaylistUploadedFiles
            .Include(f => f.Items)
            .FirstOrDefaultAsync(f => f.PlaylistId == playlist.Id);
        Assert.NotNull(uploadedFile);
        Assert.Equal("test.m3u", uploadedFile.OriginalFileName);
        Assert.Equal(2, uploadedFile.Items.Count);
        Assert.All(uploadedFile.Items, item =>
            Assert.Equal(PlaylistUploadedFileItemStatus.Resolved, item.Status));
    }

    [Fact]
    public async Task ImportPlaylistAsync_WithMissingFiles_CreatesMissingEntries()
    {
        // Arrange
        var service = GetPlaylistImportService();
        var context = await MockFactory().CreateDbContextAsync();

        var user = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@melodee.net",
            EmailNormalized = "TEST@MELODEE.NET",
            PublicKey = "testkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // M3U with non-existent files
        var m3uContent = """
            #EXTM3U
            nonexistent1.mp3
            nonexistent2.mp3
            """;
        var fileContent = Encoding.UTF8.GetBytes(m3uContent);

        // Act
        var result = await service.ImportPlaylistAsync(
            user.Id,
            "test.m3u",
            fileContent);

        // Assert
        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.TotalEntries);
        Assert.Equal(0, result.Data.MatchedCount);
        Assert.Equal(2, result.Data.MissingCount);
        Assert.Equal(2, result.Data.MissingReferences.Count);

        // Verify missing entries were stored
        var uploadedFile = await context.PlaylistUploadedFiles
            .Include(f => f.Items)
            .FirstOrDefaultAsync();
        Assert.NotNull(uploadedFile);
        Assert.Equal(2, uploadedFile.Items.Count);
        Assert.All(uploadedFile.Items, item =>
            Assert.Equal(PlaylistUploadedFileItemStatus.Missing, item.Status));
    }

    [Fact]
    public async Task ImportPlaylistAsync_WithMixedMatches_CreatesPartialPlaylist()
    {
        // Arrange
        var service = GetPlaylistImportService();
        var context = await MockFactory().CreateDbContextAsync();

        var user = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@melodee.net",
            EmailNormalized = "TEST@MELODEE.NET",
            PublicKey = "testkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Users.Add(user);

        var artist = new Artist
        {
            Name = "Test Artist",
            NameNormalized = "TEST ARTIST",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Artists.Add(artist);

        var album = new Album
        {
            Name = "Test Album",
            NameNormalized = "TEST ALBUM",
            Artist = artist,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Albums.Add(album);

        var song = new Song
        {
            Title = "Existing Song",
            TitleNormalized = "EXISTING SONG",
            FileName = "existing.mp3",
            Album = album,
            SongNumber = 1,
            FileSize = 1000,
            FileHash = "hash1",
            Duration = 180000,
            SamplingRate = 44100,
            BitRate = 320,
            BitDepth = 16,
            BPM = 120,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Songs.Add(song);
        await context.SaveChangesAsync();

        // M3U with one existing and one missing file
        var m3uContent = """
            #EXTM3U
            existing.mp3
            missing.mp3
            """;
        var fileContent = Encoding.UTF8.GetBytes(m3uContent);

        // Act
        var result = await service.ImportPlaylistAsync(
            user.Id,
            "mixed.m3u",
            fileContent);

        // Assert
        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.Equal(2, result.Data.TotalEntries);
        Assert.Equal(1, result.Data.MatchedCount);
        Assert.Equal(1, result.Data.MissingCount);

        // Verify playlist has 1 song
        var playlist = await context.Playlists
            .Include(p => p.Songs)
            .FirstOrDefaultAsync(p => p.ApiKey == result.Data.PlaylistApiKey);
        Assert.NotNull(playlist);
        Assert.Single(playlist.Songs);

        // Verify upload file has 2 items with different statuses
        var uploadedFile = await context.PlaylistUploadedFiles
            .Include(f => f.Items)
            .FirstOrDefaultAsync();
        Assert.NotNull(uploadedFile);
        Assert.Equal(2, uploadedFile.Items.Count);
        Assert.Single(uploadedFile.Items.Where(i => i.Status == PlaylistUploadedFileItemStatus.Resolved));
        Assert.Single(uploadedFile.Items.Where(i => i.Status == PlaylistUploadedFileItemStatus.Missing));
    }

    [Fact]
    public async Task ImportPlaylistAsync_WithURLEncodedPaths_DecodesCorrectly()
    {
        // Arrange
        var service = GetPlaylistImportService();
        var context = await MockFactory().CreateDbContextAsync();

        var user = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@melodee.net",
            EmailNormalized = "TEST@MELODEE.NET",
            PublicKey = "testkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Users.Add(user);

        var artist = new Artist
        {
            Name = "Test Artist",
            NameNormalized = "TEST ARTIST",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Artists.Add(artist);

        var album = new Album
        {
            Name = "Test Album",
            NameNormalized = "TEST ALBUM",
            Artist = artist,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Albums.Add(album);

        var song = new Song
        {
            Title = "Song With Spaces",
            TitleNormalized = "SONG WITH SPACES",
            FileName = "song with spaces.mp3",
            Album = album,
            SongNumber = 1,
            FileSize = 1000,
            FileHash = "hash1",
            Duration = 180000,
            SamplingRate = 44100,
            BitRate = 320,
            BitDepth = 16,
            BPM = 120,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Songs.Add(song);
        await context.SaveChangesAsync();

        // M3U with URL-encoded filename
        var m3uContent = """
            #EXTM3U
            song%20with%20spaces.mp3
            """;
        var fileContent = Encoding.UTF8.GetBytes(m3uContent);

        // Act
        var result = await service.ImportPlaylistAsync(
            user.Id,
            "encoded.m3u",
            fileContent);

        // Assert
        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.TotalEntries);
        Assert.Equal(1, result.Data.MatchedCount);
        Assert.Equal(0, result.Data.MissingCount);
    }

    [Fact]
    public async Task ImportPlaylistAsync_WithEmptyFile_ReturnsValidationError()
    {
        // Arrange
        var service = GetPlaylistImportService();
        var context = await MockFactory().CreateDbContextAsync();

        var user = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@melodee.net",
            EmailNormalized = "TEST@MELODEE.NET",
            PublicKey = "testkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        // Empty M3U file
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
        Assert.Equal(OperationResponseType.ValidationFailure, result.Type);
    }

    [Fact]
    public async Task ImportPlaylistAsync_WithCommentsAndBlankLines_IgnoresThem()
    {
        // Arrange
        var service = GetPlaylistImportService();
        var context = await MockFactory().CreateDbContextAsync();

        var user = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@melodee.net",
            EmailNormalized = "TEST@MELODEE.NET",
            PublicKey = "testkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Users.Add(user);

        var artist = new Artist
        {
            Name = "Test Artist",
            NameNormalized = "TEST ARTIST",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Artists.Add(artist);

        var album = new Album
        {
            Name = "Test Album",
            NameNormalized = "TEST ALBUM",
            Artist = artist,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Albums.Add(album);

        var song = new Song
        {
            Title = "Test Song",
            TitleNormalized = "TEST SONG",
            FileName = "test.mp3",
            Album = album,
            SongNumber = 1,
            FileSize = 1000,
            FileHash = "hash1",
            Duration = 180000,
            SamplingRate = 44100,
            BitRate = 320,
            BitDepth = 16,
            BPM = 120,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Songs.Add(song);
        await context.SaveChangesAsync();

        // M3U with comments and blank lines
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
        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.TotalEntries);
        Assert.Equal(1, result.Data.MatchedCount);
    }

    [Fact]
    public async Task ImportPlaylistAsync_WithBackslashes_ConvertsToForwardSlashes()
    {
        // Arrange
        var service = GetPlaylistImportService();
        var context = await MockFactory().CreateDbContextAsync();

        var user = new User
        {
            UserName = "testuser",
            UserNameNormalized = "TESTUSER",
            Email = "test@melodee.net",
            EmailNormalized = "TEST@MELODEE.NET",
            PublicKey = "testkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Users.Add(user);

        var artist = new Artist
        {
            Name = "Artist",
            NameNormalized = "ARTIST",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Artists.Add(artist);

        var album = new Album
        {
            Name = "Album",
            NameNormalized = "ALBUM",
            Artist = artist,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Albums.Add(album);

        var song = new Song
        {
            Title = "Song",
            TitleNormalized = "SONG",
            FileName = "test.mp3",
            Album = album,
            SongNumber = 1,
            FileSize = 1000,
            FileHash = "hash1",
            Duration = 180000,
            SamplingRate = 44100,
            BitRate = 320,
            BitDepth = 16,
            BPM = 120,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
        context.Songs.Add(song);
        await context.SaveChangesAsync();

        // M3U with Windows-style paths
        var m3uContent = """
            #EXTM3U
            D:\Music\Artist\Album\test.mp3
            """;
        var fileContent = Encoding.UTF8.GetBytes(m3uContent);

        // Act
        var result = await service.ImportPlaylistAsync(
            user.Id,
            "windows.m3u",
            fileContent);

        // Assert
        AssertResultIsSuccessful(result);
        Assert.NotNull(result.Data);
        Assert.Equal(1, result.Data.TotalEntries);

        // Verify the reference was normalized
        var uploadedFile = await context.PlaylistUploadedFiles
            .Include(f => f.Items)
            .FirstOrDefaultAsync();
        Assert.NotNull(uploadedFile);
        var item = uploadedFile.Items.First();
        Assert.DoesNotContain('\\', item.NormalizedReference);
        Assert.Contains('/', item.NormalizedReference);
    }
}
