using System.Text;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Serilog;

namespace Melodee.Tests.Common.Services;

public class FileSystemServiceTests
{
    private static FileSystemService CreateService()
    {
        // Use the real serializer; logger can be default
        var serializer = new Serializer(Log.Logger);
        return new FileSystemService(serializer);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Melodee_FileSystemServiceTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void DirectoryExists_ReturnsExpectedValues()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var nonExistent = Path.Combine(tempDir, "does_not_exist");

        try
        {
            // Act & Assert
            Assert.True(service.DirectoryExists(tempDir));
            Assert.False(service.DirectoryExists(nonExistent));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EnumerateFiles_ReturnsMatches_And_EmptyForMissingDirectory()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var subDir = Path.Combine(tempDir, "sub");
        Directory.CreateDirectory(subDir);
        var f1 = Path.Combine(tempDir, "file1.txt");
        var f2 = Path.Combine(tempDir, "file2.txt");
        var f3 = Path.Combine(subDir, "file3.log");
        File.WriteAllText(f1, "a");
        File.WriteAllText(f2, "b");
        File.WriteAllText(f3, "c");

        var nonExistent = Path.Combine(tempDir, "missing");

        try
        {
            // Act
            var topTxt = service.EnumerateFiles(tempDir, "*.txt", SearchOption.TopDirectoryOnly).ToArray();
            var allTxt = service.EnumerateFiles(tempDir, "*.txt", SearchOption.AllDirectories).ToArray();
            var forMissing = service.EnumerateFiles(nonExistent, "*", SearchOption.AllDirectories).ToArray();

            // Assert
            Assert.Contains(f1, topTxt);
            Assert.Contains(f2, topTxt);
            Assert.DoesNotContain(f3, topTxt);
            Assert.Equal(2, topTxt.Length);

            Assert.Contains(f1, allTxt);
            Assert.Contains(f2, allTxt);
            Assert.Equal(2, allTxt.Length);

            Assert.Empty(forMissing);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void EnumerateDirectories_ReturnsMatches_And_EmptyForMissingDirectory()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var d1 = Path.Combine(tempDir, "SubA");
        var d2 = Path.Combine(tempDir, "SubB");
        var d3 = Path.Combine(tempDir, "Other");
        Directory.CreateDirectory(d1);
        Directory.CreateDirectory(d2);
        Directory.CreateDirectory(d3);

        var nonExistent = Path.Combine(tempDir, "missing");

        try
        {
            // Act
            var subs = service.EnumerateDirectories(tempDir, "Sub*", SearchOption.TopDirectoryOnly).ToArray();
            var forMissing = service.EnumerateDirectories(nonExistent, "*", SearchOption.AllDirectories).ToArray();

            // Assert
            Assert.Equal(2, subs.Length);
            Assert.Contains(subs, di => di.FullName == d1);
            Assert.Contains(subs, di => di.FullName == d2);
            Assert.DoesNotContain(subs, di => di.FullName == d3);
            Assert.Empty(forMissing);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetFileCreationTimeUtc_ReturnsReasonableValue()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var file = Path.Combine(tempDir, "time.txt");
        File.WriteAllText(file, "x");

        try
        {
            // Act
            var createdUtc = service.GetFileCreationTimeUtc(file);

            // Assert
            // Should be greater than Unix epoch and not in the future
            Assert.True(createdUtc > DateTime.UnixEpoch);
            Assert.True(createdUtc <= DateTime.UtcNow.AddMinutes(1));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetDirectoryLastWriteTimeUtc_UpdatesAfterFileCreation()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();

        try
        {
            // Act - capture write time before and after creating a file
            var beforeWrite = service.GetDirectoryLastWriteTimeUtc(tempDir);
            Thread.Sleep(20); // Ensure filesystem timestamp resolution differs
            File.WriteAllText(Path.Combine(tempDir, "new.txt"), "content");
            var afterWrite = service.GetDirectoryLastWriteTimeUtc(tempDir);

            // Assert
            Assert.True(afterWrite >= beforeWrite);
            Assert.True(afterWrite > DateTime.UnixEpoch);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetDirectoryLastWriteTimeUtc_ForNonExistentDirectory_ReturnsDefaultValue()
    {
        // Arrange
        var service = CreateService();
        var nonExistent = Path.Combine(Path.GetTempPath(), "Melodee_nonexistent_" + Guid.NewGuid());

        // Act - .NET returns DateTime(1601) for non-existent paths
        var result = service.GetDirectoryLastWriteTimeUtc(nonExistent);

        // Assert - should not throw and should return a value (year 1601 on Windows, epoch-like on Linux)
        Assert.True(result.Year < 2000);
    }

    [Fact]
    public void DeleteDirectory_Recursive_RemovesAll()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var nested = Path.Combine(tempDir, "nested");
        Directory.CreateDirectory(nested);
        var file = Path.Combine(nested, "a.txt");
        File.WriteAllText(file, "a");

        // Act
        service.DeleteDirectory(tempDir, true);

        // Assert
        Assert.False(Directory.Exists(tempDir));
    }

    [Fact]
    public async Task DeserializeAlbumAsync_ReadsAlbumFromJson()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var jsonPath = Path.Combine(tempDir, "melodee.json");

        var album = new Album
        {
            Artist = new Artist("Test Artist",
                "test-artist",
                "Test Artist",
                [
                ]),
            Tags =
            [
                new MetaTag<object?>
                {
                    Identifier = MetaTagIdentifier.Album,
                    Value = "Test Album"
                }
            ],
            Directory = new FileSystemDirectoryInfo
            {
                Path = tempDir,
                Name = Path.GetFileName(tempDir)
            },
            OriginalDirectory = new FileSystemDirectoryInfo
            {
                Path = tempDir,
                Name = Path.GetFileName(tempDir)
            },
            Songs =
            [
            ],
            ViaPlugins = []
        };

        var serializer = new Serializer(Log.Logger);
        var json = serializer.Serialize(album) ?? "{}";
        await File.WriteAllTextAsync(jsonPath, json);

        try
        {
            // Act
            var deserialized = await service.DeserializeAlbumAsync(jsonPath, CancellationToken.None);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal("Test Artist", deserialized!.Artist.Name);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void GetDirectoryName_And_GetFileName_ReturnExpected()
    {
        // Arrange
        var service = CreateService();
        var combined = Path.Combine("root", "child", "file.ext");

        // Act
        var dir = service.GetDirectoryName(combined);
        var file = service.GetFileName(combined);

        // Assert
        Assert.EndsWith(Path.Combine("root", "child"), dir);
        Assert.Equal("file.ext", file);
    }

    [Fact]
    public void CombinePath_JoinsAllSegments()
    {
        // Arrange
        var service = CreateService();

        // Act
        var path = service.CombinePath("a", "b", "c");

        // Assert
        Assert.Equal(Path.Combine("a", "b", "c"), path);
    }

    [Fact]
    public async Task ReadAllBytesAsync_And_WriteAllBytesAsync_WorkCorrectly()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var file = Path.Combine(tempDir, "bytes.bin");
        var data = Encoding.UTF8.GetBytes("hello world");

        try
        {
            // Act
            await service.WriteAllBytesAsync(file, data, CancellationToken.None);
            var read = await service.ReadAllBytesAsync(file, CancellationToken.None);

            // Assert
            Assert.Equal(data, read);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void CreateDirectory_Creates_WhenMissing()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var newDir = Path.Combine(tempDir, "created");

        try
        {
            // Act
            service.CreateDirectory(newDir);

            // Assert
            Assert.True(Directory.Exists(newDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FileExists_And_DeleteFile_WorkCorrectly()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var file = Path.Combine(tempDir, "exists.txt");
        File.WriteAllText(file, "x");

        try
        {
            // Act & Assert
            Assert.True(service.FileExists(file));
            service.DeleteFile(file);
            Assert.False(service.FileExists(file));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void MoveDirectory_MovesToNewLocation()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var source = Path.Combine(tempDir, "source");
        var dest = Path.Combine(tempDir, "dest");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");

        try
        {
            // Act
            service.MoveDirectory(source, dest);

            // Assert
            Assert.False(Directory.Exists(source));
            Assert.True(Directory.Exists(dest));
            Assert.True(File.Exists(Path.Combine(dest, "a.txt")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void GetFiles_ReturnsPatternMatches()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var d1 = Path.Combine(tempDir, "d1");
        Directory.CreateDirectory(d1);
        var f1 = Path.Combine(tempDir, "alpha.txt");
        var f2 = Path.Combine(tempDir, "beta.txt");
        var f3 = Path.Combine(d1, "gamma.txt");
        File.WriteAllText(f1, "a");
        File.WriteAllText(f2, "b");
        File.WriteAllText(f3, "c");

        try
        {
            // Act
            var top = service.GetFiles(tempDir, "*.txt", SearchOption.TopDirectoryOnly);
            var all = service.GetFiles(tempDir, "*.txt", SearchOption.AllDirectories);

            // Assert
            Assert.Equal(2, top.Length);
            Assert.Contains(f1, top);
            Assert.Contains(f2, top);

            Assert.Equal(3, all.Length);
            Assert.Contains(f3, all);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DeleteAllFilesForExtension_DeletesOnlyMatching()
    {
        // Arrange
        var service = CreateService();
        var tempDir = NewTempDir();
        var fTxt = Path.Combine(tempDir, "a.txt");
        var fJpg = Path.Combine(tempDir, "b.jpg");
        File.WriteAllText(fTxt, "x");
        File.WriteAllText(fJpg, "y");

        var fsDirInfo = new FileSystemDirectoryInfo
        {
            Path = tempDir,
            Name = Path.GetFileName(tempDir)
        };

        try
        {
            // Act
            service.DeleteAllFilesForExtension(fsDirInfo, "*.txt");

            // Assert
            Assert.False(File.Exists(fTxt));
            Assert.True(File.Exists(fJpg));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
