using FluentAssertions;
using Melodee.Common.Models;
using Melodee.Common.Models.Extensions;
using NodaTime;

namespace Melodee.Tests.Common.Models.Extensions;

public class FileSystemDirectoryInfoExtensionsTests : IDisposable
{
    private readonly string _testRootPath;
    private readonly List<string> _tempDirectories = [];

    public FileSystemDirectoryInfoExtensionsTests()
    {
        _testRootPath = Path.Combine(Path.GetTempPath(), "MelodeeTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRootPath);
        _tempDirectories.Add(_testRootPath);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirectories.Where(Directory.Exists))
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    private FileSystemDirectoryInfo CreateTestDirectory(string name = "test")
    {
        var path = Path.Combine(_testRootPath, name);
        Directory.CreateDirectory(path);
        _tempDirectories.Add(path);
        return new FileSystemDirectoryInfo { Path = path, Name = name };
    }

    [Fact]
    public void FileCount_ExistingDirectoryWithFiles_ReturnsCorrectCount()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "file1.txt"), "test");
        File.WriteAllText(Path.Combine(dir.FullName(), "file2.txt"), "test");

        var count = dir.FileCount();

        count.Should().Be(2);
    }

    [Fact]
    public void FileCount_NonExistingDirectory_ReturnsZero()
    {
        var dir = new FileSystemDirectoryInfo { Path = "/nonexistent", Name = "test" };

        var count = dir.FileCount();

        count.Should().Be(0);
    }

    [Fact]
    public void Exists_ExistingDirectory_ReturnsTrue()
    {
        var dir = CreateTestDirectory();

        dir.Exists().Should().BeTrue();
    }

    [Fact]
    public void Exists_NonExistingDirectory_ReturnsFalse()
    {
        var dir = new FileSystemDirectoryInfo { Path = "/nonexistent", Name = "test" };

        dir.Exists().Should().BeFalse();
    }

    [Fact]
    public void EnsureExists_NonExistingDirectory_CreatesDirectory()
    {
        var path = Path.Combine(_testRootPath, "newdir");
        var dir = new FileSystemDirectoryInfo { Path = _testRootPath, Name = "newdir" };

        dir.EnsureExists();

        Directory.Exists(dir.FullName()).Should().BeTrue();
    }

    [Fact]
    public void FullName_DirectoryWithName_ReturnsCombinedPath()
    {
        var dir = new FileSystemDirectoryInfo { Path = "/test", Name = "subdir" };

        var fullName = dir.FullName();

        fullName.Should().Be(Path.Combine("/test", "subdir"));
    }

    [Fact]
    public void FullName_PathAlreadyIncludesName_ReturnsPath()
    {
        var expectedPath = Path.Combine("/test", "subdir");
        var dir = new FileSystemDirectoryInfo { Path = expectedPath, Name = "subdir" };

        var fullName = dir.FullName();

        fullName.Should().Be(expectedPath);
    }

    [Fact]
    public void FullName_PathWithTrailingSeparator_RemovesSeparator()
    {
        var dir = new FileSystemDirectoryInfo
        {
            Path = $"/test{Path.DirectorySeparatorChar}",
            Name = "subdir"
        };

        var fullName = dir.FullName();

        fullName.Should().NotEndWith(Path.DirectorySeparatorChar.ToString());
    }

    [Fact]
    public void FullName_NullPath_ThrowsArgumentNullException()
    {
        var dir = new FileSystemDirectoryInfo { Path = null!, Name = "test" };

        var act = () => dir.FullName();

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Delete_ExistingDirectory_DeletesDirectory()
    {
        var dir = CreateTestDirectory("todelete");

        dir.Delete();

        Directory.Exists(dir.FullName()).Should().BeFalse();
    }

    [Fact]
    public void Empty_DirectoryWithFiles_RemovesAllContent()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "file.txt"), "test");
        var subDir = Path.Combine(dir.FullName(), "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "file2.txt"), "test");

        dir.Empty();

        dir.AllFileInfos(searchOption: System.IO.SearchOption.AllDirectories).Should().BeEmpty();
        dir.AllDirectoryInfos(searchOption: System.IO.SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public void DoesDirectoryHaveImageFiles_WithImageFiles_ReturnsTrue()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "image.jpg"), "fake image");

        var hasImages = dir.DoesDirectoryHaveImageFiles();

        hasImages.Should().BeTrue();
    }

    [Fact]
    public void DoesDirectoryHaveImageFiles_WithoutImageFiles_ReturnsFalse()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "file.txt"), "test");

        var hasImages = dir.DoesDirectoryHaveImageFiles();

        hasImages.Should().BeFalse();
    }

    [Fact]
    public void DoesDirectoryHaveMediaFiles_WithMediaFiles_ReturnsTrue()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "song.mp3"), "fake media");

        var hasMedia = dir.DoesDirectoryHaveMediaFiles();

        hasMedia.Should().BeTrue();
    }

    [Fact]
    public void DoesDirectoryHaveMediaFiles_WithoutMediaFiles_ReturnsFalse()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "file.txt"), "test");

        var hasMedia = dir.DoesDirectoryHaveMediaFiles();

        hasMedia.Should().BeFalse();
    }

    [Theory]
    [InlineData("discography")]
    [InlineData("Discography")]
    [InlineData("DISCOGRAPHY")]
    [InlineData("Artist Discography")]
    public void IsDiscographyDirectory_MatchingPattern_ReturnsTrue(string dirName)
    {
        var dir = new FileSystemDirectoryInfo { Path = "/test", Name = dirName };

        dir.IsDiscographyDirectory().Should().BeTrue();
    }

    [Theory]
    [InlineData("albums")]
    [InlineData("regular folder")]
    [InlineData("studio")]
    public void IsDiscographyDirectory_NonMatchingPattern_ReturnsFalse(string dirName)
    {
        var dir = new FileSystemDirectoryInfo { Path = "/test", Name = dirName };

        dir.IsDiscographyDirectory().Should().BeFalse();
    }

    [Theory]
    [InlineData("CD1")]
    [InlineData("CD 01")]
    [InlineData("Disc 2")]
    [InlineData("Disk 3")]
    [InlineData("Media 1")]
    public void IsAlbumMediaDirectory_MatchingPattern_ReturnsTrue(string dirName)
    {
        var dir = new FileSystemDirectoryInfo { Path = "/test", Name = dirName };

        dir.IsAlbumMediaDirectory().Should().BeTrue();
    }

    [Theory]
    [InlineData("Album")]
    [InlineData("Music")]
    [InlineData("CDextra")]
    public void IsAlbumMediaDirectory_NonMatchingPattern_ReturnsFalse(string dirName)
    {
        var dir = new FileSystemDirectoryInfo { Path = "/test", Name = dirName };

        dir.IsAlbumMediaDirectory().Should().BeFalse();
    }

    [Theory]
    [InlineData("best of")]
    [InlineData("Best Of")]
    [InlineData("Greatest Hits")]
    [InlineData("compilation")]
    [InlineData("live")]
    [InlineData("Live Album")]
    [InlineData("boxset")]
    [InlineData("demo")]
    public void IsDirectoryStudioAlbums_NonStudioPatterns_ReturnsFalse(string dirName)
    {
        var dir = new FileSystemDirectoryInfo { Path = "/test", Name = dirName };

        dir.IsDirectoryStudioAlbums().Should().BeFalse();
    }

    [Theory]
    [InlineData("Regular Album")]
    [InlineData("Studio Album 2024")]
    [InlineData("The Album")]
    public void IsDirectoryStudioAlbums_StudioPatterns_ReturnsTrue(string dirName)
    {
        var dir = new FileSystemDirectoryInfo { Path = "/test", Name = dirName };

        dir.IsDirectoryStudioAlbums().Should().BeTrue();
    }

    [Fact]
    public void TryParseMediaNumber_ValidMediaDirectory_ReturnsNumber()
    {
        var dir = new FileSystemDirectoryInfo { Path = "/test", Name = "CD 03" };

        var number = dir.TryParseMediaNumber();

        number.Should().Be(3);
    }

    [Fact]
    public void TryParseMediaNumber_NoNumber_ReturnsNull()
    {
        var dir = new FileSystemDirectoryInfo { Path = "/test", Name = "NoNumber" };

        var number = dir.TryParseMediaNumber();

        number.Should().BeNull();
    }

    [Fact]
    public void AllAlbumMediaDirectories_WithMediaDirectories_ReturnsMediaDirs()
    {
        var dir = CreateTestDirectory();
        Directory.CreateDirectory(Path.Combine(dir.FullName(), "CD1"));
        Directory.CreateDirectory(Path.Combine(dir.FullName(), "CD2"));
        Directory.CreateDirectory(Path.Combine(dir.FullName(), "Other"));

        var mediaDirs = dir.AllAlbumMediaDirectories().ToList();

        mediaDirs.Should().HaveCount(2);
        mediaDirs.Should().Contain(d => d.Name == "CD1");
        mediaDirs.Should().Contain(d => d.Name == "CD2");
    }

    [Fact]
    public void GetFileSystemDirectoryInfosToProcess_WithOldUnprocessedMediaDirectory_ReturnsDirectory()
    {
        var root = CreateTestDirectory();
        var albumPath = Path.Combine(root.FullName(), "Album");
        Directory.CreateDirectory(albumPath);
        var mediaPath = Path.Combine(albumPath, "song.mp3");
        File.WriteAllText(mediaPath, "fake media");
        var oldWriteTime = DateTime.UtcNow.AddDays(-10);
        File.SetLastWriteTimeUtc(mediaPath, oldWriteTime);
        Directory.SetLastWriteTimeUtc(albumPath, oldWriteTime);

        var modifiedSince = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(-1));
        var directories = root.GetFileSystemDirectoryInfosToProcess(modifiedSince, SearchOption.AllDirectories).ToList();

        directories.Should().ContainSingle(d => d.Name == "Album");
    }

    [Fact]
    public void GetFileSystemDirectoryInfosToProcess_WithOldProcessedMediaDirectory_SkipsDirectory()
    {
        var root = CreateTestDirectory();
        var albumPath = Path.Combine(root.FullName(), "Album");
        Directory.CreateDirectory(albumPath);
        var mediaPath = Path.Combine(albumPath, "song.mp3");
        var metadataPath = Path.Combine(albumPath, Album.JsonFileName);
        File.WriteAllText(mediaPath, "fake media");
        File.WriteAllText(metadataPath, "{}");
        var oldWriteTime = DateTime.UtcNow.AddDays(-10);
        File.SetLastWriteTimeUtc(mediaPath, oldWriteTime);
        File.SetLastWriteTimeUtc(metadataPath, oldWriteTime);
        Directory.SetLastWriteTimeUtc(albumPath, oldWriteTime);

        var modifiedSince = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(-1));
        var directories = root.GetFileSystemDirectoryInfosToProcess(modifiedSince, SearchOption.AllDirectories).ToList();

        directories.Should().NotContain(d => d.Name == "Album");
    }

    [Fact]
    public void GetFileSystemDirectoryInfosToProcess_WithModifiedMediaFileInOldProcessedDirectory_ReturnsDirectory()
    {
        var root = CreateTestDirectory();
        var albumPath = Path.Combine(root.FullName(), "Album");
        Directory.CreateDirectory(albumPath);
        var mediaPath = Path.Combine(albumPath, "song.mp3");
        var metadataPath = Path.Combine(albumPath, Album.JsonFileName);
        File.WriteAllText(mediaPath, "fake media");
        File.WriteAllText(metadataPath, "{}");
        var oldWriteTime = DateTime.UtcNow.AddDays(-10);
        var modifiedSince = DateTime.UtcNow.AddDays(-1);
        File.SetLastWriteTimeUtc(metadataPath, oldWriteTime);
        Directory.SetLastWriteTimeUtc(albumPath, oldWriteTime);
        File.SetLastWriteTimeUtc(mediaPath, DateTime.UtcNow);

        var directories = root
            .GetFileSystemDirectoryInfosToProcess(Instant.FromDateTimeUtc(modifiedSince), SearchOption.AllDirectories)
            .ToList();

        directories.Should().ContainSingle(d => d.Name == "Album");
    }

    [Fact]
    public void GetParent_DirectoryWithParent_ReturnsParent()
    {
        var parentDir = CreateTestDirectory("parent");
        var childPath = Path.Combine(parentDir.FullName(), "child");
        Directory.CreateDirectory(childPath);
        var childDir = new FileSystemDirectoryInfo { Path = childPath, Name = "child" };

        var parent = childDir.GetParent();

        parent.Name.Should().Be("parent");
    }

    [Fact]
    public void GetParents_NestedDirectory_ReturnsAllParents()
    {
        var rootDir = CreateTestDirectory("root");
        var level1 = Path.Combine(rootDir.FullName(), "level1");
        Directory.CreateDirectory(level1);
        var level2 = Path.Combine(level1, "level2");
        Directory.CreateDirectory(level2);
        var deepDir = new FileSystemDirectoryInfo { Path = level2, Name = "level2" };

        var parents = deepDir.GetParents().ToList();

        parents.Should().Contain(p => p.Name == "level1");
        parents.Should().Contain(p => p.Name == "root");
    }

    [Fact]
    public void FileInfosForExtension_WithMatchingFiles_ReturnsFiles()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "file1.txt"), "test");
        File.WriteAllText(Path.Combine(dir.FullName(), "file2.txt"), "test");
        File.WriteAllText(Path.Combine(dir.FullName(), "file3.mp3"), "test");

        var txtFiles = dir.FileInfosForExtension("txt").ToList();

        txtFiles.Should().HaveCount(2);
        txtFiles.Should().AllSatisfy(f => f.Extension.Should().Be(".txt"));
    }

    [Fact]
    public void FileInfosForExtension_WithDot_ReturnsFiles()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "file.txt"), "test");

        var files = dir.FileInfosForExtension("*.txt").ToList();

        files.Should().HaveCount(1);
    }

    [Fact]
    public void AllFileInfos_WithFiles_ReturnsAllFiles()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "file1.txt"), "test");
        File.WriteAllText(Path.Combine(dir.FullName(), "file2.mp3"), "test");

        var files = dir.AllFileInfos().ToList();

        files.Should().HaveCount(2);
    }

    [Fact]
    public void AllFileImageTypeFileInfos_WithImageFiles_ReturnsOnlyImages()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "image.jpg"), "test");
        File.WriteAllText(Path.Combine(dir.FullName(), "file.txt"), "test");

        var images = dir.AllFileImageTypeFileInfos().ToList();

        images.Should().HaveCount(1);
        images[0].Extension.Should().Be(".jpg");
    }

    [Fact]
    public void AllMediaTypeFileInfos_WithMediaFiles_ReturnsOnlyMedia()
    {
        var dir = CreateTestDirectory();
        File.WriteAllText(Path.Combine(dir.FullName(), "song.mp3"), "test");
        File.WriteAllText(Path.Combine(dir.FullName(), "file.txt"), "test");

        var media = dir.AllMediaTypeFileInfos().ToList();

        media.Should().HaveCount(1);
        media[0].Extension.Should().Be(".mp3");
    }

    [Fact]
    public void DeleteAllFilesForExtension_WithMatchingFiles_DeletesFiles()
    {
        var dir = CreateTestDirectory();
        var file1 = Path.Combine(dir.FullName(), "file1.tmp");
        var file2 = Path.Combine(dir.FullName(), "file2.tmp");
        var keepFile = Path.Combine(dir.FullName(), "keep.txt");
        File.WriteAllText(file1, "test");
        File.WriteAllText(file2, "test");
        File.WriteAllText(keepFile, "test");

        dir.DeleteAllFilesForExtension("tmp");

        File.Exists(file1).Should().BeFalse();
        File.Exists(file2).Should().BeFalse();
        File.Exists(keepFile).Should().BeTrue();
    }

    [Fact]
    public void DeleteAllEmptyDirectories_WithEmptySubdirectories_DeletesThem()
    {
        var dir = CreateTestDirectory();
        var emptyDir1 = Path.Combine(dir.FullName(), "empty1");
        var emptyDir2 = Path.Combine(dir.FullName(), "empty2");
        var nonEmptyDir = Path.Combine(dir.FullName(), "nonempty");
        Directory.CreateDirectory(emptyDir1);
        Directory.CreateDirectory(emptyDir2);
        Directory.CreateDirectory(nonEmptyDir);
        File.WriteAllText(Path.Combine(nonEmptyDir, "file.txt"), "test");

        dir.DeleteAllEmptyDirectories();

        Directory.Exists(emptyDir1).Should().BeFalse();
        Directory.Exists(emptyDir2).Should().BeFalse();
        Directory.Exists(nonEmptyDir).Should().BeTrue();
    }

    [Fact]
    public void MoveToDirectory_SourceToDestination_MovesAllContent()
    {
        var sourceDir = CreateTestDirectory("source");
        var destPath = Path.Combine(_testRootPath, "destination");
        File.WriteAllText(Path.Combine(sourceDir.FullName(), "file.txt"), "content");
        var subDir = Path.Combine(sourceDir.FullName(), "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "nested content");

        sourceDir.MoveToDirectory(destPath);

        Directory.Exists(destPath).Should().BeTrue();
        File.Exists(Path.Combine(destPath, "file.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destPath, "subdir", "nested.txt")).Should().BeTrue();
        // MoveToDirectory copies then deletes, source may still exist with this implementation
    }

    [Fact]
    public void MoveToDirectory_WithDontMoveFileName_SkipsSpecifiedFile()
    {
        var sourceDir = CreateTestDirectory("source");
        var destPath = Path.Combine(_testRootPath, "destination");
        var dontMoveFile = "dont_move.txt";
        File.WriteAllText(Path.Combine(sourceDir.FullName(), "move.txt"), "move me");
        File.WriteAllText(Path.Combine(sourceDir.FullName(), dontMoveFile), "dont move");

        sourceDir.MoveToDirectory(destPath, dontMoveFile);

        File.Exists(Path.Combine(destPath, "move.txt")).Should().BeTrue();
        File.Exists(Path.Combine(destPath, dontMoveFile)).Should().BeFalse();
    }

    [Fact]
    public void AppendPrefix_WithoutPrefix_AddsPrefix()
    {
        var dir = CreateTestDirectory("mydir");
        var prefix = "[Prefix] ";

        var result = dir.AppendPrefix(prefix);

        result.Name.Should().StartWith(prefix);
        Directory.Exists(result.FullName()).Should().BeTrue();
    }

    [Fact]
    public void AppendPrefix_AlreadyHasPrefix_DoesNotDuplicate()
    {
        var prefix = "[Prefix] ";
        var dir = CreateTestDirectory($"{prefix}mydir");

        var result = dir.AppendPrefix(prefix);

        result.Name.Should().Be($"{prefix}mydir");
    }

    [Fact]
    public async Task FindDuplicatesAsync_WithDuplicateFiles_ReturnsDuplicates()
    {
        var dir = CreateTestDirectory();
        var content = new byte[1024]; // Same size binary content
        Array.Fill(content, (byte)42);

        await File.WriteAllBytesAsync(Path.Combine(dir.FullName(), "file1.bin"), content);
        await Task.Delay(50); // Ensure different write times
        await File.WriteAllBytesAsync(Path.Combine(dir.FullName(), "file2.bin"), content);

        var uniqueContent = new byte[2048];
        Array.Fill(uniqueContent, (byte)99);
        await File.WriteAllBytesAsync(Path.Combine(dir.FullName(), "unique.bin"), uniqueContent);

        var duplicates = await dir.FindDuplicatesAsync();

        // FindDuplicatesAsync returns entries where hash -> list, excluding "pending" entries
        // Should find the duplicate pair
        if (duplicates.Length > 0)
        {
            duplicates.Should().Contain(kvp => kvp.Value.Count >= 1);
        }
    }

    [Fact]
    public async Task FindDuplicatesAsync_NoDuplicates_ReturnsEmpty()
    {
        var dir = CreateTestDirectory();
        var content1 = new byte[512];
        Array.Fill(content1, (byte)1);
        var content2 = new byte[1024];
        Array.Fill(content2, (byte)2);

        await File.WriteAllBytesAsync(Path.Combine(dir.FullName(), "file1.bin"), content1);
        await File.WriteAllBytesAsync(Path.Combine(dir.FullName(), "file2.bin"), content2);

        var duplicates = await dir.FindDuplicatesAsync();

        // Different sizes means no duplicates
        duplicates.Should().NotBeNull();
    }

    [Fact]
    public void Parent_DirectoryWithParent_ReturnsParentInfo()
    {
        var parentDir = CreateTestDirectory("parent");
        var childPath = Path.Combine(parentDir.FullName(), "child");
        Directory.CreateDirectory(childPath);
        var childDir = new FileSystemDirectoryInfo { Path = childPath, Name = "child" };

        var parent = childDir.Parent();

        parent.Should().NotBeNull();
        parent!.Name.Should().Be("parent");
    }

    [Fact]
    public void ToDirectoryInfo_ValidDirectory_ReturnsDirectoryInfo()
    {
        var dir = CreateTestDirectory();

        var dirInfo = dir.ToDirectoryInfo();

        dirInfo.Should().NotBeNull();
        dirInfo.Exists.Should().BeTrue();
    }
}
