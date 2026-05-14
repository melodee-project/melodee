using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Plugins.MetaData.Directory.Models;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Processor;
using Melodee.Common.Plugins.Validation;
using SimpleFileVerification = Melodee.Common.Plugins.MetaData.Directory.SimpleFileVerification;

namespace Melodee.Tests.Common.Plugins.MetaData;

public class SimpleFileVerificationTests : TestsBase
{
    [Fact]
    public async Task ValidateSfvFileAsync()
    {
        var testFile = @"/melodee_test/inbound/00-k 2024/00-holy_truth-fire_proof-(dzb707)-web-2024.sfv";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var sfv = new SimpleFileVerification(Serializer,
                [
                    new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration())
                ], new AlbumValidator(NewPluginsConfiguration()),
                NewPluginsConfiguration());
            var sfvResult = await sfv.ProcessDirectoryAsync(new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/inbound/00-k 2024",
                Name = "00-k 2024"
            });
            Assert.NotNull(sfvResult);
            Assert.True(sfvResult.IsSuccess);
        }
    }

    [Fact]
    public async Task ValidateSfvFile2Async()
    {
        var testFile = @"/melodee_test/inbound/Swartz/00-edu_schwartz-with_me-(wthi110)-web-2024.sfv";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var sfv = new SimpleFileVerification(Serializer,
            [
                new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration())
            ], new AlbumValidator(NewPluginsConfiguration()), NewPluginsConfiguration());
            var sfvResult = await sfv.ProcessDirectoryAsync(new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/inbound/Swartz",
                Name = "Swartz"
            });
            Assert.NotNull(sfvResult);
            Assert.True(sfvResult.IsSuccess);
        }
    }

    [Fact]
    public async Task ValidateSfvFile3Async()
    {
        // A SFV which has CRCs that don't match should fail
        var testFile = @"/melodee_test/inbound/The Sound Of Melodic Techno Vol. 21/00-va-the_sound_of_melodic_techno_vol._21-web-2024.sfv";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var sfv = new SimpleFileVerification(Serializer,
                [
                    new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration())
                ], new AlbumValidator(NewPluginsConfiguration()),
                NewPluginsConfiguration());
            var sfvResult = await sfv.ProcessDirectoryAsync(new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/inbound/The Sound Of Melodic Techno Vol. 21",
                Name = "The Sound Of Melodic Techno Vol. 21/"
            });
            Assert.NotNull(sfvResult);
            Assert.False(sfvResult.IsSuccess);
        }
    }

    [Fact]
    public void ValidateModelFullLineParsing()
    {
        // <SongNumber>-<AlbumArtist>-<SongTitle>.mp3 <crcHash>
        var input = "01-avatar-bound_to_the_wall.mp3 7a84ce20";
        var shouldBe = new SfvLine
        {
            IsValid = false,
            CrcHash = "7a84ce20",
            FileSystemFileInfo = new FileSystemFileInfo
            {
                Name = "01-avatar-bound_to_the_wall.mp3",
                Size = 0
            }
        };
        var parsedModel = SimpleFileVerification.ModelFromSfvLine("/fake_path", input);
        Assert.Equal(shouldBe, parsedModel);
    }

    [Fact]
    public void ValidateModelNoAlbumTitleLineParsing()
    {
        var input = "01-pole_shift.mp3 aff033ca";
        var shouldBe = new SfvLine
        {
            IsValid = false,
            CrcHash = "aff033ca",
            FileSystemFileInfo = new FileSystemFileInfo
            {
                Name = "01-pole_shift.mp3",
                Size = 0
            }
        };
        var parsedModel = SimpleFileVerification.ModelFromSfvLine("/fake_path", input);
        Assert.Equal(shouldBe, parsedModel);
    }

    [Fact]
    public void ValidateModelOnlyFilenameLineParsing()
    {
        var input = "pole_shift.mp3 aff033ca";
        var shouldBe = new SfvLine
        {
            IsValid = false,
            CrcHash = "aff033ca",
            FileSystemFileInfo = new FileSystemFileInfo
            {
                Name = "pole_shift.mp3",
                Size = 0
            }
        };
        var parsedModel = SimpleFileVerification.ModelFromSfvLine("/fake_path", input);
        Assert.Equal(shouldBe, parsedModel);
    }
}
