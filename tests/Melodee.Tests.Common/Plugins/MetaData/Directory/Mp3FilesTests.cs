using FluentAssertions;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Plugins.MetaData.Directory;

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
}
