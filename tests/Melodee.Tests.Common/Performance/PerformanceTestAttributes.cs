namespace Melodee.Tests.Common.Performance;

internal static class PerformanceTestGate
{
    private const string PerformanceTestEnvironmentVariable = "MELODEE_RUN_PERF_TESTS";

    public const string SkipReason =
        "Set MELODEE_RUN_PERF_TESTS=true to run performance and benchmark tests.";

    public static bool IsEnabled =>
        string.Equals(
            Environment.GetEnvironmentVariable(PerformanceTestEnvironmentVariable),
            "true",
            StringComparison.OrdinalIgnoreCase);
}

public sealed class PerformanceFactAttribute : FactAttribute
{
    public PerformanceFactAttribute()
    {
        if (!PerformanceTestGate.IsEnabled)
        {
            Skip = PerformanceTestGate.SkipReason;
        }
    }
}

public sealed class PerformanceTheoryAttribute : TheoryAttribute
{
    public PerformanceTheoryAttribute()
    {
        if (!PerformanceTestGate.IsEnabled)
        {
            Skip = PerformanceTestGate.SkipReason;
        }
    }
}
