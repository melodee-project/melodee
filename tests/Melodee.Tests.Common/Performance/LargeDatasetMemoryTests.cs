using Melodee.Common.Models;
using Melodee.Tests.Common.Services;
using NodaTime;
using DataAlbum = Melodee.Common.Data.Models.Album;
using DataArtist = Melodee.Common.Data.Models.Artist;
using DataPlaylist = Melodee.Common.Data.Models.Playlist;
using DataPlaylistSong = Melodee.Common.Data.Models.PlaylistSong;
using DataSong = Melodee.Common.Data.Models.Song;
using DataUser = Melodee.Common.Data.Models.User;

namespace Melodee.Tests.Common.Performance;

public class LargeDatasetMemoryTests : ServiceTestBase
{
    [PerformanceFact]
    public async Task LoadPlaylistWithThousandsOfSongs_DoesNotExceedMemoryThreshold()
    {
        // Arrange: create one playlist with many songs
        const int songCount = 2500;
        await using var context = await MockFactory().CreateDbContextAsync();

        var user = new DataUser
        {
            UserName = "mem_user",
            UserNameNormalized = "MEM_USER",
            Email = "mem@example.com",
            EmailNormalized = "MEM@EXAMPLE.COM",
            PublicKey = "memkey",
            PasswordEncrypted = "encrypted",
            ApiKey = Guid.NewGuid(),
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            IsAdmin = false,
            IsLocked = false
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var artist = new DataArtist
        {
            ApiKey = Guid.NewGuid(),
            Name = "Mem Artist",
            NameNormalized = "MEM ARTIST",
            SortName = "Mem Artist",
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            Directory = "mem-artist",
            LibraryId = 3 // Storage default seeded
        };
        context.Artists.Add(artist);
        await context.SaveChangesAsync();

        var album = new DataAlbum
        {
            ApiKey = Guid.NewGuid(),
            ArtistId = artist.Id,
            Name = "Mem Album",
            NameNormalized = "MEM ALBUM",
            SortName = "Mem Album",
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            ReleaseDate = new LocalDate(2020, 1, 1),
            Directory = "mem-album"
        };
        context.Albums.Add(album);
        await context.SaveChangesAsync();

        var songs = new List<DataSong>(songCount);
        for (int i = 0; i < songCount; i++)
        {
            songs.Add(new DataSong
            {
                ApiKey = Guid.NewGuid(),
                AlbumId = album.Id,
                Title = $"Song {i}",
                TitleNormalized = $"SONG {i}",
                SongNumber = i + 1,
                FileName = $"song_{i}.mp3",
                FileSize = 1024,
                FileHash = Guid.NewGuid().ToString("N"),
                Duration = 60_000,
                SamplingRate = 44100,
                BitRate = 192,
                BitDepth = 16,
                BPM = 120,
                ContentType = "audio/mpeg",
                CreatedAt = SystemClock.Instance.GetCurrentInstant()
            });
        }
        context.Songs.AddRange(songs);
        await context.SaveChangesAsync();

        var playlist = new DataPlaylist
        {
            ApiKey = Guid.NewGuid(),
            Name = "Big Playlist",
            CreatedAt = SystemClock.Instance.GetCurrentInstant(),
            UserId = user.Id
        };
        context.Playlists.Add(playlist);
        await context.SaveChangesAsync();

        // Link songs to playlist
        var playlistSongs = songs.Select((s, i) => new DataPlaylistSong
        {
            PlaylistId = playlist.Id,
            SongId = s.Id,
            SongApiKey = s.ApiKey,
            PlaylistOrder = i
        });
        context.PlaylistSong.AddRange(playlistSongs);
        await context.SaveChangesAsync();

        var service = GetPlaylistService();
        var userInfo = new UserInfo(user.Id, user.ApiKey, user.UserName, user.Email, string.Empty, string.Empty);

        // Act: load a single page to ensure pagination prevents loading all songs
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var before = GC.GetTotalMemory(true);

        var page = await service.SongsForPlaylistAsync(playlist.ApiKey, userInfo, new PagedRequest { Page = 0, PageSize = 100 });

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var after = GC.GetTotalMemory(true);

        // Assert
        Assert.True(page.IsSuccess);
        Assert.Equal(songCount, page.TotalCount);
        Assert.True(page.Data.Count() <= 100);

        // Ensure memory delta stays bounded (heuristic; allows CI variance)
        var delta = after - before;
        Assert.True(delta < 64 * 1024 * 1024, $"Memory delta too large: {delta} bytes");
    }
}
