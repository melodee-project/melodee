using System.Net;
using FluentAssertions;
using Melodee.Common.Data;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Melodee.Tests.Blazor;

public class ScalarIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;

    public ScalarIntegrationTests()
    {
        var inMemoryProvider = new ServiceCollection()
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
                        ["security.secretKey"] = new string('s', 32),
                        ["QuartzDisabled"] = "true"
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
                        options.UseInMemoryDatabase("ScalarIntegrationTests");
                        options.UseInternalServiceProvider(inMemoryProvider);
                    });
                });
            });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task GetScalarV1_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/scalar/v1");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
        content.Should().Contain("openapi");
        content.Should().Contain("Melodee API");
    }
}
