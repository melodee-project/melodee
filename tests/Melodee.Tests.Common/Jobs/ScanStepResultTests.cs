using Melodee.Common.Jobs;

namespace Melodee.Tests.Common.Jobs;

public class ScanStepResultTests
{
    [Fact]
    public void AlbumsHandledByStorageTransfer_WithMovedAndMergedAlbums_ReturnsCombinedCount()
    {
        var result = new ScanStepResult(
            AlbumsMoved: 4,
            AlbumsMergedWithExisting: 5);

        Assert.Equal(9, result.AlbumsHandledByStorageTransfer);
    }

    [Fact]
    public void Constructor_WithSkippedReasonCounts_SetsReasonCounts()
    {
        var skippedReasons = new Dictionary<string, int>
        {
            ["HasInvalidArtists"] = 10,
            ["HasInvalidArtists, HasNoImages"] = 2
        };

        var result = new ScanStepResult(
            AlbumsSkippedByStatus: 12,
            AlbumsSkippedByReason: skippedReasons);

        Assert.Equal(12, result.AlbumsSkippedByStatus);
        Assert.Equal(10, result.AlbumsSkippedByReason!["HasInvalidArtists"]);
        Assert.Equal(2, result.AlbumsSkippedByReason["HasInvalidArtists, HasNoImages"]);
    }
}
