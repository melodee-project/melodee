using System.ComponentModel;
using Spectre.Console.Cli;

namespace Melodee.Cli.CommandSettings;

public class UserListSettings : GlobalSettings
{
    [Description("Maximum number of users to return. (default: 50)")]
    [CommandOption("-n|--limit")]
    [DefaultValue(50)]
    public int Limit { get; init; } = 50;

    [Description("Output raw JSON instead of formatted table.")]
    [CommandOption("--raw")]
    [DefaultValue(false)]
    public bool ReturnRaw { get; init; }
}
