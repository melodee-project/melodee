using System.Data.Common;
using Melodee.Common.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Melodee.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly DbConnection _connection;

    public CustomWebApplicationFactory()
    {
        // Create in-memory SQLite connection for tests
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove existing DbContextFactory registrations
            var descriptors = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<MelodeeDbContext>) ||
                    d.ServiceType == typeof(IDbContextFactory<MelodeeDbContext>) ||
                    d.ServiceType == typeof(IConfigureOptions<DbContextOptions<MelodeeDbContext>>))
                .ToList();

            foreach (var descriptor in descriptors)
            {
                services.Remove(descriptor);
            }

            // Add in-memory database
            services.AddDbContextFactory<MelodeeDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
