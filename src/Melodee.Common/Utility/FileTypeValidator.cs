using System.IO;
using System.Runtime.InteropServices;

namespace Melodee.Common.Utility;

/// <summary>
/// Utility for validating file contents using magic bytes (file signatures).
/// </summary>
public static class FileTypeValidator
{
    private static readonly Dictionary<byte[], string> MagicBytesSignatures = new()
    {
        { [0xFF, 0xD8, 0xFF], "image/jpeg" },
        { [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], "image/png" },
        { [0x52, 0x49, 0x46, 0x46], "image/webp" }, // RIFF...WEBP
        { [0x49, 0x44, 0x33], "audio/mpeg" }, // ID3
        { [0xFF, 0xFB], "audio/mpeg" }, // MP3 frame sync
        { [0x66, 0x4C, 0x61, 0x43], "audio/flac" }, // fLaC
        { [0x4F, 0x67, 0x67, 0x53], "audio/ogg" }, // OggS
        { [0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D], "audio/mp4" }, // ftyp...isom (MP4)
        { [0x52, 0x49, 0x46, 0x46], "audio/wav" }, // RIFF....WAVE
        { [0x50, 0x4B, 0x03, 0x04], "application/zip" }, // PK.. (ZIP local file header)
        { [0x50, 0x4B, 0x05, 0x06], "application/zip" }, // PK.. (ZIP empty archive)
        { [0x50, 0x4B, 0x07, 0x08], "application/zip" }  // PK.. (ZIP spanned archive)
    };

    private static readonly HashSet<string> AllowedImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private static readonly HashSet<string> AllowedAudioContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "audio/mpeg",
        "audio/flac",
        "audio/mp4",
        "audio/ogg",
        "audio/wav"
    };

    /// <summary>
    /// Validates that the file content matches the expected content type using magic bytes.
    /// </summary>
    /// <param name="fileStream">The file stream to validate.</param>
    /// <param name="expectedContentType">The expected content type.</param>
    /// <returns>True if the file signature matches the expected content type; otherwise, false.</returns>
    public static bool ValidateMagicBytes(Stream fileStream, string expectedContentType)
    {
        if (fileStream == null || !fileStream.CanRead || fileStream.Length == 0)
        {
            return false;
        }

        var signatures = MagicBytesSignatures
            .Where(kv => kv.Value.Equals(expectedContentType, StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key)
            .ToList();

        if (!signatures.Any())
        {
            return false;
        }

        try
        {
            var maxSignatureLength = signatures.Max(s => s.Length);
            var buffer = new byte[maxSignatureLength];
            var bytesRead = fileStream.Read(buffer, 0, maxSignatureLength);
            fileStream.Position = 0;

            if (bytesRead < maxSignatureLength)
            {
                return false;
            }

            foreach (var signature in signatures)
            {
                if (MatchesSignature(buffer, signature))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates an image file using magic bytes.
    /// </summary>
    /// <param name="fileStream">The image file stream.</param>
    /// <returns>True if the file is a valid image with allowed type; otherwise, false.</returns>
    public static bool IsValidImage(Stream fileStream)
    {
        if (fileStream == null || !fileStream.CanRead || fileStream.Length == 0)
        {
            return false;
        }

        try
        {
            var buffer = new byte[8];
            var bytesRead = fileStream.Read(buffer, 0, 8);
            fileStream.Position = 0;

            if (bytesRead < 4)
            {
                return false;
            }

            foreach (var kvp in MagicBytesSignatures)
            {
                if (kvp.Value.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
                    AllowedImageContentTypes.Contains(kvp.Value))
                {
                    if (MatchesSignature(buffer, kvp.Key))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Determines the content type from file magic bytes.
    /// </summary>
    /// <param name="fileStream">The file stream.</param>
    /// <returns>The detected content type, or null if not recognized.</returns>
    public static string? DetectContentType(Stream fileStream)
    {
        if (fileStream == null || !fileStream.CanRead || fileStream.Length == 0)
        {
            return null;
        }

        try
        {
            var maxLength = MagicBytesSignatures.Max(kv => kv.Key.Length);
            var buffer = new byte[maxLength];
            var bytesRead = fileStream.Read(buffer, 0, maxLength);
            fileStream.Position = 0;

            if (bytesRead < 4)
            {
                return null;
            }

            foreach (var kvp in MagicBytesSignatures)
            {
                if (MatchesSignature(buffer, kvp.Key))
                {
                    return kvp.Value;
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if the buffer matches the given signature.
    /// </summary>
    private static bool MatchesSignature(byte[] buffer, byte[] signature)
    {
        if (buffer.Length < signature.Length)
        {
            return false;
        }

        for (int i = 0; i < signature.Length; i++)
        {
            if (buffer[i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates that a content type is allowed for images.
    /// </summary>
    public static bool IsAllowedImageContentType(string contentType)
    {
        return AllowedImageContentTypes.Contains(contentType);
    }

    /// <summary>
    /// Validates that a content type is allowed for audio.
    /// </summary>
    public static bool IsAllowedAudioContentType(string contentType)
    {
        return AllowedAudioContentTypes.Contains(contentType);
    }
}
