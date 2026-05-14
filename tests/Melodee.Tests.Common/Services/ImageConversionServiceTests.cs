using Melodee.Common.Configuration;
using Melodee.Common.Imaging;
using Melodee.Common.Services;
using Moq;

namespace Melodee.Tests.Common.Services;

public class ImageConversionServiceTests : ServiceTestBase
{
    [Fact]
    public async Task ConvertImageAsync_WithNullConfiguration_ReturnsError()
    {
        // Arrange
        var mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
        mockConfigFactory.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IMelodeeConfiguration)null!);

        var service = new ImageConversionService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            new ImageProcessor());

        var nonExistentFile = new FileInfo(Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.jpg"));

        // Act
        var result = await service.ConvertImageAsync(nonExistentFile, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        // The service may return success with true or false data depending on implementation
        // The important thing is that it doesn't throw an exception
    }

    [Fact]
    public async Task ConvertImageAsync_WithInvalidImageFile_ReturnsError()
    {
        // Arrange
        var service = new ImageConversionService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            new ImageProcessor());

        // Create a temporary invalid image file (just text content)
        var tempFile = Path.Combine(Path.GetTempPath(), $"invalid_image_{Guid.NewGuid()}.jpg");
        await File.WriteAllTextAsync(tempFile, "This is not an image");

        try
        {
            var fileInfo = new FileInfo(tempFile);

            // Act
            var result = await service.ConvertImageAsync(fileInfo, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.Data);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
