using Melodee.Common.Utility;

namespace Melodee.Tests.Common.Utility
{
    public class FileHelperTests : IDisposable
    {
        private readonly string _testDir;

        public FileHelperTests()
        {
            _testDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testDir);
        }

        [Theory]
        [InlineData("test.mp3", new byte[] { 0xFF, 0xFB }, "audio/mpeg")]
        [InlineData("test.flac", new byte[] { 0x66, 0x4C, 0x61, 0x43 }, "audio/flac")]
        [InlineData("test.wav", new byte[] { 0x52, 0x49, 0x46, 0x46 }, "audio/wav")]
        [InlineData("test.ogg", new byte[] { 0x4F, 0x67, 0x67, 0x53 }, "audio/ogg")]
        [InlineData("test.m4a", new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 }, "audio/mp4")]
        public void GetMimeType_MagicNumberMatch_ReturnsExpectedMimeType(string fileName, byte[] magicBytes, string expectedMimeType)
        {
            var filePath = Path.Combine(_testDir, fileName);
            File.WriteAllBytes(filePath, magicBytes.Concat(new byte[16]).ToArray());
            var result = FileHelper.GetMimeType(filePath);
            Assert.Equal(expectedMimeType, result);
        }

        [Theory]
        [InlineData("test.mp3", "audio/mpeg")]
        [InlineData("test.flac", "audio/x-flac")]
        [InlineData("test.wav", "audio/wav")]
        [InlineData("test.ogg", "audio/ogg")]
        [InlineData("test.m4a", "audio/mp4")]
        [InlineData("test.txt", "text/plain")]
        [InlineData("test.unknown", "application/octet-stream")]
        public void GetMimeType_NoMagicNumber_FallsBackToExtension(string fileName, string expectedMimeType)
        {
            var filePath = Path.Combine(_testDir, fileName);
            File.WriteAllBytes(filePath, new byte[] { 0x01, 0x02, 0x03, 0x04 });
            var result = FileHelper.GetMimeType(filePath);
            Assert.Equal(expectedMimeType, result);
        }

        [Fact]
        public void GetMimeType_FileDoesNotExist_ReturnsMimeTypeByExtension()
        {
            var filePath = Path.Combine(_testDir, "notfound.mp3");
            var result = FileHelper.GetMimeType(filePath);
            Assert.Equal("audio/mpeg", result);
        }

        [Fact]
        public void GetMimeType_ThrowsException_ReturnsMimeTypeByExtension()
        {
            // Create a file and lock it to simulate exception
            var filePath = Path.Combine(_testDir, "locked.mp3");
            File.WriteAllBytes(filePath, new byte[] { 0xFF, 0xFB });
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = FileHelper.GetMimeType(filePath);
                Assert.Equal("audio/mpeg", result); // Should fallback to extension
            }
        }

        [Fact]
        public void GetMimeType_EmptyFile_ReturnsMimeTypeByExtension()
        {
            var filePath = Path.Combine(_testDir, "empty.wav");
            File.WriteAllBytes(filePath, Array.Empty<byte>());
            var result = FileHelper.GetMimeType(filePath);
            Assert.Equal("audio/wav", result);
        }

        public void Dispose()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
        }

        [Theory]
        [InlineData(".mp3", true)]
        [InlineData("mp3", true)]
        [InlineData(".MP3", true)]
        [InlineData("MP3", true)]
        [InlineData(".flac", true)]
        [InlineData(".m4a", true)]
        [InlineData(".wav", true)]
        [InlineData(".txt", false)]
        [InlineData(".pdf", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsFileMediaType_DetectsMediaExtensionsCaseInsensitively(string? extension, bool expected)
        {
            Assert.Equal(expected, FileHelper.IsFileMediaType(extension));
        }

        [Theory]
        [InlineData(".jpg", true)]
        [InlineData("jpg", true)]
        [InlineData(".JPG", true)]
        [InlineData(".png", true)]
        [InlineData(".webp", true)]
        [InlineData(".bmp", true)]
        [InlineData(".mp3", false)]
        [InlineData(".txt", false)]
        [InlineData(null, false)]
        public void IsFileImageType_DetectsImageExtensionsCaseInsensitively(string? extension, bool expected)
        {
            Assert.Equal(expected, FileHelper.IsFileImageType(extension));
        }

        [Theory]
        [InlineData(".cue", true)]
        [InlineData("cue", true)]
        [InlineData(".CUE", true)]
        [InlineData(".m3u", true)]
        [InlineData(".sfv", true)]
        [InlineData(".mp3", false)]
        [InlineData(null, false)]
        public void IsFileMediaMetaDataType_DetectsMetadataExtensionsCaseInsensitively(string? extension, bool expected)
        {
            Assert.Equal(expected, FileHelper.IsFileMediaMetaDataType(extension));
        }
    }
}

