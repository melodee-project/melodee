using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NodaTime;

namespace Melodee.Tests.Blazor;

/// <summary>
/// Integration coverage for cookie-based auth endpoints.
/// </summary>
[Trait("Category", "Integration")]
public class AuthCookieIntegrationTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _userName = "cookieuser";
    private readonly string _password = "TestPassword123!";
    private readonly string _email = "cookieuser@example.com";
    private readonly ServiceProvider _inMemoryProvider;

    /// <summary>
    /// Configures a WebApplicationFactory with in-memory database and auth settings.
    /// </summary>
    public AuthCookieIntegrationTests()
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

                    // Remove existing ArtistSearchEngineServiceDbContext registrations
                    var artistSearchEngineDescriptors = services.Where(d =>
                            d.ServiceType == typeof(DbContextOptions<ArtistSearchEngineServiceDbContext>) ||
                            d.ServiceType == typeof(IDbContextFactory<ArtistSearchEngineServiceDbContext>) ||
                            d.ServiceType == typeof(IDbContextOptionsConfiguration<ArtistSearchEngineServiceDbContext>) ||
                            d.ServiceType == typeof(IConfigureOptions<DbContextOptions<ArtistSearchEngineServiceDbContext>>))
                        .ToList();

                    foreach (var descriptor in artistSearchEngineDescriptors)
                    {
                        services.Remove(descriptor);
                    }

                    // Remove existing MusicBrainzDbContext registrations
                    var musicBrainzDescriptors = services.Where(d =>
                            d.ServiceType == typeof(DbContextOptions<MusicBrainzDbContext>) ||
                            d.ServiceType == typeof(IDbContextFactory<MusicBrainzDbContext>) ||
                            d.ServiceType == typeof(IDbContextOptionsConfiguration<MusicBrainzDbContext>) ||
                            d.ServiceType == typeof(IConfigureOptions<DbContextOptions<MusicBrainzDbContext>>))
                        .ToList();

                    foreach (var descriptor in musicBrainzDescriptors)
                    {
                        services.Remove(descriptor);
                    }

                    services.AddDbContextFactory<MelodeeDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("AuthCookieTests");
                        options.UseInternalServiceProvider(_inMemoryProvider);
                    });

                    services.AddDbContextFactory<ArtistSearchEngineServiceDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("AuthCookieTests_ArtistSearchEngine");
                        options.UseInternalServiceProvider(_inMemoryProvider);
                    });

                    services.AddDbContextFactory<MusicBrainzDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("AuthCookieTests_MusicBrainz");
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

    /// <summary>
    /// Seeds a test user with a bcrypt password hash.
    /// </summary>
    public async Task InitializeAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHashService>();
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        if (!await context.Users.AnyAsync(u => u.UserNameNormalized == _userName.ToUpperInvariant()))
        {
            var user = new User
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
    }

    /// <summary>
    /// Disposes the test client and factory.
    /// </summary>
    public Task DisposeAsync()
    {
        _client.Dispose();
        _inMemoryProvider.Dispose();
        return _factory.DisposeAsync().AsTask();
    }

    /// <summary>
    /// Ensures cookie sign-in returns a secure cookie and authorizes a follow-up call.
    /// </summary>
    [Fact]
    public async Task CookieSignIn_WithValidCredentials_SetsSecureHttpOnlyCookieAndAuthorizes()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/cookie/sign-in");
        request.Headers.Add("REMOTE_ADDR", "127.0.0.1");
        request.Content = JsonContent.Create(new { userName = _userName, password = _password });
        var response = await _client.SendAsync(request);

        var responseBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"response body: {responseBody}");
        response.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders).Should().BeTrue();
        var authCookie = setCookieHeaders!.FirstOrDefault(h => h.StartsWith("melodee_auth=", StringComparison.OrdinalIgnoreCase));
        authCookie.Should().NotBeNull();
        authCookie.Should().ContainEquivalentOf("httponly");
        authCookie.Should().ContainEquivalentOf("secure");
        authCookie.Should().ContainEquivalentOf("samesite=strict");

        var cookieValue = authCookie!.Split(';', 2)[0];
        var signOutRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/cookie/sign-out");
        signOutRequest.Headers.Add("Cookie", cookieValue);

        var signOutResponse = await _client.SendAsync(signOutRequest);
        signOutResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
