using System.ComponentModel.DataAnnotations;

namespace Melodee.Blazor.Configuration;

public class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitingPolicyOptions MelodeeApi { get; set; } = new()
    {
        TokenLimit = 30,
        QueueLimit = 10,
        ReplenishmentPeriodSeconds = 30,
        TokensPerPeriod = 30,
        AutoReplenishment = true
    };

    public RateLimitingPolicyOptions MelodeeAuth { get; set; } = new()
    {
        TokenLimit = 10,
        QueueLimit = 5,
        ReplenishmentPeriodSeconds = 60,
        TokensPerPeriod = 10,
        AutoReplenishment = true
    };
}

public class RateLimitingPolicyOptions
{
    [Range(1, int.MaxValue, ErrorMessage = "TokenLimit must be at least 1")]
    public int TokenLimit { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "QueueLimit must be non-negative")]
    public int QueueLimit { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "ReplenishmentPeriodSeconds must be at least 1")]
    public int ReplenishmentPeriodSeconds { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "TokensPerPeriod must be at least 1")]
    public int TokensPerPeriod { get; set; }

    public bool AutoReplenishment { get; set; }
}
