namespace Melodee.Common.Models.Scripting;

public record DirectoryProcessingContext
{
    public string Path { get; init; } = string.Empty;

    public string DirectoryName { get; init; } = string.Empty;

    public int TotalFilesCount { get; init; }

    public double TotalSizeMegabytes { get; init; }

    public string MostRecentModified { get; init; } = string.Empty;

    /// <summary>
    /// Count of media files with ID3 tags (mp3, flac, opus, etc.) that song plugins can process.
    /// </summary>
    public int MediaFilesCount { get; init; }

    public double TotalDurationMinutes { get; init; }

    public int[] TrackNumbers { get; init; } = [];

    public bool HasTrackNumberGaps { get; init; }
}
