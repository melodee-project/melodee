using System.ComponentModel;
using Spectre.Console.Cli;

namespace Melodee.Cli.CommandSettings;

/// <summary>
/// Global settings that apply to all commands.
/// These can be specified before the command name.
/// </summary>
public class GlobalSettings : Spectre.Console.Cli.CommandSettings
{
    [CommandOption("--server <URL>")]
    [Description("Remote Melodee server URL (e.g., https://demo.melodee.org)")]
    public string? Server { get; init; }

    [CommandOption("--token <TOKEN>")]
    [Description("API authentication token (Bearer token)")]
    public string? Token { get; init; }

    [CommandOption("--profile <NAME>")]
    [Description("Profile name from config file (~/.config/melodee/mcli.json)")]
    public string? Profile { get; init; }

    [CommandOption("--json")]
    [Description("Output compact JSON (default: pretty-printed)")]
    [DefaultValue(false)]
    public bool Json { get; init; }
}
