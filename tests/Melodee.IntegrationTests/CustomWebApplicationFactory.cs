using DecentDB.EntityFrameworkCore;
using Melodee.Common.Data;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Melodee.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _tempDbDir;

    public CustomWebApplicationFactory()
    {
        // Skip default DB registration in Program.cs - we provide our own DecentDB contexts
        Environment.SetEnvironmentVariable("MELODEE_SKIP_DB_REGISTRATION", "true");

        _tempDbDir = Path.Combine(Path.GetTempPath(), $"melodee-integration-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDbDir);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var melodeeDbFile = Path.Combine(_tempDbDir, "melodee.ddb");
            var artistSearchDbFile = Path.Combine(_tempDbDir, "artist-search.ddb");
            var musicBrainzDbFile = Path.Combine(_tempDbDir, "musicbrainz.ddb");

            services.AddDbContextFactory<MelodeeDbContext>(options =>
            {
                options.UseDecentDB($"Data Source={melodeeDbFile}", x => x.UseNodaTime());
            });

            services.AddDbContextFactory<ArtistSearchEngineServiceDbContext>(options =>
            {
                options.UseDecentDB($"Data Source={artistSearchDbFile}");
            });

            services.AddDbContextFactory<MusicBrainzDbContext>(options =>
            {
                options.UseDecentDB($"Data Source={musicBrainzDbFile}");
            });

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
            try
            {
                if (Directory.Exists(_tempDbDir))
                {
                    Directory.Delete(_tempDbDir, true);
                }
            }
            catch
            {
                // Best effort cleanup
            }

            Environment.SetEnvironmentVariable("MELODEE_SKIP_DB_REGISTRATION", null);
        }
    }
}