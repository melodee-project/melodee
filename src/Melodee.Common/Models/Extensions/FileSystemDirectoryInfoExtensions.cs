using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Extensions;
using Melodee.Common.Utility;
using NodaTime;
using Serilog;
using SearchOption = System.IO.SearchOption;

namespace Melodee.Common.Models.Extensions;

public static class FileSystemDirectoryInfoExtensions
{
    public static readonly Regex IsDirectoryNotStudioAlbumsRegex =
        new(
            @"(single(s)*|\s?best\s?of|greatest(s*)\s?hit(s*)|compilation(s*)|live|boxset(s*)|bootleg(s*)|promo(s*)|demo(s*))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly Regex IsDirectoryDiscographyRegex =
        new("(discography)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static readonly Regex IsDirectoryAlbumMediaDirectoryRegex =
        new(@"^(cd|disc|disk|side|media|a|b|c|d|e|f){1,}\s*([0-9]+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static async Task<KeyValuePair<string, List<FileInfo>>[]> FindDuplicatesAsync(
        this FileSystemDirectoryInfo directory, string searchPattern = "*.*",
        CancellationToken cancellationToken = default)
    {
        var fileGroups = new ConcurrentDictionary<long, ConcurrentDictionary<string, List<FileInfo>>>();
        var files = Directory.GetFiles(directory.FullName(), searchPattern, SearchOption.AllDirectories);

        await Parallel.ForEachAsync(files, cancellationToken, async (filePath, token) =>
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length;

                // Group by file size first
                var sizeGroup = fileGroups.GetOrAdd(fileSize, _ => new ConcurrentDictionary<string, List<FileInfo>>());

                // Only calculate hash if there's another file of the same size
                if (sizeGroup.Count > 0)
                {
                    var hash = await CalculateFileHashAsync(filePath, token);
                    sizeGroup.AddOrUpdate(
                        hash,
                        key => [fileInfo],
                        (key, existingList) =>
                        {
                            lock (existingList)
                            {
                                // This file is duplicate and we want to delete
                                existingList.Add(fileInfo);
                                return existingList;
                            }
                        });
                }
                else
                {
                    // This is the file from the duplicates we want to keep
                    sizeGroup.TryAdd("pending", [fileInfo]);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Console.WriteLine($"Error processing file {filePath}: {ex.Message}");
            }
        });

        // Combine all duplicates across size groups
        var result = fileGroups
            .Where(g => g.Value.Count > 1)
            .SelectMany(x => x.Value)
            .Where(x => !x.Key.Equals("pending")).ToArray();

        return result;
    }

    private static async Task<string> CalculateFileHashAsync(string filePath, CancellationToken cancellationToken)
    {
        using var sha256 = SHA256.Create();
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await sha256.ComputeHashAsync(stream, cancellationToken);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    public static long FileCount(this FileSystemDirectoryInfo directory)
    {
        if (!directory.Exists())
        {
            return 0;
        }

        return directory.AllFileInfos(searchOption: SearchOption.AllDirectories).LongCount();
    }

    public static void Empty(this FileSystemDirectoryInfo directory)
    {
        foreach (var file in directory.AllFileInfos(searchOption: SearchOption.AllDirectories))
        {
            file.Delete();
        }

        foreach (var subDirectory in directory.AllDirectoryInfos(searchOption: SearchOption.AllDirectories))
        {
            subDirectory.Delete(true);
        }
    }

    public static bool DoesDirectoryHaveImageFiles(this FileSystemDirectoryInfo directory)
    {
        return directory.Exists() && directory.AllFileInfos("*.*", SearchOption.AllDirectories)
            .Any(x => FileHelper.IsFileImageType(x.Extension));
    }

    public static bool DoesDirectoryHaveMediaFiles(this FileSystemDirectoryInfo directory)
    {
        return directory.Exists() && directory.AllFileInfos("*.*", SearchOption.AllDirectories)
            .Any(x => FileHelper.IsFileMediaType(x.Extension));
    }

    /// <summary>
    ///     Rename the Directory and prepend the given prefix.
    /// </summary>
    public static FileSystemDirectoryInfo AppendPrefix(this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        string prefix)
    {
        var d = new DirectoryInfo(fileSystemDirectoryInfo.Path);
        if (d.Name.StartsWith(prefix))
        {
            return fileSystemDirectoryInfo;
        }

        var newName = $"{prefix}{d.Name}";
        var moveTo = Path.Combine(d.Parent!.FullName, newName);
        if (Directory.Exists(moveTo))
        {
            moveTo.ToFileSystemDirectoryInfo().MoveToDirectory($"{moveTo}-{DateTime.UtcNow.Ticks.ToString()}");
        }

        d.ToDirectorySystemInfo().MoveToDirectory(moveTo);
        return new FileSystemDirectoryInfo
        {
            Path = moveTo,
            Name = newName
        };
    }

    public static DirectoryInfo ToDirectoryInfo(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        return new DirectoryInfo(fileSystemDirectoryInfo.FullName());
    }

    public static string FullName(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        if (fileSystemDirectoryInfo.Path.Nullify() == null)
        {
            throw new ArgumentNullException(nameof(fileSystemDirectoryInfo.Path), "Path cannot be null");
        }
        var p = fileSystemDirectoryInfo.Path;
        if (p.LastOrDefault() == Path.DirectorySeparatorChar)
        {
            p = p[..^1];
        }

        if (p.EndsWith($"{Path.DirectorySeparatorChar}{fileSystemDirectoryInfo.Name}"))
        {
            return fileSystemDirectoryInfo.Path;
        }

        return Path.Combine(fileSystemDirectoryInfo.Path, fileSystemDirectoryInfo.Name);
    }

    public static void EnsureExists(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        if (!fileSystemDirectoryInfo.Exists())
        {
            Directory.CreateDirectory(fileSystemDirectoryInfo.FullName());
        }
    }

    public static bool Exists(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        return Directory.Exists(fileSystemDirectoryInfo.FullName());
    }

    public static void Delete(this FileSystemDirectoryInfo fileSystemDirectoryInfo, bool? recursive = null)
    {
        Directory.Delete(fileSystemDirectoryInfo.FullName(), recursive ?? true);
    }

    public static IEnumerable<FileInfo> MelodeeJsonFiles(this FileSystemDirectoryInfo fileSystemDirectoryInfo, bool? recursive = null)
    {
        var dirInfo = new DirectoryInfo(fileSystemDirectoryInfo.FullName());
        if (!dirInfo.Exists)
        {
            return [];
        }

        return dirInfo.GetFiles($"*{Album.JsonFileName}", new EnumerationOptions
        {
            RecurseSubdirectories = recursive ?? true,
            MatchCasing = MatchCasing.CaseInsensitive
        }).OrderBy(x => x.Name).ToArray();
    }

    public static IEnumerable<FileInfo> FileInfosForExtension(this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        string extension,
        bool? recursive = null)
    {
        var dirInfo = new DirectoryInfo(fileSystemDirectoryInfo.FullName());
        if (!dirInfo.Exists)
        {
            return [];
        }

        var searchPattern = extension;
        if (!extension.Contains('.'))
        {
            searchPattern = $"*.{extension}";
        }

        return dirInfo.GetFiles(searchPattern, new EnumerationOptions
        {
            RecurseSubdirectories = recursive ?? true,
            MatchCasing = MatchCasing.CaseInsensitive
        }).OrderBy(x => x.Name).ToArray();
    }

    public static FileSystemFileInfo? GetFileForCrcHash(this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        string extension, string crcHash)
    {
        foreach (var fileInfoForExtension in fileSystemDirectoryInfo.FileInfosForExtension(extension))
        {
            if (Crc32.Calculate(fileInfoForExtension) == crcHash)
            {
                return fileInfoForExtension.ToFileSystemInfo();
            }
        }

        return null;
    }

    public static bool IsDiscographyDirectory(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        var dir = fileSystemDirectoryInfo.Name;
        return !string.IsNullOrWhiteSpace(dir) && IsDirectoryDiscographyRegex.IsMatch(dir);
    }

    /// <summary>
    ///     Is the directory a directory that holds a media (e.g. 'CD01' or 'DISCA')
    /// </summary>
    public static bool IsAlbumMediaDirectory(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        var dir = fileSystemDirectoryInfo.Name;
        return !string.IsNullOrWhiteSpace(dir) && IsDirectoryAlbumMediaDirectoryRegex.IsMatch(dir);
    }

    public static int? TryParseMediaNumber(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        var dir = fileSystemDirectoryInfo.Name;
        return string.IsNullOrWhiteSpace(dir) ? null : dir.ExtractNumber();
    }

    public static IEnumerable<FileSystemDirectoryInfo> AllAlbumMediaDirectories(
        this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        var result = new List<FileSystemDirectoryInfo>();
        foreach (var dirInfo in fileSystemDirectoryInfo.AllDirectoryInfos())
        {
            if (dirInfo.ToDirectorySystemInfo().IsAlbumMediaDirectory())
            {
                result.Add(dirInfo.ToDirectorySystemInfo());
            }
        }

        return result.ToArray();
    }

    public static FileSystemDirectoryInfo GetParent(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        return fileSystemDirectoryInfo.GetParents().First();
    }

    public static IEnumerable<FileSystemDirectoryInfo> GetParents(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        var results = new List<FileSystemDirectoryInfo>();
        var dirInfo = fileSystemDirectoryInfo.ToDirectoryInfo();
        var parent = dirInfo.Parent;
        while (parent != null)
        {
            results.Add(parent.ToDirectorySystemInfo());
            parent = parent.Parent;
        }

        return results.ToArray();
    }

    public static IEnumerable<FileSystemDirectoryInfo> GetFileSystemDirectoryInfosToProcess(
        this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Instant? modifiedSince,
        SearchOption searchOption)
    {
        if (string.IsNullOrWhiteSpace(fileSystemDirectoryInfo.Path))
        {
            yield break;
        }

        var dirInfo = new DirectoryInfo(fileSystemDirectoryInfo.Path);
        if (!dirInfo.Exists)
        {
            yield break;
        }

        var modifiedSinceValue = modifiedSince?.ToDateTimeUtc() ?? DateTime.MinValue;

        // Use streaming enumeration to avoid loading all directories into memory at once
        foreach (var dir in dirInfo.EnumerateDirectories("*.*", searchOption))
        {
            // Include directories with any files (not just media files) so that 
            // script-based deletion can evaluate and clean up non-media directories
            var hasFiles = dir.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly).Any();
            var hasMediaFiles = hasFiles && dir.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly)
                .Any(x => FileHelper.IsFileMediaType(x.Extension));
            
            if (dir.LastWriteTimeUtc >= modifiedSinceValue && (hasMediaFiles || hasFiles))
            {
                var fsDir = dir.ToFileSystemDirectoryInfo();
                if (fsDir != null)
                {
                    yield return fsDir;
                }
            }
        }

        // Check if the root directory itself has files (media or non-media)
        var rootHasFiles = dirInfo.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly).Any();
        var rootHasMediaFiles = rootHasFiles && dirInfo.EnumerateFiles("*.*", SearchOption.TopDirectoryOnly)
            .Any(x => x.LastWriteTimeUtc >= modifiedSinceValue && FileHelper.IsFileMediaType(x.Extension));
        
        if (rootHasMediaFiles || (rootHasFiles && dirInfo.LastWriteTimeUtc >= modifiedSinceValue))
        {
            yield return fileSystemDirectoryInfo;
        }
    }

    public static (string, int) GetNextFileNameForType(this FileSystemDirectoryInfo fileSystemDirectoryInfo, string imageType)
    {
        var highestNumberFound = 0;
        var allImagesInDirectory = fileSystemDirectoryInfo.AllFileImageTypeFileInfos().ToArray();
        if (allImagesInDirectory.Length != 0)
        {
            foreach (var image in allImagesInDirectory)
            {
                if (image.Name.EndsWith($"{imageType}.jpg", StringComparison.OrdinalIgnoreCase))
                {
                    var number = SafeParser.ToNumber<short>(image.Name.ToNumberOnly());
                    if (number > highestNumberFound)
                    {
                        highestNumberFound = number;
                    }
                }
            }
        }

        highestNumberFound++;
        return (Path.Combine(fileSystemDirectoryInfo.Path,
                $"{ImageInfo.ImageFilePrefix}{highestNumberFound.ToStringPadLeft(MelodeeConfiguration.ImageNameNumberPadding)}-{imageType}.jpg"),
            highestNumberFound);
    }

    public static IEnumerable<FileInfo> AllFileImageTypeFileInfos(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        return fileSystemDirectoryInfo.AllFileInfos().Where(fileInfo => FileHelper.IsFileImageType(fileInfo.Extension));
    }

    public static IEnumerable<FileInfo> AllMediaTypeFileInfos(this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        SearchOption? searchOption = null)
    {
        return fileSystemDirectoryInfo.AllFileInfos(searchOption: searchOption)
            .Where(fileInfo => FileHelper.IsFileMediaType(fileInfo.Extension));
    }

    public static IEnumerable<FileInfo> AllFileInfos(this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        string? searchPattern = null, SearchOption? searchOption = null)
    {
        var dirInfo = new DirectoryInfo(fileSystemDirectoryInfo.FullName());
        if (!dirInfo.Exists)
        {
            return [];
        }
        return dirInfo.EnumerateFiles(searchPattern ?? "*.*", searchOption ?? SearchOption.TopDirectoryOnly);
    }

    public static FileSystemDirectoryInfo? Parent(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        var dirInfo = new DirectoryInfo(fileSystemDirectoryInfo.FullName());
        return dirInfo.Parent?.ToDirectorySystemInfo();
    }

    public static IEnumerable<DirectoryInfo> AllDirectoryInfos(this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        string? searchPattern = null, SearchOption? searchOption = null)
    {
        var dirInfo = new DirectoryInfo(fileSystemDirectoryInfo.Path);
        return !dirInfo.Exists ? [] : dirInfo.EnumerateDirectories(searchPattern ?? "*.*", searchOption ?? SearchOption.TopDirectoryOnly);
    }

    public static void DeleteAllEmptyDirectories(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        foreach (var directory in fileSystemDirectoryInfo.AllDirectoryInfos())
        {
            DeleteEmptyDirs(directory.FullName);
        }
    }

    private static void DeleteEmptyDirs(string dir)
    {
        if (string.IsNullOrEmpty(dir))
        {
            throw new ArgumentException("Starting directory is a null reference or an empty string", nameof(dir));
        }

        if (!Directory.Exists(dir))
        {
            return;
        }

        try
        {
            foreach (var d in Directory.EnumerateDirectories(dir))
            {
                DeleteEmptyDirs(d);
            }

            if (Directory.EnumerateFileSystemEntries(dir).Any())
            {
                return;
            }

            try
            {
                Directory.Delete(dir);
                Log.Debug("\ud83d\udeae Deleted Empty Directory [{dir}]", dir);
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (DirectoryNotFoundException)
            {
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static void DeleteAllFilesForExtension(this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        string extension)
    {
        var filesToDelete = fileSystemDirectoryInfo.FileInfosForExtension(extension);
        foreach (var fileToDelete in filesToDelete)
        {
            fileToDelete.Delete();
        }
    }

    public static void MarkAllFilesForExtensionsProcessed(this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        Dictionary<string, object?> configuration, params string[] extensions)
    {
        ChangeFileExtensions(fileSystemDirectoryInfo,
            SafeParser.ToString(configuration[SettingRegistry.ProcessingProcessedExtension]), extensions);
    }

    private static void ChangeFileExtensions(this FileSystemDirectoryInfo fileSystemDirectoryInfo, string newExtension,
        params string[] extensions)
    {
        if (newExtension.Nullify() == null)
        {
            return;
        }

        foreach (var extension in extensions)
        {
            var filesToMarkProcessed = fileSystemDirectoryInfo.FileInfosForExtension(extension);
            foreach (var fileToDelete in filesToMarkProcessed)
            {
                var moveToFileName = Path.Combine(fileToDelete.DirectoryName!, $"{fileToDelete.Name}.{newExtension}");
                fileToDelete.MoveTo(moveToFileName);
            }
        }
    }

    /// <summary>
    ///     This is false then the name of the directory has fragments that test for non studio albums (e.g. 'live',
    ///     'compilation')
    /// </summary>
    /// <param name="fileSystemDirectoryInfo"></param>
    /// <returns></returns>
    public static bool IsDirectoryStudioAlbums(this FileSystemDirectoryInfo fileSystemDirectoryInfo)
    {
        var dir = fileSystemDirectoryInfo.FullName();
        return !string.IsNullOrWhiteSpace(dir) && !IsDirectoryNotStudioAlbumsRegex.IsMatch(dir);
    }

    /// <summary>
    ///     This exists because in some systems where data is on one mapped drive it cannot be "Moved" to another mapped drive
    ///     ("Cross link" error), it must be copied and then deleted.
    /// </summary>
    public static void MoveToDirectory(this FileSystemDirectoryInfo fileSystemDirectoryInfo,
        string destination,
        string? dontMoveFileName = null,
        bool? isNestedDirectory = false)
    {
        if (!Directory.Exists(destination))
        {
            Directory.CreateDirectory(destination);
        }

        var toMove = fileSystemDirectoryInfo.FullName();
        var files = Directory.GetFiles(toMove);
        var directories = Directory.GetDirectories(toMove);
        foreach (var file in files)
        {
            if (!string.Equals(Path.GetFileName(file), dontMoveFileName, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
                }
                catch (PathTooLongException)
                {
                    Trace.WriteLine($"Aborting move. Unable to move file, path is too long. [{file}]");
                    return;
                }
            }
        }

        foreach (var d in directories)
        {
            var dd = Path.Combine(toMove, Path.GetFileName(d)).ToFileSystemDirectoryInfo();
            dd.MoveToDirectory(Path.Combine(destination, Path.GetFileName(d)), dontMoveFileName, true);
        }

        if (!(isNestedDirectory ?? false))
        {
            Directory.Delete(toMove, true);
            var dirInfo = new DirectoryInfo(destination);
            fileSystemDirectoryInfo.Path = destination;
            fileSystemDirectoryInfo.Name = dirInfo.Name;
        }
    }
}
