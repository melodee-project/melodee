namespace Melodee.Common.Services.Models;

public record ProcessingEvent(
    ProcessingEventType Type,
    string EventName,
    int Max,
    int Current,
    string Message,
    long BytesProcessed = 0,
    long TotalBytes = 0,
    int BatchSize = 0,
    int BatchCurrent = 0,
    ProcessingEventStatistics? Statistics = null);

/// <summary>
/// Detailed statistics for move operations to provide clarity in TUI output.
/// </summary>
public record ProcessingEventStatistics(
    int TotalMelodeeFilesFound,
    int AlbumsReadyToMove,
    int AlbumsMoved,
    int AlbumsMergedWithExisting,
    int AlbumsSkippedByStatus,
    int AlbumsSkippedAsDuplicateDirectory,
    int AlbumsFailedToLoad,
    IReadOnlyDictionary<string, int>? AlbumsSkippedByReason = null);
