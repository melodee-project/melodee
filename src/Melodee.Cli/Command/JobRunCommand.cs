using System.Diagnostics;
using Melodee.Cli.CommandSettings;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Jobs;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.Scrobbling;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Services;
using Melodee.Common.Services.SearchEngines;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Serilog;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     Run a background job by name.
/// </summary>
public class JobRunCommand : CommandBase<JobRunSettings>
{
    private static readonly Dictionary<string, Type> JobTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ArtistHousekeepingJob"] = typeof(ArtistHousekeepingJob),
        ["ArtistSearchEngineRepositoryHousekeepingJob"] = typeof(ArtistSearchEngineRepositoryHousekeepingJob),
        ["ChartUpdateJob"] = typeof(ChartUpdateJob),
        ["MusicBrainzUpdateDatabaseJob"] = typeof(MusicBrainzUpdateDatabaseJob),
        ["NowPlayingCleanupJob"] = typeof(NowPlayingCleanupJob)
    };

    public override async Task<int> ExecuteAsync(CommandContext context, JobRunSettings settings, CancellationToken cancellationToken)
    {
        var jobName = settings.JobName.Trim();

        if (!JobTypes.TryGetValue(jobName, out var jobType))
        {
            AnsiConsole.MarkupLine($"[red]Unknown job: {jobName.EscapeMarkup()}[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[yellow]Available jobs:[/]");
            foreach (var name in JobTypes.Keys.OrderBy(k => k))
            {
                AnsiConsole.MarkupLine($"  • {name}");
            }
            return 1;
        }

        using var scope = CreateServiceProvider().CreateScope();

        JobBase job;
        try
        {
            job = CreateJob(jobType, scope.ServiceProvider);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Failed to create job instance: {ex.Message.EscapeMarkup()}[/]");
            return 1;
        }

        var jc = new MelodeeJobExecutionContext(cancellationToken);
        if (settings.BatchSize != null)
        {
            jc.Put(JobMapNameRegistry.BatchSize, settings.BatchSize);
        }

        AnsiConsole.MarkupLine($"[green]Starting job:[/] {jobName}");

        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        var startedAt = SystemClock.Instance.GetCurrentInstant();
        var stopwatch = Stopwatch.StartNew();
        string? errorMessage = null;
        var success = false;
        JobResult? jobResult = null;

        try
        {
            // Subscribe to progress updates
            jc.Progress.ProgressChanged += progress =>
            {
                // Progress is logged but not displayed inline during Status spinner
                // The progress info will be available after completion
            };

            await AnsiConsole.Progress()
                .AutoClear(false)
                .HideCompleted(false)
                .Columns(
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new SpinnerColumn())
                .StartAsync(async ctx =>
                {
                    var overallTask = ctx.AddTask($"[bold]{jobName}[/]", maxValue: 100);
                    ProgressTask? currentStageTask = null;
                    string? lastStageName = null;

                    // Subscribe to progress updates
                    jc.Progress.ProgressChanged += progress =>
                    {
                        overallTask.Value = progress.OverallPercentComplete;

                        // Handle stage changes
                        if (progress.CurrentStageProgress != null)
                        {
                            var stageName = progress.CurrentStageProgress.StageName;

                            if (stageName != lastStageName)
                            {
                                // Complete previous stage task
                                if (currentStageTask != null)
                                {
                                    currentStageTask.Value = currentStageTask.MaxValue;
                                    currentStageTask.StopTask();
                                }

                                // Create new stage task
                                var maxValue = progress.CurrentStageProgress.TotalItems > 0
                                    ? progress.CurrentStageProgress.TotalItems
                                    : 100;
                                currentStageTask = ctx.AddTask($"  {stageName}", maxValue: maxValue);
                                lastStageName = stageName;
                            }

                            // Update current stage progress
                            if (currentStageTask != null)
                            {
                                if (progress.CurrentStageProgress.TotalItems > 0)
                                {
                                    currentStageTask.Value = progress.CurrentStageProgress.CurrentItem;
                                }
                                else
                                {
                                    // Indeterminate progress - pulse
                                    currentStageTask.IsIndeterminate = true;
                                }

                                if (progress.CurrentStageProgress.CurrentItemDescription != null)
                                {
                                    currentStageTask.Description = $"  {stageName}: {progress.CurrentStageProgress.CurrentItemDescription}";
                                }
                            }
                        }
                    };

                    await job.Execute(jc);

                    // Complete any remaining tasks
                    if (currentStageTask != null)
                    {
                        currentStageTask.Value = currentStageTask.MaxValue;
                        currentStageTask.StopTask();
                    }
                    overallTask.Value = 100;
                });

            jobResult = jc.JobResult;

            if (jobResult != null)
            {
                switch (jobResult.Status)
                {
                    case JobResultStatus.Success:
                        success = true;
                        AnsiConsole.MarkupLine($"[green]✓ Job completed successfully:[/] {jobName}");
                        AnsiConsole.MarkupLine($"  [grey]{jobResult.Message.EscapeMarkup()}[/]");
                        break;
                    case JobResultStatus.Skipped:
                        success = true; // Skipped is not a failure
                        AnsiConsole.MarkupLine($"[yellow]⚠ Job skipped:[/] {jobName}");
                        AnsiConsole.MarkupLine($"  [grey]{jobResult.Message.EscapeMarkup()}[/]");
                        break;
                    case JobResultStatus.Failed:
                        errorMessage = jobResult.Message;
                        AnsiConsole.MarkupLine($"[red]✗ Job failed:[/] {jobName}");
                        AnsiConsole.MarkupLine($"  [grey]{jobResult.Message.EscapeMarkup()}[/]");
                        break;
                }
            }
            else
            {
                // Job didn't set a result - assume success for backward compatibility
                success = true;
                AnsiConsole.MarkupLine($"[green]✓ Job completed:[/] {jobName}");
            }
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            AnsiConsole.MarkupLine($"[red]✗ Job failed:[/] {ex.Message.EscapeMarkup()}");
            if (settings.Verbose)
            {
                AnsiConsole.WriteException(ex);
            }
        }
        finally
        {
            stopwatch.Stop();

            if (job.DoCreateJobHistory)
            {
                await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
                var jobHistory = new JobHistory
                {
                    JobName = jobName,
                    StartedAt = startedAt,
                    CompletedAt = SystemClock.Instance.GetCurrentInstant(),
                    DurationInMs = stopwatch.Elapsed.TotalMilliseconds,
                    Success = success,
                    ErrorMessage = errorMessage,
                    WasManualTrigger = true
                };
                dbContext.JobHistories.Add(jobHistory);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            AnsiConsole.MarkupLine($"[grey]Elapsed time: {stopwatch.Elapsed.TotalSeconds:F1}s[/]");
        }

        return success ? 0 : 1;
    }

    private static JobBase CreateJob(Type jobType, IServiceProvider sp)
    {
        var logger = sp.GetRequiredService<ILogger>();
        var configFactory = sp.GetRequiredService<IMelodeeConfigurationFactory>();

        if (jobType == typeof(ArtistHousekeepingJob))
        {
            return new ArtistHousekeepingJob(
                logger,
                configFactory,
                sp.GetRequiredService<ArtistService>(),
                sp.GetRequiredService<IDbContextFactory<MelodeeDbContext>>(),
                sp.GetRequiredService<ArtistImageSearchEngineService>(),
                sp.GetRequiredService<IHttpClientFactory>());
        }

        if (jobType == typeof(ArtistSearchEngineRepositoryHousekeepingJob))
        {
            return new ArtistSearchEngineRepositoryHousekeepingJob(
                logger,
                configFactory,
                sp.GetRequiredService<ArtistSearchEngineService>(),
                sp.GetRequiredService<IDbContextFactory<ArtistSearchEngineServiceDbContext>>());
        }

        if (jobType == typeof(ChartUpdateJob))
        {
            return new ChartUpdateJob(
                logger,
                configFactory,
                sp.GetRequiredService<ChartService>());
        }

        if (jobType == typeof(MusicBrainzUpdateDatabaseJob))
        {
            return new MusicBrainzUpdateDatabaseJob(
                logger,
                configFactory,
                sp.GetRequiredService<SettingService>(),
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<IDbContextFactory<MusicBrainzDbContext>>(),
                sp.GetRequiredService<IMusicBrainzRepository>());
        }

        if (jobType == typeof(NowPlayingCleanupJob))
        {
            return new NowPlayingCleanupJob(
                logger,
                configFactory,
                sp.GetRequiredService<NowPlayingDatabaseRepository>());
        }

        throw new InvalidOperationException($"No factory method for job type: {jobType.Name}");
    }
}
