using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Imaging;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Processor;
using Melodee.Common.Utility;
using SongModel = Melodee.Common.Models.Song;

namespace Melodee.Tests.Common.Plugins.MetaData;

public class MetaTagTests : TestsBase
{
    [Fact]
    public async Task ValidateLoadingTagsForSimpleMp3FileAsync()
    {
        var testFile = @"/melodee_test/tests/test.mp3";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var metaTag = new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration());
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var tagResult = await metaTag.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(tagResult);
            Assert.True(tagResult.IsSuccess);
            Assert.NotNull(tagResult.Data);
            Assert.NotNull(tagResult.Data.Tags);
            Assert.NotNull(tagResult.Data.File);
            if (tagResult.Data.MediaAudios != null)
            {
                var bitRate = tagResult.Data.MediaAudios.FirstOrDefault(x => x.Identifier == MediaAudioIdentifier.BitRate);
                Assert.NotNull(bitRate);
                var parsedBitRate = SafeParser.ToNumber<int>(bitRate.Value);
                Assert.True(parsedBitRate > 0);

                var bitRateFromParser = SafeParser.ToNumber<int>(tagResult.Data.MediaAudios?.FirstOrDefault(x => x.Identifier == MediaAudioIdentifier.BitRate)?.Value);
                Assert.Equal(parsedBitRate, bitRateFromParser);
            }

            Assert.Equal(fileInfo.FullName, tagResult.Data.File.FullName(dirInfo));
            Assert.NotNull(tagResult.Data.Title()?.Nullify());
        }
    }

    [Fact]
    public async Task ValidateLoadingTagsForSimpleMp3FileAsync2()
    {
        var testFile = @"/melodee_test/tests/test4.mp3";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var metaTag = new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration());
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var tagResult = await metaTag.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(tagResult);
            Assert.True(tagResult.IsSuccess);
            Assert.NotNull(tagResult.Data);
            Assert.NotNull(tagResult.Data.Tags);
            Assert.NotNull(tagResult.Data.File);
            Assert.Equal(fileInfo.FullName, tagResult.Data.File.FullName(dirInfo));
            Assert.NotNull(tagResult.Data.Title()?.Nullify());
        }
    }

    [Fact]
    public async Task ValidateLoadingTagsForBorkedMp3FileAsync()
    {
        var testFile = @"/melodee_test/tests/borked.mp3";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var metaTag = new IdSharpMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), NewPluginsConfiguration());
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var tagResult = await metaTag.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(tagResult);
            Assert.True(tagResult.IsSuccess);
            Assert.NotNull(tagResult.Data);
            Assert.False(tagResult.Data.IsValid(NewConfiguration()));
            Assert.NotNull(tagResult.Data.Tags);
            Assert.NotNull(tagResult.Data.File);
            Assert.Equal(fileInfo.FullName, tagResult.Data.File.FullName(dirInfo));
        }
    }

    [Fact]
    public async Task ValidateLoadingEditingAndSavingTagsForSimpleMp3FileAsync()
    {
        var testFile = @"/melodee_test/tests/test.mp3";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var metaTag = new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration());
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var tagResult = await metaTag.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(tagResult);
            Assert.True(tagResult.IsSuccess);
            Assert.NotNull(tagResult.Data);
            Assert.NotNull(tagResult.Data.Tags);
            Assert.NotNull(tagResult.Data.File);
            Assert.Equal(fileInfo.FullName, tagResult.Data.File.FullName(dirInfo));
            Assert.NotNull(tagResult.Data.Title()?.Nullify());

            // Use a simple string without spaces to avoid MP3 tag parsing issues
            var newAlbumValue = $"TestAlbum{DateTime.Now.Ticks % 10000}";
            var tags = tagResult.Data.Tags.ToList();
            tags.Remove(tags.First(x => x.Identifier == MetaTagIdentifier.Album));
            tags.Add(new MetaTag<object?>
            {
                Identifier = MetaTagIdentifier.Album,
                Value = newAlbumValue
            });
            var tagUpdateResult = await metaTag.UpdateSongAsync(dirInfo, new SongModel
            {
                CrcHash = "12345678",
                File = fileInfo.ToFileSystemInfo(),
                Tags = tags
            });
            Assert.NotNull(tagUpdateResult);
            Assert.True(tagUpdateResult.IsSuccess);
            Assert.True(tagUpdateResult.Data);

            tagResult = await metaTag.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(tagResult);
            Assert.True(tagResult.IsSuccess);
            Assert.NotNull(tagResult.Data);
            Assert.NotNull(tagResult.Data.Tags);
            Assert.NotNull(tagResult.Data.File);
            Assert.Equal(fileInfo.FullName, tagResult.Data.File.FullName(dirInfo));
            Assert.NotNull(tagResult.Data.AlbumTitle()?.Nullify());
            Assert.Equal(newAlbumValue, tagResult.Data.AlbumTitle(), true);
        }
    }

    [Fact]
    public async Task ValidateLoadingTagsForMp3FileAsync()
    {
        var testFile = @"/melodee_test/tests/test2.mp3";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var metaTag = new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration());
            var tagResult = await metaTag.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(tagResult);
            Assert.True(tagResult.IsSuccess);
            Assert.NotNull(tagResult.Data);

            var Song = tagResult.Data;

            Assert.NotNull(Song.Tags);
            Assert.NotNull(Song.File);
            Assert.Equal(fileInfo.FullName, Song.File.FullName(dirInfo));
            Assert.NotNull(Song.Title()?.Nullify());
            Assert.False(Song.TitleHasUnwantedText());
            Assert.True(Song.Duration() > 0);
        }
    }

    [Fact]
    public async Task ValidateLoadingTagsForMp3Test4FileAsync()
    {
        var testFile = @"/melodee_test/tests/test4.mp3";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var metaTag = new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration());
            var tagResult = await metaTag.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(tagResult);
            Assert.True(tagResult.IsSuccess);
            Assert.NotNull(tagResult.Data);

            var Song = tagResult.Data;

            Assert.NotNull(Song.Tags);
            Assert.NotNull(Song.File);
            Assert.Equal(fileInfo.FullName, Song.File.FullName(dirInfo));
            Assert.NotNull(Song.Title()?.Nullify());
            Assert.NotEmpty(Song.ToSongFileName(dirInfo));
        }
    }

    [Fact]
    public async Task ValidateMultipleSongArtistForMp3Async()
    {
        var testFile = @"/melodee_test/tests/multipleSongArtistsTest.mp3";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var metaTag = new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration());
            var tagResult = await metaTag.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(tagResult);
            Assert.True(tagResult.IsSuccess);
            Assert.NotNull(tagResult.Data);

            var Song = tagResult.Data;

            Assert.NotNull(Song.Tags);
            Assert.NotNull(Song.File);
            var albumArtist = Song.Tags!.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.AlbumArtist);
            Assert.NotNull(albumArtist?.Value);
            var songArtists = Song.Tags!.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.Artist);
            Assert.NotNull(songArtists?.Value);
        }
    }

    [Fact]
    public async Task ValidateSongWithSelfTitledArtristAsync()
    {
        var testFile = @"/melodee_test/tests/songFromSelfTitledAlbum.mp3";
        var fileInfo = new FileInfo(testFile);
        if (fileInfo.Exists)
        {
            var dirInfo = new FileSystemDirectoryInfo
            {
                Path = @"/melodee_test/tests/",
                Name = "tests"
            };
            var metaTag = new AtlMetaTag(new MetaTagsProcessor(NewPluginsConfiguration(), Serializer), new ImageProcessor(), GetImageConvertor(), GetImageValidator(), NewPluginsConfiguration());
            var tagResult = await metaTag.ProcessFileAsync(dirInfo, fileInfo.ToFileSystemInfo());
            Assert.NotNull(tagResult);
            Assert.True(tagResult.IsSuccess);
            Assert.NotNull(tagResult.Data);

            var song = tagResult.Data;

            Assert.NotNull(song.Tags);
            Assert.NotNull(song.File);
            var albumArtist = song.Tags!.FirstOrDefault(x => x.Identifier == MetaTagIdentifier.AlbumArtist);
            Assert.NotNull(albumArtist?.Value);
        }
    }
}
