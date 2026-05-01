using Melodee.Common.Models;
using Melodee.Common.Services;

namespace Melodee.Tests.Common.Services;

/// <summary>
/// Mock implementation of IFileSystemService for testing purposes.
/// This implementation provides safe, non-destructive operations that don't touch the actual file system.
/// </summary>
public class MockFileSystemService : IFileSystemService
{
    private readonly Dictionary<string, byte[]> _files = new();
    private readonly HashSet<string> _directories = [];
    private readonly Dictionary<string, Album> _albumsByFile = new();
    private readonly Dictionary<string, DateTime> _fileCreationTimes = new();

    public bool DirectoryExists(string path) => _directories.Contains(path) || path == "/" || path == "\\";

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption)
    {
        return _files.Keys.Where(f => f.StartsWith(path));
    }

    public IEnumerable<DirectoryInfo> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption)
    {
        return _directories
            .Where(d => d.StartsWith(path) && d != path)
            .Select(d => new DirectoryInfo(d));
    }

    public DateTime GetFileCreationTimeUtc(string filePath)
    {
        return _fileCreationTimes.TryGetValue(filePath, out var time) ? time : DateTime.UtcNow;
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        _directories.Remove(path);
        if (recursive)
        {
            var toRemove = _directories.Where(d => d.StartsWith(path)).ToList();
            foreach (var dir in toRemove)
            {
                _directories.Remove(dir);
            }
        }
    }

    public void DeleteDirectory(string root, string path, bool recursive)
    {
        DeleteDirectory(path, recursive);
    }

    public Task<Album?> DeserializeAlbumAsync(string filePath, CancellationToken cancellationToken)
    {
        _albumsByFile.TryGetValue(filePath, out var album);
        return Task.FromResult(album);
    }

    public string GetDirectoryName(string path) => Path.GetDirectoryName(path) ?? string.Empty;

    public string GetFileName(string path) => Path.GetFileName(path);

    public string CombinePath(params string[] paths) => Path.Combine(paths);

    public Task<byte[]> ReadAllBytesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        _files.TryGetValue(filePath, out var bytes);
        return Task.FromResult(bytes ?? []);
    }

    public Task WriteAllBytesAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
    {
        _files[filePath] = bytes;
        var directory = GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(directory);
        }
        return Task.CompletedTask;
    }

    public void CreateDirectory(string path)
    {
        _directories.Add(path);
    }

    public bool FileExists(string path) => _files.ContainsKey(path);

    public void DeleteFile(string path) => _files.Remove(path);

    public void DeleteFile(string root, string path) => _files.Remove(path);

    public void MoveDirectory(string sourcePath, string destinationPath)
    {
        if (_directories.Contains(sourcePath))
        {
            _directories.Remove(sourcePath);
            _directories.Add(destinationPath);
        }
    }

    public void MoveDirectory(string root, string sourcePath, string destinationPath)
    {
        MoveDirectory(sourcePath, destinationPath);
    }

    public string[] GetFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly)
    {
        return _files.Keys.Where(f => f.StartsWith(path)).ToArray();
    }

    // Fluent API methods for test setup
    public MockFileSystemService SetDirectoryExists(string path)
    {
        _directories.Add(path);
        return this;
    }

    public MockFileSystemService AddFilesToDirectory(string directoryPath, params string[] filePaths)
    {
        _directories.Add(directoryPath);
        foreach (var filePath in filePaths)
        {
            _files[filePath] = [];
            var directory = GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                _directories.Add(directory);
            }
        }
        return this;
    }

    public MockFileSystemService SetAlbumForFile(string filePath, Album album)
    {
        _albumsByFile[filePath] = album;
        _files[filePath] = []; // Ensure file exists
        var directory = GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(directory);
        }
        return this;
    }

    public MockFileSystemService AddFile(string filePath, byte[]? content = null)
    {
        _files[filePath] = content ?? [];
        var directory = GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            _directories.Add(directory);
        }
        return this;
    }

    public MockFileSystemService AddSubdirectory(string parentPath, string subdirectoryName)
    {
        var fullPath = CombinePath(parentPath, subdirectoryName);
        _directories.Add(fullPath);
        return this;
    }

    public MockFileSystemService SetFileCreationTime(string filePath, DateTime creationTime)
    {
        _fileCreationTimes[filePath] = creationTime;
        return this;
    }

    public void DeleteAllFilesForExtension(FileSystemDirectoryInfo directoryInfo, string searchPattern)
    {
        DeleteAllFilesForExtension(directoryInfo.Path, directoryInfo, searchPattern);
    }

    public void DeleteAllFilesForExtension(string root, FileSystemDirectoryInfo directoryInfo, string searchPattern)
    {
        var directoryPath = directoryInfo.Path;
        var extension = searchPattern.StartsWith("*.") ? searchPattern[1..] : searchPattern;
        var filesToRemove = _files.Keys
            .Where(f => f.StartsWith(directoryPath) && f.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var file in filesToRemove)
        {
            _files.Remove(file);
        }
    }

    /// <summary>
    /// Resets the mock file system to its initial empty state.
    /// </summary>
    public void Reset()
    {
        _files.Clear();
        _directories.Clear();
        _albumsByFile.Clear();
        _fileCreationTimes.Clear();
    }
}
