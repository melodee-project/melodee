namespace Melodee.Common.Imaging;

/// <summary>
///     Contains a variety of methods useful in generating image hashes for image comparison
///     and recognition.
///     Credit for the AverageHash implementation to David Oftedal of the University of Oslo.
///     
///     NOTE: This class is now a thin wrapper around <see cref="IImageProcessor"/>.
///     Prefer injecting <see cref="IImageProcessor"/> directly for new code.
/// </summary>
public static class ImageHasher
{
    /// <summary>
    ///     Generate a hash for the image to be able to find like/matching images.
    /// </summary>
    /// <param name="imageProcessor">The image processor instance.</param>
    /// <param name="bytes">Image bytes</param>
    /// <returns>Hash of Image</returns>
    public static ulong AverageHash(IImageProcessor imageProcessor, byte[] bytes)
    {
        return imageProcessor.ComputeAverageHash(bytes);
    }

    /// <summary>
    ///     Computes the average hash of the image content in the given file.
    /// </summary>
    /// <param name="imageProcessor">The image processor instance.</param>
    /// <param name="path">Path to the input file.</param>
    /// <returns>The hash of the input file's image content.</returns>
    public static ulong AverageHash(IImageProcessor imageProcessor, string path)
    {
        return imageProcessor.ComputeAverageHash(File.ReadAllBytes(path));
    }

    public static bool ImagesAreSame(IImageProcessor imageProcessor, string path1, string path2)
    {
        return imageProcessor.ImagesAreSame(path1, path2);
    }

    public static bool ImagesAreSame(IImageProcessor imageProcessor, byte[] image1, byte[] image2)
    {
        return imageProcessor.ImagesAreSame(image1, image2);
    }

    /// <summary>
    ///     Returns a percentage-based similarity value between the two given hashes. The higher
    ///     the percentage, the closer the hashes are to being identical.
    /// </summary>
    /// <param name="imageProcessor">The image processor instance.</param>
    /// <param name="hash1">The first hash.</param>
    /// <param name="hash2">The second hash.</param>
    /// <returns>The similarity percentage.</returns>
    public static double Similarity(IImageProcessor imageProcessor, ulong hash1, ulong hash2)
    {
        return imageProcessor.Similarity(hash1, hash2);
    }

    /// <summary>
    ///     Returns a percentage-based similarity value between the image content of the two given
    ///     files. The higher the percentage, the closer the image contents are to being identical.
    /// </summary>
    public static double Similarity(IImageProcessor imageProcessor, string path1, string path2)
    {
        return imageProcessor.Similarity(path1, path2);
    }

    /// <summary>
    ///     Returns a percentage-based similarity value between the image content of the two given
    ///     files. The higher the percentage, the closer the image contents are to being identical.
    /// </summary>
    public static double Similarity(IImageProcessor imageProcessor, byte[] image1, byte[] image2)
    {
        return imageProcessor.Similarity(image1, image2);
    }
}
