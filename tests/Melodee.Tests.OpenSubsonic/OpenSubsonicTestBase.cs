using System.Text.Json.Nodes;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NodaTime;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic;

public abstract class OpenSubsonicTestBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected readonly WebApplicationFactory<Program> Factory;
    protected readonly HttpClient Client;
    protected readonly string TestUserName = "testuser";
    protected readonly string TestPassword = "testpassword";
    protected string? AuthToken;
    protected string? AuthSalt;

    protected OpenSubsonicTestBase(ITestOutputHelper output)
    {
        Output = output;
        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Find and remove the existing DbContext registration
                    var descriptorsToRemove = services.Where(d =>
                        d.ServiceType == typeof(DbContextOptions<MelodeeDbContext>) ||
                        d.ServiceType == typeof(MelodeeDbContext))
                    .ToList();

                    foreach (var descriptor in descriptorsToRemove)
                    {
                        services.Remove(descriptor);
                    }

                    // Add DbContext with in-memory database
                    services.AddDbContext<MelodeeDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("OpenSubsonicTestDb");
                    });
                });
            });

        Client = Factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<Melodee.Common.Data.MelodeeDbContext>();
        context.Database.EnsureCreated();

        await CreateTestUserAsync(context);
        await CreateTestLibraryAsync(context);
        await AuthenticateAsync();
    }

    public async Task DisposeAsync()
    {
        await Factory.DisposeAsync();
    }

    private async Task CreateTestUserAsync(Melodee.Common.Data.MelodeeDbContext context)
    {
        var existingUser = await context.Users.FirstOrDefaultAsync(u => u.UserName == TestUserName);
        if (existingUser != null)
        {
            return;
        }

        var publicKey = EncryptionHelper.GenerateRandomPublicKeyBase64();
        var encryptedPassword = EncryptionHelper.Encrypt(
            "H+Kiik6VMKfTD2MesF1GoMjczTrD5RhuKckJ5+/UQWOdWajGcsEC3yEnlJ5eoy8Y",
            TestPassword,
            publicKey);

        var user = new User
        {
            UserName = TestUserName,
            UserNameNormalized = TestUserName.ToUpperInvariant(),
            Email = "test@example.com",
            EmailNormalized = "test@example.com".ToUpperInvariant(),
            PublicKey = publicKey,
            PasswordEncrypted = encryptedPassword,
            IsAdmin = false,
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
    }

    private async Task CreateTestLibraryAsync(Melodee.Common.Data.MelodeeDbContext context)
    {
        var existingLibrary = await context.Libraries.FirstOrDefaultAsync(l => l.Type == (int)LibraryType.Storage);
        if (existingLibrary != null)
        {
            return;
        }

        var library = new Library
        {
            Name = "Test Storage Library",
            Path = "/tmp/test_library",
            Type = (int)LibraryType.Storage,
            CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
        };

        context.Libraries.Add(library);
        await context.SaveChangesAsync();
    }

    private async Task AuthenticateAsync()
    {
        AuthSalt = Guid.NewGuid().ToString("N")[..16];
        AuthToken = HashHelper.CreateMd5($"{TestPassword}{AuthSalt}");

        var response = await Client.GetAsync($"/rest/ping?u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");
        response.EnsureSuccessStatusCode();
    }

    protected async Task<HttpResponseMessage> GetAsync(string url)
    {
        return await Client.GetAsync($"/rest/{url}&u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");
    }

    protected async Task<string> GetResponseContentAsync(string url)
    {
        var response = await GetAsync(url);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    protected async Task<(bool success, string? content)> TryGetResponseContentAsync(string url)
    {
        try
        {
            var content = await GetResponseContentAsync(url);
            return (true, content);
        }
        catch
        {
            return (false, null);
        }
    }

    protected void AssertOpenSubsonicResponse(string endpoint, string responseContent)
    {
        Assert.False(string.IsNullOrEmpty(responseContent), $"Response from {endpoint} should not be empty");

        var json = JsonNode.Parse(responseContent);
        Assert.NotNull(json);

        var root = json?["subsonic-response"];
        Assert.NotNull(root);

        var status = root?["status"]?.ToString();
        Assert.Equal("ok", status);

        var version = root?["version"]?.ToString();
        Assert.NotNull(version);
        Assert.Matches(@"^\d+\.\d+\.\d+$", version);
    }

    protected async Task AssertEndpointConformsToSubsonicSchemaAsync(string endpoint, string url, string expectedResponseElement)
    {
        var content = await GetResponseContentAsync(url);
        AssertOpenSubsonicResponse(endpoint, content);

        var json = JsonNode.Parse(content);
        Assert.NotNull(json);

        var root = json?["subsonic-response"];
        Assert.NotNull(root);

        var responseElement = root?[expectedResponseElement];
        Assert.NotNull(responseElement);

        var errors = SubsonicSchemaValidator.ValidateResponseElement(expectedResponseElement, responseElement);
        Assert.True(errors.Count == 0,
            $"Response from {endpoint} does not conform to Subsonic XSD schema:\n{string.Join("\n", errors)}");
    }

    protected async Task<HttpResponseMessage> GetAsyncWithRange(string url, string rangeHeader)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"/rest/{url}&u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");
        request.Headers.Add("Range", rangeHeader);
        return await Client.SendAsync(request);
    }
}
