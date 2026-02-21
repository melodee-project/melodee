using System.Diagnostics;
using FluentAssertions;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace Melodee.Tests.Common.Plugins.SearchEngine.MusicBrainz;

/// <summary>
/// Benchmark tests for MusicBrainz import performance.
/// These tests measure and report detailed timing for each phase of the import process.
/// Run with: dotnet test --filter "FullyQualifiedName~MusicBrainzImportBenchmark" -v n
/// </summary>
[Trait("Category", "Benchmark")]
public class MusicBrainzImportBenchmark : IDisposable
{
    private readonly ILogger _logger;
    private readonly string _testDataPath;
    private readonly string _testDbPath;

    public MusicBrainzImportBenchmark()
    {
        _logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();

        _testDataPath = Path.Combine(Path.GetTempPath(), $"mb-benchmark-data-{Guid.NewGuid():N}");
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mb-benchmark-db-{Guid.NewGuid():N}");

        Directory.CreateDirectory(_testDataPath);
        Directory.CreateDirectory(_testDbPath);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDataPath))
            {
                Directory.Delete(_testDataPath, true);
            }
            if (Directory.Exists(_testDbPath))
            {
                Directory.Delete(_testDbPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Comprehensive benchmark that measures all phases of the import process.
    /// This test outputs detailed timing information for performance analysis.
    /// </summary>
    [Theory]
    [InlineData(1000, 5)]   // Small: ~1K artists, ~5K albums
    [InlineData(5000, 5)]   // Medium: ~5K artists, ~25K albums
    [InlineData(10000, 5)]  // Large: ~10K artists, ~50K albums
    public async Task BenchmarkImport_MeasuresAllPhases(int artistCount, int albumsPerArtist)
    {
        var results = new BenchmarkResults
        {
            ArtistCount = artistCount,
            AlbumsPerArtist = albumsPerArtist,
            TestStartTime = DateTime.UtcNow
        };

        var storagePath = Path.Combine(_testDataPath, $"storage-{artistCount}");
        var mbDumpPath = Path.Combine(storagePath, "staging", "mbdump");
        var dbFile = Path.Combine(_testDbPath, $"musicbrainz-{artistCount}.ddb");

        // Phase 0: Generate test data
        var genSw = Stopwatch.StartNew();
        var stats = MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, artistCount, albumsPerArtist);
        genSw.Stop();
        results.DataGenerationMs = genSw.ElapsedMilliseconds;
        results.TotalRecordsGenerated = stats.ArtistCount + stats.AliasCount + stats.ReleaseCount +
                                         stats.ReleaseGroupCount + stats.ArtistCreditCount;

        // Setup database
        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        // Create importer with progress tracking
        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);
        var phaseTimings = new Dictionary<string, long>();
        var currentPhase = "";
        var phaseStopwatch = new Stopwatch();

        void ProgressCallback(string phase, int current, int total, string? message)
        {
            if (phase != currentPhase)
            {
                if (!string.IsNullOrEmpty(currentPhase))
                {
                    phaseStopwatch.Stop();
                    phaseTimings[currentPhase] = phaseStopwatch.ElapsedMilliseconds;
                }
                currentPhase = phase;
                phaseStopwatch.Restart();
            }
        }

        // Run the import
        var importSw = Stopwatch.StartNew();
        await importer.ImportAsync(
            context,
            storagePath,
            ProgressCallback,
            CancellationToken.None);
        importSw.Stop();

        // Capture final phase timing
        if (!string.IsNullOrEmpty(currentPhase))
        {
            phaseStopwatch.Stop();
            phaseTimings[currentPhase] = phaseStopwatch.ElapsedMilliseconds;
        }

        results.TotalImportMs = importSw.ElapsedMilliseconds;
        results.PhaseTimings = phaseTimings;

        // Get final counts
        results.ImportedArtists = await context.Artists.CountAsync();
        results.ImportedAlbums = await context.Albums.CountAsync();
        results.ImportedRelations = await context.ArtistRelations.CountAsync();

        // Calculate metrics
        results.RecordsPerSecond = results.TotalRecordsGenerated / (results.TotalImportMs / 1000.0);

        // Get database file size
        results.DatabaseSizeBytes = new FileInfo(dbFile).Length;

        // Output results
        OutputBenchmarkResults(results);

        // Basic assertions
        results.ImportedArtists.Should().Be(artistCount);
        results.ImportedAlbums.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// Runs multiple iterations to get stable performance numbers.
    /// </summary>
    [Fact]
    public async Task BenchmarkImport_MultipleIterations_ForStableMetrics()
    {
        const int iterations = 3;
        const int artistCount = 2000;
        const int albumsPerArtist = 5;

        var allResults = new List<double>();

        for (var i = 0; i < iterations; i++)
        {
            var storagePath = Path.Combine(_testDataPath, $"iteration-{i}");
            var mbDumpPath = Path.Combine(storagePath, "staging", "mbdump");
            var dbFile = Path.Combine(_testDbPath, $"iteration-{i}.ddb");

            var stats = MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, artistCount, albumsPerArtist);
            var totalRecords = stats.ArtistCount + stats.AliasCount + stats.ReleaseCount +
                               stats.ReleaseGroupCount + stats.ArtistCreditCount;

            var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
                .UseDecentDB($"Data Source={dbFile}")
                .Options;

            await using var context = new MusicBrainzDbContext(dbOptions);
            await context.Database.EnsureCreatedAsync();

            var importer = new DecentDBStreamingMusicBrainzImporter(_logger);
            var sw = Stopwatch.StartNew();

            await importer.ImportAsync(
                context,
                storagePath,
                null,
                CancellationToken.None);

            sw.Stop();
            var recordsPerSecond = totalRecords / sw.Elapsed.TotalSeconds;
            allResults.Add(recordsPerSecond);

            Console.WriteLine($"[Iteration {i + 1}] Time: {sw.Elapsed.TotalSeconds:F2}s, Rate: {recordsPerSecond:N0} records/sec");
        }

        var avg = allResults.Average();
        var min = allResults.Min();
        var max = allResults.Max();
        var stdDev = Math.Sqrt(allResults.Average(v => Math.Pow(v - avg, 2)));

        Console.WriteLine();
        Console.WriteLine("=== BENCHMARK SUMMARY ===");
        Console.WriteLine($"Iterations: {iterations}");
        Console.WriteLine($"Average: {avg:N0} records/sec");
        Console.WriteLine($"Min: {min:N0} records/sec");
        Console.WriteLine($"Max: {max:N0} records/sec");
        Console.WriteLine($"Std Dev: {stdDev:N0} records/sec");
        Console.WriteLine($"Coefficient of Variation: {(stdDev / avg * 100):F1}%");
    }

    /// <summary>
    /// Memory-focused benchmark that tracks peak memory usage.
    /// </summary>
    [Theory]
    [InlineData(5000, 5)]
    public async Task BenchmarkImport_TracksMemoryUsage(int artistCount, int albumsPerArtist)
    {
        var storagePath = Path.Combine(_testDataPath, $"memory-{artistCount}");
        var mbDumpPath = Path.Combine(storagePath, "staging", "mbdump");
        var dbFile = Path.Combine(_testDbPath, $"memory-{artistCount}.ddb");

        var stats = MusicBrainzTestDataGenerator.GenerateTestData(mbDumpPath, artistCount, albumsPerArtist);

        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        // Force GC before measuring
        GC.Collect(2, GCCollectionMode.Forced, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);

        var startMemory = GC.GetTotalMemory(true);
        var peakMemory = startMemory;

        var importer = new DecentDBStreamingMusicBrainzImporter(_logger);

        // Track memory during import using a background task
        var cts = new CancellationTokenSource();
        var memoryTracker = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var currentMemory = GC.GetTotalMemory(false);
                if (currentMemory > peakMemory)
                {
                    peakMemory = currentMemory;
                }
                await Task.Delay(100, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }, cts.Token);

        var sw = Stopwatch.StartNew();
        await importer.ImportAsync(
            context,
            storagePath,
            null,
            CancellationToken.None);
        sw.Stop();

        cts.Cancel();
        try
        {
            await memoryTracker;
        }
        catch (OperationCanceledException)
        {
        }

        var endMemory = GC.GetTotalMemory(true);
        var memoryIncrease = endMemory - startMemory;

        Console.WriteLine();
        Console.WriteLine("=== MEMORY BENCHMARK ===");
        Console.WriteLine($"Artists: {artistCount:N0}, Albums/Artist: {albumsPerArtist}");
        Console.WriteLine($"Total Records: {stats.ArtistCount + stats.AliasCount + stats.ReleaseCount:N0}");
        Console.WriteLine($"Import Time: {sw.Elapsed.TotalSeconds:F2}s");
        Console.WriteLine($"Start Memory: {startMemory / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"Peak Memory: {peakMemory / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"End Memory: {endMemory / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"Memory Increase: {memoryIncrease / 1024.0 / 1024.0:F2} MB");
        Console.WriteLine($"Peak Memory per 1K Records: {(peakMemory - startMemory) / (stats.ArtistCount + stats.AliasCount + stats.ReleaseCount) * 1000 / 1024.0:F2} KB");
    }

    private static void OutputBenchmarkResults(BenchmarkResults results)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              MUSICBRAINZ IMPORT BENCHMARK RESULTS                ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ Test Configuration                                               ║");
        Console.WriteLine($"║   Artists: {results.ArtistCount,10:N0}    Albums/Artist: {results.AlbumsPerArtist,5}              ║");
        Console.WriteLine($"║   Total Records Generated: {results.TotalRecordsGenerated,10:N0}                        ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ Timing Summary                                                   ║");
        Console.WriteLine($"║   Data Generation:    {results.DataGenerationMs,8:N0} ms                              ║");
        Console.WriteLine($"║   Total Import:       {results.TotalImportMs,8:N0} ms                              ║");
        Console.WriteLine($"║   Records/Second:     {results.RecordsPerSecond,8:N0}                                 ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ Phase Breakdown                                                  ║");

        foreach (var (phase, ms) in results.PhaseTimings.OrderBy(p => p.Value))
        {
            var phaseName = phase.Length > 24 ? phase[..24] : phase.PadRight(24);
            Console.WriteLine($"║   {phaseName} {ms,8:N0} ms                              ║");
        }

        Console.WriteLine("╠══════════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║ Results                                                          ║");
        Console.WriteLine($"║   Imported Artists:   {results.ImportedArtists,10:N0}                              ║");
        Console.WriteLine($"║   Imported Albums:    {results.ImportedAlbums,10:N0}                              ║");
        Console.WriteLine($"║   Imported Relations: {results.ImportedRelations,10:N0}                              ║");
        Console.WriteLine($"║   Database Size:      {results.DatabaseSizeBytes / 1024.0 / 1024.0,10:F2} MB                          ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    private sealed class BenchmarkResults
    {
        public int ArtistCount { get; set; }
        public int AlbumsPerArtist { get; set; }
        public DateTime TestStartTime { get; set; }
        public long DataGenerationMs { get; set; }
        public long TotalImportMs { get; set; }
        public int TotalRecordsGenerated { get; set; }
        public double RecordsPerSecond { get; set; }
        public int ImportedArtists { get; set; }
        public int ImportedAlbums { get; set; }
        public int ImportedRelations { get; set; }
        public long DatabaseSizeBytes { get; set; }
        public Dictionary<string, long> PhaseTimings { get; set; } = new();
    }
}
