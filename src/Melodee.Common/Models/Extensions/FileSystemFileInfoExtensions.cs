using Melodee.Common.Extensions;

namespace Melodee.Common.Models.Extensions;

/// <summary>
///     These take the DirectoryInfo for the directory as the FileSystem should be light as possible, an example is there
///     may be 10 images which should only have unique names in the same album directory.
/// </summary>
public static class FileSystemFileInfoExtensions
{
    public static FileInfo ToFileInfo(this FileSystemFileInfo fileSystemFileInfo, FileSystemDirectoryInfo directoryInfo)
    {
        return new FileInfo(fileSystemFileInfo.FullName(directoryInfo));
    }

    public static string FullName(this FileSystemFileInfo fileSystemFileInfo, FileSystemDirectoryInfo directoryInfo)
    {
        return Path.Combine(directoryInfo.FullName(), fileSystemFileInfo.Name);
    }

    public static bool Exists(this FileSystemFileInfo fileSystemFileInfo, FileSystemDirectoryInfo directoryInfo)
    {
        if (File.Exists(fileSystemFileInfo.FullName(directoryInfo)))
        {
            return true;
        }

        var originalName = fileSystemFileInfo.OriginalName.Nullify();
        return originalName is not null &&
               !string.Equals(originalName, fileSystemFileInfo.Name, StringComparison.OrdinalIgnoreCase) &&
               File.Exists(Path.Combine(directoryInfo.FullName(), originalName));
    }

    public static string Extension(this FileSystemFileInfo fileSystemFileInfo, FileSystemDirectoryInfo directoryInfo)
    {
        return Path.GetExtension(fileSystemFileInfo.FullName(directoryInfo));
    }

    public static async Task<string?> ImageBase64(this FileSystemFileInfo fileInfo, FileSystemDirectoryInfo directoryInfo, CancellationToken cancellationToken = default)
    {
        var fi = new FileInfo(fileInfo.FullName(directoryInfo));
        if (fi.Exists)
        {
            var imageBytes = await File.ReadAllBytesAsync(fi.FullName, cancellationToken).ConfigureAwait(false);
            return $"data:image/jpeg;base64,{Convert.ToBase64String(imageBytes)}";
        }

        return null;
    }
}
