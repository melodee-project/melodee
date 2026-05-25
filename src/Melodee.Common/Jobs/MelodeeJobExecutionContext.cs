using Quartz;
using Quartz.Impl;

namespace Melodee.Common.Jobs;

/// <summary>
///     Result status for job execution indicating why a job completed.
/// </summary>
public enum JobResultStatus
{
    /// <summary>Job executed successfully and performed work.</summary>
    Success,

    /// <summary>Job was skipped due to a precondition not being met.</summary>
    Skipped,

    /// <summary>Job failed with an error.</summary>
    Failed
}

/// <summary>
///     Result of a job execution with status and message.
/// </summary>
public record JobResult(JobResultStatus Status, string Message);

/// <summary>
///     Progress information for a job stage.
/// </summary>
public record JobStageProgress(
    string StageName,
    int CurrentItem,
    int TotalItems,
    string? CurrentItemDescription = null)
{
    public double PercentComplete => TotalItems > 0 ? (double)CurrentItem / TotalItems * 100 : 0;

    public override string ToString() => TotalItems > 0
        ? $"{StageName}: {CurrentItem:N0}/{TotalItems:N0} ({PercentComplete:F1}%){(CurrentItemDescription != null ? $" - {CurrentItemDescription}" : "")}"
        : $"{StageName}: {CurrentItemDescription ?? "Processing..."}";
}

/// <summary>
///     Overall job progress tracking across multiple stages.
/// </summary>
public class JobProgress
{
    private readonly List<string> _completedStages = [];
    private readonly object _lock = new();

    public string[] StageNames { get; private set; } = [];
    public JobStageProgress? CurrentStageProgress { get; private set; }
    public int CurrentStageIndex { get; private set; }
    public int TotalStages => StageNames.Length;
    public string[] CompletedStages => [.. _completedStages];

    /// <summary>
    ///     Event raised when progress is updated.
    /// </summary>
    public event Action<JobProgress>? ProgressChanged;

    /// <summary>
    ///     Initialize job progress with the stages that will be executed.
    /// </summary>
    public void Initialize(params string[] stageNames)
    {
        lock (_lock)
        {
            StageNames = stageNames;
            CurrentStageIndex = 0;
            _completedStages.Clear();
            CurrentStageProgress = null;
        }
        RaiseProgressChanged();
    }

    /// <summary>
    ///     Start a new stage with known total items.
    /// </summary>
    public void StartStage(string stageName, int totalItems)
    {
        lock (_lock)
        {
            CurrentStageIndex = Array.IndexOf(StageNames, stageName);
            if (CurrentStageIndex < 0)
            {
                CurrentStageIndex = _completedStages.Count;
            }
            CurrentStageProgress = new JobStageProgress(stageName, 0, totalItems);
        }
        RaiseProgressChanged();
    }

    /// <summary>
    ///     Start a new stage with indeterminate progress.
    /// </summary>
    public void StartStage(string stageName, string? description = null)
    {
        lock (_lock)
        {
            CurrentStageIndex = Array.IndexOf(StageNames, stageName);
            if (CurrentStageIndex < 0)
            {
                CurrentStageIndex = _completedStages.Count;
            }
            CurrentStageProgress = new JobStageProgress(stageName, 0, 0, description);
        }
        RaiseProgressChanged();
    }

    /// <summary>
    ///     Update progress within the current stage.
    /// </summary>
    public void UpdateProgress(int currentItem, string? itemDescription = null)
    {
        lock (_lock)
        {
            if (CurrentStageProgress != null)
            {
                CurrentStageProgress = CurrentStageProgress with
                {
                    CurrentItem = currentItem,
                    CurrentItemDescription = itemDescription ?? CurrentStageProgress.CurrentItemDescription
                };
            }
        }
        RaiseProgressChanged();
    }

    /// <summary>
    ///     Update progress with a description only (for indeterminate stages).
    /// </summary>
    public void UpdateProgress(string description)
    {
        lock (_lock)
        {
            if (CurrentStageProgress != null)
            {
                CurrentStageProgress = CurrentStageProgress with { CurrentItemDescription = description };
            }
        }
        RaiseProgressChanged();
    }

    /// <summary>
    ///     Mark the current stage as complete and move to the next.
    /// </summary>
    public void CompleteStage()
    {
        lock (_lock)
        {
            if (CurrentStageProgress != null)
            {
                _completedStages.Add(CurrentStageProgress.StageName);
                CurrentStageProgress = null;
            }
        }
        RaiseProgressChanged();
    }

    /// <summary>
    ///     Get overall progress percentage across all stages.
    /// </summary>
    public double OverallPercentComplete
    {
        get
        {
            lock (_lock)
            {
                if (TotalStages == 0) return 0;

                var completedWeight = _completedStages.Count;
                var currentStageWeight = CurrentStageProgress?.PercentComplete / 100 ?? 0;

                return (completedWeight + currentStageWeight) / TotalStages * 100;
            }
        }
    }

    public override string ToString()
    {
        lock (_lock)
        {
            var overall = $"Overall: {OverallPercentComplete:F1}% ({_completedStages.Count}/{TotalStages} stages)";
            return CurrentStageProgress != null
                ? $"{overall} | {CurrentStageProgress}"
                : overall;
        }
    }

    private void RaiseProgressChanged()
    {
        ProgressChanged?.Invoke(this);
    }
}

public class MelodeeJobExecutionContext(CancellationToken cancellation) : IJobExecutionContext
{
    public const string ForceMode = "ForceMode";
    public const string ScanJustDirectory = "ScanJustDirectory";
    public const string Verbose = "Verbose";
    public const string ChainOnComplete = "ChainOnComplete";
    public const string DirectoryRunContext = "DirectoryRunContext";

    private readonly Dictionary<object, object> _dataMap = new();

    /// <summary>
    ///     The result of the job execution. Jobs should set this to indicate what happened.
    /// </summary>
    public JobResult? JobResult { get; set; }

    /// <summary>
    ///     Progress tracking for the job. Jobs can use this to report progress.
    /// </summary>
    public JobProgress Progress { get; } = new();

    public void Put(object key, object objectValue)
    {
        if (!_dataMap.TryAdd(key, objectValue))
        {
            _dataMap[key] = objectValue;
        }

        if (key is string stringKey)
        {
            MergedJobDataMap[stringKey] = objectValue;
        }
    }

    public object? Get(object key)
    {
        _dataMap.TryGetValue(key, out var value);
        return value;
    }

    public IScheduler Scheduler { get; } = null!;
    public ITrigger Trigger { get; } = null!;
    public ICalendar? Calendar { get; } = null!;
    public bool Recovering { get; } = false;
    public TriggerKey RecoveringTriggerKey { get; } = null!;
    public int RefireCount { get; } = 0;
    public JobDataMap MergedJobDataMap { get; } = new();
    public IJobDetail JobDetail { get; } = new JobDetailImpl();
    public IJob JobInstance { get; } = null!;
    public DateTimeOffset FireTimeUtc { get; } = DateTimeOffset.MinValue;
    public DateTimeOffset? ScheduledFireTimeUtc { get; } = DateTimeOffset.MinValue;
    public DateTimeOffset? PreviousFireTimeUtc { get; } = DateTimeOffset.MinValue;
    public DateTimeOffset? NextFireTimeUtc { get; } = DateTimeOffset.MinValue;
    public string FireInstanceId { get; } = null!;
    public object? Result { get; set; }
    public TimeSpan JobRunTime { get; } = TimeSpan.Zero;
    public CancellationToken CancellationToken { get; } = cancellation;
}
