using System.Text;
using FluentAssertions;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Jobs;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using Melodee.Common.Services.Scanning;
using Melodee.Tests.Common.Services;

namespace Melodee.Tests.Common.Jobs;

public class StagingAlbumRevalidationJobTests : ServiceTestBase
{
    [Fact]
    public async Task Execute_WithTrustedItunesArtistId_RevalidatesAndClearsInvalidArtistReason()
    {
        const string stagingPath = "/melodee_test/staging";
        const string albumDirectoryName = "iTunes Artist - [2026] Trusted Album";
        var albumDirectoryPath = Path.Combine(stagingPath, albumDirectoryName);
        var albumFilePath = Path.Combine(albumDirectoryPath, Album.JsonFileName);
        var mockFileSystem = new MockFileSystemService().SetDirectoryExists(stagingPath);
        var album = CreateStaleInvalidArtistAlbum(albumDirectoryPath, albumDirectoryName);
        mockFileSystem.SetAlbumForFile(albumFilePath, album);

        var albumDiscoveryService = new AlbumDiscoveryService(
            Logger,
            CacheManager,
            MockFactory(),
            MockConfigurationFactory(),
            mockFileSystem);
        var job = new StagingAlbumRevalidationJob(
            Logger,
            MockConfigurationFactory(),
            MockLibraryService(),
            albumDiscoveryService,
            GetArtistSearchEngineService(),
            Serializer,
            mockFileSystem);
        var context = new MelodeeJobExecutionContext(CancellationToken.None);

        await job.Execute(context);

        var result = context.Result.Should().BeOfType<ScanStepResult>().Subject;
        result.AlbumsRevalidated.Should().Be(1);

        var persistedBytes = await mockFileSystem.ReadAllBytesAsync(albumFilePath);
        var persistedAlbum = Serializer.Deserialize<Album>(Encoding.UTF8.GetString(persistedBytes));
        persistedAlbum.Should().NotBeNull();
        persistedAlbum!.StatusReasons.HasFlag(AlbumNeedsAttentionReasons.HasInvalidArtists).Should().BeFalse();
    }

    private static Album CreateStaleInvalidArtistAlbum(string albumDirectoryPath, string albumDirectoryName)
    {
        var artistName = "iTunes Artist";
        return new Album
        {
            Id = Guid.NewGuid(),
            AlbumType = AlbumType.Album,
            Artist = new Artist(
                artistName,
                artistName.ToNormalizedString()!,
                artistName)
            {
                ItunesId = "123456789"
            },
            Directory = new FileSystemDirectoryInfo
            {
                Path = albumDirectoryPath,
                Name = albumDirectoryName
            },
            OriginalDirectory = new FileSystemDirectoryInfo
            {
                Path = albumDirectoryPath,
                Name = albumDirectoryName
            },
            Status = AlbumStatus.Invalid,
            StatusReasons = AlbumNeedsAttentionReasons.HasInvalidArtists,
            ViaPlugins = [],
            Tags =
            [
                new MetaTag<object?> { Identifier = MetaTagIdentifier.AlbumArtist, Value = artistName },
                new MetaTag<object?> { Identifier = MetaTagIdentifier.Album, Value = "Trusted Album" },
                new MetaTag<object?> { Identifier = MetaTagIdentifier.RecordingYear, Value = "2026" }
            ]
        };
    }
}
