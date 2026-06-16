using Melodee.Common.Jobs;

namespace Melodee.Tests.Common.Jobs;

public class MelodeeJobExecutionContextTests
{
    [Fact]
    public void Constructor_WithCancellationToken_SetsCancellationToken()
    {
        var cts = new CancellationTokenSource();
        var context = new MelodeeJobExecutionContext(cts.Token);

        Assert.Equal(cts.Token, context.CancellationToken);
    }

    [Fact]
    public void Constructor_WithDefaultCancellationToken_SetsNoneCancellationToken()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);

        Assert.Equal(CancellationToken.None, context.CancellationToken);
    }

    [Fact]
    public void Put_WithNewKey_AddsValue()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);
        const string key = "testKey";
        const string value = "testValue";

        context.Put(key, value);

        Assert.Equal(value, context.Get(key));
    }

    [Fact]
    public void Put_WithExistingKey_UpdatesValue()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);
        const string key = "testKey";
        const string originalValue = "originalValue";
        const string newValue = "newValue";

        context.Put(key, originalValue);
        context.Put(key, newValue);

        Assert.Equal(newValue, context.Get(key));
    }

    [Fact]
    public void Constructor_MergedJobDataMap_IsNotNull()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);

        Assert.NotNull(context.MergedJobDataMap);
    }

    [Fact]
    public void Put_WithStringKey_MirrorsValueToMergedJobDataMap()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);
        const string key = MelodeeJobExecutionContext.ChainOnComplete;

        context.Put(key, true);

        Assert.True(context.MergedJobDataMap.ContainsKey(key));
        Assert.True(context.MergedJobDataMap.GetBoolean(key));
    }

    [Fact]
    public void Get_WithNonExistentKey_ReturnsNull()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);

        var result = context.Get("nonExistentKey");

        Assert.Null(result);
    }

    [Fact]
    public void Get_WithExistingKey_ReturnsStoredValue()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);
        const string key = "myKey";
        var value = new { Name = "Test", Count = 42 };

        context.Put(key, value);
        var result = context.Get(key);

        Assert.Equal(value, result);
    }

    [Fact]
    public void JobResult_InitialValue_IsNull()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);

        Assert.Null(context.JobResult);
    }

    [Fact]
    public void JobResult_WhenSet_ReturnsSetValue()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);
        var jobResult = new JobResult(JobResultStatus.Success, "Test message");

        context.JobResult = jobResult;

        Assert.NotNull(context.JobResult);
        Assert.Equal(JobResultStatus.Success, context.JobResult.Status);
        Assert.Equal("Test message", context.JobResult.Message);
    }

    [Fact]
    public void JobResult_CanBeOverwritten()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);
        var firstResult = new JobResult(JobResultStatus.Skipped, "First");
        var secondResult = new JobResult(JobResultStatus.Failed, "Second");

        context.JobResult = firstResult;
        context.JobResult = secondResult;

        Assert.Equal(JobResultStatus.Failed, context.JobResult.Status);
        Assert.Equal("Second", context.JobResult.Message);
    }

    [Fact]
    public void Put_WithIntegerKey_WorksCorrectly()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);
        const int key = 123;
        const string value = "intKeyValue";

        context.Put(key, value);

        Assert.Equal(value, context.Get(key));
    }

    [Fact]
    public void Put_WithNullValue_StoresNull()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);
        const string key = "nullKey";

        context.Put(key, null!);

        Assert.Null(context.Get(key));
    }

    [Fact]
    public void ForceMode_Constant_HasExpectedValue()
    {
        Assert.Equal("ForceMode", MelodeeJobExecutionContext.ForceMode);
    }

    [Fact]
    public void ScanJustDirectory_Constant_HasExpectedValue()
    {
        Assert.Equal("ScanJustDirectory", MelodeeJobExecutionContext.ScanJustDirectory);
    }

    [Fact]
    public void Verbose_Constant_HasExpectedValue()
    {
        Assert.Equal("Verbose", MelodeeJobExecutionContext.Verbose);
    }

    [Fact]
    public void Progress_IsNotNull_WhenContextCreated()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);

        Assert.NotNull(context.Progress);
    }

    [Fact]
    public void Progress_CanBeInitialized_WithStages()
    {
        var context = new MelodeeJobExecutionContext(CancellationToken.None);

        context.Progress.Initialize("Stage1", "Stage2", "Stage3");

        Assert.Equal(3, context.Progress.TotalStages);
        Assert.Equal(new[] { "Stage1", "Stage2", "Stage3" }, context.Progress.StageNames);
    }
}
