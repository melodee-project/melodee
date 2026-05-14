using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Processor;
using Serilog;
using CueSheet = Melodee.Common.Plugins.MetaData.Directory.CueSheet;

namespace Melodee.Tests.Common.Plugins.MetaData;

public class CUETests : TestsBase
{
    [Fact]
    public async Task ValidateCueSheetParsingAsync()
    {
        var testFile = @"/melodee_test/inbound/Pixel_-_Reality_Strikes_Back-2004-MYCEL/01-pixel_-_reality_strikes_back-mycel.cue";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var parsed = await CueSheet.ParseFileAsync(testFile, NewConfiguration());
            Assert.NotNull(parsed);
            Assert.True(parsed.IsValid);
        }
    }

    [Fact]
    public async Task ValidateCueSheetFileAsync()
    {
        var testFile = @"/melodee_test/inbound/Pixel_-_Reality_Strikes_Back-2004-MYCEL/01-pixel_-_reality_strikes_back-mycel.cue";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Verbose()
                .WriteTo.Console()
                .WriteTo.File("/melodee_test/log.txt", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            var cueSheet = new CueSheet(
                Serializer,
                [
                    new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration())
                ], GetAlbumValidator(), NewPluginsConfiguration());

            var sfvResult = await cueSheet.ProcessDirectoryAsync(new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/inbound/Pixel_-_Reality_Strikes_Back-2004-MYCEL",
                Name = "Pixel_-_Reality_Strikes_Back-2004-MYCEL"
            });
            Assert.NotNull(sfvResult);
            Assert.True(sfvResult.IsSuccess);
        }
    }
}
