using DecentDB.EntityFrameworkCore;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Tests.Common.Data;

public class ArtistSearchEngineServiceDbContextMigrationTests
{
    [Fact]
    public async Task Migrate_OnFreshFile_CreatesArtistsTable()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"artist_search_engine_migrate_{Guid.NewGuid():N}.ddb");
        try
        {
            var connectionString = $"Data Source={tempPath}";
            var optionsBuilder = new DbContextOptionsBuilder<ArtistSearchEngineServiceDbContext>();
            optionsBuilder.UseDecentDB(connectionString, o => o.UseNodaTime());

            await using var ctx = new ArtistSearchEngineServiceDbContext(optionsBuilder.Options);
            await ctx.Database.MigrateAsync();

            await using var verify = new ArtistSearchEngineServiceDbContext(optionsBuilder.Options);
            var canConnect = await verify.Database.CanConnectAsync();
            Assert.True(canConnect);

            var artistCount = await verify.Artists.CountAsync();
            Assert.Equal(0, artistCount);

            var historyTableExists = await verify
                .Database.SqlQueryRaw<long>(
                    "SELECT COUNT(*) AS Value FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'")
                .ToListAsync();
            Assert.True(historyTableExists.Count > 0);

            var appliedMigrations = await verify.Database.GetAppliedMigrationsAsync();
            Assert.Contains(appliedMigrations, m => m.Contains("InitialArtistSearchEngineSchema"));
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
            try { File.Delete(tempPath + ".wal"); } catch { }
        }
    }

    [Fact]
    public async Task Migrate_OnExistingSchemaFile_IsNoOp()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"artist_search_engine_baseline_{Guid.NewGuid():N}.ddb");
        try
        {
            var connectionString = $"Data Source={tempPath}";
            var optionsBuilder = new DbContextOptionsBuilder<ArtistSearchEngineServiceDbContext>();
            optionsBuilder.UseDecentDB(connectionString, o => o.UseNodaTime());

            await using (var seedCtx = new ArtistSearchEngineServiceDbContext(optionsBuilder.Options))
            {
                await seedCtx.Database.MigrateAsync();
                seedCtx.Artists.Add(new Artist
                {
                    Name = "Test Artist",
                    NameNormalized = "TESTARTIST",
                    SortName = "Test Artist"
                });
                await seedCtx.SaveChangesAsync();
            }

            var artistsBefore = 0;
            await using (var readCtx = new ArtistSearchEngineServiceDbContext(optionsBuilder.Options))
            {
                artistsBefore = await readCtx.Artists.CountAsync();
            }
            Assert.Equal(1, artistsBefore);

            await using (var migrateCtx = new ArtistSearchEngineServiceDbContext(optionsBuilder.Options))
            {
                await migrateCtx.Database.MigrateAsync();
            }

            await using (var verifyCtx = new ArtistSearchEngineServiceDbContext(optionsBuilder.Options))
            {
                var artistsAfter = await verifyCtx.Artists.CountAsync();
                Assert.Equal(artistsBefore, artistsAfter);
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
            try { File.Delete(tempPath + ".wal"); } catch { }
        }
    }

    [Fact]
    public async Task Migrate_OnProductionSchemaCopy_IsNoOp()
    {
        var fixturePath = Environment.GetEnvironmentVariable("MELODEE_TEST_ARTIST_SEARCH_ENGINE_DDB");
        if (string.IsNullOrEmpty(fixturePath))
        {
            return;
        }

        if (!File.Exists(fixturePath))
        {
            throw new FileNotFoundException(
                $"Set MELODEE_TEST_ARTIST_SEARCH_ENGINE_DDB to a DecentDB file matching the production schema. File not found: {fixturePath}");
        }

        var productionCopy = Path.Combine(
            Path.GetTempPath(),
            $"artist_search_engine_prodcopy_{Guid.NewGuid():N}.ddb");
        File.Copy(fixturePath, productionCopy, overwrite: true);

        try
        {
            var connectionString = $"Data Source={productionCopy}";
            var optionsBuilder = new DbContextOptionsBuilder<ArtistSearchEngineServiceDbContext>();
            optionsBuilder.UseDecentDB(connectionString, o => o.UseNodaTime());

            int artistCountBefore;
            int albumCountBefore;
            await using (var readCtx = new ArtistSearchEngineServiceDbContext(optionsBuilder.Options))
            {
                artistCountBefore = await readCtx.Artists.CountAsync();
                albumCountBefore = await readCtx.Albums.CountAsync();
            }

            Assert.True(artistCountBefore > 0, "Fixture must contain at least one artist");

            await using (var migrateCtx = new ArtistSearchEngineServiceDbContext(optionsBuilder.Options))
            {
                var pendingBefore = await migrateCtx.Database.GetPendingMigrationsAsync();
                Assert.NotEmpty(pendingBefore);
                Assert.Contains(pendingBefore, m => m.Contains("InitialArtistSearchEngineSchema"));

                await migrateCtx.Database.MigrateAsync();
            }

            await using (var verifyCtx = new ArtistSearchEngineServiceDbContext(optionsBuilder.Options))
            {
                var artistCountAfter = await verifyCtx.Artists.CountAsync();
                var albumCountAfter = await verifyCtx.Albums.CountAsync();
                Assert.Equal(artistCountBefore, artistCountAfter);
                Assert.Equal(albumCountBefore, albumCountAfter);

                var applied = await verifyCtx.Database.GetAppliedMigrationsAsync();
                Assert.Contains(applied, m => m.Contains("InitialArtistSearchEngineSchema"));

                var pendingAfter = await verifyCtx.Database.GetPendingMigrationsAsync();
                Assert.Empty(pendingAfter);
            }
        }
        finally
        {
            try { File.Delete(productionCopy); } catch { }
            try { File.Delete(productionCopy + ".wal"); } catch { }
        }
    }
}
