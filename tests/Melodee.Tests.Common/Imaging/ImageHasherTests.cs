using Melodee.Common.Imaging;
using SkiaSharp;

namespace Melodee.Tests.Common.Imaging;

public class ImageHasherTests
{
    private readonly IImageProcessor _imageProcessor = new ImageProcessor();

    private static byte[] CreateSolidColorImage(int width, int height, byte r, byte g, byte b)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(new SKColor(r, g, b));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data == null) return [];
        return data.ToArray();
    }

    private static byte[] CreateGradientImage(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var brightness = (byte)(255 * y / height);
                bitmap.SetPixel(x, y, new SKColor(brightness, brightness, brightness));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data == null) return [];
        return data.ToArray();
    }

    private static byte[] CreateCheckerboardImage(int width, int height, int squareSize)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var isWhite = ((x / squareSize) + (y / squareSize)) % 2 == 0;
                bitmap.SetPixel(x, y, isWhite ? new SKColor(255, 255, 255) : new SKColor(0, 0, 0));
            }
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        if (data == null) return [];
        return data.ToArray();
    }

    [Fact]
    public void AverageHash_IdenticalImages_ProduceSameHash()
    {
        var image1 = CreateSolidColorImage(100, 100, 128, 128, 128);
        var image2 = CreateSolidColorImage(100, 100, 128, 128, 128);

        var hash1 = _imageProcessor.ComputeAverageHash(image1);
        var hash2 = _imageProcessor.ComputeAverageHash(image2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void AverageHash_SameImage_ConsistentHash()
    {
        var image = CreateSolidColorImage(100, 100, 200, 150, 100);

        var hash1 = _imageProcessor.ComputeAverageHash(image);
        var hash2 = _imageProcessor.ComputeAverageHash(image);
        var hash3 = _imageProcessor.ComputeAverageHash(image);

        Assert.Equal(hash1, hash2);
        Assert.Equal(hash2, hash3);
    }

    [Fact]
    public void AverageHash_DifferentSizes_SameContent_SameHash()
    {
        var image1 = CreateSolidColorImage(50, 50, 100, 100, 100);
        var image2 = CreateSolidColorImage(200, 200, 100, 100, 100);

        var hash1 = _imageProcessor.ComputeAverageHash(image1);
        var hash2 = _imageProcessor.ComputeAverageHash(image2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void AverageHash_SolidColorImages_ProduceSameHash()
    {
        // Solid color images all have uniform pixels, so they produce the same hash
        var blackImage = CreateSolidColorImage(100, 100, 0, 0, 0);
        var whiteImage = CreateSolidColorImage(100, 100, 255, 255, 255);
        var grayImage = CreateSolidColorImage(100, 100, 128, 128, 128);

        var hashBlack = _imageProcessor.ComputeAverageHash(blackImage);
        var hashWhite = _imageProcessor.ComputeAverageHash(whiteImage);
        var hashGray = _imageProcessor.ComputeAverageHash(grayImage);

        // All solid colors produce max value (all bits set)
        Assert.Equal(ulong.MaxValue, hashBlack);
        Assert.Equal(ulong.MaxValue, hashWhite);
        Assert.Equal(ulong.MaxValue, hashGray);
    }

    [Fact]
    public void AverageHash_GradientImage_ProducesConsistentHash()
    {
        var gradient1 = CreateGradientImage(100, 100);
        var gradient2 = CreateGradientImage(100, 100);

        var hash1 = _imageProcessor.ComputeAverageHash(gradient1);
        var hash2 = _imageProcessor.ComputeAverageHash(gradient2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Similarity_IdenticalHashes_Returns100Percent()
    {
        var hash = 0x123456789ABCDEF0UL;

        var similarity = _imageProcessor.Similarity(hash, hash);

        Assert.Equal(100.0, similarity);
    }

    [Fact]
    public void Similarity_CompletelyDifferentHashes_ReturnsLowPercentage()
    {
        var hash1 = 0x0000000000000000UL;
        var hash2 = 0xFFFFFFFFFFFFFFFFUL;

        var similarity = _imageProcessor.Similarity(hash1, hash2);

        Assert.Equal(0.0, similarity);
    }

    [Fact]
    public void Similarity_OnebitDifference_ReturnsHighPercentage()
    {
        var hash1 = 0x0000000000000000UL;
        var hash2 = 0x0000000000000001UL;

        var similarity = _imageProcessor.Similarity(hash1, hash2);

        var expectedSimilarity = (64.0 - 1.0) * 100.0 / 64.0;
        Assert.Equal(expectedSimilarity, similarity, 2);
    }

    [Fact]
    public void Similarity_IdenticalImages_Returns100Percent()
    {
        var image1 = CreateSolidColorImage(100, 100, 128, 64, 192);
        var image2 = CreateSolidColorImage(100, 100, 128, 64, 192);

        var similarity = _imageProcessor.Similarity(image1, image2);

        Assert.Equal(100.0, similarity);
    }

    [Fact]
    public void Similarity_SlightlyDifferentColors_ReturnsHighSimilarity()
    {
        var image1 = CreateSolidColorImage(100, 100, 128, 128, 128);
        var image2 = CreateSolidColorImage(100, 100, 130, 130, 130);

        var similarity = _imageProcessor.Similarity(image1, image2);

        Assert.True(similarity > 90.0);
    }

    [Fact]
    public void Similarity_SolidColorImages_Returns100Percent()
    {
        // Solid colors all have the same hash, so they're 100% similar
        var black = CreateSolidColorImage(100, 100, 0, 0, 0);
        var white = CreateSolidColorImage(100, 100, 255, 255, 255);

        var similarity = _imageProcessor.Similarity(black, white);

        Assert.Equal(100.0, similarity);
    }

    [Fact]
    public void ImagesAreSame_IdenticalImages_ReturnsTrue()
    {
        var image1 = CreateSolidColorImage(100, 100, 100, 150, 200);
        var image2 = CreateSolidColorImage(100, 100, 100, 150, 200);

        var areSame = _imageProcessor.ImagesAreSame(image1, image2);

        Assert.True(areSame);
    }

    [Fact]
    public void ImagesAreSame_SolidColors_ReturnsTrue()
    {
        // All solid colors produce the same hash
        var image1 = CreateSolidColorImage(100, 100, 100, 100, 100);
        var image2 = CreateSolidColorImage(100, 100, 200, 200, 200);

        var areSame = _imageProcessor.ImagesAreSame(image1, image2);

        Assert.True(areSame);
    }

    [Fact]
    public void AverageHash_CheckerboardPattern_ProducesConsistentHash()
    {
        var checker1 = CreateCheckerboardImage(64, 64, 8);
        var checker2 = CreateCheckerboardImage(64, 64, 8);

        var hash1 = _imageProcessor.ComputeAverageHash(checker1);
        var hash2 = _imageProcessor.ComputeAverageHash(checker2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void AverageHash_DifferentCheckerboardSizes_DifferentHashes()
    {
        var checker1 = CreateCheckerboardImage(64, 64, 4);
        var checker2 = CreateCheckerboardImage(64, 64, 16);

        var hash1 = _imageProcessor.ComputeAverageHash(checker1);
        var hash2 = _imageProcessor.ComputeAverageHash(checker2);

        Assert.NotEqual(hash1, hash2);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(128, 128, 128)]
    [InlineData(255, 255, 255)]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    public void AverageHash_VariousColors_ProducesStableHashes(byte r, byte g, byte b)
    {
        var image = CreateSolidColorImage(100, 100, r, g, b);

        var hash1 = _imageProcessor.ComputeAverageHash(image);
        var hash2 = _imageProcessor.ComputeAverageHash(image);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void Similarity_SimilarGradients_ReturnsHighSimilarity()
    {
        var gradient1 = CreateGradientImage(100, 100);
        var gradient2 = CreateGradientImage(100, 100);

        var similarity = _imageProcessor.Similarity(gradient1, gradient2);

        Assert.Equal(100.0, similarity);
    }

    [Theory]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(200, 200)]
    [InlineData(500, 500)]
    public void AverageHash_VariousSizes_SameColor_ProducesSameHash(int width, int height)
    {
        var image1 = CreateSolidColorImage(width, height, 128, 128, 128);
        var image2 = CreateSolidColorImage(100, 100, 128, 128, 128);

        var hash1 = _imageProcessor.ComputeAverageHash(image1);
        var hash2 = _imageProcessor.ComputeAverageHash(image2);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void AverageHash_VerySmallImage_WorksCorrectly()
    {
        var image = CreateSolidColorImage(8, 8, 100, 100, 100);

        var hash = _imageProcessor.ComputeAverageHash(image);

        Assert.NotEqual(0UL, hash);
    }

    [Fact]
    public void Similarity_ReturnValueBetweenZeroAnd100()
    {
        var image1 = CreateSolidColorImage(100, 100, 50, 100, 150);
        var image2 = CreateSolidColorImage(100, 100, 200, 150, 100);

        var similarity = _imageProcessor.Similarity(image1, image2);

        Assert.InRange(similarity, 0.0, 100.0);
    }
}
