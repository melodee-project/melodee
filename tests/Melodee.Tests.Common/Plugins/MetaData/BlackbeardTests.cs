using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.MetaData.Directory.Blackbeard;

namespace Melodee.Tests.Common.Plugins.MetaData;

public class BlackbeardTests : TestsBase
{
    [Fact]
    public async Task ProcessDirectoryAsync_WithBlackbeardProvenance_CreatesAlbumMetadata()
    {
        var albumDirectory = Path.Combine(Path.GetTempPath(), $"melodee-blackbeard-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(albumDirectory);
            await File.WriteAllTextAsync(Path.Combine(albumDirectory, "01 - First Track.mp3"), "track-one");
            await File.WriteAllTextAsync(Path.Combine(albumDirectory, "02 - Second Track.mp3"), "track-two");
            await File.WriteAllTextAsync(
                Path.Combine(albumDirectory, Blackbeard.HandlesFileName),
                ProvenanceJson());

            var plugin = new Blackbeard(Serializer, GetAlbumValidator(), NewPluginsConfiguration());

            var result = await plugin.ProcessDirectoryAsync(new FileSystemDirectoryInfo
            {
                Path = albumDirectory,
                Name = Path.GetFileName(albumDirectory)
            });

            Assert.Equal(1, result.Data);

            var melodeeJson = Directory.GetFiles(albumDirectory, $"*{Album.JsonFileName}")
                .Single(x => Path.GetFileName(x) != Blackbeard.HandlesFileName);
            var album = await Album.DeserializeAndInitializeAlbumAsync(
                Serializer,
                melodeeJson);
            Assert.NotNull(album);
            Assert.Equal("Blackbeard Artist", album.Artist.Name);
            Assert.Equal("Blackbeard Album", album.AlbumTitle());
            Assert.Equal(2026, album.AlbumYear());
            Assert.Equal(2, album.SongTotalValue());

            var songs = album.Songs!.OrderBy(x => x.SortOrder).ToArray();
            Assert.Equal(2, songs.Length);
            Assert.Equal("First Track", songs[0].Title());
            Assert.Equal("Second Track", songs[1].Title());
            Assert.Equal(187000, songs[0].Duration());
            Assert.Equal(320, songs[0].BitRate());
            Assert.Equal(44100, songs[0].SamplingRate());
            Assert.Equal(2, songs[0].ChannelCount());
            Assert.Contains(album.Files, x => x.ProcessedByPlugin == nameof(Blackbeard));

            var serializedAlbum = Serializer.Serialize(album) ?? string.Empty;
            Assert.DoesNotContain("apikey", serializedAlbum, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("conversion_command", serializedAlbum, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("/mnt/incoming", serializedAlbum, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(albumDirectory))
            {
                Directory.Delete(albumDirectory, true);
            }
        }
    }

    [Fact]
    public async Task ProcessDirectoryAsync_WithUnsupportedSchemaVersion_ReturnsVisibleWarningAndSkips()
    {
        var albumDirectory = Path.Combine(Path.GetTempPath(), $"melodee-blackbeard-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(albumDirectory);
            await File.WriteAllTextAsync(Path.Combine(albumDirectory, "01 - First Track.mp3"), "track-one");
            await File.WriteAllTextAsync(
                Path.Combine(albumDirectory, Blackbeard.HandlesFileName),
                ProvenanceJson().Replace("\"schema_version\": 1", "\"schema_version\": 2", StringComparison.Ordinal));

            var plugin = new Blackbeard(Serializer, GetAlbumValidator(), NewPluginsConfiguration());

            var result = await plugin.ProcessDirectoryAsync(new FileSystemDirectoryInfo
            {
                Path = albumDirectory,
                Name = Path.GetFileName(albumDirectory)
            });

            Assert.Equal(0, result.Data);
            var message = Assert.Single(result.Messages ?? []);
            Assert.Contains("unsupported Blackbeard schema version [2]", message);
            Assert.Contains($"Supported version is [{Blackbeard.SupportedSchemaVersion}]", message);
            Assert.DoesNotContain(
                Directory.GetFiles(albumDirectory, $"*{Album.JsonFileName}"),
                x => Path.GetFileName(x) != Blackbeard.HandlesFileName);
        }
        finally
        {
            if (Directory.Exists(albumDirectory))
            {
                Directory.Delete(albumDirectory, true);
            }
        }
    }

    [Fact]
    public async Task AlbumForProvenanceFileAsync_WhenTrackPathEscapesDirectory_SkipsUnsafeTrack()
    {
        var albumDirectory = Path.Combine(Path.GetTempPath(), $"melodee-blackbeard-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(albumDirectory);
            await File.WriteAllTextAsync(Path.Combine(albumDirectory, "01 - First Track.mp3"), "track-one");
            var provenancePath = Path.Combine(albumDirectory, Blackbeard.HandlesFileName);
            await File.WriteAllTextAsync(provenancePath, ProvenanceJson("../outside.mp3"));

            var plugin = new Blackbeard(Serializer, GetAlbumValidator(), NewPluginsConfiguration());

            var album = await plugin.AlbumForProvenanceFileAsync(
                new FileInfo(provenancePath),
                new FileSystemDirectoryInfo
                {
                    Path = albumDirectory,
                    Name = Path.GetFileName(albumDirectory)
                });

            Assert.NotNull(album);
            var song = Assert.Single(album.Songs!);
            Assert.Equal("First Track", song.Title());
        }
        finally
        {
            if (Directory.Exists(albumDirectory))
            {
                Directory.Delete(albumDirectory, true);
            }
        }
    }

    private static string ProvenanceJson(string secondTrackPath = "02 - Second Track.mp3")
    {
        return $$"""
        {
          "schema_version": 1,
          "blackbeard_version": "0.1.0",
          "release": {
            "guid": "https://example.invalid/release",
            "canonical_release_key": "blackbeard artist|blackbeard album|2026",
            "album_artist": "Blackbeard Artist",
            "album_title": "Blackbeard Album",
            "release_year": 2026,
            "staged_path": "Blackbeard Artist - Blackbeard Album (2026)"
          },
          "tracks": [
            {
              "nzb_source": "https://example.invalid/api?t=get&apikey=secret",
              "staged_track_path": "01 - First Track.mp3",
              "track_number": 1,
              "track_total": 2,
              "disc_number": 1,
              "disc_total": 1,
              "title": "First Track",
              "artist": "Blackbeard Artist",
              "album_artist": "Blackbeard Artist",
              "album_title": "Blackbeard Album",
              "release_year": 2026,
              "source": {
                "path": "/mnt/incoming/blackbeard/downloads/source.flac"
              },
              "output": {
                "path": "01 - First Track.mp3",
                "conversion_command": "ffmpeg -i /mnt/incoming/blackbeard/downloads/source.flac output.mp3"
              },
              "normalization": {
                "tags_after": {
                  "title": "First Track",
                  "artist": "Blackbeard Artist",
                  "album_artist": "Blackbeard Artist",
                  "album_title": "Blackbeard Album",
                  "release_year": 2026,
                  "track_number": 1,
                  "track_total": 2,
                  "disc_number": 1,
                  "disc_total": 1,
                  "genres": ["Ambient"],
                  "codec": "mp3",
                  "container": "mp3",
                  "bitrate": 320,
                  "sample_rate": 44100,
                  "channels": 2,
                  "duration_ms": 187000
                }
              },
              "final_validation": {
                "passed": true,
                "classification": "completed"
              }
            },
            {
              "staged_track_path": "{{secondTrackPath}}",
              "track_number": 2,
              "track_total": 2,
              "disc_number": 1,
              "disc_total": 1,
              "title": "Second Track",
              "artist": "Blackbeard Artist",
              "album_artist": "Blackbeard Artist",
              "album_title": "Blackbeard Album",
              "release_year": 2026,
              "output": {
                "path": "{{secondTrackPath}}"
              },
              "normalization": {
                "tags_after": {
                  "title": "Second Track",
                  "artist": "Blackbeard Artist",
                  "album_artist": "Blackbeard Artist",
                  "album_title": "Blackbeard Album",
                  "release_year": 2026,
                  "track_number": 2,
                  "track_total": 2,
                  "disc_number": 1,
                  "disc_total": 1,
                  "genres": ["Ambient"],
                  "codec": "mp3",
                  "container": "mp3",
                  "bitrate": 256,
                  "sample_rate": 44100,
                  "channels": 2,
                  "duration_ms": 188000
                }
              },
              "final_validation": {
                "passed": true,
                "classification": "completed"
              }
            }
          ]
        }
        """;
    }
}
