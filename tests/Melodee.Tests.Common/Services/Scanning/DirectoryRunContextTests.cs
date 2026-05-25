using Melodee.Common.Models.SearchEngines;
using Melodee.Common.Services.Scanning;

namespace Melodee.Tests.Common.Services.Scanning;

public sealed class DirectoryRunContextTests
{
    [Fact]
    public void NormalizeArtistKey_BasicName_NormalizesCorrectly()
    {
        var query = new ArtistQuery { Name = "The Beatles" };

        var key = DirectoryRunContext.NormalizeArtistKey(query);

        Assert.Contains("THEBEATLES", key);
        Assert.StartsWith("ARTIST:", key);
    }

    [Fact]
    public void NormalizeArtistKey_WithMusicBrainzId_IncludesInKey()
    {
        var query = new ArtistQuery
        {
            Name = "Test Artist",
            MusicBrainzId = "12345678-1234-1234-1234-123456789012"
        };

        var key = DirectoryRunContext.NormalizeArtistKey(query);

        Assert.Contains("MBID:12345678-1234-1234-1234-123456789012", key);
    }

    [Fact]
    public void NormalizeArtistKey_WithSpotifyId_IncludesInKey()
    {
        var query = new ArtistQuery
        {
            Name = "Test Artist",
            SpotifyId = "spotify123"
        };

        var key = DirectoryRunContext.NormalizeArtistKey(query);

        Assert.Contains("SPOTIFY:spotify123", key);
    }

    [Fact]
    public void NormalizeArtistKey_CaseInsensitive_SameKey()
    {
        var query1 = new ArtistQuery { Name = "The Beatles" };
        var query2 = new ArtistQuery { Name = "THE BEATLES" };
        var query3 = new ArtistQuery { Name = "the beatles" };

        var key1 = DirectoryRunContext.NormalizeArtistKey(query1);
        var key2 = DirectoryRunContext.NormalizeArtistKey(query2);
        var key3 = DirectoryRunContext.NormalizeArtistKey(query3);

        Assert.Equal(key1, key2);
        Assert.Equal(key2, key3);
    }

    [Fact]
    public void NormalizeArtistKey_WhitespaceTrimmed()
    {
        var query1 = new ArtistQuery { Name = "Test Artist" };
        var query2 = new ArtistQuery { Name = "  Test Artist  " };

        var key1 = DirectoryRunContext.NormalizeArtistKey(query1);
        var key2 = DirectoryRunContext.NormalizeArtistKey(query2);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void NormalizeAlbumImageKey_BasicAlbum_NormalizesCorrectly()
    {
        var query = new AlbumQuery
        {
            Artist = "The Beatles",
            Name = "Abbey Road",
            Year = 1969
        };

        var key = DirectoryRunContext.NormalizeAlbumImageKey(query);

        Assert.StartsWith("ALBUM:", key);
        Assert.Contains("THE BEATLES", key);
        Assert.Contains("ABBEY ROAD", key);
    }

    [Fact]
    public void NormalizeAlbumImageKey_WithYear_IncludesInKey()
    {
        var query = new AlbumQuery
        {
            Artist = "Pink Floyd",
            Name = "The Wall",
            Year = 1979
        };

        var key = DirectoryRunContext.NormalizeAlbumImageKey(query);

        Assert.Contains("1979", key);
    }

    [Fact]
    public void NormalizeAlbumImageKey_SameAlbumDifferentCase_SameKey()
    {
        var query1 = new AlbumQuery { Artist = "Pink Floyd", Name = "The Wall", Year = 1979 };
        var query2 = new AlbumQuery { Artist = "PINK FLOYD", Name = "THE WALL", Year = 1979 };

        var key1 = DirectoryRunContext.NormalizeAlbumImageKey(query1);
        var key2 = DirectoryRunContext.NormalizeAlbumImageKey(query2);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void NormalizeAlbumImageKey_DifferentAlbums_DifferentKeys()
    {
        var query1 = new AlbumQuery { Artist = "Pink Floyd", Name = "The Wall", Year = 1979 };
        var query2 = new AlbumQuery { Artist = "Pink Floyd", Name = "Wish You Were Here", Year = 1975 };

        var key1 = DirectoryRunContext.NormalizeAlbumImageKey(query1);
        var key2 = DirectoryRunContext.NormalizeAlbumImageKey(query2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void NormalizeAlbumImageKey_SameAlbumDifferentYears_DifferentKeys()
    {
        var query1 = new AlbumQuery { Artist = "Test", Name = "Album", Year = 2020 };
        var query2 = new AlbumQuery { Artist = "Test", Name = "Album", Year = 2021 };

        var key1 = DirectoryRunContext.NormalizeAlbumImageKey(query1);
        var key2 = DirectoryRunContext.NormalizeAlbumImageKey(query2);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void Constructor_CreatesValidCaches()
    {
        using var context = new DirectoryRunContext();

        Assert.NotNull(context.ArtistSearchCache);
        Assert.NotNull(context.ForcedArtistSearchCache);
        Assert.NotNull(context.AlbumImageCache);
        Assert.NotNull(context.ApiThrottler);
    }

    [Fact]
    public void ForcedArtistSearchCache_WhenNormalCacheHasNegativeResult_RemainsSeparate()
    {
        using var context = new DirectoryRunContext();
        var query = new ArtistQuery { Name = "Shared Artist" };

        context.ArtistSearchCache.AddNegative(query);
        var found = context.ForcedArtistSearchCache.TryGet(query, out _, out _);

        Assert.False(found);
    }

    [Fact]
    public void AddPluginTime_AccumulatesTime()
    {
        using var context = new DirectoryRunContext();

        context.AddPluginTime(100);
        context.AddPluginTime(50);
        context.AddPluginTime(25);

        var summary = context.GetPerformanceSummary();

        Assert.Equal(175, summary.PluginTimeMs);
    }

    [Fact]
    public void IncrementDirectoriesProcessed_IncrementsConcurrently()
    {
        using var context = new DirectoryRunContext();

        Parallel.For(0, 100, _ => context.IncrementDirectoriesProcessed());

        var summary = context.GetPerformanceSummary();

        Assert.Equal(100, summary.DirectoriesProcessed);
    }

    [Fact]
    public void GetPerformanceSummary_WithRecordedCounters_ReturnsSnapshot()
    {
        using var context = new DirectoryRunContext();

        context.AddConversionTime(125);
        context.AddCopyTime(75);
        context.AddEnrichmentTime(250);
        context.RecordArtistSearchPersistenceConflict();
        context.RecordArtistSearchPersistenceRetry();
        context.RecordArtistSearchPersistenceCorruption();
        context.RecordAlbumSkippedRevalidation();

        var summary = context.GetPerformanceSummary();

        Assert.Equal(125, summary.ConversionTimeMs);
        Assert.Equal(1, summary.ConversionFilesProcessed);
        Assert.Equal(75, summary.CopyTimeMs);
        Assert.Equal(250, summary.EnrichmentTimeMs);
        Assert.Equal(1, summary.ArtistSearchPersistenceConflicts);
        Assert.Equal(1, summary.ArtistSearchPersistenceRetries);
        Assert.Equal(1, summary.ArtistSearchPersistenceCorruptions);
        Assert.Equal(1, summary.AlbumsSkippedRevalidation);
    }
}
