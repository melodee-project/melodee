namespace Melodee.Common.Models.Scripting;

public record ScriptEvaluationResult
{
    public bool Result { get; init; }

    public bool IsDefault { get; init; }

    public string? SelectedOverrideId { get; init; }

    public string? ScriptKey { get; init; }

    public string? ScriptHash { get; init; }

    public string? OnDeny { get; init; }

    public TimeSpan Duration { get; init; }

    public string? ErrorMessage { get; init; }
}
