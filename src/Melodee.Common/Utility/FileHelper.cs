using MimeKit;

namespace Melodee.Common.Utility;

public static class FileHelper
{
    public static readonly string MelodeeTagFileExtension = ".mtg";

    private static readonly Dictionary<byte[], string> MagicNumbers = new()
    {
        [[0xFF, 0xFB]] = "audio/mpeg", // MP3
        [[0x66, 0x4C, 0x61, 0x43]] = "audio/flac", // FLAC
        [[0x52, 0x49, 0x46, 0x46]] = "audio/wav", // WAV/RIFF
        [[0x4F, 0x67, 0x67, 0x53]] = "audio/ogg", // OGG
        [[0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70]] = "audio/mp4" // M4A
    };


    private static readonly HashSet<string> MediaMetaDataFileTypeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "cue",
        "m3u",
        "sfv"
    };

    private static readonly HashSet<string> MediaFileTypeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "aac",
        "alac",
        "ac3",
        "bonk",
        "aiff",
        "ape",
        "flac",
        "m4a",
        "m4b",
        "mp1",
        "mp2",
        "mp3",
        "oga",
        "ogg",
        "opus",
        "spx",
        "sfu",
        "tta",
        "wav",
        "wma"
    };

    private static readonly HashSet<string> ImageFileTypeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "bmp",
        "gif",
        "jfif",
        "image", // This is a temporary file extension used when downloading images and converting
        "jpeg",
        "jpg",
        "png",
        "tiff",
        "webp"
    };

    public static bool IsFileMediaType(string? extension)
    {
        return !string.IsNullOrEmpty(extension) &&
               MediaFileTypeExtensions.Contains(extension.Replace(".", ""));
    }

    public static bool IsFileImageType(string? extension)
    {
        return !string.IsNullOrEmpty(extension) &&
               ImageFileTypeExtensions.Contains(extension.Replace(".", ""));
    }

    public static bool IsFileMediaMetaDataType(string? extension)
    {
        return !string.IsNullOrEmpty(extension) &&
               MediaMetaDataFileTypeExtensions.Contains(extension.Replace(".", ""));
    }

    public static int GetNumberOfTotalFilesForDirectory(DirectoryInfo directoryInfo)
    {
        return directoryInfo.EnumerateFiles("*.*", SearchOption.AllDirectories).Count();
    }

    public static IEnumerable<IGrouping<string, FileInfo>> AllFileExtensionsForDirectory(DirectoryInfo directoryInfo)
    {
        return directoryInfo.EnumerateFiles("*.*", SearchOption.AllDirectories).GroupBy(x => x.Extension);
    }

    public static int GetNumberOfMediaFilesForDirectory(IEnumerable<IGrouping<string, FileInfo>> fileExtensions)
    {
        var result = 0;

        foreach (var extGroup in fileExtensions)
        {
            if (IsFileMediaType(extGroup.Key))
            {
                result += extGroup.Count();
            }
        }

        return result;
    }

    public static int GetNumberOfImageFilesForDirectory(IEnumerable<IGrouping<string, FileInfo>> fileExtensions)
    {
        var result = 0;
        foreach (var extGroup in fileExtensions)
        {
            if (IsFileImageType(extGroup.Key))
            {
                result += extGroup.Count();
            }
        }

        return result;
    }

    public static int GetNumberOfMediaMetaDataFilesForDirectory(IEnumerable<IGrouping<string, FileInfo>> fileExtensions)
    {
        var result = 0;
        foreach (var extGroup in fileExtensions)
        {
            if (IsFileMediaMetaDataType(extGroup.Key))
            {
                result += extGroup.Count();
            }
        }

        return result;
    }

    public static string GetMimeType(string filePath)
    {
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var buffer = new byte[8];
            fs.ReadExactly(buffer, 0, buffer.Length);

            foreach (var magic in MagicNumbers.Where(magic => buffer.Take(magic.Key.Length).SequenceEqual(magic.Key)))
            {
                return magic.Value;
            }
        }
        catch
        {
            // Fall back to extension-based detection
        }

        return MimeTypes.GetMimeType(Path.GetFileName(filePath));
    }

}
