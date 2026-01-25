using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Plugins.MetaData.Song;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IDirectoryContextProvider
{
    Task<DirectoryProcessingContext> BuildContextAsync(
        FileSystemDirectoryInfo directory, 
        ISongPlugin[] songPlugins,
        CancellationToken cancellationToken = default);
}

public sealed class DirectoryContextProvider : IDirectoryContextProvider
{
    private readonly ILogger _logger;

    public DirectoryContextProvider(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<DirectoryProcessingContext> BuildContextAsync(
        FileSystemDirectoryInfo directory, 
        ISongPlugin[] songPlugins,
        CancellationToken cancellationToken = default)
    {
        var directoryInfo = new DirectoryInfo(directory.Path);
        if (!directoryInfo.Exists)
        {
            return CreateEmptyContext(directory);
        }

        var files = directoryInfo.GetFiles("*", SearchOption.TopDirectoryOnly);
        var totalSizeBytes = files.Sum(f => f.Length);
        var mostRecentModified = files.Length > 0
            ? files.Max(f => f.LastWriteTimeUtc).ToString("O")
            : DateTime.UtcNow.ToString("O");

        // Process media files using song plugins
        var totalDurationMs = 0.0;
        var trackNumbers = new List<int>();
        var mediaFilesCount = 0;

        foreach (var file in files)
        {
            var fileInfo = new FileSystemFileInfo
            {
                Name = file.Name,
                Size = file.Length
            };

            foreach (var plugin in songPlugins)
            {
                if (!plugin.DoesHandleFile(directory, fileInfo))
                {
                    continue;
                }

                try
                {
                    var result = await plugin.ProcessFileAsync(directory, fileInfo, cancellationToken);
                    if (result.IsSuccess && result.Data != null)
                    {
                        mediaFilesCount++;
                        var song = result.Data;
                        
                        var duration = song.Duration();
                        if (duration.HasValue && duration.Value > 0)
                        {
                            totalDurationMs += duration.Value;
                        }

                        var trackNumber = song.SongNumber();
                        if (trackNumber > 0)
                        {
                            trackNumbers.Add(trackNumber);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Failed to read metadata from {File}", file.FullName);
                }

                break; // Only use first matching plugin
            }
        }

        var sortedTrackNumbers = trackNumbers.OrderBy(x => x).ToArray();
        var hasTrackNumberGaps = CalculateHasTrackNumberGaps(sortedTrackNumbers);

        return new DirectoryProcessingContext
        {
            Path = directory.Path,
            DirectoryName = directory.Name,
            TotalFilesCount = files.Length,
            TotalSizeMegabytes = Math.Round(totalSizeBytes / (1024.0 * 1024.0), 2),
            MostRecentModified = mostRecentModified,
            MediaFilesCount = mediaFilesCount,
            TotalDurationMinutes = Math.Round(totalDurationMs / 60000.0, 2), // Convert ms to minutes
            TrackNumbers = sortedTrackNumbers,
            HasTrackNumberGaps = hasTrackNumberGaps
        };
    }

    private static bool CalculateHasTrackNumberGaps(int[] sortedTrackNumbers)
    {
        if (sortedTrackNumbers.Length < 2)
        {
            return false;
        }

        // Check if track numbers are sequential (allowing start from any number)
        for (var i = 1; i < sortedTrackNumbers.Length; i++)
        {
            if (sortedTrackNumbers[i] != sortedTrackNumbers[i - 1] + 1)
            {
                return true;
            }
        }

        return false;
    }

    private static DirectoryProcessingContext CreateEmptyContext(FileSystemDirectoryInfo directory)
    {
        return new DirectoryProcessingContext
        {
            Path = directory.Path,
            DirectoryName = directory.Name,
            TotalFilesCount = 0,
            TotalSizeMegabytes = 0,
            MostRecentModified = DateTime.UtcNow.ToString("O"),
            MediaFilesCount = 0,
            TotalDurationMinutes = 0,
            TrackNumbers = [],
            HasTrackNumberGaps = false
        };
    }
}
