namespace Melodee.Blazor.Components.Components;

public sealed record MonacoCompletionItem
{
    public string Label { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public string? Documentation { get; init; }
    public string? InsertText { get; init; }
}

