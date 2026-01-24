using System.Text.Json.Serialization;

namespace Melodee.Common.Models.Scripting;

public sealed record ScriptDefaultConfig
{
    public bool Enabled { get; init; } = true;

    public string Body { get; init; } = string.Empty;

    public string OnDeny { get; init; } = "skip";
}

public sealed record ScriptConfig
{
    public int Version { get; init; } = 1;

    public bool Enabled { get; init; } = true;

    public string Engine { get; init; } = "jint";

    public int TimeoutMs { get; init; } = 50;

    public int MaxStatements { get; init; } = 10000;

    public ScriptDefaultConfig Default { get; init; } = new();

    public string? DefaultBody { get; init; }

    public List<ScriptOverrideConfig> Overrides { get; init; } = new();

    [JsonIgnore]
    public string SettingKey { get; init; } = string.Empty;

    [JsonIgnore]
    public string SettingEtag { get; init; } = string.Empty;
}
