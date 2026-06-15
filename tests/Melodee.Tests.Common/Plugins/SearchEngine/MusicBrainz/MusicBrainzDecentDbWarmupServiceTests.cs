using DecentDB.EntityFrameworkCore;
using FluentAssertions;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data.Models.Materialized;
using Melodee.Tests.Common.Services;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Melodee.Tests.Common.Plugins.SearchEngine.MusicBrainz;

public class MusicBrainzDecentDbWarmupServiceTests : ServiceTestBase
{
    [Fact]
    public async Task WarmHotIndexesAsync_WhenMaterializedRowsExist_WarmsExpectedIndexedShapes()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"melodee-mb-warmup-{Guid.NewGuid():N}.ddb");

        try
        {
            var dbContextFactory = await CreateSeededFactoryAsync(databasePath);
            var service = new MusicBrainzDecentDbWarmupService(Logger, dbContextFactory);

            var result = await service.WarmHotIndexesAsync();

            result.Succeeded.Should().BeTrue(result.Message);
            result.Skipped.Should().BeFalse();
            result.WarmedQueryCount.Should().BeGreaterThanOrEqualTo(5);
            result.Measurements.Select(measurement => measurement.Name).Should().Contain([
                "exact-normalized-name",
                "exact-musicbrainz-id-raw",
                "aliases-by-artist-id",
                "exact-normalized-alias",
                "albums-by-artist-id"
            ]);
            result.Measurements.Single(measurement => measurement.Name == "exact-normalized-name").RowCount
                .Should().Be(1);
            result.Measurements.Single(measurement => measurement.Name == "exact-musicbrainz-id-raw").RowCount
                .Should().Be(1);
            result.Measurements.Single(measurement => measurement.Name == "exact-normalized-alias").RowCount
                .Should().Be(1);
            result.Measurements.Single(measurement => measurement.Name == "albums-by-artist-id").RowCount
                .Should().Be(1);
        }
        finally
        {
            DeleteDatabaseArtifacts(databasePath);
        }
    }

    [Fact]
    public async Task WarmHotIndexesAsync_WhenNoArtistsExist_ReturnsSkippedResult()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"melodee-mb-warmup-empty-{Guid.NewGuid():N}.ddb");

        try
        {
            var dbContextFactory = await CreateEmptyFactoryAsync(databasePath);
            var service = new MusicBrainzDecentDbWarmupService(Logger, dbContextFactory);

            var result = await service.WarmHotIndexesAsync();

            result.Succeeded.Should().BeFalse();
            result.Skipped.Should().BeTrue();
            result.Message.Should().Contain("no materialized artists");
            result.WarmedQueryCount.Should().Be(0);
        }
        finally
        {
            DeleteDatabaseArtifacts(databasePath);
        }
    }

    private static async Task<IDbContextFactory<MusicBrainzDbContext>> CreateSeededFactoryAsync(string databasePath)
    {
        var dbContextOptions = CreateOptions(databasePath);
        await using var context = new MusicBrainzDbContext(dbContextOptions);
        await context.Database.EnsureCreatedAsync();
        context.Artists.Add(new Artist
        {
            MusicBrainzArtistId = 1001,
            Name = "Example Artist",
            SortName = "Example Artist",
            NameNormalized = "exampleartist",
            MusicBrainzIdRaw = "11111111-1111-1111-1111-111111111111"
        });
        context.ArtistAliases.Add(new ArtistAliasLookup
        {
            MusicBrainzArtistId = 1001,
            NameNormalized = "examplealias"
        });
        context.Albums.Add(new Album
        {
            MusicBrainzArtistId = 1001,
            Name = "Example Album",
            SortName = "Example Album",
            NameNormalized = "examplealbum",
            MusicBrainzIdRaw = "22222222-2222-2222-2222-222222222222",
            ReleaseGroupMusicBrainzIdRaw = "33333333-3333-3333-3333-333333333333",
            ReleaseDate = new DateTime(2026, 1, 1)
        });
        await context.SaveChangesAsync();

        return CreateFactory(dbContextOptions);
    }

    private static async Task<IDbContextFactory<MusicBrainzDbContext>> CreateEmptyFactoryAsync(string databasePath)
    {
        var dbContextOptions = CreateOptions(databasePath);
        await using var context = new MusicBrainzDbContext(dbContextOptions);
        await context.Database.EnsureCreatedAsync();
        await context.SaveChangesAsync();

        return CreateFactory(dbContextOptions);
    }

    private static DbContextOptions<MusicBrainzDbContext> CreateOptions(string databasePath)
    {
        return new DbContextOptionsBuilder<MusicBrainzDbContext>()
            .UseDecentDB($"Data Source={databasePath}", optionsBuilder => optionsBuilder.UseNodaTime())
            .Options;
    }

    private static IDbContextFactory<MusicBrainzDbContext> CreateFactory(
        DbContextOptions<MusicBrainzDbContext> dbContextOptions)
    {
        var dbContextFactory = new Mock<IDbContextFactory<MusicBrainzDbContext>>();
        dbContextFactory.Setup(factory => factory.CreateDbContext())
            .Returns(() => new MusicBrainzDbContext(dbContextOptions));
        dbContextFactory.Setup(factory => factory.CreateDbContextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MusicBrainzDbContext(dbContextOptions));
        return dbContextFactory.Object;
    }

    private static void DeleteDatabaseArtifacts(string databasePath)
    {
        foreach (var path in new[]
                 {
                     databasePath,
                     $"{databasePath}.wal",
                     $"{databasePath}-wal",
                     $"{databasePath}.shm",
                     $"{databasePath}-shm",
                     $"{databasePath}.coord"
                 })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
