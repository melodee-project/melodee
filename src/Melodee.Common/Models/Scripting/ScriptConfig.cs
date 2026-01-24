using System.Text.Json.Serialization;

namespace Melodee.Common.Models.Scripting;

public sealed record ScriptDefaultConfig
{
    public bool Enabled { get; set; } = true;

    public string Body { get; set; } = string.Empty;

    public string OnDeny { get; set; } = "skip";
}

public sealed record ScriptConfig
{
    public int Version { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public string Engine { get; set; } = "jint";

    public int TimeoutMs { get; set; } = 50;

    public int MaxStatements { get; set; } = 10000;

    public ScriptDefaultConfig Default { get; set; } = new();

    public string? DefaultBody { get; set; }

    public List<ScriptOverrideConfig> Overrides { get; set; } = new();

    [JsonIgnore]
    public string SettingKey { get; set; } = string.Empty;

    [JsonIgnore]
    public string SettingEtag { get; set; } = string.Empty;
}
