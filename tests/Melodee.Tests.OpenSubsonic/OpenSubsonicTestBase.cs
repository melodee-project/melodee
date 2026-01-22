using System.Text.Json.Nodes;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Enums;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NodaTime;
using Xunit.Abstractions;

namespace Melodee.Tests.OpenSubsonic;

[Collection(OpenSubsonicTestCollection.Name)]
public abstract class OpenSubsonicTestBase : IAsyncLifetime
{
    protected readonly ITestOutputHelper Output;
    protected readonly ServiceProvider InMemoryProvider;
    protected readonly WebApplicationFactory<Program> Factory;
    protected readonly HttpClient Client;
    protected readonly string TestUserName = "testuser";
    protected readonly string TestPassword = "testpassword";
    protected string? AuthToken;
    protected string? AuthSalt;

    // Fixed GUIDs for test data
    protected static readonly Guid TestArtistApiKey = new("11111111-1111-1111-1111-111111111111");
    protected static readonly Guid TestAlbumApiKey = new("22222222-2222-2222-2222-222222222222");
    protected static readonly Guid TestSongApiKey = new("33333333-3333-3333-3333-333333333333");
    protected static readonly Guid TestInternetRadioApiKey = new("44444444-4444-4444-4444-444444444444");
    protected static readonly Guid TestPlaylistApiKey = new("55555555-5555-5555-5555-555555555555");
    protected static readonly Guid TestShareApiKey = new("66666666-6666-6666-6666-666666666666");
    protected static readonly Guid TestBookmarkApiKey = new("77777777-7777-7777-7777-777777777777");

    protected OpenSubsonicTestBase(ITestOutputHelper output)
    {
        Output = output;

        // Set environment variables for required settings (used by MelodeeConfigurationFactory)
        // MelodeeConfigurationFactory replaces underscores with periods when reading environment variables
        Environment.SetEnvironmentVariable("security_secretKey", new string('s', 32));
        Environment.SetEnvironmentVariable("openSubsonicServer_openSubsonic_serverSupportedVersion", "1.16.1");
        Environment.SetEnvironmentVariable("openSubsonicServer_openSubsonicServer_type", "Melodee");
        Environment.SetEnvironmentVariable("openSubsonicServer_openSubsonicServerLicenseEmail", "noreply@localhost.lan");
        Environment.SetEnvironmentVariable("podcast_enabled", "true");

        InMemoryProvider = new ServiceCollection()
            .AddEntityFrameworkInMemoryDatabase()
            .BuildServiceProvider();

        Factory = new WebApplicationFactory<Program>()
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
                        ["security.secretKey"] = new string('s', 32),
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
                    // Find and remove all DbContext-related registrations
                    var descriptorsToRemove = services.Where(d =>
                        d.ServiceType == typeof(DbContextOptions<MelodeeDbContext>) ||
                        d.ServiceType == typeof(IDbContextFactory<MelodeeDbContext>) ||
                        d.ServiceType == typeof(IDbContextOptionsConfiguration<MelodeeDbContext>) ||
                        d.ServiceType == typeof(IConfigureOptions<DbContextOptions<MelodeeDbContext>>))
                    .ToList();

                    foreach (var descriptor in descriptorsToRemove)
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

                    // Add DbContextFactory with in-memory database
                    services.AddDbContextFactory<MelodeeDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("OpenSubsonicTestDb");
                    });

                    services.AddDbContextFactory<ArtistSearchEngineServiceDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("OpenSubsonicTestDb_ArtistSearchEngine");
                    });

                    services.AddDbContextFactory<MusicBrainzDbContext>(options =>
                    {
                        options.UseInMemoryDatabase("OpenSubsonicTestDb_MusicBrainz");
                    });

                    // Replace DefaultImages singleton with test version that doesn't require files
                    var defaultImagesDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(DefaultImages));
                    if (defaultImagesDescriptor != null)
                    {
                        services.Remove(defaultImagesDescriptor);
                    }
                    services.AddSingleton(new DefaultImages
                    {
                        UserAvatarBytes = [],
                        AlbumCoverBytes = [],
                        ArtistBytes = [],
                        PlaylistImageBytes = [],
                        ChartImageBytes = []
                    });
                });
            });

        Client = Factory.CreateClient();

        // Add X-Forwarded-For header for all requests (required by OpenSubsonic controller)
        Client.DefaultRequestHeaders.Add("X-Forwarded-For", "127.0.0.1");
    }

    public async Task InitializeAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MelodeeDbContext>>();
        await using var context = await contextFactory.CreateDbContextAsync();
        await context.Database.EnsureCreatedAsync();

        var artistSearchEngineFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ArtistSearchEngineServiceDbContext>>();
        await using var artistSearchEngineContext = await artistSearchEngineFactory.CreateDbContextAsync();
        await artistSearchEngineContext.Database.EnsureCreatedAsync();

        var musicBrainzFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MusicBrainzDbContext>>();
        await using var musicBrainzContext = await musicBrainzFactory.CreateDbContextAsync();
        await musicBrainzContext.Database.EnsureCreatedAsync();

        await SeedRequiredSettingsAsync(context);
        await CreateTestUserAsync(context);
        await CreateTestLibraryAsync(context);
        await CreateTestMusicDataAsync(context);
        await AuthenticateAsync();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        InMemoryProvider.Dispose();
        await Factory.DisposeAsync();
    }

    private async Task SeedRequiredSettingsAsync(Melodee.Common.Data.MelodeeDbContext context)
    {
        var seedTime = Instant.FromDateTimeUtc(DateTime.UtcNow);
        
        // Get existing settings
        var allSettings = await context.Settings.ToListAsync();
        var existingKeys = allSettings.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        // Required settings for OpenSubsonic API
        var requiredSettings = new Dictionary<string, (string Comment, string Value, SettingCategory Category)>
        {
            [SettingRegistry.OpenSubsonicServerSupportedVersion] = ("OpenSubsonic server supported Subsonic API version.", "1.16.1", SettingCategory.Api),
            [SettingRegistry.OpenSubsonicServerType] = ("OpenSubsonic server name.", "Melodee", SettingCategory.Api),
            [SettingRegistry.OpenSubsonicServerLicenseEmail] = ("OpenSubsonic email to use in License responses.", "noreply@localhost.lan", SettingCategory.Api),
            [SettingRegistry.PodcastEnabled] = ("Podcasts feature enabled.", "true", SettingCategory.System)
        };
        
        // Add missing required settings
        var settings = new List<Setting>();
        var nextId = allSettings.Any() ? allSettings.Max(s => s.Id) + 1 : 100;
        
        foreach (var (key, (comment, value, category)) in requiredSettings)
        {
            if (!existingKeys.Contains(key))
            {
                settings.Add(new Setting
                {
                    Id = nextId++,
                    ApiKey = Guid.NewGuid(),
                    Category = (int)category,
                    Key = key,
                    Comment = comment,
                    Value = value,
                    CreatedAt = seedTime
                });
            }
        }

        if (settings.Count > 0)
        {
            context.Settings.AddRange(settings);
            await context.SaveChangesAsync();
        }
        
        // Reset configuration factory cache so it reloads with the new settings
        var configFactory = Factory.Services.GetRequiredService<IMelodeeConfigurationFactory>();
        configFactory.Reset();
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
            IsAdmin = true,
            HasSettingsRole = true,
            HasDownloadRole = true,
            HasUploadRole = true,
            HasPlaylistRole = true,
            HasCoverArtRole = true,
            HasCommentRole = true,
            HasPodcastRole = true,
            HasStreamRole = true,
            HasJukeboxRole = true,
            HasShareRole = true,
            IsScrobblingEnabled = true,
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

    private async Task CreateTestMusicDataAsync(Melodee.Common.Data.MelodeeDbContext context)
    {
        // Get the library we just created
        var library = await context.Libraries.FirstOrDefaultAsync(l => l.Type == (int)LibraryType.Storage);
        if (library == null)
        {
            return;
        }

        // Create test artist if not exists
        var existingArtist = await context.Artists.FirstOrDefaultAsync(a => a.ApiKey == TestArtistApiKey);
        Melodee.Common.Data.Models.Artist artist;
        if (existingArtist == null)
        {
            artist = new Melodee.Common.Data.Models.Artist
            {
                ApiKey = TestArtistApiKey,
                Name = "Test Artist",
                NameNormalized = "TEST ARTIST",
                SortName = "Test Artist",
                Directory = "/tmp/test_library/Test Artist",
                LibraryId = library.Id,
                AlbumCount = 1,
                SongCount = 1,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Artists.Add(artist);
            await context.SaveChangesAsync();
        }
        else
        {
            artist = existingArtist;
        }

        // Create test album if not exists
        var existingAlbum = await context.Albums.FirstOrDefaultAsync(a => a.ApiKey == TestAlbumApiKey);
        Melodee.Common.Data.Models.Album album;
        if (existingAlbum == null)
        {
            album = new Melodee.Common.Data.Models.Album
            {
                ApiKey = TestAlbumApiKey,
                Name = "Test Album",
                NameNormalized = "TEST ALBUM",
                SortName = "Test Album",
                ArtistId = artist.Id,
                Duration = 180.5,
                SongCount = 1,
                ReleaseDate = new LocalDate(2024, 1, 1),
                Directory = "/tmp/test_library/Test Artist/Test Album",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Albums.Add(album);
            await context.SaveChangesAsync();
        }
        else
        {
            album = existingAlbum;
        }

        // Create test song if not exists
        var existingSong = await context.Songs.FirstOrDefaultAsync(s => s.ApiKey == TestSongApiKey);
        if (existingSong == null)
        {
            var song = new Melodee.Common.Data.Models.Song
            {
                ApiKey = TestSongApiKey,
                Title = "Test Song",
                TitleNormalized = "TEST SONG",
                AlbumId = album.Id,
                SongNumber = 1,
                FileName = "01 - Test Song.mp3",
                FileSize = 5000000,
                FileHash = "abc123def456",
                Duration = 180.5,
                BitRate = 320,
                BitDepth = 16,
                SamplingRate = 44100,
                ChannelCount = 2,
                ContentType = "audio/mpeg",
                BPM = 120,
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.Songs.Add(song);
            await context.SaveChangesAsync();
        }

        // Create test radio station if not exists
        var existingRadio = await context.RadioStations.FirstOrDefaultAsync(r => r.ApiKey == TestInternetRadioApiKey);
        if (existingRadio == null)
        {
            var radioStation = new RadioStation
            {
                ApiKey = TestInternetRadioApiKey,
                Name = "Test Radio",
                StreamUrl = "http://example.com/stream",
                HomePageUrl = "http://example.com",
                CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
            };
            context.RadioStations.Add(radioStation);
            await context.SaveChangesAsync();
        }

        // Get test user for playlist
        var user = await context.Users.FirstOrDefaultAsync(u => u.UserName == TestUserName);
        if (user != null)
        {
            // Create test playlist if not exists
            var existingPlaylist = await context.Playlists.FirstOrDefaultAsync(p => p.ApiKey == TestPlaylistApiKey);
            if (existingPlaylist == null)
            {
                var playlist = new Playlist
                {
                    Id = 1,
                    ApiKey = TestPlaylistApiKey,
                    Name = "Test Playlist",
                    UserId = user.Id,
                    IsPublic = true,
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                context.Playlists.Add(playlist);
                await context.SaveChangesAsync();
            }

            // Create test share if not exists
            var existingShare = await context.Shares.FirstOrDefaultAsync(s => s.ApiKey == TestShareApiKey);
            var song = await context.Songs.FirstOrDefaultAsync(s => s.ApiKey == TestSongApiKey);
            if (existingShare == null && song != null)
            {
                var share = new Share
                {
                    Id = 1,
                    ApiKey = TestShareApiKey,
                    UserId = user.Id,
                    ShareId = song.Id,  // Share a song
                    ShareUniqueId = Guid.NewGuid().ToString("N"),
                    ShareType = (int)ShareType.Song,
                    ExpiresAt = Instant.FromDateTimeUtc(DateTime.UtcNow.AddDays(30)),
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                context.Shares.Add(share);
                await context.SaveChangesAsync();
            }

            // Create test bookmark if not exists
            var existingBookmark = await context.Bookmarks.FirstOrDefaultAsync(b => b.ApiKey == TestBookmarkApiKey);
            if (existingBookmark == null && song != null)
            {
                var bookmark = new Bookmark
                {
                    ApiKey = TestBookmarkApiKey,
                    UserId = user.Id,
                    SongId = song.Id,
                    Position = 60000, // 1 minute in milliseconds
                    Comment = "Test bookmark",
                    CreatedAt = Instant.FromDateTimeUtc(DateTime.UtcNow)
                };
                context.Bookmarks.Add(bookmark);
                await context.SaveChangesAsync();
            }
        }
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
        var separator = url.Contains('?') ? '&' : '?';
        return await Client.GetAsync($"/rest/{url}{separator}u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");
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

        try
        {
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
        catch (Exception ex) when (ex is System.Text.Json.JsonException)
        {
            // Log the actual response for debugging
            throw new Exception($"Failed to parse JSON response from {endpoint}. Response content (first 500 chars): {(responseContent.Length > 500 ? responseContent[..500] : responseContent)}");
        }
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
        var separator = url.Contains('?') ? '&' : '?';
        var request = new HttpRequestMessage(HttpMethod.Get, $"/rest/{url}{separator}u={TestUserName}&t={AuthToken}&s={AuthSalt}&v=1.16.1&c=test&f=json");
        request.Headers.Add("Range", rangeHeader);
        return await Client.SendAsync(request);
    }
}
