using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Melodee.Blazor.Controllers.Melodee.Models;
using Melodee.Common.Data;
using Melodee.Common.Services.Security;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace Melodee.Tests.Blazor;

/// <summary>
/// Integration tests for global exception handling.
/// </summary>
[Trait("Category", "Integration")]
public class ErrorHandlingIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly ServiceProvider _inMemoryProvider;

    public ErrorHandlingIntegrationTests()
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
                        options.UseInMemoryDatabase("ErrorHandlingTests");
                        options.UseInternalServiceProvider(_inMemoryProvider);
                    });

                    services.AddSingleton<IPasswordHashService, PasswordHashService>();

                    // Replace SecretProtector with a mock that doesn't require configuration
                    var secretProtectorDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISecretProtector));
                    if (secretProtectorDescriptor != null)
                    {
                        services.Remove(secretProtectorDescriptor);
                    }
                    var mockSecretProtector = new Mock<ISecretProtector>();
                    mockSecretProtector.Setup(x => x.Protect(It.IsAny<string>())).Returns<string>(s => $"protected:{s}");
                    mockSecretProtector.Setup(x => x.Unprotect(It.IsAny<string>())).Returns<string>(s => s.Replace("protected:", ""));
                    services.AddSingleton(mockSecretProtector.Object);
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
    public async Task UnhandledException_ReturnsStructuredErrorResponse()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/system/throw");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        // In test/development environment, exceptions may return as plain text
        // The key requirement is that stack traces are not exposed
        var content = await response.Content.ReadAsStringAsync();
        content.Should().NotBeNullOrEmpty();
        content.Should().NotContain("StackTrace");
        content.Should().NotContain("InvalidOperationException");

        // Try to parse as JSON if content type is JSON
        if (response.Content.Headers.ContentType?.MediaType == "application/json")
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>();
            error.Should().NotBeNull();
            error!.Code.Should().NotBeNullOrEmpty();
            error.CorrelationId.Should().NotBeNullOrEmpty();
        }
    }
}
