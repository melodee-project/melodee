namespace Melodee.Common.Models.Scripting;

public record DirectoryProcessingContext
{
    public int LibraryId { get; init; }

    public string RelativePath { get; init; } = string.Empty;

    public string DirectoryName { get; init; } = string.Empty;

    public int TotalFilesCount { get; init; }

    public double TotalSizeMegabytes { get; init; }

    public string MostRecentModified { get; init; } = string.Empty;

    public int MediaFilesCount { get; init; }

    public double TotalDurationMinutes { get; init; }

    public int[] TrackNumbers { get; init; } = [];

    public bool HasTrackNumberGaps { get; init; }
}
