namespace Melodee.Common.Models.Scripting;

public record ScriptEvaluationResult
{
    public bool Result { get; init; }

    public bool IsDefault { get; init; }

    public string? SelectedOverrideId { get; init; }

    public TimeSpan Duration { get; init; }

    public string? ErrorMessage { get; init; }
}
