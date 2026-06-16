using Melodee.Common.Jobs;
using Moq;
using Quartz;

namespace Melodee.Tests.Common.Jobs;

public class LibraryInboundProcessJobTests
{
    [Fact]
    public void ShouldBypassScanTimestamp_WithManualContext_ReturnsTrue()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);

        var result = InvokeShouldBypassScanTimestamp(context);

        Assert.True(result);
    }

    [Fact]
    public void ShouldBypassScanTimestamp_WithScheduledContext_ReturnsFalse()
    {
        var context = CreateScheduledContext(new JobDataMap());

        var result = InvokeShouldBypassScanTimestamp(context);

        Assert.False(result);
    }

    [Fact]
    public void ShouldBypassScanTimestamp_WithScheduledForceModeContext_ReturnsTrue()
    {
        var jobDataMap = new JobDataMap
        {
            [MelodeeJobExecutionContext.ForceMode] = true
        };
        var context = CreateScheduledContext(jobDataMap);

        var result = InvokeShouldBypassScanTimestamp(context);

        Assert.True(result);
    }

    private static IJobExecutionContext CreateScheduledContext(JobDataMap jobDataMap)
    {
        var context = new Mock<IJobExecutionContext>();
        context.SetupGet(x => x.Trigger).Returns(Mock.Of<ICronTrigger>());
        context.SetupGet(x => x.MergedJobDataMap).Returns(jobDataMap);

        return context.Object;
    }

    private static bool InvokeShouldBypassScanTimestamp(IJobExecutionContext context)
    {
        var method = typeof(LibraryInboundProcessJob).GetMethod(
            "ShouldBypassScanTimestamp",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (bool)method!.Invoke(null, [context])!;
    }
}
