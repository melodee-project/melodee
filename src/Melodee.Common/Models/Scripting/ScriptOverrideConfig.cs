namespace Melodee.Common.Models.Scripting;

public sealed record ScriptOverrideConfig
{
    public bool Enabled { get; init; } = true;

    public int? LibraryId { get; init; }

    public string? PathPrefix { get; init; }

    public string OnDeny { get; init; } = "skip";

    public string Body { get; init; } = string.Empty;
}
