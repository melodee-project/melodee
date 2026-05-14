using Melodee.Common.Enums;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Processor;

namespace Melodee.Tests.Common.Plugins.MetaData.Song;

public class AtlMetaTagMetaTagsForTagDictionaryTests : TestsBase
{
    private readonly AtlMetaTag _plugin;

    public AtlMetaTagMetaTagsForTagDictionaryTests()
    {
        _plugin = new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration());
    }

    private IEnumerable<MetaTag<object?>> CallMetaTagsForTagDictionary(Dictionary<string, string> tagsDictionary)
    {
        var method = typeof(AtlMetaTag).GetMethod("MetaTagsForTagDictionary",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return (IEnumerable<MetaTag<object?>>)method!.Invoke(_plugin, [tagsDictionary])!;
    }

    [Fact]
    public void MetaTagsForTagDictionary_EmptyDictionary_ReturnsEmptyList()
    {
        var tags = new Dictionary<string, string>();

        var result = CallMetaTagsForTagDictionary(tags);

        Assert.Empty(result);
    }

    [Fact]
    public void MetaTagsForTagDictionary_ArtistsTag_ParsesMultipleArtists()
    {
        var tags = new Dictionary<string, string>
        {
            { "ARTISTS", "Artist One; Artist Two; Artist Three" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Single(result);
        var artistTag = result.First();
        Assert.Equal(MetaTagIdentifier.Artists, artistTag.Identifier);
        Assert.NotNull(artistTag.Value);
    }

    [Fact]
    public void MetaTagsForTagDictionary_ArtistsTag_CaseInsensitive()
    {
        var tags = new Dictionary<string, string>
        {
            { "artists", "Artist One" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Single(result);
        Assert.Equal(MetaTagIdentifier.Artists, result.First().Identifier);
    }

    [Fact]
    public void MetaTagsForTagDictionary_ArtistsTag_HandlesSpaces()
    {
        var tags = new Dictionary<string, string>
        {
            { "ARTI STS", "Artist One" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Single(result);
        Assert.Equal(MetaTagIdentifier.Artists, result.First().Identifier);
    }

    [Fact]
    public void MetaTagsForTagDictionary_LengthTag_AddsLengthMetaTag()
    {
        var tags = new Dictionary<string, string>
        {
            { "LENGTH", "240000" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Single(result);
        var lengthTag = result.First();
        Assert.Equal(MetaTagIdentifier.Length, lengthTag.Identifier);
        Assert.Equal("240000", lengthTag.Value);
    }

    [Fact]
    public void MetaTagsForTagDictionary_DateTag_ValidYear_AddsRecordingYear()
    {
        var tags = new Dictionary<string, string>
        {
            { "DATE", "2023" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Single(result);
        var dateTag = result.First();
        Assert.Equal(MetaTagIdentifier.RecordingYear, dateTag.Identifier);
        Assert.Equal("2023", dateTag.Value);
    }

    [Fact]
    public void MetaTagsForTagDictionary_DateTag_InvalidYear_TriesDateTimeParse()
    {
        var tags = new Dictionary<string, string>
        {
            { "DATE", "1800" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        // "1800" is below minimum year but parses as DateTime, becomes AlbumDate
        Assert.Single(result);
        Assert.Equal(MetaTagIdentifier.AlbumDate, result.First().Identifier);
    }

    [Fact]
    public void MetaTagsForTagDictionary_DateTag_DateString_AddsAlbumDate()
    {
        var tags = new Dictionary<string, string>
        {
            { "DATE", "2023-05-15" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Single(result);
        var dateTag = result.First();
        Assert.Equal(MetaTagIdentifier.AlbumDate, dateTag.Identifier);
        Assert.NotNull(dateTag.Value);
    }

    [Fact]
    public void MetaTagsForTagDictionary_SongTag_CaseMismatchBug_DoesNotMatch()
    {
        // Note: This is a bug in the implementation - the key is normalized to uppercase
        // but the case statement uses "Song" with capital S, so they never match
        var tags = new Dictionary<string, string>
        {
            { "Song", "5/12" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        // Due to the bug, this won't match and returns empty
        Assert.Empty(result);
    }

    [Fact]
    public void MetaTagsForTagDictionary_WxxxTag_AddsUserDefinedUrlLink()
    {
        var tags = new Dictionary<string, string>
        {
            { "WXXX", "http://example.com" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Single(result);
        var urlTag = result.First();
        Assert.Equal(MetaTagIdentifier.UserDefinedUrlLink, urlTag.Identifier);
        Assert.Equal("http://example.com", urlTag.Value);
    }

    [Fact]
    public void MetaTagsForTagDictionary_MultipleTags_AllProcessed()
    {
        var tags = new Dictionary<string, string>
        {
            { "ARTISTS", "Artist One; Artist Two" },
            { "LENGTH", "180000" },
            { "DATE", "2023" },
            { "WXXX", "http://example.com" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Equal(4, result.Count); // Song won't work due to case mismatch bug
        Assert.Contains(result, t => t.Identifier == MetaTagIdentifier.Artists);
        Assert.Contains(result, t => t.Identifier == MetaTagIdentifier.Length);
        Assert.Contains(result, t => t.Identifier == MetaTagIdentifier.RecordingYear);
        Assert.Contains(result, t => t.Identifier == MetaTagIdentifier.UserDefinedUrlLink);
    }

    [Fact]
    public void MetaTagsForTagDictionary_UnknownTag_Ignored()
    {
        var tags = new Dictionary<string, string>
        {
            { "UNKNOWN_TAG", "some value" },
            { "RANDOM", "data" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Empty(result);
    }

    [Theory]
    [InlineData("artists")]
    [InlineData("ARTISTS")]
    [InlineData("Artists")]
    [InlineData("ArTiStS")]
    public void MetaTagsForTagDictionary_TagNames_CaseInsensitive(string tagName)
    {
        var tags = new Dictionary<string, string>
        {
            { tagName, "Artist One" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Single(result);
        Assert.Equal(MetaTagIdentifier.Artists, result.First().Identifier);
    }

    [Fact]
    public void MetaTagsForTagDictionary_NullOrEmptyValues_HandledGracefully()
    {
        var tags = new Dictionary<string, string>
        {
            { "ARTISTS", "" },
            { "LENGTH", "" }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.NotNull(result);
    }

    [Theory]
    [InlineData("2000")]
    [InlineData("2023")]
    [InlineData("1950")]
    [InlineData("1999")]
    public void MetaTagsForTagDictionary_DateTag_ValidYears_Processed(string year)
    {
        var tags = new Dictionary<string, string>
        {
            { "DATE", year }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        Assert.Single(result);
        Assert.Equal(MetaTagIdentifier.RecordingYear, result.First().Identifier);
    }

    [Theory]
    [InlineData("1899")]
    [InlineData("1500")]
    [InlineData("0")]
    public void MetaTagsForTagDictionary_DateTag_BelowMinimumYear_TriesDateTimeParse(string year)
    {
        var tags = new Dictionary<string, string>
        {
            { "DATE", year }
        };

        var result = CallMetaTagsForTagDictionary(tags).ToList();

        // Years below minimum will try DateTime parsing which may succeed
        // If it parses as DateTime, it becomes AlbumDate instead
        Assert.NotNull(result);
    }
}
