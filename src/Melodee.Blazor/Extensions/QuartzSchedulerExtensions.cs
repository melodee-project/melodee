using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Quartz;

namespace Melodee.Blazor.Extensions;

public static class QuartzSchedulerExtensions
{
    private const ScanStatus DefaultScanStatus = ScanStatus.Idle;

    public static async Task ScheduleJobIfConfigured<TJob>(
        this IScheduler scheduler,
        IMelodeeConfiguration configuration,
        string settingKey,
        JobKey jobKey,
        string? triggerName = null,
        bool includeScanStatusJobData = false) where TJob : IJob
    {
        var cronExpression = configuration.GetValue<string>(settingKey);
        if (cronExpression.Nullify() == null)
        {
            return;
        }

        var jobBuilder = JobBuilder.Create<TJob>()
            .WithIdentity(jobKey)
            .Build();

        var triggerBuilder = TriggerBuilder.Create()
            .WithIdentity(triggerName ?? $"{jobKey.Name}-trigger")
            .WithCronSchedule(cronExpression!)
            .StartNow();

        if (includeScanStatusJobData)
        {
            triggerBuilder.UsingJobData(JobMapNameRegistry.ScanStatus, DefaultScanStatus.ToString())
                .UsingJobData(JobMapNameRegistry.Count, 0);
        }

        await scheduler.ScheduleJob(jobBuilder, triggerBuilder.Build());
    }
}
