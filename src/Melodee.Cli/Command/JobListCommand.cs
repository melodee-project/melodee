using Melodee.Cli.CommandSettings;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Quartz;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Melodee.Cli.Command;

/// <summary>
///     List all known background jobs with their execution history and statistics.
/// </summary>
public class JobListCommand : CommandBase<JobListSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, JobListSettings settings, CancellationToken cancellationToken)
    {
        using var scope = CreateServiceProvider().CreateScope();
        var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Get cron expressions from settings to calculate next fire times
        var nextFireTimes = new Dictionary<string, DateTimeOffset?>();
        var cronSettings = await dbContext.Settings
            .Where(s => s.Key.StartsWith("jobs.") && s.Key.EndsWith(".cronExpression"))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);

        foreach (var (jobKey, jobInfo) in JobRegistry.Jobs)
        {
            if (jobInfo.CronSettingKey != null &&
                cronSettings.TryGetValue(jobInfo.CronSettingKey, out var cronExpression) &&
                !string.IsNullOrWhiteSpace(cronExpression))
            {
                try
                {
                    var cron = new CronExpression(cronExpression);
                    var nextFire = cron.GetNextValidTimeAfter(DateTimeOffset.UtcNow);
                    nextFireTimes[jobKey.Name] = nextFire;
                }
                catch
                {
                    // Invalid cron expression, skip
                }
            }
        }

        var twentyFourHoursAgo = SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromHours(24));

        var jobStats = await dbContext.JobHistories
            .GroupBy(h => h.JobName)
            .Select(g => new
            {
                JobName = g.Key,
                TotalRuns = g.Count(),
                SuccessCount = g.Count(h => h.Success),
                FailureCount = g.Count(h => !h.Success),
                ManualTriggers = g.Count(h => h.WasManualTrigger),
                AvgDurationMs = g.Where(h => h.DurationInMs.HasValue).Average(h => h.DurationInMs),
                MinDurationMs = g.Where(h => h.DurationInMs.HasValue).Min(h => h.DurationInMs),
                MaxDurationMs = g.Where(h => h.DurationInMs.HasValue).Max(h => h.DurationInMs),
                LastRunAt = g.Max(h => h.CompletedAt),
                LastSuccess = g.Where(h => h.Success).Max(h => h.CompletedAt),
                LastFailure = g.Where(h => !h.Success).Max(h => h.CompletedAt),
                LastErrorMessage = g.Where(h => !h.Success)
                    .OrderByDescending(h => h.CompletedAt)
                    .Select(h => h.ErrorMessage)
                    .FirstOrDefault(),
                Failures24h = g.Count(h => !h.Success && h.StartedAt > twentyFourHoursAgo)
            })
            .ToListAsync(cancellationToken);

        var allKnownJobs = JobRegistry.AllJobNames.ToHashSet();
        foreach (var stat in jobStats)
        {
            allKnownJobs.Add(stat.JobName);
        }

        var jobList = allKnownJobs
            .OrderBy(j => j)
            .Select(jobName =>
            {
                var stats = jobStats.FirstOrDefault(s => s.JobName == jobName);
                nextFireTimes.TryGetValue(jobName, out var nextRun);
                return new
                {
                    JobName = jobName,
                    Description = JobRegistry.GetDescription(jobName),
                    TotalRuns = stats?.TotalRuns ?? 0,
                    SuccessCount = stats?.SuccessCount ?? 0,
                    FailureCount = stats?.FailureCount ?? 0,
                    ManualTriggers = stats?.ManualTriggers ?? 0,
                    SuccessRate = stats != null && stats.TotalRuns > 0
                        ? (double)stats.SuccessCount / stats.TotalRuns * 100
                        : (double?)null,
                    AvgDurationMs = stats?.AvgDurationMs,
                    MinDurationMs = stats?.MinDurationMs,
                    MaxDurationMs = stats?.MaxDurationMs,
                    LastRunAt = stats?.LastRunAt,
                    NextRunAt = nextRun,
                    LastSuccess = stats?.LastSuccess,
                    LastFailure = stats?.LastFailure,
                    LastErrorMessage = stats?.LastErrorMessage,
                    Failures24h = stats?.Failures24h ?? 0
                };
            })
            .ToList();

        if (settings.ReturnRaw)
        {
            var jsonOutput = jobList.Select(job => new
            {
                job.JobName,
                job.Description,
                job.TotalRuns,
                job.SuccessCount,
                job.FailureCount,
                job.ManualTriggers,
                SuccessRate = job.SuccessRate.HasValue ? Math.Round(job.SuccessRate.Value, 1) : (double?)null,
                AvgDurationMs = job.AvgDurationMs.HasValue ? Math.Round(job.AvgDurationMs.Value, 2) : (double?)null,
                MinDurationMs = job.MinDurationMs.HasValue ? Math.Round(job.MinDurationMs.Value, 2) : (double?)null,
                MaxDurationMs = job.MaxDurationMs.HasValue ? Math.Round(job.MaxDurationMs.Value, 2) : (double?)null,
                LastRunAt = job.LastRunAt?.ToDateTimeUtc(),
                NextRunAt = job.NextRunAt?.UtcDateTime,
                LastSuccess = job.LastSuccess?.ToDateTimeUtc(),
                LastFailure = job.LastFailure?.ToDateTimeUtc(),
                job.LastErrorMessage,
                job.Failures24h
            });
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(jsonOutput, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }

        var totalJobs = jobList.Count;
        var totalRuns = jobList.Sum(j => j.TotalRuns);
        var totalFailures24h = jobList.Sum(j => j.Failures24h);

        var summaryTable = new Table();
        summaryTable.Border = TableBorder.Rounded;
        summaryTable.AddColumn(new TableColumn("[blue]Jobs[/]").Centered());
        summaryTable.AddColumn(new TableColumn("[blue]Total Runs[/]").Centered());
        summaryTable.AddColumn(new TableColumn("[blue]Failures (24h)[/]").Centered());
        summaryTable.AddColumn(new TableColumn("[blue]Active Jobs[/]").Centered());
        summaryTable.AddRow(
            $"[bold]{totalJobs}[/]",
            $"[bold]{totalRuns:N0}[/]",
            totalFailures24h > 0 ? $"[bold red]{totalFailures24h}[/]" : $"[bold green]0[/]",
            $"[bold]{jobList.Count(j => j.TotalRuns > 0)}[/]"
        );

        AnsiConsole.Write(summaryTable);
        AnsiConsole.WriteLine();

        var table = new Table();
        table.Border = TableBorder.Rounded;
        table.AddColumn(new TableColumn("[bold]Job Name[/]"));
        table.AddColumn(new TableColumn("[bold]Runs[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Success[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Avg Time[/]").RightAligned());
        table.AddColumn(new TableColumn("[bold]Last Run[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Next Run*[/]").Centered());
        table.AddColumn(new TableColumn("[bold]Status[/]").Centered());

        foreach (var job in jobList)
        {
            var successRateText = job.SuccessRate.HasValue
                ? $"{job.SuccessRate:F0}%"
                : "---";

            var successRateColor = job.SuccessRate switch
            {
                >= 95 => "green",
                >= 80 => "yellow",
                _ when job.SuccessRate.HasValue => "red",
                _ => "grey"
            };

            var avgTimeText = job.AvgDurationMs.HasValue
                ? FormatDuration(job.AvgDurationMs.Value)
                : "[grey]---[/]";

            var lastRunText = job.LastRunAt.HasValue
                ? job.LastRunAt.Value.ToDateTimeUtc().ToString(Iso8601DateFormat)
                : "[grey]Never[/]";

            var nextRunText = job.NextRunAt.HasValue
                ? job.NextRunAt.Value.LocalDateTime.ToString(Iso8601DateFormat)
                : "[grey]Not scheduled[/]";

            string statusMarkup;
            if (job.Failures24h > 0)
            {
                statusMarkup = $"[red]⚠ {job.Failures24h} failures[/]";
            }
            else if (job.TotalRuns == 0)
            {
                statusMarkup = "[grey]No runs[/]";
            }
            else if (job.FailureCount > 0 && job.LastFailure > job.LastSuccess)
            {
                statusMarkup = "[yellow]Last failed[/]";
            }
            else
            {
                statusMarkup = "[green]✓ OK[/]";
            }

            table.AddRow(
                $"[bold]{job.JobName.EscapeMarkup()}[/]",
                job.TotalRuns.ToString("N0"),
                $"[{successRateColor}]{successRateText}[/]",
                avgTimeText,
                lastRunText,
                nextRunText,
                statusMarkup
            );
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]* Next Run is approximate, calculated from cron expression[/]");
        AnsiConsole.WriteLine();

        var jobsWithErrors = jobList.Where(j => !string.IsNullOrEmpty(j.LastErrorMessage)).ToList();
        if (jobsWithErrors.Count != 0)
        {
            AnsiConsole.MarkupLine("[bold red]Recent Errors:[/]");
            foreach (var job in jobsWithErrors.Take(3))
            {
                var errorPanel = new Panel($"[red]{job.LastErrorMessage?.EscapeMarkup()}[/]")
                    .Header($"[bold]{job.JobName}[/] - {job.LastFailure?.ToDateTimeUtc().ToString(Iso8601DateFormat)}")
                    .Border(BoxBorder.Rounded)
                    .BorderColor(Color.Red);
                AnsiConsole.Write(errorPanel);
            }
        }

        return 0;
    }

    private static string FormatDuration(double milliseconds)
    {
        return milliseconds switch
        {
            < 1000 => $"{milliseconds:F0}ms",
            < 60000 => $"{milliseconds / 1000:F1}s",
            < 3600000 => $"{milliseconds / 60000:F1}m",
            _ => $"{milliseconds / 3600000:F1}h"
        };
    }
}
