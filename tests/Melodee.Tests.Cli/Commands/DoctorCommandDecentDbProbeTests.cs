using DecentDB.EntityFrameworkCore;
using FluentAssertions;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using MusicBrainzAlbum = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Album;
using MusicBrainzArtist = Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized.Artist;

namespace Melodee.Tests.Cli.Commands;

public class DoctorCommandDecentDbProbeTests
{
    [Fact]
    public async Task ProbeDecentDbDatabaseAsync_WithMissingFile_ReturnsFailure()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"missing-musicbrainz-{Guid.NewGuid():N}.ddb");

        var result = await CliDoctorService.ProbeDecentDbDatabaseAsync(
            $"Data Source={databasePath}",
            ["Artist"],
            [],
            requireRowsForReadQueries: true);

        result.Success.Should().BeFalse();
        result.Details.Should().Contain("does not exist");
    }

    [Fact]
    public async Task ProbeDecentDbDatabaseAsync_WithEmptyFile_ReturnsFailure()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"empty-musicbrainz-{Guid.NewGuid():N}.ddb");

        try
        {
            await File.WriteAllTextAsync(databasePath, string.Empty);

            var result = await CliDoctorService.ProbeDecentDbDatabaseAsync(
                $"Data Source={databasePath}",
                ["Artist"],
                [],
                requireRowsForReadQueries: true);

            result.Success.Should().BeFalse();
            result.Details.Should().Contain("empty");
        }
        finally
        {
            DeleteDatabaseArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task ProbeDecentDbDatabaseAsync_WithMusicBrainzSchemaAndRows_ReturnsSuccess()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"musicbrainz-doctor-ok-{Guid.NewGuid():N}.ddb");

        try
        {
            var options = new DbContextOptionsBuilder<MusicBrainzDbContext>()
                .UseDecentDB($"Data Source={databasePath}")
                .Options;

            await using (var db = new MusicBrainzDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                await db.Artists.AddAsync(new MusicBrainzArtist
                {
                    MusicBrainzArtistId = 14,
                    Name = "The Blackbelt Band",
                    SortName = "Blackbelt Band, The",
                    NameNormalized = "THEBLACKBELTBAND",
                    MusicBrainzIdRaw = Guid.NewGuid().ToString()
                });
                await db.Albums.AddAsync(new MusicBrainzAlbum
                {
                    MusicBrainzArtistId = 14,
                    Name = "Test Album",
                    SortName = "Test Album",
                    NameNormalized = "TESTALBUM",
                    ReleaseType = 1,
                    MusicBrainzIdRaw = Guid.NewGuid().ToString(),
                    ReleaseGroupMusicBrainzIdRaw = Guid.NewGuid().ToString(),
                    ReleaseDate = new DateTime(2026, 1, 1)
                });
                await db.SaveChangesAsync();
            }

            var result = await CliDoctorService.ProbeDecentDbDatabaseAsync(
                $"Data Source={databasePath}",
                ["Artist", "Album", "ArtistAlias"],
                [
                    """SELECT "Id" FROM "Artist" LIMIT 1""",
                    """SELECT "Id" FROM "Album" LIMIT 1"""
                ],
                requireRowsForReadQueries: true);

            result.Success.Should().BeTrue();
            result.Details.Should().Contain("opened");
            result.Details.Should().Contain("readQueries=2");
        }
        finally
        {
            DeleteDatabaseArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task ProbeDecentDbDatabaseAsync_WithMusicBrainzSchemaButNoRows_ReturnsFailure()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"musicbrainz-doctor-empty-{Guid.NewGuid():N}.ddb");

        try
        {
            var options = new DbContextOptionsBuilder<MusicBrainzDbContext>()
                .UseDecentDB($"Data Source={databasePath}")
                .Options;

            await using (var db = new MusicBrainzDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
            }

            var result = await CliDoctorService.ProbeDecentDbDatabaseAsync(
                $"Data Source={databasePath}",
                ["Artist", "Album", "ArtistAlias"],
                [
                    """SELECT "Id" FROM "Artist" LIMIT 1""",
                    """SELECT "Id" FROM "Album" LIMIT 1"""
                ],
                requireRowsForReadQueries: true);

            result.Success.Should().BeFalse();
            result.Details.Should().Contain("returned no rows");
        }
        finally
        {
            DeleteDatabaseArtifacts(databasePath);
        }
    }

    private static void DeleteDatabaseArtifacts(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}.wal", $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
