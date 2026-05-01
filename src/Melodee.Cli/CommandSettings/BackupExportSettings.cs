using System.ComponentModel;
using Spectre.Console.Cli;

namespace Melodee.Cli.CommandSettings;

public class BackupExportSettings : Spectre.Console.Cli.CommandSettings
{
    [Description("Path to write the export JSON file. If not specified, output goes to stdout.")]
    [CommandOption("--output <PATH>")]
    public string? OutputPath { get; init; }

    [Description("Write export to stdout instead of a file.")]
    [CommandOption("--stdout")]
    [DefaultValue(false)]
    public bool WriteToStdout { get; init; }

    [Description("Redact secret values (keys containing 'secret', 'token', or 'password').")]
    [CommandOption("--redact-secrets")]
    [DefaultValue(false)]
    public bool RedactSecrets { get; init; }

    [Description("Output results in JSON format (useful for piping to other tools).")]
    [CommandOption("--raw")]
    [DefaultValue(false)]
    public bool ReturnRaw { get; init; }
}
