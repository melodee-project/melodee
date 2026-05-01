using Melodee.Common.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Melodee.Tests.Blazor.Startup;

public class StartupSmokeTests : IAsyncLifetime
{
    private readonly ServiceProvider _inMemoryProvider;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public StartupSmokeTests()
    {
        _inMemoryProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    var settings = new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=melodee_test;Username=test;Password=test",
                        ["ConnectionStrings:ArtistSearchEngineConnection"] = "Data Source=:memory:",
                        ["ConnectionStrings:MusicBrainzConnection"] = "Data Source=:memory:",
                        ["Jwt:Key"] = new string('k', 64),
                        ["Jwt:Issuer"] = "melodee-tests",
                        ["Jwt:Audience"] = "melodee-tests",
                        ["QuartzDisabled"] = "true",
                        ["RateLimiting:MelodeeApi:TokenLimit"] = "30",
                        ["RateLimiting:MelodeeApi:QueueLimit"] = "10",
                        ["RateLimiting:MelodeeApi:ReplenishmentPeriodSeconds"] = "30",
                        ["RateLimiting:MelodeeApi:TokensPerPeriod"] = "30",
                        ["RateLimiting:MelodeeApi:AutoReplenishment"] = "true",
                        ["RateLimiting:MelodeeAuth:TokenLimit"] = "10",
                        ["RateLimiting:MelodeeAuth:QueueLimit"] = "5",
                        ["RateLimiting:MelodeeAuth:ReplenishmentPeriodSeconds"] = "60",
                        ["RateLimiting:MelodeeAuth:TokensPerPeriod"] = "10",
                        ["RateLimiting:MelodeeAuth:AutoReplenishment"] = "true"
                    };

                    config.AddInMemoryCollection(settings);
                });

                builder.ConfigureServices(services =>
                {
                    var descriptors = services.Where(d =>
                            d.ServiceType == typeof(DbContextOptions<MelodeeDbContext>) ||
                            d.ServiceType == typeof(IDbContextFactory<MelodeeDbContext>) ||
                            d.ServiceType == typeof(IDbContextOptionsConfiguration<MelodeeDbContext>) ||
                            d.ServiceType == typeof(IConfigureOptions<DbContextOptions<MelodeeDbContext>>))
                        .ToList();

                    foreach (var descriptor in descriptors)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContextFactory<MelodeeDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("StartupSmokeTests");
                        options.UseInternalServiceProvider(_inMemoryProvider);
                    });
                });
            });

        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _inMemoryProvider.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task ApplicationCanStartWithProductionLikeConfiguration()
    {
        var response = await _client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode, "Health check endpoint should return success");
    }

    [Fact]
    public async Task HealthCheckEndpointResponds()
    {
        var response = await _client.GetAsync("/health");
        Assert.True(response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable,
            "Health check should either succeed or indicate service is unavailable");
    }
}
