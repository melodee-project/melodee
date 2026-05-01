using System.Data.Common;
using FluentAssertions;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Tests.Common.Plugins.SearchEngine.MusicBrainz;

public class DecentDBDiagnosticTests : IDisposable
{
    private readonly string _dbFile;

    public DecentDBDiagnosticTests()
    {
        _dbFile = Path.Combine(Path.GetTempPath(), $"diag_{Guid.NewGuid():N}.ddb");
    }

    public void Dispose()
    {
        if (File.Exists(_dbFile)) File.Delete(_dbFile);
    }

    [Fact]
    public async Task EnsureCreated_EFCoreLinq_Works()
    {
        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={_dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        context.ArtistsStaging.Add(new Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Staging.ArtistStaging
        {
            ArtistId = 1,
            MusicBrainzIdRaw = "test",
            Name = "Test",
            NameNormalized = "test",
            SortName = "Test"
        });
        await context.SaveChangesAsync();

        var count = await context.ArtistsStaging.CountAsync();
        count.Should().Be(1);
    }

    [Fact]
    public async Task EnsureCreated_RawSql_Works()
    {
        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={_dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var sqlResult = await context.Database.ExecuteSqlRawAsync(
            "INSERT INTO ArtistStaging (ArtistId, MusicBrainzIdRaw, Name, NameNormalized, SortName) VALUES (2, 'test2', 'Test2', 'test2', 'Test2')");
        sqlResult.Should().Be(1);
    }

    [Fact]
    public async Task EnsureCreated_AdoNet_Works()
    {
        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={_dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var conn = context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT INTO ArtistStaging (ArtistId, MusicBrainzIdRaw, Name, NameNormalized, SortName) VALUES (3, 'test3', 'Test3', 'test3', 'Test3')";
        var affected = cmd.ExecuteNonQuery();
        affected.Should().Be(1);
    }

    [Fact]
    public async Task EnsureCreated_AdoNetMultiRowInsert_Works()
    {
        var dbOptions = new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={_dbFile}")
            .Options;

        await using var context = new MusicBrainzDbContext(dbOptions);
        await context.Database.EnsureCreatedAsync();

        var conn = context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync();
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
                          INSERT INTO ArtistStaging (ArtistId, MusicBrainzIdRaw, Name, NameNormalized, SortName)
                          VALUES (@p0_0, @p0_1, @p0_2, @p0_3, @p0_4),
                                 (@p1_0, @p1_1, @p1_2, @p1_3, @p1_4)
                          """;

        AddParameter(cmd, "@p0_0", 10L);
        AddParameter(cmd, "@p0_1", "test10");
        AddParameter(cmd, "@p0_2", "Test10");
        AddParameter(cmd, "@p0_3", "test10");
        AddParameter(cmd, "@p0_4", "Test10");
        AddParameter(cmd, "@p1_0", 11L);
        AddParameter(cmd, "@p1_1", "test11");
        AddParameter(cmd, "@p1_2", "Test11");
        AddParameter(cmd, "@p1_3", "test11");
        AddParameter(cmd, "@p1_4", "Test11");

        var affected = cmd.ExecuteNonQuery();
        affected.Should().Be(2);

        var count = await context.ArtistsStaging.CountAsync();
        count.Should().Be(2);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
