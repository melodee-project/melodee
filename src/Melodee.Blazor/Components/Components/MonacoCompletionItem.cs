using System.Text.Json.Serialization;

namespace Melodee.Blazor.Components.Components;

public sealed record MonacoCompletionItem
{
    [JsonPropertyName("label")]
    public string Label { get; init; } = string.Empty;

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("documentation")]
    public string? Documentation { get; init; }

    [JsonPropertyName("insertText")]
    public string? InsertText { get; init; }
}
