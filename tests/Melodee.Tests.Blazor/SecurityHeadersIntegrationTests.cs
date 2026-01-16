using System.Net;
using FluentAssertions;
using Melodee.Common.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Melodee.Tests.Blazor;

[Trait("Category", "Integration")]
public class SecurityHeadersIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly ServiceProvider _inMemoryProvider;

    public SecurityHeadersIntegrationTests()
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
                        ["security.secretKey"] = new string('s', 32),
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
                        options.UseInMemoryDatabase("SecurityHeadersTests");
                        options.UseInternalServiceProvider(_inMemoryProvider);
                    });
                });
            });

        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
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
    public async Task Response_ShouldContainRequiredSecurityHeaders()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/info");
        var response = await _client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"Response body: {content}");

        response.Headers.Should().ContainKey("X-Content-Type-Options");
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");

        response.Headers.Should().ContainKey("X-Frame-Options");
        response.Headers.GetValues("X-Frame-Options").Should().Contain("SAMEORIGIN");

        response.Headers.Should().ContainKey("Referrer-Policy");
        response.Headers.GetValues("Referrer-Policy").Should().Contain("strict-origin-when-cross-origin");

        response.Headers.Should().ContainKey("Content-Security-Policy");
        var csp = response.Headers.GetValues("Content-Security-Policy").First();
        csp.Should().Contain("default-src 'self'");
        csp.Should().Contain("object-src 'none'");
    }

    [Fact]
    public async Task Response_ShouldContainPermissionsPolicyHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/info");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Permissions-Policy");
        var permissionsPolicy = response.Headers.GetValues("Permissions-Policy").First();
        permissionsPolicy.Should().Contain("geolocation=()");
        permissionsPolicy.Should().Contain("microphone=()");
    }

    [Fact]
    public async Task NonProductionEnvironment_ShouldNotContainHstsHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/info");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var headers = response.Headers;
        var hasHsts = headers.TryGetValues("Strict-Transport-Security", out _);
        hasHsts.Should().BeFalse();
    }
}
