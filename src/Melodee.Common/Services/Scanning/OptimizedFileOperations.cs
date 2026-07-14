using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Serilog;

namespace Melodee.Common.Services.Scanning;

/// <summary>
///     Optimized file operations for high-performance directory processing
/// </summary>
public static class OptimizedFileOperations
{
    // Tracks file fingerprints (path:size:lastWriteTime) that have already been processed, so
    // unchanged files can be skipped. Despite the historical name, this is NOT a content hash; it
    // is a lightweight identity fingerprint. The DateTime value records when the fingerprint was
    // added and is used only for periodic eviction, not for change detection.
    private static readonly ConcurrentDictionary<string, DateTime> FileFingerprintCache = new();
    private const int MaxIoRetries = 5;
    private static readonly TimeSpan MaxIoBackoff = TimeSpan.FromSeconds(8);

    /// <summary>
    ///     Default max concurrent file copies (conservative for network storage).
    /// </summary>
    public const int DefaultMaxConcurrentCopies = 4;

    /// <summary>
    ///     Default streaming buffer size (moderate to avoid memory pressure).
    /// </summary>
    public const int DefaultBufferSize = 256 * 1024; // 256KB streaming buffer

    /// <summary>
    ///     Asynchronously copy files with controlled concurrency and streaming.
    /// </summary>
    /// <param name="filePairs">Source and destination path pairs.</param>
    /// <param name="deleteOriginal">Whether to delete originals after copy.</param>
    /// <param name="bufferSize">Streaming buffer size per file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="maxConcurrentCopies">Maximum concurrent copy operations (default: 4).</param>
    /// <returns>Result containing count and timing metrics.</returns>
    public static async Task<FileCopyResult> CopyFilesAsync(
        IEnumerable<(string sourcePath, string destinationPath)> filePairs,
        bool deleteOriginal = false,
        int bufferSize = DefaultBufferSize,
        CancellationToken cancellationToken = default,
        int maxConcurrentCopies = DefaultMaxConcurrentCopies)
    {
        var stopwatch = Stopwatch.StartNew();
        var pairs = filePairs.ToList();

        if (pairs.Count == 0)
        {
            return new FileCopyResult(0, 0, 0, TimeSpan.Zero);
        }

        var copiedCount = 0;
        long totalBytesCopied = 0;

        using var semaphore = new SemaphoreSlim(maxConcurrentCopies, maxConcurrentCopies);
        var tasks = new List<Task<(int count, long bytes)>>();

        foreach (var (sourcePath, destinationPath) in pairs)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

            var task = CopyFileWithThrottleAsync(
                sourcePath,
                destinationPath,
                deleteOriginal,
                bufferSize,
                semaphore,
                cancellationToken);
            tasks.Add(task);
        }

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        foreach (var (count, bytes) in results)
        {
            copiedCount += count;
            totalBytesCopied += bytes;
        }

        stopwatch.Stop();
        var result = new FileCopyResult(copiedCount, pairs.Count, totalBytesCopied, stopwatch.Elapsed);

        Log.Information(
            "[OptimizedFileOperations] Copied {Copied}/{Total} files, {Bytes:N0} bytes in {Duration:N1}ms ({ThroughputMBs:N2} MB/s)",
            result.FilesCopied,
            result.FilesAttempted,
            result.TotalBytes,
            result.Duration.TotalMilliseconds,
            result.ThroughputMBPerSecond);

        return result;
    }

    /// <summary>
    ///     Legacy overload for backward compatibility.
    /// </summary>
    public static async Task<int> CopyFilesAsync(
        IEnumerable<(string sourcePath, string destinationPath)> filePairs,
        bool deleteOriginal,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        var result = await CopyFilesAsync(
            filePairs,
            deleteOriginal,
            bufferSize,
            cancellationToken,
            DefaultMaxConcurrentCopies).ConfigureAwait(false);
        return result.FilesCopied;
    }

    /// <summary>
    ///     Copy a single file with throttling and optimized streaming.
    /// </summary>
    private static async Task<(int count, long bytes)> CopyFileWithThrottleAsync(
        string sourcePath,
        string destinationPath,
        bool deleteOriginal,
        int bufferSize,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await CopyFileOptimizedAsync(sourcePath, destinationPath, bufferSize, cancellationToken).ConfigureAwait(false);

            if (deleteOriginal && bytes > 0)
            {
                try
                {
                    File.Delete(sourcePath);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete original file after copy");
                }
            }

            return (bytes > 0 ? 1 : 0, bytes);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <summary>
    ///     Optimized file copy using streaming with bounded buffers.
    ///     Returns bytes copied or 0 if skipped/failed.
    /// </summary>
    private static async Task<long> CopyFileOptimizedAsync(
        string sourcePath,
        string destinationPath,
        int bufferSize,
        CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await WaitForFileStabilityAsync(sourcePath, cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                Log.Warning("File not stable for copy, skipping");
                return 0;
            }

            try
            {
                var destinationDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
                {
                    Directory.CreateDirectory(destinationDir);
                }

                if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                {
                    return 0;
                }

                var sourceInfo = new FileInfo(sourcePath);
                if (!sourceInfo.Exists)
                {
                    return 0;
                }

                var destInfo = new FileInfo(destinationPath);
                if (destInfo.Exists && destInfo.Length == sourceInfo.Length &&
                    Math.Abs((destInfo.LastWriteTime - sourceInfo.LastWriteTime).TotalSeconds) < 2)
                {
                    return 0;
                }

                // Streaming copy with bounded buffer (does not allocate large buffers in parallel)
                await using var sourceStream = new FileStream(
                    sourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize,
                    FileOptions.SequentialScan | FileOptions.Asynchronous);

                await using var destStream = new FileStream(
                    destinationPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize,
                    FileOptions.SequentialScan | FileOptions.Asynchronous);

                await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken).ConfigureAwait(false);

                File.SetLastWriteTime(destinationPath, sourceInfo.LastWriteTime);
                File.SetCreationTime(destinationPath, sourceInfo.CreationTime);

                return sourceInfo.Length;
            }
            catch (IOException ex) when (IsSharingViolation(ex) && attempt < MaxIoRetries)
            {
                attempt++;
                var delay = IoBackoff(attempt);
                Log.Warning(ex,
                    "Retrying copy for locked file attempt {Attempt}/{MaxAttempts} after {Delay}ms",
                    attempt,
                    MaxIoRetries,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                Log.Warning(ex,
                    "Giving up copy for locked file after {Attempts} attempts",
                    attempt);
                return 0;
            }
        }
    }

    private static bool IsSharingViolation(IOException ex)
    {
        var hresult = ex.HResult & 0xFFFF;
        return hresult is 32 or 33 or 5 || ex.Message.Contains("used by another process", StringComparison.OrdinalIgnoreCase);
    }

    private static TimeSpan IoBackoff(int attempt)
    {
        var baseDelayMs = Math.Min(MaxIoBackoff.TotalMilliseconds, Math.Pow(2, attempt) * 100);
        var jitter = Random.Shared.Next(50, 200);
        return TimeSpan.FromMilliseconds(baseDelayMs + jitter);
    }

    public static async Task<bool> WaitForFileStabilityAsync(
        string path,
        int checks = 2,
        int delayMs = 120,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        long? previousLength = null;
        DateTime? previousWrite = null;

        for (var i = 0; i < checks; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return false;
            }

            if (previousLength.HasValue && previousWrite.HasValue &&
                info.Length == previousLength && info.LastWriteTime == previousWrite)
            {
                return true;
            }

            previousLength = info.Length;
            previousWrite = info.LastWriteTime;

            await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    ///     Batch delete files with parallel processing
    /// </summary>
    public static async Task<int> DeleteFilesAsync(
        IEnumerable<string> filePaths,
        CancellationToken cancellationToken = default)
    {
        var deletedCount = 0;
        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) // I/O bound, use fewer threads (minimum 1)
        };

        await Task.Run(() =>
        {
            Parallel.ForEach(filePaths, parallelOptions, filePath =>
            {
                try
                {
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        Interlocked.Increment(ref deletedCount);
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to delete file: {FilePath}", filePath);
                }
            });
        }, cancellationToken).ConfigureAwait(false);

        return deletedCount;
    }

    /// <summary>
    ///     Check if file has changed using a cached fingerprint (path + size + last write time)
    ///     comparison. A file is considered unchanged when its current fingerprint matches a
    ///     previously recorded entry. Note: this is a fingerprint, not a content hash.
    /// </summary>
    public static bool HasFileChanged(string filePath, DateTime? lastProcessDate = null)
    {
        if (!File.Exists(filePath))
        {
            return false;
        }

        var fileInfo = new FileInfo(filePath);

        // Quick date check first
        if (lastProcessDate.HasValue && fileInfo.LastWriteTime <= lastProcessDate.Value)
        {
            return false;
        }

        // Use cached fingerprint for identity comparison
        var cacheKey = $"{filePath}:{fileInfo.Length}:{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}";

        return !FileFingerprintCache.ContainsKey(cacheKey);
    }

    /// <summary>
    ///     Record the file's current fingerprint (path + size + last write time) in the cache so a
    ///     subsequent <see cref="HasFileChanged" /> call can skip it. Note: this is a fingerprint,
    ///     not a content hash.
    /// </summary>
    public static void UpdateFileHashCache(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var fileInfo = new FileInfo(filePath);
            var cacheKey = $"{filePath}:{fileInfo.Length}:{fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}";

            FileFingerprintCache.TryAdd(cacheKey, DateTime.UtcNow);

            // Clean old cache entries periodically
            if (FileFingerprintCache.Count > 10000)
            {
                var cutoff = DateTime.UtcNow.AddHours(-1);
                var keysToRemove = FileFingerprintCache
                    .Where(kvp => kvp.Value < cutoff)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    FileFingerprintCache.TryRemove(key, out _);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to update file fingerprint cache for: {FilePath}", filePath);
        }
    }

    /// <summary>
    ///     Efficiently enumerate files with lazy loading
    /// </summary>
    public static IAsyncEnumerable<FileInfo> EnumerateFilesAsync(
        string directoryPath,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly,
        CancellationToken cancellationToken = default)
    {
        return EnumerateFilesAsyncImpl(directoryPath, searchPattern, searchOption, cancellationToken);
    }

    private static async IAsyncEnumerable<FileInfo> EnumerateFilesAsyncImpl(
        string directoryPath,
        string searchPattern,
        SearchOption searchOption,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directoryPath))
        {
            yield break;
        }

        await Task.Yield(); // Allow other operations to proceed

        var enumerationOptions = new EnumerationOptions
        {
            RecurseSubdirectories = searchOption == SearchOption.AllDirectories,
            IgnoreInaccessible = true,
            ReturnSpecialDirectories = false
        };

        foreach (var filePath in Directory.EnumerateFiles(directoryPath, searchPattern, enumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            FileInfo fileInfo;
            try
            {
                fileInfo = new FileInfo(filePath);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to get file info for: {FilePath}", filePath);
                continue;
            }

            yield return fileInfo;

            // Yield periodically to allow other operations
            if (Random.Shared.Next(100) == 0)
            {
                await Task.Yield();
            }
        }
    }

    /// <summary>
    ///     Write text to file asynchronously with retry logic
    /// </summary>
    public static async Task WriteTextFileAsync(
        string filePath,
        string content,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        while (retryCount < maxRetries)
        {
            try
            {
                await File.WriteAllTextAsync(filePath, content, cancellationToken).ConfigureAwait(false);
                UpdateFileHashCache(filePath);
                return;
            }
            catch (IOException) when (retryCount < maxRetries - 1)
            {
                retryCount++;
                await Task.Delay(100 * retryCount, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
///     Result of a batch file copy operation with timing metrics.
/// </summary>
public readonly record struct FileCopyResult(
    int FilesCopied,
    int FilesAttempted,
    long TotalBytes,
    TimeSpan Duration)
{
    /// <summary>
    ///     Copy throughput in MB/s.
    /// </summary>
    public double ThroughputMBPerSecond => Duration.TotalSeconds > 0
        ? TotalBytes / 1024.0 / 1024.0 / Duration.TotalSeconds
        : 0;
}
