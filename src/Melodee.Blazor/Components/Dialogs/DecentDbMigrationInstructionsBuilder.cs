using System.Data.Common;
using Melodee.Blazor.Components.Pages;
using Melodee.Common.Services.Doctor;

namespace Melodee.Blazor.Components.Dialogs;

internal sealed record DecentDbMigrationTarget(
    string DisplayNameKey,
    string ErrorDetails,
    string? DatabasePath,
    string? MigratedDatabasePath,
    string? MigrationCommand,
    string? VerificationCommand,
    string? ReplacementCommand);

internal static class DecentDbMigrationInstructionsBuilder
{
    internal const string MigrationGuideUrl = "https://decentdb.org/user-guide/migration/";
    internal const string ReleasesUrl = "https://github.com/sphildreth/decentdb/releases";
    internal const string SourceRepositoryUrl = "https://github.com/sphildreth/decentdb";
    internal const string BuildCommand = "cargo build --release -p decentdb-migrate";

    internal static IReadOnlyList<DecentDbMigrationTarget> Build(
        IEnumerable<DoctorCheckResult> issues,
        IConfiguration configuration,
        bool isWindows)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(configuration);

        var targets = new List<DecentDbMigrationTarget>();
        foreach (var issue in DashboardHealthWarningEvaluator.GetUnsupportedDecentDbIssues(issues)
                     .DistinctBy(x => x.Name))
        {
            var database = GetDatabaseConfiguration(issue.Name);
            if (database is null)
            {
                continue;
            }

            var databasePath = GetDatabasePath(configuration.GetConnectionString(database.Value.ConnectionStringName));
            if (databasePath is null)
            {
                targets.Add(new DecentDbMigrationTarget(
                    database.Value.DisplayNameKey,
                    issue.Details,
                    null,
                    null,
                    null,
                    null,
                    null));
                continue;
            }

            var migratedDatabasePath = CreateMigratedDatabasePath(databasePath);
            targets.Add(new DecentDbMigrationTarget(
                database.Value.DisplayNameKey,
                issue.Details,
                databasePath,
                migratedDatabasePath,
                CreateMigrationCommand(databasePath, migratedDatabasePath, isWindows),
                CreateVerificationCommand(migratedDatabasePath, isWindows),
                CreateReplacementCommand(databasePath, migratedDatabasePath, isWindows)));
        }

        return targets;
    }

    internal static string QuotePosixArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    internal static string QuotePowerShellArgument(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";
    }

    private static (string ConnectionStringName, string DisplayNameKey)? GetDatabaseConfiguration(string checkName)
    {
        return checkName switch
        {
            "MusicBrainzDatabase" => ("MusicBrainzConnection", "AdminDoctor.Check.MusicBrainzDatabase"),
            "ArtistSearchEngineDatabase" => ("ArtistSearchEngineConnection", "AdminDoctor.Check.ArtistSearchEngineDatabase"),
            _ => null
        };
    }

    private static string? GetDatabasePath(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
            var configuredPath = builder.ContainsKey("Data Source")
                ? builder["Data Source"]?.ToString()
                : null;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                return null;
            }

            return Path.GetFullPath(configuredPath);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static string CreateMigratedDatabasePath(string databasePath)
    {
        var directory = Path.GetDirectoryName(databasePath) ?? string.Empty;
        var extension = Path.GetExtension(databasePath);
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(databasePath);
        var migratedFileName = $"{fileNameWithoutExtension}_migrated{extension}";

        return Path.Combine(directory, migratedFileName);
    }

    private static string CreateMigrationCommand(string databasePath, string migratedDatabasePath, bool isWindows)
    {
        var executable = isWindows ? @".\decentdb-migrate.exe" : "./decentdb-migrate";
        Func<string, string> quote = isWindows
            ? QuotePowerShellArgument
            : QuotePosixArgument;

        return $"{executable} --source {quote(databasePath)} --dest {quote(migratedDatabasePath)}";
    }

    private static string CreateVerificationCommand(string migratedDatabasePath, bool isWindows)
    {
        var executable = isWindows ? @".\decentdb.exe" : "./decentdb";
        Func<string, string> quote = isWindows
            ? QuotePowerShellArgument
            : QuotePosixArgument;

        return $"{executable} info --db {quote(migratedDatabasePath)}";
    }

    private static string CreateReplacementCommand(string databasePath, string migratedDatabasePath, bool isWindows)
    {
        return isWindows
            ? CreatePowerShellReplacementCommand(databasePath, migratedDatabasePath)
            : CreatePosixReplacementCommand(databasePath, migratedDatabasePath);
    }

    private static string CreatePosixReplacementCommand(string databasePath, string migratedDatabasePath)
    {
        return string.Join('\n',
        [
            "set -euo pipefail",
            $"source_db={QuotePosixArgument(databasePath)}",
            $"migrated_db={QuotePosixArgument(migratedDatabasePath)}",
            "backup_dir=\"${source_db}.pre-migration-$(date +%Y%m%d-%H%M%S)\"",
            "mkdir \"$backup_dir\"",
            "mv \"$source_db\" \"$backup_dir/\"",
            "for suffix in .wal .coord .wal-idx; do",
            "  if [ -e \"${source_db}${suffix}\" ]; then",
            "    mv \"${source_db}${suffix}\" \"$backup_dir/\"",
            "  fi",
            "done",
            "mv \"$migrated_db\" \"$source_db\"",
            "if [ -e \"${migrated_db}.wal\" ]; then",
            "  mv \"${migrated_db}.wal\" \"${source_db}.wal\"",
            "fi",
            "for suffix in .coord .wal-idx; do",
            "  if [ -e \"${migrated_db}${suffix}\" ]; then",
            "    mv \"${migrated_db}${suffix}\" \"$backup_dir/\"",
            "  fi",
            "done"
        ]);
    }

    private static string CreatePowerShellReplacementCommand(string databasePath, string migratedDatabasePath)
    {
        return string.Join('\n',
        [
            "$ErrorActionPreference = 'Stop'",
            "Set-StrictMode -Version Latest",
            $"$sourceDb = {QuotePowerShellArgument(databasePath)}",
            $"$migratedDb = {QuotePowerShellArgument(migratedDatabasePath)}",
            "$backupDir = \"${sourceDb}.pre-migration-$(Get-Date -Format 'yyyyMMdd-HHmmss')\"",
            "New-Item -ItemType Directory -Path $backupDir | Out-Null",
            "Move-Item -LiteralPath $sourceDb -Destination $backupDir",
            "foreach ($suffix in '.wal', '.coord', '.wal-idx') {",
            "  $sidecar = \"${sourceDb}${suffix}\"",
            "  if (Test-Path -LiteralPath $sidecar) {",
            "    Move-Item -LiteralPath $sidecar -Destination $backupDir",
            "  }",
            "}",
            "Move-Item -LiteralPath $migratedDb -Destination $sourceDb",
            "if (Test-Path -LiteralPath \"${migratedDb}.wal\") {",
            "  Move-Item -LiteralPath \"${migratedDb}.wal\" -Destination \"${sourceDb}.wal\"",
            "}",
            "foreach ($suffix in '.coord', '.wal-idx') {",
            "  $sidecar = \"${migratedDb}${suffix}\"",
            "  if (Test-Path -LiteralPath $sidecar) {",
            "    Move-Item -LiteralPath $sidecar -Destination $backupDir",
            "  }",
            "}"
        ]);
    }
}
