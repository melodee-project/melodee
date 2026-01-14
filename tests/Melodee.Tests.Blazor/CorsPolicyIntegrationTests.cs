using System.Net;
using FluentAssertions;
using Melodee.Common.Data;
using Melodee.Common.Services.Security;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Melodee.Tests.Blazor;

[Trait("Category", "Integration")]
public class CorsPolicyIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly ServiceProvider _inMemoryProvider;
    private readonly string _allowedOrigin = "https://trusted.example.com";
    private readonly string _disallowedOrigin = "https://malicious.example.com";

    public CorsPolicyIntegrationTests()
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
                        ["Cors:AllowedOrigins:0"] = _allowedOrigin
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
                        options.UseInMemoryDatabase("CorsPolicyTests");
                        options.UseInternalServiceProvider(_inMemoryProvider);
                    });

                    services.AddSingleton<IPasswordHashService, PasswordHashService>();

                    var corsDescriptors = services.Where(d => d.ServiceType == typeof(ICorsService) || d.ServiceType == typeof(ICorsPolicyProvider)).ToList();
                    foreach (var descriptor in corsDescriptors)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddCors(options =>
                    {
                        options.AddPolicy("MelodeeCors", policy =>
                        {
                            policy.WithOrigins(_allowedOrigin);
                            policy.WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS");
                            policy.WithHeaders("Authorization", "Content-Type", "If-None-Match", "If-Match");
                            policy.WithExposedHeaders("Accept-Ranges", "Content-Range", "Content-Length", "Content-Type", "ETag");
                            policy.AllowCredentials();
                        });
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
    public async Task RequestWithDisallowedOrigin_DoesNotReceiveAccessControlAllowOriginHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/info");
        request.Headers.Add("Origin", _disallowedOrigin);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().NotContainKey("Access-Control-Allow-Origin");
    }

    [Fact]
    public async Task RequestWithAllowedOrigin_ReceivesAccessControlAllowOriginHeader()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/system/info");
        request.Headers.Add("Origin", _allowedOrigin);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.Should().ContainKey("Access-Control-Allow-Origin");
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().Contain(_allowedOrigin);
    }
}
