using System.Diagnostics;
using System.Text.Json;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;

namespace Melodee.Benchmarks;

internal static class MusicBrainzImportProbe
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = MusicBrainzProbeOptions.Parse(args);
        try
        {
            var storagePath = options.RequireString("storage");
            var databasePath = options.RequireString("db");
            var outputPath = options.GetString("output");
            var cleanTarget = options.GetBool("clean");
            var sampleIntervalMs = Math.Max(250, options.GetInt32("sample-interval-ms", 5000));

            if (!Directory.Exists(Path.Combine(storagePath, "staging", "mbdump")))
            {
                throw new DirectoryNotFoundException(
                    $"MusicBrainz staging dump was not found under {Path.Combine(storagePath, "staging", "mbdump")}.");
            }

            if (cleanTarget)
            {
                DeleteDatabaseFiles(databasePath);
            }

            var report = await RunProbeAsync(
                    storagePath,
                    databasePath,
                    sampleIntervalMs,
                    CancellationToken.None)
                .ConfigureAwait(false);
            await WriteReportAsync(report, outputPath, CancellationToken.None).ConfigureAwait(false);
            return report.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<MusicBrainzImportProbeReport> RunProbeAsync(
        string storagePath,
        string databasePath,
        int sampleIntervalMs,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var samples = new List<ImportProbeSample>();
        var phaseTracker = new ImportPhaseTracker();
        DecentDBMusicBrainzImportSummary? summary = null;
        string? errorMessage = null;
        var success = false;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            linkedCts.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;

        var sampler = Task.Run(
            () => SampleProcessAsync(databasePath, sampleIntervalMs, samples, linkedCts.Token),
            linkedCts.Token);

        try
        {
            var logger = new LoggerConfiguration()
                .MinimumLevel.Is(LogEventLevel.Information)
                .WriteTo.Console()
                .CreateLogger();

            var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
                .UseDecentDB($"Data Source={databasePath}")
                .Options;

            await using var context = new MusicBrainzDbContext(dbOptions);
            await context.Database.EnsureCreatedAsync(linkedCts.Token).ConfigureAwait(false);

            var importer = new DecentDBStreamingMusicBrainzImporter(logger);
            summary = await importer.ImportAsync(
                    context,
                    storagePath,
                    phaseTracker.Progress,
                    linkedCts.Token)
                .ConfigureAwait(false);
            success = true;
        }
        catch (OperationCanceledException)
        {
            errorMessage = "Import probe was cancelled.";
        }
        catch (Exception ex)
        {
            errorMessage = ex.ToString();
        }
        finally
        {
            stopwatch.Stop();
            phaseTracker.Complete();
            linkedCts.Cancel();
            Console.CancelKeyPress -= cancelHandler;

            try
            {
                await sampler.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        samples.Add(CaptureSample(databasePath));

        return new MusicBrainzImportProbeReport(
            "musicbrainz-import-probe",
            storagePath,
            databasePath,
            startedAt,
            DateTimeOffset.UtcNow,
            stopwatch.Elapsed.TotalMilliseconds,
            success,
            errorMessage,
            summary,
            phaseTracker.Phases,
            samples);
    }

    private static async Task SampleProcessAsync(
        string databasePath,
        int sampleIntervalMs,
        List<ImportProbeSample> samples,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (samples)
            {
                samples.Add(CaptureSample(databasePath));
            }

            await Task.Delay(sampleIntervalMs, cancellationToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static ImportProbeSample CaptureSample(string databasePath)
    {
        var process = Process.GetCurrentProcess();
        var linuxStatus = LinuxProcessStatus.ReadCurrent();
        return new ImportProbeSample(
            DateTimeOffset.UtcNow,
            process.WorkingSet64,
            process.PeakWorkingSet64,
            process.TotalProcessorTime.TotalMilliseconds,
            linuxStatus.VmRssBytes,
            linuxStatus.VmHwmBytes,
            GetFileLength(databasePath),
            GetFileLength($"{databasePath}.wal"),
            GetFileLength($"{databasePath}-wal"));
    }

    private static long GetFileLength(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static void DeleteDatabaseFiles(string databasePath)
    {
        foreach (var path in new[]
                 {
                     databasePath,
                     $"{databasePath}.wal",
                     $"{databasePath}.shm",
                     $"{databasePath}-wal",
                     $"{databasePath}-shm"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private static async Task WriteReportAsync(
        MusicBrainzImportProbeReport report,
        string? outputPath,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            Console.WriteLine(json);
            return;
        }

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllTextAsync(outputPath, json, cancellationToken).ConfigureAwait(false);
        Console.WriteLine($"Wrote MusicBrainz import probe report to {outputPath}");
    }

    private sealed class ImportPhaseTracker
    {
        private readonly object gate = new();
        private readonly List<ImportProbePhase> phases = [];
        private string? currentPhase;
        private DateTimeOffset currentStartedAt;
        private string? currentMessage;
        private int current;
        private int total;

        public IReadOnlyList<ImportProbePhase> Phases
        {
            get
            {
                lock (gate)
                {
                    return phases.ToArray();
                }
            }
        }

        public void Progress(string phase, int currentValue, int totalValue, string? message)
        {
            lock (gate)
            {
                if (!string.Equals(currentPhase, phase, StringComparison.Ordinal))
                {
                    FinishCurrentPhase();
                    currentPhase = phase;
                    currentStartedAt = DateTimeOffset.UtcNow;
                }

                current = currentValue;
                total = totalValue;
                currentMessage = message;
            }
        }

        public void Complete()
        {
            lock (gate)
            {
                FinishCurrentPhase();
            }
        }

        private void FinishCurrentPhase()
        {
            if (currentPhase is null)
            {
                return;
            }

            var finishedAt = DateTimeOffset.UtcNow;
            phases.Add(new ImportProbePhase(
                currentPhase,
                currentStartedAt,
                finishedAt,
                (finishedAt - currentStartedAt).TotalMilliseconds,
                current,
                total,
                currentMessage));
            currentPhase = null;
            currentMessage = null;
            current = 0;
            total = 0;
        }
    }

    private readonly record struct LinuxProcessStatus(long? VmRssBytes, long? VmHwmBytes)
    {
        public static LinuxProcessStatus ReadCurrent()
        {
            const string statusPath = "/proc/self/status";
            if (!File.Exists(statusPath))
            {
                return new LinuxProcessStatus(null, null);
            }

            long? vmRss = null;
            long? vmHwm = null;
            foreach (var line in File.ReadLines(statusPath))
            {
                if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
                {
                    vmRss = ParseKilobyteLine(line);
                }
                else if (line.StartsWith("VmHWM:", StringComparison.Ordinal))
                {
                    vmHwm = ParseKilobyteLine(line);
                }
            }

            return new LinuxProcessStatus(vmRss, vmHwm);
        }

        private static long? ParseKilobyteLine(string line)
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2 && long.TryParse(parts[1], out var kilobytes)
                ? kilobytes * 1024
                : null;
        }
    }

    private sealed record MusicBrainzImportProbeReport(
        string ProbeName,
        string StoragePath,
        string DatabasePath,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset FinishedAtUtc,
        double DurationMilliseconds,
        bool Success,
        string? ErrorMessage,
        DecentDBMusicBrainzImportSummary? ImportSummary,
        IReadOnlyList<ImportProbePhase> PhaseTimings,
        IReadOnlyList<ImportProbeSample> Samples);

    private sealed record ImportProbePhase(
        string Name,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset FinishedAtUtc,
        double DurationMilliseconds,
        int Current,
        int Total,
        string? LastMessage);

    private sealed record ImportProbeSample(
        DateTimeOffset TimestampUtc,
        long WorkingSetBytes,
        long PeakWorkingSetBytes,
        double TotalProcessorMilliseconds,
        long? VmRssBytes,
        long? VmHwmBytes,
        long DatabaseBytes,
        long WalBytes,
        long DashWalBytes);
}
