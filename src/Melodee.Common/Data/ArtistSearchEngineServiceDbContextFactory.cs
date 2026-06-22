using DecentDB.EntityFrameworkCore;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Melodee.Common.Data;

public class ArtistSearchEngineServiceDbContextFactory : IDesignTimeDbContextFactory<ArtistSearchEngineServiceDbContext>
{
    public ArtistSearchEngineServiceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ArtistSearchEngineConnection");

        if (string.IsNullOrEmpty(connectionString))
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .Build();
            connectionString = configuration.GetConnectionString("ArtistSearchEngineConnection");
        }

        if (string.IsNullOrEmpty(connectionString))
        {
            connectionString = "Data Source=./_design-time/artistSearchEngine.ddb";
        }

        var builder = new DbContextOptionsBuilder<ArtistSearchEngineServiceDbContext>();
        builder.UseDecentDB(connectionString, o => o.UseNodaTime());
        return new ArtistSearchEngineServiceDbContext(builder.Options);
    }
}
