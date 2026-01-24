namespace Melodee.Common.Models.Scripting;

public sealed record ScriptConfig
{
    public bool Enabled { get; init; } = true;

    public string Engine { get; init; } = "jint";

    public int TimeoutMs { get; init; } = 50;

    public int MaxStatements { get; init; } = 10000;

    public string? DefaultBody { get; init; }

    public List<ScriptOverrideConfig> Overrides { get; init; } = new();
}
