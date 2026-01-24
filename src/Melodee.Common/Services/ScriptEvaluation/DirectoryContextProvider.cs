using Melodee.Common.Data.Models;
using Melodee.Common.Models;
using Melodee.Common.Models.Scripting;
using Serilog;

namespace Melodee.Common.Services.ScriptEvaluation;

public interface IDirectoryContextProvider
{
    DirectoryProcessingContext BuildContext(FileSystemDirectoryInfo directory, Library library);
}

public sealed class DirectoryContextProvider : IDirectoryContextProvider
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ILogger _logger;
    private readonly string[] _supportedExtensions =
    {
        ".mp3", ".flac", ".ogg", ".m4a", ".wav", ".aiff", ".aac", ".wma", ".opus"
    };

    public DirectoryContextProvider(
        IFileSystemService fileSystemService,
        ILogger logger)
    {
        _fileSystemService = fileSystemService;
        _logger = logger;
    }

    public DirectoryProcessingContext BuildContext(FileSystemDirectoryInfo directory, Library library)
    {
        var directoryInfo = new DirectoryInfo(directory.Path);
        if (!directoryInfo.Exists)
        {
            return CreateEmptyContext(directory, library);
        }

        var files = directoryInfo.GetFiles("*", SearchOption.TopDirectoryOnly);
        var totalSizeBytes = files.Sum(f => f.Length);
        var mostRecentModified = files.Length > 0
            ? files.Max(f => f.LastWriteTimeUtc).ToString("O")
            : DateTime.UtcNow.ToString("O");

        var mediaFilesCount = files.Count(f => IsMediaFile(f.Name));

        var relativePath = GetRelativePath(directory.Path, library.Path);

        return new DirectoryProcessingContext
        {
            LibraryId = library.Id,
            RelativePath = relativePath,
            DirectoryName = directory.Name,
            TotalFilesCount = files.Length,
            TotalSizeMegabytes = Math.Round(totalSizeBytes / (1024.0 * 1024.0), 2),
            MostRecentModified = mostRecentModified,
            MediaFilesCount = mediaFilesCount,
            TotalDurationMinutes = 0,
            TrackNumbers = [],
            HasTrackNumberGaps = false
        };
    }

    private static string GetRelativePath(string fullPath, string basePath)
    {
        if (!basePath.EndsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
        {
            basePath += System.IO.Path.DirectorySeparatorChar;
        }

        try
        {
            var baseUri = new Uri(basePath);
            var fullUri = new Uri(fullPath.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar);
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString()
                .Replace('/', System.IO.Path.DirectorySeparatorChar));
        }
        catch
        {
            return fullPath;
        }
    }

    private static DirectoryProcessingContext CreateEmptyContext(FileSystemDirectoryInfo directory, Library library)
    {
        var relativePath = GetRelativePath(directory.Path, library.Path);
        return new DirectoryProcessingContext
        {
            LibraryId = library.Id,
            RelativePath = relativePath,
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

    private bool IsMediaFile(string fileName)
    {
        var extension = System.IO.Path.GetExtension(fileName);
        return _supportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }
}
