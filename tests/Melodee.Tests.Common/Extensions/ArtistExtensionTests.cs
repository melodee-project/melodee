using Melodee.Common.Extensions;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Utility;

namespace Melodee.Tests.Common.Extensions;

public class ArtistExtensionTests
{
    [Theory]
    [InlineData("Bob Jones", false)]
    [InlineData("Bob Various", false)]
    [InlineData("Various Bob", false)]
    [InlineData("VA", true)]
    [InlineData("[VA]", true)]
    [InlineData("various artists", true)]
    [InlineData("Various Artists", true)]
    [InlineData("[Various Artists]", true)]
    [InlineData("VARIOUS ARTISTS", true)]
    public void ValidateIsVariousArtists(string input, bool shouldBe)
    {
        Assert.Equal(shouldBe, Artist.NewArtistFromName(input).IsVariousArtist());
    }

    [Theory]
    [InlineData("Bob Jones", false)]
    [InlineData("Bob Cast", false)]
    [InlineData("Song Bob", false)]
    [InlineData("Sound Bob", false)]
    [InlineData("Original Cast", true)]
    [InlineData("Original Broadway Cast", true)]
    public void ValidateIsCastRecordSongArtists(string input, bool shouldBe)
    {
        Assert.Equal(shouldBe, Artist.NewArtistFromName(input).IsCastRecording());
    }

    [Fact]
    public void IsValid_WithItunesId_ReturnsTrue()
    {
        var artistName = "iTunes Artist";
        var artist = new Artist(
            artistName,
            artistName.ToNormalizedString()!,
            artistName)
        {
            ItunesId = "123456789"
        };

        Assert.True(artist.IsValid());
    }

    [Fact]
    public void IsValid_WithNameButNoTrustedIdentifier_ReturnsFalse()
    {
        var artistName = "Unknown Artist";
        var artist = new Artist(
            artistName,
            artistName.ToNormalizedString()!,
            artistName);

        Assert.False(artist.IsValid());
    }

    [Fact]
    public void ToDirectoryName_WithItunesIdOnly_ReturnsDirectoryName()
    {
        var artistName = "iTunes Artist";
        var artist = new Artist(
            artistName,
            artistName.ToNormalizedString()!,
            artistName)
        {
            ItunesId = "123456789"
        };

        var directoryName = artist.ToDirectoryName(255);

        Assert.Contains(SafeParser.Hash(artist.ItunesId).ToString(), directoryName);
    }
}
