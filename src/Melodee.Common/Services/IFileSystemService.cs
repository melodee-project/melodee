using Melodee.Common.Models;

namespace Melodee.Common.Services;

public interface IFileSystemService
{
    bool DirectoryExists(string path);
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
    IEnumerable<DirectoryInfo> EnumerateDirectories(string path, string searchPattern, SearchOption searchOption);
    DateTime GetFileCreationTimeUtc(string filePath);
    void DeleteDirectory(string path, bool recursive);
    void DeleteDirectory(string root, string path, bool recursive);
    Task<Album?> DeserializeAlbumAsync(string filePath, CancellationToken cancellationToken);
    string GetDirectoryName(string path);
    string GetFileName(string path);
    string CombinePath(params string[] paths);

    // Additional methods needed by ArtistService
    Task<byte[]> ReadAllBytesAsync(string filePath, CancellationToken cancellationToken = default);
    Task WriteAllBytesAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default);
    void CreateDirectory(string path);
    bool FileExists(string path);
    void DeleteFile(string path);
    void DeleteFile(string root, string path);
    void MoveDirectory(string sourcePath, string destinationPath);
    void MoveDirectory(string root, string sourcePath, string destinationPath);
    string[] GetFiles(string path, string searchPattern = "*", SearchOption searchOption = SearchOption.TopDirectoryOnly);
    void DeleteAllFilesForExtension(FileSystemDirectoryInfo directoryInfo, string jpg);
    void DeleteAllFilesForExtension(string root, FileSystemDirectoryInfo directoryInfo, string jpg);
}
