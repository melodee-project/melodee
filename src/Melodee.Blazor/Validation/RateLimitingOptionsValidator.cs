using Melodee.Blazor.Configuration;
using Microsoft.Extensions.Options;

namespace Melodee.Blazor.Validation;

public class RateLimitingOptionsValidator : IValidateOptions<RateLimitingOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitingOptions options)
    {
        var errors = new List<string>();

        if (options.MelodeeApi.TokenLimit <= 0)
        {
            errors.Add("RateLimiting:MelodeeApi:TokenLimit must be positive.");
        }
        if (options.MelodeeApi.QueueLimit < 0)
        {
            errors.Add("RateLimiting:MelodeeApi:QueueLimit must be non-negative.");
        }
        if (options.MelodeeApi.ReplenishmentPeriodSeconds <= 0)
        {
            errors.Add("RateLimiting:MelodeeApi:ReplenishmentPeriodSeconds must be positive.");
        }
        if (options.MelodeeApi.TokensPerPeriod <= 0)
        {
            errors.Add("RateLimiting:MelodeeApi:TokensPerPeriod must be positive.");
        }

        if (options.MelodeeAuth.TokenLimit <= 0)
        {
            errors.Add("RateLimiting:MelodeeAuth:TokenLimit must be positive.");
        }
        if (options.MelodeeAuth.QueueLimit < 0)
        {
            errors.Add("RateLimiting:MelodeeAuth:QueueLimit must be non-negative.");
        }
        if (options.MelodeeAuth.ReplenishmentPeriodSeconds <= 0)
        {
            errors.Add("RateLimiting:MelodeeAuth:ReplenishmentPeriodSeconds must be positive.");
        }
        if (options.MelodeeAuth.TokensPerPeriod <= 0)
        {
            errors.Add("RateLimiting:MelodeeAuth:TokensPerPeriod must be positive.");
        }

        if (options.MelodeeAuth.TokenLimit > options.MelodeeApi.TokenLimit)
        {
            errors.Add("RateLimiting:MelodeeAuth:TokenLimit must be less than or equal to RateLimiting:MelodeeApi:TokenLimit (auth policy should be stricter).");
        }
        if (options.MelodeeAuth.ReplenishmentPeriodSeconds < options.MelodeeApi.ReplenishmentPeriodSeconds)
        {
            errors.Add("RateLimiting:MelodeeAuth:ReplenishmentPeriodSeconds must be greater than or equal to RateLimiting:MelodeeApi:ReplenishmentPeriodSeconds (auth policy should be stricter).");
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }
}
