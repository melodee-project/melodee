using FluentAssertions;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Services.ScriptEvaluation;
using Moq;
using Serilog;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

public sealed class DirectoryContextProviderTests
{
    private static ILogger CreateLogger()
    {
        return new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Console()
            .CreateLogger();
    }

    private static ISongPlugin CreatePlugin(bool handlesFile, OperationResult<Song> result)
    {
        var mock = new Mock<ISongPlugin>();
        mock.SetupGet(x => x.Id).Returns("test-plugin");
        mock.SetupGet(x => x.DisplayName).Returns("Test Plugin");
        mock.SetupProperty(x => x.IsEnabled, true);
        mock.SetupGet(x => x.SortOrder).Returns(0);
        mock.SetupGet(x => x.StopProcessing).Returns(false);
        mock.Setup(x => x.DoesHandleFile(It.IsAny<FileSystemDirectoryInfo>(), It.IsAny<FileSystemFileInfo>()))
            .Returns(handlesFile);
        mock.Setup(x => x.ProcessFileAsync(
                It.IsAny<FileSystemDirectoryInfo>(),
                It.IsAny<FileSystemFileInfo>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static Song CreateSong(int trackNumber)
    {
        return new Song
        {
            CrcHash = Guid.NewGuid().ToString("N"),
            File = new FileSystemFileInfo
            {
                Name = $"track-{trackNumber}.mp3",
                Size = 100
            },
            Tags =
            [
                new MetaTag<object?>
                {
                    Identifier = MetaTagIdentifier.TrackNumber,
                    Value = trackNumber
                }
            ]
        };
    }

    private static string CreateTempDirectory(params string[] files)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"melodee-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        foreach (var file in files)
        {
            File.WriteAllBytes(Path.Combine(directory, file), [1, 2, 3]);
        }

        return directory;
    }

    [Fact]
    public async Task BuildContextAsync_NoMediaFiles_HasTrackNumberGapsIsFalse()
    {
        var logger = CreateLogger();
        var provider = new DirectoryContextProvider(logger);
        var path = CreateTempDirectory("notes.txt");

        try
        {
            var context = await provider.BuildContextAsync(
                new FileSystemDirectoryInfo { Path = path, Name = "test" },
                [CreatePlugin(false, new OperationResult<Song> { Data = CreateSong(1) })],
                CancellationToken.None);

            context.MediaFilesCount.Should().Be(0);
            context.TrackNumbers.Should().BeEmpty();
            context.HasTrackNumberGaps.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Fact]
    public async Task BuildContextAsync_MediaFilesSequential_HasTrackNumberGapsIsFalse()
    {
        var logger = CreateLogger();
        var provider = new DirectoryContextProvider(logger);
        var path = CreateTempDirectory("track1.mp3", "track2.mp3", "track3.mp3");

        try
        {
            var songs = new Queue<Song>([CreateSong(1), CreateSong(2), CreateSong(3)]);
            var mock = new Mock<ISongPlugin>();
            mock.SetupGet(x => x.Id).Returns("test-plugin");
            mock.SetupGet(x => x.DisplayName).Returns("Test Plugin");
            mock.SetupProperty(x => x.IsEnabled, true);
            mock.SetupGet(x => x.SortOrder).Returns(0);
            mock.SetupGet(x => x.StopProcessing).Returns(false);
            mock.Setup(x => x.DoesHandleFile(It.IsAny<FileSystemDirectoryInfo>(), It.IsAny<FileSystemFileInfo>()))
                .Returns(true);
            mock.Setup(x => x.ProcessFileAsync(
                    It.IsAny<FileSystemDirectoryInfo>(),
                    It.IsAny<FileSystemFileInfo>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new OperationResult<Song> { Data = songs.Dequeue() });

            var context = await provider.BuildContextAsync(
                new FileSystemDirectoryInfo { Path = path, Name = "test" },
                [mock.Object],
                CancellationToken.None);

            context.MediaFilesCount.Should().Be(3);
            context.TrackNumbers.Should().BeEquivalentTo([1, 2, 3]);
            context.HasTrackNumberGaps.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }

    [Fact]
    public async Task BuildContextAsync_MediaFilesWithGap_HasTrackNumberGapsIsTrue()
    {
        var logger = CreateLogger();
        var provider = new DirectoryContextProvider(logger);
        var path = CreateTempDirectory("track1.mp3", "track3.mp3");

        try
        {
            var songs = new Queue<Song>([CreateSong(1), CreateSong(3)]);
            var mock = new Mock<ISongPlugin>();
            mock.SetupGet(x => x.Id).Returns("test-plugin");
            mock.SetupGet(x => x.DisplayName).Returns("Test Plugin");
            mock.SetupProperty(x => x.IsEnabled, true);
            mock.SetupGet(x => x.SortOrder).Returns(0);
            mock.SetupGet(x => x.StopProcessing).Returns(false);
            mock.Setup(x => x.DoesHandleFile(It.IsAny<FileSystemDirectoryInfo>(), It.IsAny<FileSystemFileInfo>()))
                .Returns(true);
            mock.Setup(x => x.ProcessFileAsync(
                    It.IsAny<FileSystemDirectoryInfo>(),
                    It.IsAny<FileSystemFileInfo>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new OperationResult<Song> { Data = songs.Dequeue() });

            var context = await provider.BuildContextAsync(
                new FileSystemDirectoryInfo { Path = path, Name = "test" },
                [mock.Object],
                CancellationToken.None);

            context.MediaFilesCount.Should().Be(2);
            context.TrackNumbers.Should().BeEquivalentTo([1, 3]);
            context.HasTrackNumberGaps.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(path, true);
        }
    }
}
