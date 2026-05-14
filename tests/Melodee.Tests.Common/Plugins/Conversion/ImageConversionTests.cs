using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.Conversion.Image;

namespace Melodee.Tests.Common.Plugins.Conversion;

public class ImageConversionTests
{
    [Fact]
    public async Task ValidateConvertingPngToJpgAsync()
    {
        var testFile = @"/melodee_test/tests/testpng.png";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var convertor = new ImageConvertor(new ImageProcessor(), TestsBase.NewPluginsConfiguration());
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var convertorResult = await convertor.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(convertorResult);
            Assert.True(convertorResult.IsSuccess);
            Assert.NotNull(convertorResult.Data);

            var convertedFileInfo = new FileInfo(convertorResult.Data.FullName(dirInfo));
            Assert.True(convertedFileInfo.Exists);
        }
    }

    [Fact]
    public async Task ValidateConvertingGifToJpgAsync()
    {
        var testFile = @"/melodee_test/tests/testgif.gif";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var convertor = new ImageConvertor(new ImageProcessor(), TestsBase.NewPluginsConfiguration());
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var convertorResult = await convertor.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(convertorResult);
            Assert.True(convertorResult.IsSuccess);
            Assert.NotNull(convertorResult.Data);

            var convertedFileInfo = new FileInfo(convertorResult.Data.FullName(dirInfo));
            Assert.True(convertedFileInfo.Exists);
        }
    }

    [Fact]
    public async Task ValidateConvertingTiffToJpgAsync()
    {
        var testFile = @"/melodee_test/tests/testtiff.tiff";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var convertor = new ImageConvertor(new ImageProcessor(), TestsBase.NewPluginsConfiguration());
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var convertorResult = await convertor.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(convertorResult);
            Assert.True(convertorResult.IsSuccess);
            Assert.NotNull(convertorResult.Data);

            var convertedFileInfo = new FileInfo(convertorResult.Data.FullName(dirInfo));
            Assert.True(convertedFileInfo.Exists);
        }
    }

    [Fact]
    public async Task ValidateConvertingWebpJpgAsync()
    {
        var testFile = @"/melodee_test/tests/testwebp.webp";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var convertor = new ImageConvertor(new ImageProcessor(), TestsBase.NewPluginsConfiguration());
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var convertorResult = await convertor.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(convertorResult);
            Assert.True(convertorResult.IsSuccess);
            Assert.NotNull(convertorResult.Data);

            var convertedFileInfo = new FileInfo(convertorResult.Data.FullName(dirInfo));
            Assert.True(convertedFileInfo.Exists);
        }
    }

    [Fact]
    public async Task ValidateConvertingBitmapJpgAsync()
    {
        var testFile = @"/melodee_test/tests/testbmp.bmp";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var convertor = new ImageConvertor(new ImageProcessor(), TestsBase.NewPluginsConfiguration());
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var convertorResult = await convertor.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(convertorResult);
            Assert.True(convertorResult.IsSuccess);
            Assert.NotNull(convertorResult.Data);

            var convertedFileInfo = new FileInfo(convertorResult.Data.FullName(dirInfo));
            Assert.True(convertedFileInfo.Exists);
        }
    }

    [Fact]
    public async Task ValidateConvertingNonImageFailsAsync()
    {
        var testFile = @"/melodee_test/tests/test.mp3";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var convertor = new ImageConvertor(new ImageProcessor(), TestsBase.NewPluginsConfiguration());
            var convertorResult = await convertor.ProcessFileAsync(new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            }, fileInfo.ToFileSystemInfo());
            Assert.NotNull(convertorResult);
            Assert.False(convertorResult.IsSuccess);
        }
    }
}
