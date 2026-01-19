using Melodee.Cli.Client;
using Melodee.Cli.Configuration;
using Melodee.Common.Configuration;
using Melodee.Common.Data;
using Melodee.Common.Metadata;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.Scrobbling;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.Spotify;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl;
using Rebus.Config;
using Rebus.Transport.InMem;
using Serilog;
using Spectre.Console.Cli;
using SpotifyAPI.Web;

namespace Melodee.Cli.Command;

public abstract class CommandBase<T> : AsyncCommand<T> where T : Spectre.Console.Cli.CommandSettings
{
    /// <summary>
    /// ISO8601 date format for consistent CLI output that sorts correctly.
    /// Format: yyyyMMddTHHmmss (e.g., 20251230T141623)
    /// </summary>
    protected const string Iso8601DateFormat = "yyyyMMdd'T'HHmmss";

    protected IConfigurationRoot Configuration()
    {
        var basePath = Directory.GetCurrentDirectory();
        var appSettingsPath = Environment.GetEnvironmentVariable("MELODEE_APPSETTINGS_PATH");
        if (!string.IsNullOrWhiteSpace(appSettingsPath) && File.Exists(appSettingsPath))
        {
            return new ConfigurationBuilder()
                .AddJsonFile(appSettingsPath)
                .AddEnvironmentVariables()
                .Build();
        }

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("MELODEE_ENVIRONMENT") ?? "Production";

        return new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json")
            .AddJsonFile($"appsettings.{environment}.json", true)
            .AddEnvironmentVariables()
            .Build();
    }

    protected ServiceProvider CreateServiceProvider()
    {
        var configuration = Configuration();
        var services = new ServiceCollection();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .CreateLogger();

        services.AddSingleton(Log.Logger);
        services.AddHttpContextAccessor();
        services.AddSingleton<ISerializer, Serializer>();
        services.AddHttpClient();
        services.AddHttpClient("ImageFetch", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.Add("Accept", "image/*");
        });
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            Console.WriteLine("Error: Database connection string is not configured.");
            Console.WriteLine("Please set the MELODEE_ENVIRONMENT or ASPNETCORE_ENVIRONMENT environment variable to 'Development' or ensure 'DefaultConnection' is set in your configuration.");
            Environment.Exit(1);
        }

        services.AddDbContextFactory<MelodeeDbContext>(opt =>
            opt.UseNpgsql(connectionString,
                o => o.UseNodaTime().UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddDbContextFactory<MusicBrainzDbContext>(opt =>
            opt.UseSqlite(configuration.GetConnectionString("MusicBrainzConnection")));
        services.AddDbContextFactory<ArtistSearchEngineServiceDbContext>(opt
            => opt.UseSqlite(configuration.GetConnectionString("ArtistSearchEngineConnection")));
        services.AddScoped<IMusicBrainzRepository, SQLiteMusicBrainzRepository>();
        services.AddSingleton<IMelodeeConfigurationFactory, MelodeeConfigurationFactory>();
        services.AddSingleton<ICacheManager>(opt
            => new MemoryCacheManager(opt.GetRequiredService<ILogger>(),
                new TimeSpan(1,
                    0,
                    0,
                    0),
                opt.GetRequiredService<ISerializer>()));
        services.AddSingleton(Log.Logger);
        services.AddRebus(configure =>
        {
            return configure
                .Transport(t => t.UseInMemoryTransport(new InMemNetwork(), "melodee_bus"));
        });
        services.AddSingleton(SpotifyClientConfig.CreateDefault());
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<INowPlayingRepository, NowPlayingInMemoryRepository>();
        services.AddScoped<ISpotifyClientBuilder, SpotifyClientBuilder>();
        services.AddScoped<AlbumDiscoveryService>();
        services.AddScoped<AlbumImageSearchEngineService>();
        services.AddScoped<ArtistImageSearchEngineService>();
        services.AddScoped<ArtistSearchEngineService>();
        services.AddScoped<AlbumSearchEngineService>();
        services.AddScoped<ChartService>();
        services.AddScoped<DirectoryProcessorToStagingService>();
        services.AddScoped<LibraryService>();
        services.AddScoped<LibraryAuthorizationService>();
        services.AddScoped<MediaEditService>();
        services.AddScoped<MelodeeMetadataMaker>();
        services.AddScoped<NowPlayingDatabaseRepository>();
        services.AddScoped<SettingService>();
        services.AddScoped<ArtistService>();
        services.AddScoped<AlbumService>();
        services.AddScoped<SongService>();
        services.AddScoped<PlaylistService>();
        services.AddScoped<PlaylistImportService>();
        services.AddScoped<PodcastService>();
        services.AddScoped<UserService>();
        services.AddScoped<UserGroupService>();
        services.AddScoped<UserRatingService>();
        services.AddScoped<UserBookmarkService>();
        services.AddScoped<UserShareService>();
        services.AddScoped<UserPinService>();
        services.AddScoped<UserPreferenceService>();
        services.AddScoped<UserPasswordResetService>();
        services.AddScoped<UserSocialLoginService>();
        services.AddScoped<UserStarService>();
        services.AddScoped<UserFavoriteService>();
        services.AddScoped<UserAuthenticationService>();
        services.AddScoped<UserProfileService>();
        services.AddScoped<UserDeviceProfileService>();
        services.AddScoped<DeviceIdentificationService>();
        services.AddSingleton<IPasswordHashService, PasswordHashService>();
        services.AddSingleton<ISecretProtector, SecretProtector>();
        services.AddScoped<UserQueueService>();
        services.AddScoped<ArtistDuplicateFinder>();
        services.AddSingleton<ISsrfValidator, SsrfValidator>();
        services.AddSingleton<PodcastHttpClient>();
        services.AddSingleton<ISchedulerFactory, StdSchedulerFactory>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Create a Melodee client based on the provided settings.
    /// If server is specified, returns RemoteMelodeeClient, otherwise LocalMelodeeClient.
    /// </summary>
    protected IMelodeeClient CreateMelodeeClient(CommandSettings.GlobalSettings settings)
    {
        using var _ = Serilog.Context.LogContext.PushProperty("Method", nameof(CreateMelodeeClient));

        var options = RemoteModeOptions.Resolve(settings.Server, settings.Token, settings.Profile);

        if (options.IsRemoteMode)
        {
            if (string.IsNullOrWhiteSpace(options.Token))
            {
                Console.Error.WriteLine("ERROR: Missing API token. Provide --token, MELODEE_TOKEN, or a config profile token.");
                Environment.Exit(2);
            }

            // Warn if token was passed on command line
            if (!string.IsNullOrWhiteSpace(settings.Token))
            {
                Console.Error.WriteLine("WARNING: Passing tokens on the command line can leak secrets via shell history. Prefer MELODEE_TOKEN or config profiles.");
            }

            return new Client.RemoteMelodeeClient(options.GetApiBaseUrl(), options.Token);
        }
        else
        {
            // Local mode - use existing service provider
            var serviceProvider = CreateServiceProvider();
            var configFactory = serviceProvider.GetRequiredService<IMelodeeConfigurationFactory>();
            var userProfileService = serviceProvider.GetRequiredService<UserProfileService>();

            return new Client.LocalMelodeeClient(configFactory, userProfileService);
        }
    }
}
