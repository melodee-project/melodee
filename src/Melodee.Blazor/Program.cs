using System.Diagnostics;
using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Asp.Versioning;
using Blazored.SessionStorage;
using Melodee.Blazor.Components;
using Melodee.Blazor.Constants;
using Melodee.Blazor.Filters;
using Melodee.Blazor.Hubs;
using Melodee.Blazor.Middleware;
using Melodee.Blazor.Services;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Data;
using Melodee.Common.Enums;
using Melodee.Common.Extensions;
using Melodee.Common.Jobs;
using Melodee.Common.MessageBus.EventHandlers;
using Melodee.Common.Metadata;
using Melodee.Common.Models;
using Melodee.Common.Models.SearchEngines.ArtistSearchEngineServiceData;
using Melodee.Common.Plugins.MetaData.Song;
using Melodee.Common.Plugins.Scrobbling;
using Melodee.Common.Plugins.SearchEngine.MusicBrainz.Data;
using Melodee.Common.Plugins.SearchEngine.Spotify;
using Melodee.Common.Serialization;
using Melodee.Common.Services;
using Melodee.Common.Services.Caching;
using Melodee.Common.Services.Jukebox;
using Melodee.Common.Services.PartyMode;
using Melodee.Common.Services.Playback;
using Melodee.Common.Services.Playback.Factory;
using Melodee.Common.Services.Scanning;
using Melodee.Common.Services.SearchEngines;
using Melodee.Common.Services.Security;
using Melodee.Common.Utility;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using NodaTime;
using Npgsql;
using Quartz;
using Quartz.AspNetCore;
using Quartz.Impl.Matchers;
using Radzen;
using Rebus.Compression;
using Rebus.Config;
using Rebus.Persistence.InMem;
using Rebus.Transport.InMem;
using Scalar.AspNetCore;
using Serilog;
using SpotifyAPI.Web;
using ILogger = Serilog.ILogger;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", false, true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", true)
    .AddEnvironmentVariables();

Trace.Listeners.Clear();
Trace.Listeners.Add(new ConsoleTraceListener());

builder.Host.UseSerilog((hostingContext, loggerConfiguration)
    => loggerConfiguration.ReadFrom.Configuration(hostingContext.Configuration));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Configure localization
builder.Services.AddLocalization();

builder.Services.Configure<Microsoft.AspNetCore.Components.Server.CircuitOptions>(options =>
{
    options.DisconnectedCircuitMaxRetained = 100;
    options.DisconnectedCircuitRetentionPeriod = TimeSpan.FromMinutes(3);
    options.JSInteropDefaultCallTimeout = TimeSpan.FromMinutes(2);
    options.MaxBufferedUnacknowledgedRenderBatches = 50;
});

builder.Services.AddScoped<CircuitHandler, MelodeeCircuitHandler>();

builder.Services.AddControllers(options => { options.Filters.Add<ETagFilter>(); });

builder.Services.AddEndpointsApiExplorer();

// Register single OpenAPI document that contains all endpoints
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Melodee API",
            Version = "v1",
            Description = "Complete API documentation for Melodee music server. This is for the native Melodee.API, not for the OpenSubsonic-compatible or Jellyfin-compatible API endpoints on Melodee, for those see the respective API documentation websites."
        };
        return Task.CompletedTask;
    });
});

// Build connection string with optional pool-size overrides via environment variables
var defaultConnString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(defaultConnString))
{
    throw new InvalidOperationException("Missing connection string 'DefaultConnection'");
}

var npgsqlBuilder = new NpgsqlConnectionStringBuilder(defaultConnString);
var envMinPool = Environment.GetEnvironmentVariable("DB_MIN_POOL_SIZE");
var envMaxPool = Environment.GetEnvironmentVariable("DB_MAX_POOL_SIZE");
if (int.TryParse(envMinPool, out var minPool) && minPool > 0)
{
    npgsqlBuilder.MinPoolSize = minPool;
}
if (int.TryParse(envMaxPool, out var maxPool) && maxPool > 0)
{
    npgsqlBuilder.MaxPoolSize = maxPool;
}
var effectiveConnString = npgsqlBuilder.ToString();

builder.Services.AddDbContextFactory<MelodeeDbContext>(opt =>
    opt.UseNpgsql(effectiveConnString, o
        => o.UseNodaTime()
            .UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));

builder.Services.AddDbContextFactory<ArtistSearchEngineServiceDbContext>(opt
    => opt.UseSqlite(builder.Configuration.GetConnectionString("ArtistSearchEngineConnection")));

builder.Services.AddDbContextFactory<MusicBrainzDbContext>(opt =>
    opt.UseSqlite(builder.Configuration.GetConnectionString("MusicBrainzConnection")));

builder.Services.AddApiVersioning(options =>
    {
        options.DefaultApiVersion = new ApiVersion(1);
        options.ReportApiVersions = true;
        options.AssumeDefaultVersionWhenUnspecified = true;
        options.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new HeaderApiVersionReader("X-Api-Version"));
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });


// Configure forwarded headers for reverse proxy (only if enabled)
var useForwardedHeaders = SafeParser.ToBoolean(builder.Configuration["UseForwardedHeaders"]);
if (useForwardedHeaders)
{
    Trace.WriteLine("Using forwarded headers");
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
}

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("MelodeeApi", (sp, client) =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    // Use relative URLs - the client will use the current request's base address
    var httpContextAccessor = sp.GetService<IHttpContextAccessor>();
    if (httpContextAccessor?.HttpContext != null)
    {
        var request = httpContextAccessor.HttpContext.Request;
        client.BaseAddress = new Uri($"{request.Scheme}://{request.Host}");
    }
});
builder.Services.AddHttpClient("LastFm", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.BaseAddress = new Uri("https://ws.audioscrobbler.com");
});
builder.Services.AddHttpClient("ImageFetch", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.Add("Accept", "image/*");
});

builder.Services.AddAntiforgery(opt =>
{
    opt.Cookie.Name = "melodee_csrf";
    opt.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

builder.Services.AddRadzenComponents();

builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "melodee_ui_theme";
    options.Duration = TimeSpan.FromDays(9999);
});

builder.Services.AddBlazoredSessionStorage();

// Add response compression for better performance (addresses Lighthouse: Enable text compression)
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat([
        "application/javascript",
        "application/json",
        "text/css",
        "text/html",
        "text/json",
        "text/plain"
    ]);
});

builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Optimal;
});

builder.Services.Configure<GzipCompressionProviderOptions>(options =>
{
    options.Level = CompressionLevel.Optimal;
});

// SignalR for real-time party mode updates
builder.Services.AddSignalR();

builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

builder.Services.AddAuthorization();
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(x =>
    {
        x.Cookie.SameSite = SameSiteMode.Strict;
        x.Cookie.Name = "melodee_auth";
        x.Cookie.HttpOnly = true;
        x.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    })
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
    {
        var jwtKey = builder.Configuration.GetValue<string>("Jwt:Key");
        var issuer = builder.Configuration.GetValue<string>("Jwt:Issuer");
        var audience = builder.Configuration.GetValue<string>("Jwt:Audience");
        if (string.IsNullOrWhiteSpace(jwtKey) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("JWT configuration (Jwt:Key, Jwt:Issuer, Jwt:Audience) is required.");
        }

        options.RequireHttpsMetadata = true;
        options.SaveToken = true;
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<ILocalStorageService, LocalStorageService>();
builder.Services.AddScoped<ILocalizationService, LocalizationService>();
builder.Services.AddScoped<IThemeClientService, ThemeClientService>();

// Configure CORS with strict allowlist policies
var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddPolicy("MelodeeCors", policy =>
    {
        if (corsAllowedOrigins.Length > 0)
        {
            policy.WithOrigins(corsAllowedOrigins);
        }
        else if (builder.Environment.IsDevelopment())
        {
            policy.WithOrigins("http://localhost:*", "https://localhost:*");
        }
        else
        {
            throw new InvalidOperationException("CORS is not configured for production. Set Cors:AllowedOrigins in configuration.");
        }

        policy.WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS");
        policy.WithHeaders("Authorization", "Content-Type", "If-None-Match", "If-Match");
        policy.WithExposedHeaders("Accept-Ranges", "Content-Range", "Content-Length", "Content-Type", "ETag");
        policy.AllowCredentials();
    });
});

// Email services
builder.Services.AddScoped<Melodee.Blazor.Services.Email.IEmailSender, Melodee.Blazor.Services.Email.SmtpEmailSender>();
builder.Services.AddScoped<Melodee.Blazor.Services.Email.IEmailTemplateService, Melodee.Blazor.Services.Email.EmailTemplateService>();

// Doctor service for health checks and diagnostics
builder.Services.AddScoped<IDoctorService, DoctorService>();

// Playback backend services for Jukebox
builder.Services.AddSingleton<PlaybackBackendFactory>();
builder.Services.AddScoped<IPlaybackBackendService, PlaybackBackendService>();
builder.Services.AddScoped<ISubsonicJukeboxService, SubsonicJukeboxService>();

// Rate limiting service for Blazor UI
builder.Services.AddSingleton<IRateLimiterService, RateLimiterService>();
builder.Services.AddSingleton<Melodee.Mql.Services.MqlMetricsService>();
builder.Services.AddSingleton<Melodee.Mql.Interfaces.IMqlValidator, Melodee.Mql.MqlValidator>();
builder.Services.AddScoped<Melodee.Blazor.Services.IMqlSearchService, Melodee.Blazor.Services.MqlSearchService>();
builder.Services.AddMemoryCache();

// Custom blocks for page customization (uses Templates library)
builder.Services.Configure<Melodee.Blazor.Services.CustomBlocks.CustomBlocksOptions>(
    builder.Configuration.GetSection(Melodee.Blazor.Services.CustomBlocks.CustomBlocksOptions.SectionName));
builder.Services.AddScoped<Melodee.Blazor.Services.CustomBlocks.ICustomBlockService, Melodee.Blazor.Services.CustomBlocks.FileCustomBlockService>();
builder.Services.AddSingleton<Melodee.Blazor.Services.CustomBlocks.IMarkdownRenderer, Melodee.Blazor.Services.CustomBlocks.MarkdownRenderer>();
builder.Services.AddSingleton<Melodee.Blazor.Services.CustomBlocks.IHtmlSanitizerService, Melodee.Blazor.Services.CustomBlocks.HtmlSanitizerService>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("melodee-api", context =>
        RateLimitPartition.GetTokenBucketLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 30,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10,
                ReplenishmentPeriod = TimeSpan.FromSeconds(30),
                TokensPerPeriod = 30,
                AutoReplenishment = true
            }));
    options.AddPolicy("melodee-auth", context =>
        RateLimitPartition.GetTokenBucketLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 10,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 10,
                AutoReplenishment = true
            }));
    options.AddPolicy("jellyfin-api", context =>
    {
        var apiTokenLimit = builder.Configuration.GetValue<int>("Jellyfin:RateLimit:ApiTokenLimit", 200);
        var apiPeriodSeconds = builder.Configuration.GetValue<int>("Jellyfin:RateLimit:ApiPeriodSeconds", 60);
        return RateLimitPartition.GetTokenBucketLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = apiTokenLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 20,
                ReplenishmentPeriod = TimeSpan.FromSeconds(apiPeriodSeconds),
                TokensPerPeriod = apiTokenLimit,
                AutoReplenishment = true
            });
    });
    options.AddPolicy("jellyfin-auth", context =>
    {
        var authTokenLimit = builder.Configuration.GetValue<int>("Jellyfin:RateLimit:AuthTokenLimit", 10);
        var authPeriodSeconds = builder.Configuration.GetValue<int>("Jellyfin:RateLimit:AuthPeriodSeconds", 60);
        return RateLimitPartition.GetTokenBucketLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = authTokenLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5,
                ReplenishmentPeriod = TimeSpan.FromSeconds(authPeriodSeconds),
                TokensPerPeriod = authTokenLimit,
                AutoReplenishment = true
            });
    });
    options.AddPolicy("jellyfin-stream", context =>
    {
        var streamConcurrentLimit = builder.Configuration.GetValue<int>("Jellyfin:RateLimit:StreamConcurrentLimit", 10);
        return RateLimitPartition.GetConcurrencyLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = streamConcurrentLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            });
    });

    // Party mode rate limiting policies
    options.AddPolicy("party-queue-add", context =>
    {
        // Rate limit: max 20 queue additions per minute per user per session
        var userId = context.User.FindFirstValue(ClaimTypes.Sid) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            });
    });

    options.AddPolicy("party-playback-control", context =>
    {
        // Rate limit: max 30 playback control actions per minute per user per session
        var userId = context.User.FindFirstValue(ClaimTypes.Sid) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            });
    });

    options.AddPolicy("party-volume", context =>
    {
        // Rate limit: max 20 volume changes per minute per user per session
        var userId = context.User.FindFirstValue(ClaimTypes.Sid) ?? "anonymous";
        return RateLimitPartition.GetFixedWindowLimiter(userId,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 5
            });
    });
});

builder.Services.AddSingleton<IAppVersionProvider, AppVersionProvider>();

// Health checks
builder.Services.AddHealthChecks();

#region Melodee Services

builder.Services
    .AddScoped<MainLayoutProxyService>()
    .AddSingleton<ISerializer, Serializer>()
    .AddSingleton<ICacheManager>(opt
        => new MemoryCacheManager(opt.GetRequiredService<ILogger>(),
            new TimeSpan(1,
                0,
                0,
                0),
            opt.GetRequiredService<ISerializer>()))
    .AddSingleton<DefaultImages>(_ => new DefaultImages
    {
        UserAvatarBytes = File.ReadAllBytes("wwwroot/images/avatar.png"),
        AlbumCoverBytes = File.ReadAllBytes("wwwroot/images/album.jpg"),
        ArtistBytes = File.ReadAllBytes("wwwroot/images/artist.jpg"),
        PlaylistImageBytes = File.ReadAllBytes("wwwroot/images/playlist.jpg"),
        ChartImageBytes = File.ReadAllBytes("wwwroot/images/chart.jpg")
    })
    .AddSingleton(SpotifyClientConfig.CreateDefault())
    .AddScoped<ISpotifyClientBuilder, SpotifyClientBuilder>()
    .AddSingleton<IFileSystemService, FileSystemService>()
    .AddScoped<NowPlayingDatabaseRepository>()
    .AddScoped<INowPlayingRepository>(sp => sp.GetRequiredService<NowPlayingDatabaseRepository>())
    .AddSingleton<IMelodeeConfigurationFactory, MelodeeConfigurationFactory>()
    .AddSingleton<StreamingLimiter>()
    .AddSingleton<EtagRepository>()
    .AddScoped<IMusicBrainzRepository, SQLiteMusicBrainzRepository>()
    .AddScoped<SettingService>()
    .AddScoped<ArtistService>()
    .AddScoped<IBaseUrlService, BaseUrlService>()
    .AddScoped<AlbumService>()
    .AddScoped<SongService>()
    .AddScoped<ScrobbleService>()
    .AddScoped<LibraryService>()
    .AddScoped<UserService>()
    .AddScoped<AlbumDiscoveryService>()
    .AddScoped<MediaEditService>()
    .AddScoped<DirectoryProcessorToStagingService>()
    .AddScoped<ImageConversionService>()
    .AddScoped<OpenSubsonicApiService>()
    .AddScoped<AlbumImageSearchEngineService>()
    .AddScoped<ArtistImageSearchEngineService>()
    .AddScoped<AlbumSearchEngineService>()
    .AddScoped<ArtistSearchEngineService>()
    .AddScoped<StatisticsService>()
    .AddScoped<SearchService>()
    .AddScoped<ShareService>()
    .AddScoped<RequestService>()
    .AddScoped<RequestCommentService>()
    .AddScoped<RequestActivityService>()
    .AddScoped<RequestAutoCompletionService>()
    .AddScoped<RadioStationService>()
    .AddScoped<PlaylistService>()
    .AddScoped<Melodee.Common.Services.IThemeService, Melodee.Common.Services.ThemeService>()
    .AddScoped<ChartService>()
    .AddScoped<MelodeeMetadataMaker>()
    .AddScoped<AlbumRescanEventHandler>()
    .AddScoped<AlbumAddEventHandler>()
    .AddScoped<ILyricPlugin, LyricPlugin>()
    .AddScoped<UserQueueService>()
    .AddScoped<PlaybackSettingsService>()
    .AddScoped<EqualizerPresetService>()
    .AddSingleton<ISsrfValidator, SsrfValidator>()
    .AddSingleton<PodcastHttpClient>()
    .AddScoped<PodcastService>()
    .AddScoped<PodcastPlaybackService>()
    .AddScoped<PodcastOpmlService>()
    .AddScoped<PodcastDiscoveryService>()
    .AddScoped<IPartySessionService, PartySessionService>()
    .AddScoped<IPartyQueueService, PartyQueueService>()
    .AddScoped<IPartyPlaybackService, PartyPlaybackService>()
    .AddScoped<IPartyNotificationService, PartyNotificationService>()
    .AddScoped<IPartySessionEndpointRegistryService, PartySessionEndpointRegistryService>();

// Configure HttpClient for podcast discovery
builder.Services.AddHttpClient("PodcastDiscovery", client =>
{
    client.DefaultRequestHeaders.Add("User-Agent", "Melodee/1.0 (Podcast Discovery)");
    client.Timeout = TimeSpan.FromSeconds(30);
});

#endregion

builder.Services.AddSingleton<IBlacklistService, BlacklistService>();
builder.Services.AddScoped<MelodeeApiAuthFilter>();

#region Google Auth & Token Services

builder.Services.Configure<GoogleAuthOptions>(builder.Configuration.GetSection(GoogleAuthOptions.SectionName));
builder.Services.Configure<AuthPolicyOptions>(builder.Configuration.GetSection(AuthPolicyOptions.SectionName));
builder.Services.Configure<TokenOptions>(builder.Configuration.GetSection(TokenOptions.SectionName));
builder.Services.Configure<PartyModeOptions>(builder.Configuration.GetSection(PartyModeOptions.SectionName));
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddScoped<IGoogleTokenService, GoogleTokenService>();
builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();

#endregion

#region Quartz Related

builder.Services.AddQuartz(q => { q.UseTimeZoneConverter(); });
// Resolve IScheduler via factory where needed; avoid blocking sync calls here

builder.Services.AddQuartzServer(opts => { opts.WaitForJobsToComplete = true; });

// Avoid resolving IScheduler synchronously; inject ISchedulerFactory and resolve asynchronously where used

#endregion

builder.Services.AddRebus((configurer, provider) =>
{
    return configurer
        .Logging(l => l.Trace())
        .Options(o =>
        {
            o.EnableCompression(32768);
            o.SetNumberOfWorkers(2);
            o.SetMaxParallelism(20);
        })
        .Transport(t => t.UseInMemoryTransport(new InMemNetwork(), "melodee_bus"))
        .Sagas(s => s.StoreInMemory())
        .Timeouts(t => t.StoreInMemory());
});
builder.Services.AddRebusHandler<AlbumAddEventHandler>();
builder.Services.AddRebusHandler<AlbumRescanEventHandler>();
builder.Services.AddRebusHandler<ArtistRescanEventHandler>();
builder.Services.AddRebusHandler<MelodeeAlbumReprocessEventHandler>();
builder.Services.AddRebusHandler<SearchHistoryEventHandler>();
builder.Services.AddRebusHandler<UserLoginEventHandler>();
builder.Services.AddRebusHandler<UserStreamEventHandler>();

builder.WebHost.UseSetting("DetailedErrors", "true");

builder.Services.AddScoped<IStartupMelodeeConfigurationService, StartupMelodeeConfigurationService>();


var app = builder.Build();

// Use forwarded headers for reverse proxy FIRST (only if enabled)
if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

// Enable response compression early in the pipeline
app.UseResponseCompression();

// Jellyfin API routing - MUST be before UseRouting() to rewrite paths before endpoint selection
// This rewrites paths like /System/Info/Public to /api/jf/System/Info/Public
app.UseMiddleware<JellyfinRoutingMiddleware>();

// Explicit routing - required so JellyfinRoutingMiddleware can rewrite paths BEFORE endpoint selection
app.UseRouting();

// Map the default OpenAPI JSON endpoint (serves at /openapi/v1.json)
app.MapOpenApi();

// Configure Scalar API documentation UI for Melodee API
app.MapScalarApiReference(options =>
{
    options
        .WithTitle("Melodee API Documentation")
        .WithTheme(ScalarTheme.DeepSpace)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
        .HideClientButton = true;
});

// Configure static files with efficient caching - MUST be before UseStatusCodePages
// (addresses Lighthouse: Use efficient cache lifetimes)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Cache static files for 1 year
        const int durationInSeconds = 60 * 60 * 24 * 365; // 1 year
        ctx.Context.Response.Headers.CacheControl = $"public,max-age={durationInSeconds}";

        // Add ETag for better cache validation
        ctx.Context.Response.Headers.ETag = $"\"{ctx.File.LastModified:yyyyMMddHHmmss}\"";

        // Add security headers
        ctx.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        ctx.Context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";

        // Add Content Security Policy (addresses Lighthouse: CSP XSS protection)
        ctx.Context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-eval' 'unsafe-inline'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self'; " +
            "connect-src 'self' wss: ws:; " +
            "media-src 'self'; " +
            "object-src 'none'; " +
            "frame-ancestors 'self';";
    }
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", true);
    app.UseHsts(); // HSTS configured via services above
}

app.UseStatusCodePages(context =>
{
    var request = context.HttpContext.Request;
    // Don't redirect API or song streaming requests - they should return their status codes directly
    if (request.Path.StartsWithSegments("/api") ||
        request.Path.StartsWithSegments("/song") ||
        request.Path.StartsWithSegments("/rest"))
    {
        return Task.CompletedTask;
    }

    // For non-API requests, redirect to error page
    context.HttpContext.Response.Redirect("/Error");
    return Task.CompletedTask;
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Enable HTTPS redirection (addresses Lighthouse: HTTPS issues)
    app.UseHttpsRedirection();
}


#region Scheduling Quartz Jobs with Configuration

var isQuartzDisabled = SafeParser.ToBoolean(builder.Configuration[AppSettingsKeys.QuartzDisabled]);
if (!isQuartzDisabled)
{
    var quartzSchedulerFactory = app.Services.GetRequiredService<ISchedulerFactory>();
    var quartzScheduler = await quartzSchedulerFactory.GetScheduler();
    var melodeeConfigurationFactory = app.Services.GetRequiredService<IMelodeeConfigurationFactory>();
    var melodeeConfiguration = await melodeeConfigurationFactory.GetConfigurationAsync();

    // Register job history listener to track all job executions
    var scopeFactory = app.Services.GetRequiredService<IServiceScopeFactory>();
    quartzScheduler.ListenerManager.AddJobListener(
        new JobHistoryListener(scopeFactory, Log.Logger),
        GroupMatcher<JobKey>.AnyGroup());

    var artistHousekeepingCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsArtistHousekeepingCronExpression);
    if (artistHousekeepingCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<ArtistHousekeepingJob>()
                .WithIdentity(JobKeyRegistry.ArtistHousekeepingJobJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("ArtistHousekeepingJobJobKey-trigger")
                .WithCronSchedule(artistHousekeepingCronExpression!)
                .StartNow()
                .Build());
    }

    var artistSearchEngineHousekeepingCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsArtistSearchEngineHousekeepingCronExpression);
    if (artistSearchEngineHousekeepingCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<ArtistSearchEngineRepositoryHousekeepingJob>()
                .WithIdentity(JobKeyRegistry.ArtistSearchEngineHousekeepingJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("ArtistSearchEngineHousekeepingJob-trigger")
                .WithCronSchedule(artistSearchEngineHousekeepingCronExpression!)
                .StartNow()
                .Build());
    }

    var libraryInboundProcessJobKeyCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsLibraryProcessCronExpression);
    if (libraryInboundProcessJobKeyCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<LibraryInboundProcessJob>()
                .WithIdentity(JobKeyRegistry.LibraryInboundProcessJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("LibraryInboundProcessJob-trigger")
                .UsingJobData(JobMapNameRegistry.ScanStatus, ScanStatus.Idle.ToString())
                .UsingJobData(JobMapNameRegistry.Count, 0)
                .WithCronSchedule(libraryInboundProcessJobKeyCronExpression!)
                .StartNow()
                .Build());
    }

    var libraryInsertCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsLibraryInsertCronExpression);
    if (libraryInsertCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<LibraryInsertJob>()
                .WithIdentity(JobKeyRegistry.LibraryProcessJobJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("LibraryProcessJob-trigger")
                .UsingJobData(JobMapNameRegistry.ScanStatus, ScanStatus.Idle.ToString())
                .UsingJobData(JobMapNameRegistry.Count, 0)
                .WithCronSchedule(libraryInsertCronExpression!)
                .StartNow()
                .Build());
    }

    var musicBrainzUpdateDatabaseCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsMusicBrainzUpdateDatabaseCronExpression);
    if (musicBrainzUpdateDatabaseCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<MusicBrainzUpdateDatabaseJob>()
                .WithIdentity(JobKeyRegistry.MusicBrainzUpdateDatabaseJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("MusicBrainzUpdateDatabaseJob-trigger")
                .WithCronSchedule(musicBrainzUpdateDatabaseCronExpression!)
                .StartNow()
                .Build());
    }

    var nowPlayingCleanupCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsNowPlayingCleanupCronExpression);
    if (nowPlayingCleanupCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<NowPlayingCleanupJob>()
                .WithIdentity(JobKeyRegistry.NowPlayingCleanupJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("NowPlayingCleanupJob-trigger")
                .WithCronSchedule(nowPlayingCleanupCronExpression!)
                .StartNow()
                .Build());
    }

    var chartUpdateCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsChartUpdateCronExpression);
    if (chartUpdateCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<ChartUpdateJob>()
                .WithIdentity(JobKeyRegistry.ChartUpdateJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("ChartUpdateJob-trigger")
                .WithCronSchedule(chartUpdateCronExpression!)
                .StartNow()
                .Build());
    }

    var stagingAutoMoveCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsStagingAutoMoveCronExpression);
    if (stagingAutoMoveCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<StagingAutoMoveJob>()
                .WithIdentity(JobKeyRegistry.StagingAutoMoveJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("StagingAutoMoveJob-trigger")
                .UsingJobData(JobMapNameRegistry.ScanStatus, ScanStatus.Idle.ToString())
                .UsingJobData(JobMapNameRegistry.Count, 0)
                .WithCronSchedule(stagingAutoMoveCronExpression!)
                .StartNow()
                .Build());
    }

    var stagingAlbumRevalidationCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsStagingAlbumRevalidationCronExpression);
    if (stagingAlbumRevalidationCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<StagingAlbumRevalidationJob>()
                .WithIdentity(JobKeyRegistry.StagingAlbumRevalidationJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("StagingAlbumRevalidationJob-trigger")
                .UsingJobData(JobMapNameRegistry.ScanStatus, ScanStatus.Idle.ToString())
                .UsingJobData(JobMapNameRegistry.Count, 0)
                .WithCronSchedule(stagingAlbumRevalidationCronExpression!)
                .StartNow()
                .Build());
    }

    var podcastRefreshCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsPodcastRefreshCronExpression);
    if (podcastRefreshCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<PodcastRefreshJob>()
                .WithIdentity(JobKeyRegistry.PodcastRefreshJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("PodcastRefreshJob-trigger")
                .WithCronSchedule(podcastRefreshCronExpression!)
                .StartNow()
                .Build());
    }

    var podcastDownloadCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsPodcastDownloadCronExpression);
    if (podcastDownloadCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<PodcastDownloadJob>()
                .WithIdentity(JobKeyRegistry.PodcastDownloadJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("PodcastDownloadJob-trigger")
                .WithCronSchedule(podcastDownloadCronExpression!)
                .StartNow()
                .Build());
    }

    var podcastCleanupCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsPodcastCleanupCronExpression);
    if (podcastCleanupCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<PodcastCleanupJob>()
                .WithIdentity(JobKeyRegistry.PodcastCleanupJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("PodcastCleanupJob-trigger")
                .WithCronSchedule(podcastCleanupCronExpression!)
                .StartNow()
                .Build());
    }

    var podcastRecoveryCronExpression = melodeeConfiguration.GetValue<string>(SettingRegistry.JobsPodcastRecoveryCronExpression);
    if (podcastRecoveryCronExpression.Nullify() != null)
    {
        await quartzScheduler.ScheduleJob(
            JobBuilder.Create<PodcastRecoveryJob>()
                .WithIdentity(JobKeyRegistry.PodcastRecoveryJobKey)
                .Build(),
            TriggerBuilder.Create()
                .WithIdentity("PodcastRecoveryJob-trigger")
                .WithCronSchedule(podcastRecoveryCronExpression!)
                .StartNow()
                .Build());
    }
}

#endregion

app.UseCookiePolicy(new CookiePolicyOptions
{
    Secure = CookieSecurePolicy.Always,
    MinimumSameSitePolicy = SameSiteMode.Strict
});

app.UseSerilogRequestLogging(options =>
{
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value ?? string.Empty);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
    };
});

app.UseCors("MelodeeCors");

// Configure request localization with supported cultures
var supportedCultures = new[] { "en-US", "de-DE", "es-ES", "fr-FR", "it-IT", "ja-JP", "pt-BR", "ru-RU", "zh-CN", "ar-SA" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("en-US")
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

// Ensure cookie provider is checked first for culture determination
localizationOptions.RequestCultureProviders.Insert(0,
    new Microsoft.AspNetCore.Localization.CookieRequestCultureProvider());

app.UseRequestLocalization(localizationOptions);

app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Add security headers to all responses
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.UseMelodeeBlazorHeader();

// Map SignalR hub for real-time party updates
app.MapHub<Melodee.Blazor.Hubs.PartyHub>("/party-hub");

app.MapControllers();

// Map health checks for readiness/liveness probes
app.MapHealthChecks("/health");

using (var scope = app.Services.CreateScope())
{
    var configService = scope.ServiceProvider.GetRequiredService<IStartupMelodeeConfigurationService>();
    await configService.UpdateConfigurationFromEnvironmentAsync();
}

app.Run();
