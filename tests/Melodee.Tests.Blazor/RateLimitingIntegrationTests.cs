using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Common.Data;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;

namespace Melodee.Tests.Blazor;

/// <summary>
/// Integration tests for rate limiting functionality.
/// </summary>
[Trait("Category", "Integration")]
public class RateLimitingIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _userName = "ratelimituser";
    private readonly string _password = "TestPassword123!";
    private readonly string _email = "ratelimituser@example.com";
    private readonly ServiceProvider _inMemoryProvider;

    public RateLimitingIntegrationTests()
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
                        // Set low limits for testing
                        ["RateLimiting:MelodeeApi:TokenLimit"] = "10",
                        ["RateLimiting:MelodeeApi:QueueLimit"] = "5",
                        ["RateLimiting:MelodeeApi:ReplenishmentPeriodSeconds"] = "60",
                        ["RateLimiting:MelodeeApi:TokensPerPeriod"] = "10",
                        ["RateLimiting:MelodeeApi:AutoReplenishment"] = "true",
                        ["RateLimiting:MelodeeAuth:TokenLimit"] = "3", // Low limit for auth endpoint testing
                        ["RateLimiting:MelodeeAuth:QueueLimit"] = "2",
                        ["RateLimiting:MelodeeAuth:ReplenishmentPeriodSeconds"] = "60",
                        ["RateLimiting:MelodeeAuth:TokensPerPeriod"] = "3",
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
                        options.UseInMemoryDatabase("RateLimitingTests");
                        options.UseInternalServiceProvider(_inMemoryProvider);
                    });

                    services.AddSingleton<IPasswordHashService, PasswordHashService>();
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

        // Create a test user
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHashService>();
        var user = new Melodee.Common.Data.Models.User
        {
            UserName = _userName,
            UserNameNormalized = _userName.ToUpperInvariant(),
            Email = _email,
            EmailNormalized = _email.ToUpperInvariant(),
            PublicKey = EncryptionHelper.GenerateRandomPublicKeyBase64(),
            PasswordEncrypted = "legacy",
            PasswordHash = passwordHasher.Hash(_password),
            PasswordHashAlgorithm = "bcrypt",
            CreatedAt = SystemClock.Instance.GetCurrentInstant()
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        _inMemoryProvider.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    [Fact]
    public async Task AuthEndpoint_ExceedsRateLimit_Returns429()
    {
        // Arrange
        var loginData = new
        {
            userName = _userName,
            password = _password
        };

        // Act - Make multiple requests to exceed the rate limit
        for (int i = 0; i < 5; i++) // Exceed the 3-token limit
        {
            var response = await _client.PostAsJsonAsync("/api/v1/auth/authenticate", loginData);
            // First few should succeed, last one should be rate limited
            if (i < 3)
            {
                response.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Unauthorized);
            }
        }

        // The next request should be rate limited
        var finalResponse = await _client.PostAsJsonAsync("/api/v1/auth/authenticate", loginData);

        // Assert
        finalResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var error = await finalResponse.Content.ReadFromJsonAsync<ApiError>();
        error.Should().NotBeNull();
        error!.Code.Should().Be("TooManyRequests");
        error.CorrelationId.Should().NotBeNullOrEmpty();
    }
}
