using Melodee.Common.Data.Models;
using NodaTime;

namespace Melodee.Tests.Common.TestHelpers;

/// <summary>
/// Factory for creating test data entities with all required properties set.
/// </summary>
public static class TestDataFactory
{
    public static User CreateTestUser(string username = "testuser", string email = "test@melodee.net")
    {
        return new User
        {
            UserName = username,
            UserNameNormalized = username.ToUpperInvariant(),
            Email = email,
            EmailNormalized = email.ToUpperInvariant(),
            PublicKey = $"{username}_key",
            PasswordEncrypted = "encrypted_password",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    public static Artist CreateTestArtist(string name = "Test Artist", int libraryId = 1)
    {
        return new Artist
        {
            Name = name,
            NameNormalized = name.ToUpperInvariant(),
            Directory = $"/music/{name}",
            LibraryId = libraryId,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    public static Album CreateTestAlbum(Artist artist, string name = "Test Album")
    {
        return new Album
        {
            Name = name,
            NameNormalized = name.ToUpperInvariant(),
            Directory = $"{artist.Directory}/{name}",
            Artist = artist,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }

    public static Song CreateTestSong(
        Album album,
        string title = "Test Song",
        string filename = "test.mp3",
        int songNumber = 1)
    {
        return new Song
        {
            Title = title,
            TitleNormalized = title.ToUpperInvariant(),
            FileName = filename,
            ContentType = "audio/mpeg",
            Album = album,
            SongNumber = songNumber,
            FileSize = 1000 * songNumber,
            FileHash = $"hash{songNumber}",
            Duration = 180000.0,
            SamplingRate = 44100,
            BitRate = 320,
            BitDepth = 16,
            BPM = 120,
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };
    }
}
