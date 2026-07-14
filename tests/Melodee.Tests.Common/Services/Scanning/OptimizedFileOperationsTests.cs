using Melodee.Common.Services.Scanning;

namespace Melodee.Tests.Common.Services.Scanning;

public class OptimizedFileOperationsTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly List<string> _createdFiles;
    private readonly List<string> _createdDirectories;

    public OptimizedFileOperationsTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "OptimizedFileOperationsTests", Guid.NewGuid().ToString());
        _createdFiles = [];
        _createdDirectories = [];
        Directory.CreateDirectory(_testDirectory);
        _createdDirectories.Add(_testDirectory);
    }

    public void Dispose()
    {
        try
        {
            foreach (var file in _createdFiles.Where(File.Exists))
            {
                File.Delete(file);
            }

            foreach (var dir in _createdDirectories.Where(Directory.Exists).OrderByDescending(d => d.Length))
            {
                Directory.Delete(dir, true);
            }
        }
        catch
        {
            // Ignore cleanup errors in tests
        }
    }

    private string CreateTestFile(string fileName, string content = "test content")
    {
        var filePath = Path.Combine(_testDirectory, fileName);
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
            _createdDirectories.Add(directory);
        }

        File.WriteAllText(filePath, content);
        _createdFiles.Add(filePath);
        return filePath;
    }

    [Fact]
    public async Task CopyFilesAsync_WithValidFiles_CopiesSuccessfully()
    {
        // Arrange
        var sourceFile1 = CreateTestFile("source1.txt", "content1");
        var sourceFile2 = CreateTestFile("source2.txt", "content2");
        var destFile1 = Path.Combine(_testDirectory, "dest1.txt");
        var destFile2 = Path.Combine(_testDirectory, "dest2.txt");

        var filePairs = new[]
        {
            (sourceFile1, destFile1),
            (sourceFile2, destFile2)
        };

        // Act
        var result = await OptimizedFileOperations.CopyFilesAsync(filePairs);

        // Assert
        Assert.Equal(2, result.FilesCopied);
        Assert.True(File.Exists(destFile1));
        Assert.True(File.Exists(destFile2));
        Assert.Equal("content1", File.ReadAllText(destFile1));
        Assert.Equal("content2", File.ReadAllText(destFile2));

        _createdFiles.AddRange([destFile1, destFile2]);
    }

    [Fact]
    public async Task CopyFilesAsync_WithDeleteOriginal_RemovesSourceFiles()
    {
        // Arrange
        var sourceFile = CreateTestFile("source.txt", "test content");
        var destFile = Path.Combine(_testDirectory, "dest.txt");
        var filePairs = new[] { (sourceFile, destFile) };

        // Act
        var result = await OptimizedFileOperations.CopyFilesAsync(filePairs, deleteOriginal: true);

        // Assert
        Assert.Equal(1, result.FilesCopied);
        Assert.False(File.Exists(sourceFile));
        Assert.True(File.Exists(destFile));
        Assert.Equal("test content", File.ReadAllText(destFile));

        _createdFiles.Remove(sourceFile);
        _createdFiles.Add(destFile);
    }

    [Fact]
    public async Task CopyFilesAsync_WithCancellationToken_StopsOnCancellation()
    {
        // Arrange
        var sourceFiles = new List<string>();
        var filePairs = new List<(string, string)>();

        // Create enough files to exceed processor count to trigger cancellation check
        var fileCount = Math.Max(Environment.ProcessorCount + 1, 10);
        for (int i = 0; i < fileCount; i++)
        {
            var sourceFile = CreateTestFile($"source{i}.txt", $"content{i}");
            var destFile = Path.Combine(_testDirectory, $"dest{i}.txt");
            sourceFiles.Add(sourceFile);
            filePairs.Add((sourceFile, destFile));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act
        var result = await OptimizedFileOperations.CopyFilesAsync(filePairs, cancellationToken: cts.Token);

        // Assert - Should copy fewer files than requested due to cancellation
        Assert.True(result.FilesCopied < fileCount);
    }

    [Fact]
    public async Task CopyFilesAsync_WithNonExistentSource_SkipsFile()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.txt");
        var destFile = Path.Combine(_testDirectory, "dest.txt");
        var filePairs = new[] { (nonExistentFile, destFile) };

        // Act
        var result = await OptimizedFileOperations.CopyFilesAsync(filePairs);

        // Assert - FilesCopied is 0 because source doesn't exist and was skipped
        Assert.Equal(0, result.FilesCopied);
        Assert.False(File.Exists(destFile));
    }

    [Fact]
    public async Task CopyFilesAsync_WithSameSourceAndDestination_SkipsFile()
    {
        // Arrange
        var sourceFile = CreateTestFile("same.txt", "content");
        var filePairs = new[] { (sourceFile, sourceFile) };

        // Act
        var result = await OptimizedFileOperations.CopyFilesAsync(filePairs);

        // Assert - FilesCopied is 0 because source==dest and was skipped
        Assert.Equal(0, result.FilesCopied);
        Assert.True(File.Exists(sourceFile));
        Assert.Equal("content", File.ReadAllText(sourceFile));
    }

    [Fact]
    public async Task CopyFilesAsync_WithExistingDestination_SkipsIfIdentical()
    {
        // Arrange
        var sourceFile = CreateTestFile("source.txt", "content");
        var destFile = CreateTestFile("dest.txt", "content");

        // Set same timestamps to simulate identical files
        var sourceInfo = new FileInfo(sourceFile);
        File.SetLastWriteTime(destFile, sourceInfo.LastWriteTime);

        var filePairs = new[] { (sourceFile, destFile) };

        // Act
        var result = await OptimizedFileOperations.CopyFilesAsync(filePairs);

        // Assert - FilesCopied is 0 because files are identical and was skipped
        Assert.Equal(0, result.FilesCopied);
        Assert.True(File.Exists(destFile));
    }

    [Fact]
    public async Task DeleteFilesAsync_WithValidFiles_DeletesSuccessfully()
    {
        // Arrange
        var file1 = CreateTestFile("delete1.txt");
        var file2 = CreateTestFile("delete2.txt");
        var filesToDelete = new[] { file1, file2 };

        // Act
        var result = await OptimizedFileOperations.DeleteFilesAsync(filesToDelete);

        // Assert
        Assert.Equal(2, result);
        Assert.False(File.Exists(file1));
        Assert.False(File.Exists(file2));

        _createdFiles.RemoveAll(f => filesToDelete.Contains(f));
    }

    [Fact]
    public async Task DeleteFilesAsync_WithNonExistentFiles_SkipsNonExistentFiles()
    {
        // Arrange
        var existingFile = CreateTestFile("existing.txt");
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.txt");
        var filesToDelete = new[] { existingFile, nonExistentFile };

        // Act
        var result = await OptimizedFileOperations.DeleteFilesAsync(filesToDelete);

        // Assert
        Assert.Equal(1, result); // Only existing file was deleted
        Assert.False(File.Exists(existingFile));

        _createdFiles.Remove(existingFile);
    }

    [Fact]
    public async Task DeleteFilesAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        var files = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            files.Add(CreateTestFile($"delete{i}.txt"));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OptimizedFileOperations.DeleteFilesAsync(files, cts.Token));
    }

    [Fact]
    public void HasFileChanged_WithNonExistentFile_ReturnsFalse()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.txt");

        // Act
        var result = OptimizedFileOperations.HasFileChanged(nonExistentFile);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFileChanged_WithOldLastProcessDate_ReturnsFalse()
    {
        // Arrange
        var file = CreateTestFile("test.txt");
        var futureDate = DateTime.Now.AddDays(1);

        // Act
        var result = OptimizedFileOperations.HasFileChanged(file, futureDate);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasFileChanged_WithRecentFile_ReturnsTrue()
    {
        // Arrange
        var file = CreateTestFile("test.txt");
        var oldDate = DateTime.Now.AddDays(-1);

        // Act
        var result = OptimizedFileOperations.HasFileChanged(file, oldDate);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFileChanged_WithCachedFile_ReturnsFalse()
    {
        // Arrange
        var file = CreateTestFile("test.txt");

        // First call to cache the file
        OptimizedFileOperations.UpdateFileHashCache(file);

        // Act
        var result = OptimizedFileOperations.HasFileChanged(file);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void UpdateFileHashCache_WithValidFile_UpdatesCache()
    {
        // Arrange
        var file = CreateTestFile("test.txt");

        // Act
        OptimizedFileOperations.UpdateFileHashCache(file);
        var result = OptimizedFileOperations.HasFileChanged(file);

        // Assert
        Assert.False(result); // Should be cached now
    }

    [Fact]
    public void UpdateFileHashCache_WithNonExistentFile_HandlesGracefully()
    {
        // Arrange
        var nonExistentFile = Path.Combine(_testDirectory, "nonexistent.txt");

        // Act & Assert - Should not throw
        OptimizedFileOperations.UpdateFileHashCache(nonExistentFile);
    }

    [Fact]
    public void HasFileChanged_AfterFileModified_ReturnsTrue()
    {
        // Arrange - cache the fingerprint, then modify the file so the fingerprint changes
        var file = CreateTestFile("fingerprint.txt", "original");
        OptimizedFileOperations.UpdateFileHashCache(file);

        // Ensure the cached fingerprint is recognized as unchanged
        Assert.False(OptimizedFileOperations.HasFileChanged(file));

        // Modify the file content (changes size and last-write-time, altering the fingerprint)
        File.WriteAllText(file, "modified content with more bytes");

        // Act - the fingerprint should no longer match
        var result = OptimizedFileOperations.HasFileChanged(file);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasFileChanged_WithLastProcessDateBeforeWrite_ReturnsFalse()
    {
        // Arrange
        var file = CreateTestFile("datecheck.txt");
        var lastProcessDate = DateTime.UtcNow.AddHours(1); // After the file's write time

        // Act - lastProcessDate is after the file's LastWriteTime, so it is considered unchanged
        var result = OptimizedFileOperations.HasFileChanged(file, lastProcessDate);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task EnumerateFilesAsync_WithValidDirectory_ReturnsFiles()
    {
        // Arrange
        CreateTestFile("file1.txt");
        CreateTestFile("file2.txt");
        CreateTestFile("file3.log");

        // Act
        var files = new List<FileInfo>();
        await foreach (var file in OptimizedFileOperations.EnumerateFilesAsync(_testDirectory))
        {
            files.Add(file);
        }

        // Assert
        Assert.True(files.Count >= 3);
        Assert.Contains(files, f => f.Name == "file1.txt");
        Assert.Contains(files, f => f.Name == "file2.txt");
        Assert.Contains(files, f => f.Name == "file3.log");
    }

    [Fact]
    public async Task EnumerateFilesAsync_WithSearchPattern_FiltersByPattern()
    {
        // Arrange
        CreateTestFile("file1.txt");
        CreateTestFile("file2.txt");
        CreateTestFile("file3.log");

        // Act
        var files = new List<FileInfo>();
        await foreach (var file in OptimizedFileOperations.EnumerateFilesAsync(_testDirectory, "*.txt"))
        {
            files.Add(file);
        }

        // Assert
        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.EndsWith(".txt", f.Name));
    }

    [Fact]
    public async Task EnumerateFilesAsync_WithSubdirectories_ReturnsAllFiles()
    {
        // Arrange
        var subDir = Path.Combine(_testDirectory, "subdir");
        Directory.CreateDirectory(subDir);
        _createdDirectories.Add(subDir);

        CreateTestFile("file1.txt");
        CreateTestFile(Path.Combine("subdir", "file2.txt"));

        // Act
        var files = new List<FileInfo>();
        await foreach (var file in OptimizedFileOperations.EnumerateFilesAsync(_testDirectory, "*", SearchOption.AllDirectories))
        {
            files.Add(file);
        }

        // Assert
        Assert.True(files.Count >= 2);
        Assert.Contains(files, f => f.Name == "file1.txt");
        Assert.Contains(files, f => f.Name == "file2.txt");
    }

    [Fact]
    public async Task EnumerateFilesAsync_WithNonExistentDirectory_ReturnsEmpty()
    {
        // Arrange
        var nonExistentDir = Path.Combine(_testDirectory, "nonexistent");

        // Act
        var files = new List<FileInfo>();
        await foreach (var file in OptimizedFileOperations.EnumerateFilesAsync(nonExistentDir))
        {
            files.Add(file);
        }

        // Assert
        Assert.Empty(files);
    }

    [Fact]
    public async Task EnumerateFilesAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            CreateTestFile($"file{i}.txt");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Cancel immediately

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var file in OptimizedFileOperations.EnumerateFilesAsync(_testDirectory, cancellationToken: cts.Token))
            {
                // Just enumerate
            }
        });
    }

    [Fact]
    public async Task WriteTextFileAsync_WithValidInput_WritesFile()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "written.txt");
        var content = "This is test content";

        // Act
        await OptimizedFileOperations.WriteTextFileAsync(filePath, content);

        // Assert
        Assert.True(File.Exists(filePath));
        Assert.Equal(content, File.ReadAllText(filePath));

        _createdFiles.Add(filePath);
    }

    [Fact]
    public async Task WriteTextFileAsync_WithCancellationToken_RespectsCancellation()
    {
        // Arrange
        var filePath = Path.Combine(_testDirectory, "cancelled.txt");
        var content = "This should not be written";

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => OptimizedFileOperations.WriteTextFileAsync(filePath, content, cancellationToken: cts.Token));

        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task WriteTextFileAsync_WithCustomBufferSize_WritesCorrectly()
    {
        // Arrange
        var sourceFile = CreateTestFile("source.txt", "test content");
        var destFile = Path.Combine(_testDirectory, "dest.txt");
        var customBufferSize = 512; // Small buffer size for testing
        var filePairs = new[] { (sourceFile, destFile) };

        // Act
        var result = await OptimizedFileOperations.CopyFilesAsync(filePairs, bufferSize: customBufferSize);

        // Assert
        Assert.Equal(1, result.FilesCopied);
        Assert.True(File.Exists(destFile));
        Assert.Equal("test content", File.ReadAllText(destFile));

        _createdFiles.Add(destFile);
    }

    [Fact]
    public async Task CopyFilesAsync_CreatesDestinationDirectories()
    {
        // Arrange
        var sourceFile = CreateTestFile("source.txt", "content");
        var destDir = Path.Combine(_testDirectory, "subdir", "nested");
        var destFile = Path.Combine(destDir, "dest.txt");
        var filePairs = new[] { (sourceFile, destFile) };

        // Act
        await OptimizedFileOperations.CopyFilesAsync(filePairs);

        // Assert
        Assert.True(Directory.Exists(destDir));
        Assert.True(File.Exists(destFile));
        Assert.Equal("content", File.ReadAllText(destFile));

        _createdDirectories.Add(destDir);
        _createdDirectories.Add(Path.GetDirectoryName(destDir)!);
        _createdFiles.Add(destFile);
    }
}
