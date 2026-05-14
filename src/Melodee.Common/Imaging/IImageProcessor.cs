namespace Melodee.Common.Imaging;

/// <summary>
///     Centralized interface for all image processing operations,
///     abstracting the underlying imaging library.
/// </summary>
public interface IImageProcessor
{
    /// <summary>
    ///     Identifies image dimensions and format without fully decoding the image.
    /// </summary>
    Task<ImageDimensions?> IdentifyAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Identifies image dimensions and format from an in-memory byte array.
    /// </summary>
    Task<ImageDimensions?> IdentifyAsync(ReadOnlyMemory<byte> imageBytes, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Loads image dimensions from embedded picture data (synchronous).
    /// </summary>
    ImageDimensions? Identify(ReadOnlyMemory<byte> imageBytes);

    /// <summary>
    ///     Converts any supported image format to JPEG format bytes.
    /// </summary>
    byte[] ConvertToJpeg(ReadOnlyMemory<byte> imageBytes);

    /// <summary>
    ///     Converts any supported image format to GIF format bytes.
    /// </summary>
    Task<byte[]> ConvertToGifAsync(byte[] imageBytes, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Resizes and pads an image to a square of the given target size using a transparent background.
    /// </summary>
    byte[] ResizeAndPadToSquare(ReadOnlyMemory<byte> imageBytes, int targetSize);

    /// <summary>
    ///     Resizes an image if it exceeds the given max dimensions, optionally saving as GIF.
    /// </summary>
    byte[] ResizeImageIfNeeded(ReadOnlyMemory<byte> imageBytes, int maxWidth, int maxHeight, bool saveAsGif);

    /// <summary>
    ///     Computes an average hash (perceptual hash) for the given image bytes.
    /// </summary>
    ulong ComputeAverageHash(ReadOnlyMemory<byte> imageBytes);

    /// <summary>
    ///     Computes a percentage-based similarity between two average hashes.
    ///     Returns a value between 0.0 and 100.0.
    /// </summary>
    double Similarity(ulong hash1, ulong hash2);

    /// <summary>
    ///     Computes the similarity between two image files (0.0 to 100.0).
    /// </summary>
    double Similarity(string path1, string path2);

    /// <summary>
    ///     Computes the similarity between two in-memory images (0.0 to 100.0).
    /// </summary>
    double Similarity(byte[] image1, byte[] image2);

    /// <summary>
    ///     Returns true if two images are identical (100% hash match).
    /// </summary>
    bool ImagesAreSame(string path1, string path2);

    /// <summary>
    ///     Returns true if two in-memory images are identical (100% hash match).
    /// </summary>
    bool ImagesAreSame(byte[] image1, byte[] image2);
}
