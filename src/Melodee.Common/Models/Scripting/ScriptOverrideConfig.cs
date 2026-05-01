namespace Melodee.Common.Models.Scripting;

public sealed record ScriptOverrideConfig
{
    public bool Enabled { get; set; } = true;

    public int? LibraryId { get; set; }

    public string? PathPrefix { get; set; }

    public string OnDeny { get; set; } = "skip";

    public string Body { get; set; } = string.Empty;
}
