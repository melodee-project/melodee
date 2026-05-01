using System.ComponentModel;
using Spectre.Console.Cli;

namespace Melodee.Cli.CommandSettings;

/// <summary>
/// Settings for the search command.
/// </summary>
public class SearchSettings : GlobalSettings
{
    [CommandArgument(0, "<QUERY>")]
    [Description("Search query")]
    public required string Query { get; init; }

    [CommandOption("-l|--limit <LIMIT>")]
    [Description("Maximum number of results to return (default: 25)")]
    [DefaultValue(25)]
    public int Limit { get; init; } = 25;
}
