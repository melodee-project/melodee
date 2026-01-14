using FluentAssertions;
using Melodee.Common.Utility;

namespace Melodee.Tests.Common.Utility;

public class FileTypeValidatorTests
{
    [Fact]
    public void ValidateMagicBytes_WithValidJpegStream_ReturnsTrue()
    {
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        using var stream = new MemoryStream(jpegBytes);

        var result = FileTypeValidator.ValidateMagicBytes(stream, "image/jpeg");

        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateMagicBytes_WithValidPngStream_ReturnsTrue()
    {
        var pngBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        using var stream = new MemoryStream(pngBytes);

        var result = FileTypeValidator.ValidateMagicBytes(stream, "image/png");

        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateMagicBytes_WithValidWebpStream_ReturnsTrue()
    {
        var webpBytes = new byte[] { 0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50 };
        using var stream = new MemoryStream(webpBytes);

        var result = FileTypeValidator.ValidateMagicBytes(stream, "image/webp");

        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateMagicBytes_WithInvalidContentType_ReturnsFalse()
    {
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        using var stream = new MemoryStream(jpegBytes);

        var result = FileTypeValidator.ValidateMagicBytes(stream, "image/png");

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateMagicBytes_WithEmptyStream_ReturnsFalse()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());

        var result = FileTypeValidator.ValidateMagicBytes(stream, "image/jpeg");

        result.Should().BeFalse();
    }

    [Fact]
    public void ValidateMagicBytes_WithNullStream_ReturnsFalse()
    {
        Stream? nullStream = null;

        var result = FileTypeValidator.ValidateMagicBytes(nullStream!, "image/jpeg");

        result.Should().BeFalse();
    }

    [Fact]
    public void IsValidImage_WithValidJpegStream_ReturnsTrue()
    {
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        using var stream = new MemoryStream(jpegBytes);

        var result = FileTypeValidator.IsValidImage(stream);

        result.Should().BeTrue();
    }

    [Fact]
    public void IsValidImage_WithAudioBytes_ReturnsFalse()
    {
        var mp3Bytes = new byte[] { 0xFF, 0xFB, 0x92, 0x00 };
        using var stream = new MemoryStream(mp3Bytes);

        var result = FileTypeValidator.IsValidImage(stream);

        result.Should().BeFalse();
    }

    [Fact]
    public void DetectContentType_WithJpegBytes_ReturnsJpeg()
    {
        var jpegBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        using var stream = new MemoryStream(jpegBytes);

        var result = FileTypeValidator.DetectContentType(stream);

        result.Should().Be("image/jpeg");
    }

    [Fact]
    public void DetectContentType_WithUnknownBytes_ReturnsNull()
    {
        var unknownBytes = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(unknownBytes);

        var result = FileTypeValidator.DetectContentType(stream);

        result.Should().BeNull();
    }

    [Fact]
    public void IsAllowedImageContentType_WithJpeg_ReturnsTrue()
    {
        FileTypeValidator.IsAllowedImageContentType("image/jpeg").Should().BeTrue();
        FileTypeValidator.IsAllowedImageContentType("image/png").Should().BeTrue();
        FileTypeValidator.IsAllowedImageContentType("image/webp").Should().BeTrue();
    }

    [Fact]
    public void IsAllowedImageContentType_WithAudio_ReturnsFalse()
    {
        FileTypeValidator.IsAllowedImageContentType("audio/mpeg").Should().BeFalse();
    }

    [Fact]
    public void IsAllowedAudioContentType_WithMp3_ReturnsTrue()
    {
        FileTypeValidator.IsAllowedAudioContentType("audio/mpeg").Should().BeTrue();
        FileTypeValidator.IsAllowedAudioContentType("audio/flac").Should().BeTrue();
        FileTypeValidator.IsAllowedAudioContentType("audio/ogg").Should().BeTrue();
    }

    [Theory]
    [InlineData("audio/mpeg")]
    [InlineData("audio/flac")]
    [InlineData("audio/mp4")]
    [InlineData("audio/ogg")]
    [InlineData("audio/wav")]
    public void IsAllowedAudioContentType_WithValidTypes_ReturnsTrue(string contentType)
    {
        FileTypeValidator.IsAllowedAudioContentType(contentType).Should().BeTrue();
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void IsAllowedImageContentType_WithValidTypes_ReturnsTrue(string contentType)
    {
        FileTypeValidator.IsAllowedImageContentType(contentType).Should().BeTrue();
    }

    [Fact]
    public void ValidateMagicBytes_WithValidZipStream_ReturnsTrue()
    {
        var zipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(zipBytes);

        var result = FileTypeValidator.ValidateMagicBytes(stream, "application/zip");

        result.Should().BeTrue();
    }

    [Fact]
    public void DetectContentType_WithZipBytes_ReturnsZip()
    {
        var zipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00 };
        using var stream = new MemoryStream(zipBytes);

        var result = FileTypeValidator.DetectContentType(stream);

        result.Should().Be("application/zip");
    }
}
