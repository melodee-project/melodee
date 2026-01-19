namespace Melodee.Cli.Configuration;

/// <summary>
/// Root configuration for mcli profiles.
/// Loaded from ~/.config/melodee/mcli.json (Linux/macOS) or %APPDATA%\melodee\mcli.json (Windows)
/// </summary>
public class McliConfiguration
{
    public Dictionary<string, McliProfile> Profiles { get; set; } = new();
    public McliDefaults? Defaults { get; set; }
}

public class McliDefaults
{
    public string? Profile { get; set; }
}
