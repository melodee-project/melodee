using System.Data.Common;
using Melodee.Common.Data;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Melodee.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly DbConnection _melodeeConnection;
    private readonly DbConnection _artistSearchEngineConnection;
    private readonly DbConnection _musicBrainzConnection;

    public CustomWebApplicationFactory()
    {
        // Skip default DB registration in Program.cs - we provide our own SQLite contexts
        Environment.SetEnvironmentVariable("MELODEE_SKIP_DB_REGISTRATION", "true");

        // Create in-memory SQLite connections for tests
        _melodeeConnection = new SqliteConnection("DataSource=:memory:");
        _melodeeConnection.Open();

        _artistSearchEngineConnection = new SqliteConnection("DataSource=:memory:");
        _artistSearchEngineConnection.Open();

        _musicBrainzConnection = new SqliteConnection("DataSource=:memory:");
        _musicBrainzConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Add in-memory SQLite databases for testing with NodaTime support
            services.AddDbContextFactory<MelodeeDbContext>(options =>
            {
                options.UseSqlite(_melodeeConnection, x => x.UseNodaTime());
            });

            services.AddDbContextFactory<ArtistSearchEngineServiceDbContext>(options =>
            {
                options.UseSqlite(_artistSearchEngineConnection);
            });

            services.AddDbContextFactory<MusicBrainzDbContext>(options =>
            {
                options.UseSqlite(_musicBrainzConnection);
            });

            // Build the service provider and create database schemas
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();

            var melodeeContext = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>().CreateDbContext();
            melodeeContext.Database.EnsureCreated();

            var artistSearchContext = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ArtistSearchEngineServiceDbContext>>().CreateDbContext();
            artistSearchContext.Database.EnsureCreated();

            var musicBrainzContext = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MusicBrainzDbContext>>().CreateDbContext();
            musicBrainzContext.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _melodeeConnection.Dispose();
            _artistSearchEngineConnection.Dispose();
            _musicBrainzConnection.Dispose();

            // Clean up environment variable
            Environment.SetEnvironmentVariable("MELODEE_SKIP_DB_REGISTRATION", null);
        }
    }
}
