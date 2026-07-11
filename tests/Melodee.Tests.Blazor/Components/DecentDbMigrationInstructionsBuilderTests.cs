using FluentAssertions;
using Melodee.Blazor.Components.Dialogs;
using Melodee.Common.Services.Doctor;
using Microsoft.Extensions.Configuration;

namespace Melodee.Tests.Blazor.Components;

public class DecentDbMigrationInstructionsBuilderTests
{
    [Fact]
    public void Build_WithMusicBrainzError8_CreatesCommandsForConfiguredDatabase()
    {
        var databasePath = Path.GetFullPath(Path.Combine("search engine", "musicbrainz.ddb"));
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={databasePath}"
        });
        var issues = new[]
        {
            new DoctorCheckResult(
                "MusicBrainzDatabase",
                false,
                "DecentDB error 8: Unsupported database format version: 13",
                TimeSpan.Zero)
        };

        var targets = DecentDbMigrationInstructionsBuilder.Build(issues, configuration, isWindows: false);

        var target = targets.Should().ContainSingle().Which;
        var migratedPath = Path.Combine(
            Path.GetDirectoryName(databasePath)!,
            "musicbrainz_migrated.ddb");
        target.DatabasePath.Should().Be(databasePath);
        target.MigratedDatabasePath.Should().Be(migratedPath);
        target.MigrationCommand.Should().Be(
            $"./decentdb-migrate --source '{databasePath}' --dest '{migratedPath}'");
        target.VerificationCommand.Should().Be($"./decentdb info --db '{migratedPath}'");
        target.ReplacementCommand.Should().StartWith("set -euo pipefail");
        target.ReplacementCommand.Should().Contain(".wal .coord .wal-idx");
        target.ReplacementCommand.Should().Contain("pre-migration-$(date +%Y%m%d-%H%M%S)");
    }

    [Fact]
    public void Build_WithApostropheInPath_QuotesPosixCommandSafely()
    {
        var databasePath = Path.GetFullPath(Path.Combine("administrator's music", "artist search.ddb"));
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ArtistSearchEngineConnection"] = $"Data Source={databasePath}"
        });
        var issues = new[]
        {
            new DoctorCheckResult(
                "ArtistSearchEngineDatabase",
                false,
                "ERR_UNSUPPORTED_FORMAT_VERSION",
                TimeSpan.Zero)
        };

        var targets = DecentDbMigrationInstructionsBuilder.Build(issues, configuration, isWindows: false);

        targets.Should().ContainSingle()
            .Which.MigrationCommand.Should().Contain("administrator'\"'\"'s music");
    }

    [Fact]
    public void Build_ForWindows_CreatesPowerShellCommands()
    {
        var databasePath = Path.GetFullPath(Path.Combine("search", "musicbrainz.ddb"));
        var configuration = CreateConfiguration(new Dictionary<string, string?>
        {
            ["ConnectionStrings:MusicBrainzConnection"] = $"Data Source={databasePath}"
        });
        var issues = new[]
        {
            new DoctorCheckResult(
                "MusicBrainzDatabase",
                false,
                "DecentDB error 8: Unsupported database format",
                TimeSpan.Zero)
        };

        var targets = DecentDbMigrationInstructionsBuilder.Build(issues, configuration, isWindows: true);

        var target = targets.Should().ContainSingle().Which;
        target.MigrationCommand.Should().StartWith(@".\decentdb-migrate.exe --source '");
        target.VerificationCommand.Should().StartWith(@".\decentdb.exe info --db '");
        target.ReplacementCommand.Should().StartWith("$ErrorActionPreference = 'Stop'");
        target.ReplacementCommand.Should().Contain("Move-Item -LiteralPath $migratedDb -Destination $sourceDb");
    }

    [Fact]
    public void QuotePowerShellArgument_WithApostrophe_EscapesLiteralPath()
    {
        var result = DecentDbMigrationInstructionsBuilder.QuotePowerShellArgument(@"C:\Admin's Music\musicbrainz.ddb");

        result.Should().Be(@"'C:\Admin''s Music\musicbrainz.ddb'");
    }

    [Fact]
    public void Build_WithoutDataSource_KeepsErrorVisibleWithoutInventingAPath()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>());
        var issues = new[]
        {
            new DoctorCheckResult(
                "MusicBrainzDatabase",
                false,
                "DecentDB error 8: Unsupported database format",
                TimeSpan.Zero)
        };

        var targets = DecentDbMigrationInstructionsBuilder.Build(issues, configuration, isWindows: false);

        var target = targets.Should().ContainSingle().Which;
        target.DatabasePath.Should().BeNull();
        target.MigrationCommand.Should().BeNull();
        target.ErrorDetails.Should().Contain("error 8");
    }

    [Fact]
    public void Build_WithUnrelatedDoctorIssue_ReturnsNoTargets()
    {
        var configuration = CreateConfiguration(new Dictionary<string, string?>());
        var issues = new[]
        {
            new DoctorCheckResult(
                "PostgresDatabase",
                false,
                "Unsupported database format",
                TimeSpan.Zero)
        };

        var targets = DecentDbMigrationInstructionsBuilder.Build(issues, configuration, isWindows: false);

        targets.Should().BeEmpty();
    }

    private static IConfiguration CreateConfiguration(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
