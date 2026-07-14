using Melodee.Common.Enums;
using Melodee.Common.Metadata.AudioTags.Interfaces;
using Melodee.Common.Metadata.AudioTags.Models;
using Melodee.Common.Metadata.AudioTags.Readers;
using Serilog;

namespace Melodee.Common.Metadata.AudioTags;

public static class AudioTagManager
{
    public static Dictionary<MetaTagIdentifier, object> DefaultTags => new()
    {
        { MetaTagIdentifier.Title, string.Empty },
        { MetaTagIdentifier.Artist, string.Empty },
        { MetaTagIdentifier.Album, string.Empty },
        { MetaTagIdentifier.TrackNumber, 0 },
        { MetaTagIdentifier.RecordingYear, 0 },
        { MetaTagIdentifier.Genre, string.Empty },
        { MetaTagIdentifier.Comment, string.Empty }
    };

    public static async Task<IEnumerable<FileInfo>> AllMediaFilesForDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        var directoryInfo = new DirectoryInfo(directoryPath);
        if (!directoryInfo.Exists)
        {
            throw new DirectoryNotFoundException($"The directory '{directoryPath}' does not exist.");
        }

        // Using FileFormatDetector.DetectFormatAsync return all files in the directory where the detected format is not null
        var files = new List<FileInfo>();
        var allFiles = directoryInfo.EnumerateFiles("*.*", SearchOption.AllDirectories);
        foreach (var file in allFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                var format = await FileFormatDetector.DetectFormatAsync(file.FullName, cancellationToken);
                if (format != AudioFormat.Unknown)
                {
                    files.Add(file);
                }
            }
            catch (Exception ex)
            {
                // Skip files that cannot be read or do not match any known format, but log at
                // debug so corrupted-file investigations are not blind.
                Log.Logger.Debug(ex, "[{ServiceName}] Could not detect format for file [{File}]",
                    nameof(AudioTagManager), file.FullName);
            }
        }

        return files;
    }

    public static async Task<AudioTagData> ReadAllTagsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // Validate file path
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException($"The file '{filePath}' does not exist or is invalid.");
        }

        var format = await FileFormatDetector.DetectFormatAsync(filePath, cancellationToken);
        ITagReader? reader = format switch
        {
            AudioFormat.MP3 => new Id3TagReader(),
            AudioFormat.APE => new ApeTagReader(),
            AudioFormat.MP4 => new Mp4TagReader(),
            AudioFormat.WMA => new WmaTagReader(),
            AudioFormat.Vorbis => new VorbisTagReader(),
            _ => null
        };
        if (reader == null)
        {
            throw new NotSupportedException($"Unsupported or unknown audio format for file: {filePath}");
        }

        var tags = await reader.ReadTagsAsync(filePath, cancellationToken);

        // Ensure all default tags are present in the tag's dictionary
        foreach (var defaultTag in DefaultTags)
        {
            if (!tags.ContainsKey(defaultTag.Key))
            {
                tags[defaultTag.Key] = defaultTag.Value;
            }
        }

        var images = await reader.ReadImagesAsync(filePath, cancellationToken);
        var metadata = await FileMetadataReader.GetFileMetadataAsync(filePath);
        return new AudioTagData
        {
            Format = format,
            Tags = new Dictionary<MetaTagIdentifier, object>(tags),
            Images = images,
            FileMetadata = metadata
        };
    }

    /// <summary>
    ///     Determines whether the given file needs to be converted to MP3 format.
    /// </summary>
    /// <param name="fileInfo">The file to check</param>
    /// <param name="cancellationToken">Optional cancellation token</param>
    /// <returns>
    ///     True if the file is in a format other than MP3 and needs conversion, false if it's already an MP3, 
    ///     a video file, or not a valid audio file
    /// </returns>
    public static async Task<bool> NeedsConversionToMp3Async(FileInfo? fileInfo, CancellationToken cancellationToken = default)
    {
        // Check for null fileInfo to prevent NullReferenceException
        if ((!fileInfo?.Exists ?? false) || fileInfo?.Length == 0)
        {
            // If the file doesn't exist or is empty, it can't be converted
            return false;
        }

        try
        {
            // Check if the file is a video file - we don't want to convert video files
            if (await VideoFormatDetector.IsVideoFileAsync(fileInfo!.FullName, cancellationToken))
            {
                // This is a video file, so we shouldn't convert it to MP3
                return false;
            }

            // First check if the file extension is already .mp3
            if (fileInfo!.Extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            {
                // Verify it's actually a valid MP3 file
                var format = await FileFormatDetector.DetectFormatAsync(fileInfo.FullName, cancellationToken);
                return format != AudioFormat.MP3;
            }

            // For non-MP3 extensions, check if it's a valid audio format that can be converted
            var detectedFormat = await FileFormatDetector.DetectFormatAsync(fileInfo.FullName, cancellationToken);

            // Return true if it's a known audio format that isn't MP3
            return detectedFormat != AudioFormat.Unknown &&
                   detectedFormat != AudioFormat.MP3;
        }
        catch (Exception ex)
        {
            // If there's an error reading the file, assume it can't be converted. Log at debug
            // so the failure is traceable during conversion diagnostics.
            Log.Logger.Debug(ex, "[{ServiceName}] Could not determine conversion need for [{File}]",
                nameof(AudioTagManager), fileInfo?.FullName ?? "(null)");
            return false;
        }
    }
}
