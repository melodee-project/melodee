using SkiaSharp;

namespace Melodee.Common.Imaging;

/// <summary>
///     Image processing implementation using SkiaSharp.
///     Provides centralized image identification, conversion, resizing, and hashing.
/// </summary>
public sealed class ImageProcessor : IImageProcessor
{
    private static readonly byte[] bitCounts =
    [
        0, 1, 1, 2, 1, 2, 2, 3, 1, 2, 2, 3, 2, 3, 3, 4, 1, 2, 2, 3, 2, 3, 3, 4, 2, 3, 3, 4, 3, 4, 4, 5, 1, 2, 2, 3,
        2, 3, 3, 4,
        2, 3, 3, 4, 3, 4, 4, 5, 2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6, 1, 2, 2, 3, 2, 3, 3, 4, 2, 3, 3, 4,
        3, 4, 4, 5,
        2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6, 2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6, 3, 4, 4, 5,
        4, 5, 5, 6,
        4, 5, 5, 6, 5, 6, 6, 7, 1, 2, 2, 3, 2, 3, 3, 4, 2, 3, 3, 4, 3, 4, 4, 5, 2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5,
        4, 5, 5, 6,
        2, 3, 3, 4, 3, 4, 4, 5, 3, 4, 4, 5, 4, 5, 5, 6, 3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6, 5, 6, 6, 7, 2, 3, 3, 4,
        3, 4, 4, 5,
        3, 4, 4, 5, 4, 5, 5, 6, 3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6, 5, 6, 6, 7, 3, 4, 4, 5, 4, 5, 5, 6, 4, 5, 5, 6,
        5, 6, 6, 7,
        4, 5, 5, 6, 5, 6, 6, 7, 5, 6, 6, 7, 6, 7, 7, 8
    ];

    public Task<ImageDimensions?> IdentifyAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            return Task.FromResult<ImageDimensions?>(null);
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            var codec = SKCodec.Create(stream);
            if (codec == null)
            {
                return Task.FromResult<ImageDimensions?>(null);
            }

            var info = codec.Info;
            return Task.FromResult<ImageDimensions?>(new ImageDimensions(info.Width, info.Height, codec.EncodedFormat.ToString()));
        }
        catch
        {
            return Task.FromResult<ImageDimensions?>(null);
        }
    }

    public Task<ImageDimensions?> IdentifyAsync(ReadOnlyMemory<byte> imageBytes, CancellationToken cancellationToken = default)
    {
        if (imageBytes.IsEmpty)
        {
            return Task.FromResult<ImageDimensions?>(null);
        }

        try
        {
            using var stream = new SKMemoryStream(imageBytes.ToArray());
            using var codec = SKCodec.Create(stream);
            if (codec == null)
            {
                return Task.FromResult<ImageDimensions?>(null);
            }

            var info = codec.Info;
            return Task.FromResult<ImageDimensions?>(new ImageDimensions(info.Width, info.Height, codec.EncodedFormat.ToString()));
        }
        catch
        {
            return Task.FromResult<ImageDimensions?>(null);
        }
    }

    public ImageDimensions? Identify(ReadOnlyMemory<byte> imageBytes)
    {
        if (imageBytes.IsEmpty)
        {
            return null;
        }

        try
        {
            using var stream = new SKMemoryStream(imageBytes.ToArray());
            using var codec = SKCodec.Create(stream);
            if (codec == null)
            {
                return null;
            }

            var info = codec.Info;
            return new ImageDimensions(info.Width, info.Height, codec.EncodedFormat.ToString());
        }
        catch
        {
            return null;
        }
    }

    public byte[] ConvertToJpeg(ReadOnlyMemory<byte> imageBytes)
    {
        if (imageBytes.IsEmpty)
        {
            return imageBytes.ToArray();
        }

        using var bitmap = SKBitmap.Decode(imageBytes.ToArray());
        if (bitmap == null)
        {
            return imageBytes.ToArray();
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        if (data == null)
        {
            return imageBytes.ToArray();
        }

        return data.ToArray();
    }

    public async Task<byte[]> ConvertToGifAsync(byte[] imageBytes, CancellationToken cancellationToken = default)
    {
        if (imageBytes.Length == 0)
        {
            return imageBytes;
        }

        using var bitmap = SKBitmap.Decode(imageBytes);
        if (bitmap == null)
        {
            return imageBytes;
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Gif, 100);
        if (data == null)
        {
            return imageBytes;
        }

        return await Task.FromResult(data.ToArray());
    }

    public byte[] ResizeAndPadToSquare(ReadOnlyMemory<byte> imageBytes, int targetSize)
    {
        if (imageBytes.IsEmpty || targetSize <= 0)
        {
            return imageBytes.ToArray();
        }

        using var sourceBitmap = SKBitmap.Decode(imageBytes.ToArray());
        if (sourceBitmap == null)
        {
            return imageBytes.ToArray();
        }

        var info = new SKImageInfo(targetSize, targetSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null)
        {
            return imageBytes.ToArray();
        }

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        // Calculate scaling to fit within targetSize while maintaining aspect ratio
        float scale = Math.Min(
            (float)targetSize / sourceBitmap.Width,
            (float)targetSize / sourceBitmap.Height);

        int newWidth = (int)(sourceBitmap.Width * scale);
        int newHeight = (int)(sourceBitmap.Height * scale);

        // Center the scaled image
        int x = (targetSize - newWidth) / 2;
        int y = (targetSize - newHeight) / 2;

        var destRect = new SKRectI(x, y, x + newWidth, y + newHeight);
        var srcRect = new SKRectI(0, 0, sourceBitmap.Width, sourceBitmap.Height);

        canvas.DrawBitmap(sourceBitmap, srcRect, destRect, SKSamplingOptions.Default);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 85);
        if (data == null)
        {
            return imageBytes.ToArray();
        }

        return data.ToArray();
    }

    public byte[] ResizeImageIfNeeded(ReadOnlyMemory<byte> imageBytes, int maxWidth, int maxHeight, bool saveAsGif)
    {
        if (imageBytes.IsEmpty)
        {
            return imageBytes.ToArray();
        }

        using var sourceBitmap = SKBitmap.Decode(imageBytes.ToArray());
        if (sourceBitmap == null)
        {
            return imageBytes.ToArray();
        }

        if (sourceBitmap.Width <= maxWidth && sourceBitmap.Height <= maxHeight)
        {
            // No resize needed, just re-encode if format change requested
            using var srcImage = SKImage.FromBitmap(sourceBitmap);
            using var data = srcImage.Encode(saveAsGif ? SKEncodedImageFormat.Gif : SKEncodedImageFormat.Jpeg, 85);
            if (data == null)
            {
                return imageBytes.ToArray();
            }

            return data.ToArray();
        }

        // Calculate new size maintaining aspect ratio
        float scale = Math.Min(
            (float)maxWidth / sourceBitmap.Width,
            (float)maxHeight / sourceBitmap.Height);

        int newWidth = (int)(sourceBitmap.Width * scale);
        int newHeight = (int)(sourceBitmap.Height * scale);

        var resized = sourceBitmap.Resize(new SKImageInfo(newWidth, newHeight, sourceBitmap.ColorType, sourceBitmap.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized == null)
        {
            return imageBytes.ToArray();
        }

        using (resized)
        {
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(saveAsGif ? SKEncodedImageFormat.Gif : SKEncodedImageFormat.Jpeg, 85);
            if (data == null)
            {
                return imageBytes.ToArray();
            }

            return data.ToArray();
        }
    }

    public ulong ComputeAverageHash(ReadOnlyMemory<byte> imageBytes)
    {
        if (imageBytes.IsEmpty)
        {
            return 0;
        }

        using var sourceBitmap = SKBitmap.Decode(imageBytes.ToArray());
        if (sourceBitmap == null)
        {
            return 0;
        }

        // Resize to 8x8 and convert to grayscale
        var resized = sourceBitmap.Resize(new SKImageInfo(8, 8, sourceBitmap.ColorType, sourceBitmap.AlphaType), new SKSamplingOptions(SKCubicResampler.Mitchell));
        if (resized == null)
        {
            return 0;
        }

        using (resized)
        {
            var grayBytes = new byte[64];
            uint averageValue = 0;

            int i = 0;
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    var color = resized.GetPixel(x, y);
                    // Rec709 luminance formula: 0.2126 R + 0.7152 G + 0.0722 B
                    byte gray = (byte)((color.Red * 54 + color.Green * 183 + color.Blue * 19) >> 8);
                    grayBytes[i] = gray;
                    averageValue += gray;
                    i++;
                }
            }

            averageValue /= 64;

            ulong hash = 0;
            for (i = 0; i < 64; i++)
            {
                if (grayBytes[i] >= averageValue)
                {
                    hash |= 1UL << (63 - i);
                }
            }

            return hash;
        }
    }

    public double Similarity(ulong hash1, ulong hash2)
    {
        return (64 - BitCount(hash1 ^ hash2)) * 100 / 64.0;
    }

    public double Similarity(string path1, string path2)
    {
        var hash1 = ComputeAverageHash(File.ReadAllBytes(path1));
        var hash2 = ComputeAverageHash(File.ReadAllBytes(path2));
        return Similarity(hash1, hash2);
    }

    public double Similarity(byte[] image1, byte[] image2)
    {
        var hash1 = ComputeAverageHash(image1);
        var hash2 = ComputeAverageHash(image2);
        return Similarity(hash1, hash2);
    }

    public bool ImagesAreSame(string path1, string path2)
    {
        return Similarity(path1, path2) == 100;
    }

    public bool ImagesAreSame(byte[] image1, byte[] image2)
    {
        return Similarity(image1, image2) == 100;
    }

    private static uint BitCount(ulong num)
    {
        uint count = 0;
        for (; num > 0; num >>= 8)
        {
            count += bitCounts[num & 0xff];
        }

        return count;
    }
}
