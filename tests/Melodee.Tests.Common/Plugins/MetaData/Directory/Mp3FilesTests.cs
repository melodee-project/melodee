using FluentAssertions;
using Melodee.Common.Configuration;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Plugins.MetaData.Directory;
using Melodee.Common.Plugins.Validation;
using Melodee.Common.Serialization;
using Serilog;
using System.Reflection;
using Album = Melodee.Common.Models.Album;

namespace Melodee.Tests.Common.Plugins.MetaData;

public class Mp3FilesResolveArtistNameTests
{
    private static FileSystemDirectoryInfo DirectoryNamed(string name)
    {
        return new FileSystemDirectoryInfo { Path = name, Name = name };
    }

    private static List<MetaTag<object?>> AlbumTags(MetaTagIdentifier identifier, string? value)
    {
        return [new MetaTag<object?> { Identifier = identifier, Value = value }];
    }

    private static IEnumerable<Melodee.Common.Models.Song> SongsWithArtistTag(params string?[] artists)
    {
        var songs = new List<Melodee.Common.Models.Song>();
        foreach (var artist in artists)
        {
            songs.Add(new Melodee.Common.Models.Song
            {
                CrcHash = Guid.NewGuid().ToString("N"),
                File = new FileSystemFileInfo { Name = "track.mp3", Size = 1000 },
                Tags = [new MetaTag<object?> { Identifier = MetaTagIdentifier.Artist, Value = artist }]
            });
        }

        return songs;
    }

    [Fact]
    public void ResolveArtistName_FromAlbumArtistTag_ReturnsIt()
    {
        var songs = SongsWithArtistTag("The Cure").GroupBy(_ => (long?)0).Single();

        var result = Mp3Files.ResolveArtistName(
            AlbumTags(MetaTagIdentifier.AlbumArtist, "The Cure"),
            songs,
            DirectoryNamed("The Cure - The Head On The Door (2006)"));

        result.Should().Be("The Cure");
    }

    [Fact]
    public void ResolveArtistName_WhenAlbumArtistMissing_FallsBackToSongArtistTag()
    {
        // AlbumArtist tag is absent; only the per-song Artist (TPE1) is set.
        var songs = SongsWithArtistTag("ZZ Top").GroupBy(_ => (long?)0).Single();

        var result = Mp3Files.ResolveArtistName(
            AlbumTags(MetaTagIdentifier.Album, "Degüello (Remastered)"),
            songs,
            DirectoryNamed("ZZ Top - Degüello (1979)"));

        result.Should().Be("ZZ Top");
    }

    [Fact]
    public void ResolveArtistName_WhenAllTagsMissing_FallsBackToDirectoryArtistSegment()
    {
        // No AlbumArtist, no per-song Artist; derive from "Artist - Album" directory name.
        var songs = SongsWithArtistTag((string?)null).GroupBy(_ => (long?)0).Single();

        var result = Mp3Files.ResolveArtistName(
            AlbumTags(MetaTagIdentifier.Album, "OK Computer"),
            songs,
            DirectoryNamed("Radiohead - OK Computer (1997)"));

        result.Should().Be("Radiohead");
    }

    [Fact]
    public void ResolveArtistName_WhenNothingAvailable_ReturnsUnknownArtistInsteadOfThrowing()
    {
        // Previously this path threw "Invalid artist name" and aborted the entire directory.
        var songs = SongsWithArtistTag((string?)null).GroupBy(_ => (long?)0).Single();

        var result = Mp3Files.ResolveArtistName(
            AlbumTags(MetaTagIdentifier.Album, "Mystery"),
            songs,
            DirectoryNamed("[2026] Untitled"));

        result.Should().Be("Unknown Artist");
    }

    [Fact]
    public void ResolveArtistName_PrefersAlbumArtistOverSongArtist()
    {
        var songs = SongsWithArtistTag("Featured Artist").GroupBy(_ => (long?)0).Single();

        var result = Mp3Files.ResolveArtistName(
            AlbumTags(MetaTagIdentifier.AlbumArtist, "Main Artist"),
            songs,
            DirectoryNamed("Main Artist - Album"));

        result.Should().Be("Main Artist");
    }

    private static Mp3Files CreateMp3FilesService()
    {
        var serializer = new Serializer(Log.Logger);
        var config = new MelodeeConfiguration([]);
        var validator = new AlbumValidator(config);
        return new Mp3Files(
            [],
            validator,
            serializer,
            Log.Logger,
            config);
    }

    private static Melodee.Common.Models.Song CreateSongWithFile(string filePath, string crcHash)
    {
        // DuplicateHashCheck is computed from AlbumTitle, SongNumber, and Title — so songs with
        // matching tag values will be treated as duplicates by HandleDuplicates.
        var tags = new[]
        {
            new MetaTag<object?> { Identifier = MetaTagIdentifier.Album, Value = "Test Album" },
            new MetaTag<object?> { Identifier = MetaTagIdentifier.TrackNumber, Value = 1 },
            new MetaTag<object?> { Identifier = MetaTagIdentifier.Title, Value = "Track 01" }
        };

        return new Melodee.Common.Models.Song
        {
            Id = Guid.NewGuid(),
            CrcHash = crcHash,
            File = new FileSystemFileInfo
            {
                Name = Path.GetFileName(filePath),
                Size = 100
            },
            Tags = tags
        };
    }

    [Fact]
    public async Task HandleDuplicates_WithOpenDuplicateFile_DoesNotThrow()
    {
        // Arrange - create two real files with the same duplicate-hash tags so HandleDuplicates
        // tries to delete the non-best one. Keep the duplicate open to stress the delete path.
        // On Windows this forces File.Delete to throw (sharing violation); on Linux the file is
        // unlinked but the handle stays alive. Either way, HandleDuplicates must not throw.
        var tempDir = Path.Combine(Path.GetTempPath(), "Melodee_DupTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var bestFile = Path.Combine(tempDir, "best.mp3");
        var dupFile = Path.Combine(tempDir, "dup.mp3");
        File.WriteAllText(bestFile, "best");
        File.WriteAllText(dupFile, "duplicate");

        var dirInfo = new FileSystemDirectoryInfo { Path = tempDir, Name = Path.GetFileName(tempDir) };
        var service = CreateMp3FilesService();

        var bestSong = CreateSongWithFile(bestFile, "crc-best");
        var dupSong = CreateSongWithFile(dupFile, "crc-dup");
        var songs = new[] { bestSong, dupSong };

        var handleDuplicates = typeof(Mp3Files).GetMethod("HandleDuplicates",
            BindingFlags.NonPublic | BindingFlags.Instance);

        await using var lockStream = new FileStream(dupFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        try
        {
            // Act - should NOT throw regardless of platform-specific delete behavior
            var task = (Task?)handleDuplicates?.Invoke(service, [dirInfo, songs, CancellationToken.None]);
            task.Should().NotBeNull();
            await task!;

            // Assert - the method completed without throwing. The best file must remain.
            File.Exists(bestFile).Should().BeTrue();
        }
        finally
        {
            await lockStream.DisposeAsync();
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task HandleDuplicates_WithDeletableDuplicate_RemovesFileSuccessfully()
    {
        // Arrange - two files with same hash; the duplicate should be deleted and not throw.
        var tempDir = Path.Combine(Path.GetTempPath(), "Melodee_DupTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        var bestFile = Path.Combine(tempDir, "best.mp3");
        var dupFile = Path.Combine(tempDir, "dup.mp3");
        File.WriteAllText(bestFile, "best");
        File.WriteAllText(dupFile, "duplicate");

        var dirInfo = new FileSystemDirectoryInfo { Path = tempDir, Name = Path.GetFileName(tempDir) };
        var service = CreateMp3FilesService();

        var bestSong = CreateSongWithFile(bestFile, "crc-best");
        var dupSong = CreateSongWithFile(dupFile, "crc-dup");
        var songs = new[] { bestSong, dupSong };

        var handleDuplicates = typeof(Mp3Files).GetMethod("HandleDuplicates",
            BindingFlags.NonPublic | BindingFlags.Instance);

        try
        {
            // Act
            var task = (Task?)handleDuplicates?.Invoke(service, [dirInfo, songs, CancellationToken.None]);
            task.Should().NotBeNull();
            await task!;

            // Assert - the duplicate file is deleted, the best file remains
            File.Exists(dupFile).Should().BeFalse();
            File.Exists(bestFile).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
