using Melodee.Common.Data;
using Melodee.Common.Data.Models;
using Melodee.Common.Models.OpenSubsonic.Requests;
using Melodee.Common.Services.Caching;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using Serilog;

namespace Melodee.Common.Services;

/// <summary>
/// Service for identifying devices/players from HTTP requests.
/// Handles identification for OpenSubsonic, Jellyfin, and native APIs.
/// </summary>
public class DeviceIdentificationService(
    ILogger logger,
    ICacheManager cacheManager,
    IDbContextFactory<MelodeeDbContext> contextFactory)
    : ServiceBase(logger, cacheManager, contextFactory)
{
    private const string HeaderJellyfinClient = "X-Emby-Client";
    private const string HeaderJellyfinDeviceId = "X-Emby-Device-Id";
    private const string HeaderJellyfinDeviceName = "X-Emby-Device-Name";
    private const string HeaderMelodeeDeviceId = "X-Melodee-Device-Id";
    private const string HeaderUserAgent = "User-Agent";

    /// <summary>
    /// Identify or create a player from an OpenSubsonic API request
    /// </summary>
    public async Task<Player> GetOrCreatePlayerFromSubsonicAsync(
        int userId,
        ApiRequest apiRequest,
        CancellationToken cancellationToken = default)
    {
        // OpenSubsonic uses the 'c' parameter for client name
        var clientName = apiRequest.ApiRequestPlayer.Client ?? "Unknown";
        var userAgent = apiRequest.ApiRequestPlayer.UserAgent;
        var ipAddress = apiRequest.IpAddress;

        return await GetOrCreatePlayerAsync(userId, clientName, userAgent, ipAddress, cancellationToken);
    }

    /// <summary>
    /// Identify or create a player from Jellyfin request headers
    /// </summary>
    public async Task<Player> GetOrCreatePlayerFromJellyfinAsync(
        int userId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var client = httpContext.Request.Headers[HeaderJellyfinClient].ToString();
        var deviceId = httpContext.Request.Headers[HeaderJellyfinDeviceId].ToString();
        var deviceName = httpContext.Request.Headers[HeaderJellyfinDeviceName].ToString();
        var userAgent = httpContext.Request.Headers[HeaderUserAgent].ToString();
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        // Use deviceId + client as stable identifier
        var clientIdentifier = string.IsNullOrWhiteSpace(deviceId)
            ? client
            : $"{client}-{deviceId}";

        var name = string.IsNullOrWhiteSpace(deviceName) ? clientIdentifier : deviceName;

        return await GetOrCreatePlayerAsync(userId, clientIdentifier, userAgent, ipAddress, cancellationToken, name);
    }

    /// <summary>
    /// Identify or create a player from native Melodee API request
    /// </summary>
    public async Task<Player> GetOrCreatePlayerFromNativeAsync(
        int userId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var deviceId = httpContext.Request.Headers[HeaderMelodeeDeviceId].ToString();
        var userAgent = httpContext.Request.Headers[HeaderUserAgent].ToString();
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();

        // If no device ID provided, generate one based on user agent + IP
        var clientIdentifier = string.IsNullOrWhiteSpace(deviceId)
            ? $"web-{GetStableHashFromString($"{userAgent}-{ipAddress}")}"
            : deviceId;

        return await GetOrCreatePlayerAsync(userId, clientIdentifier, userAgent, ipAddress, cancellationToken);
    }

    /// <summary>
    /// Get or create a player record with the given identifier
    /// </summary>
    private async Task<Player> GetOrCreatePlayerAsync(
        int userId,
        string client,
        string? userAgent,
        string? ipAddress,
        CancellationToken cancellationToken,
        string? name = null)
    {
        await using var scopedContext = await ContextFactory.CreateDbContextAsync(cancellationToken);

        // Try to find existing player
        var player = await scopedContext.Players
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Client == client, cancellationToken);

        var now = SystemClock.Instance.GetCurrentInstant();

        if (player != null)
        {
            // Update last seen and other fields
            player.LastSeenAt = now;
            player.UserAgent = userAgent;
            player.IpAddress = ipAddress;

            await scopedContext.SaveChangesAsync(cancellationToken);

            Logger.Debug("[{ServiceName}] Updated player [{PlayerId}] [{Client}] for user [{UserId}]",
                nameof(DeviceIdentificationService), player.Id, client, userId);
        }
        else
        {
            // Create new player
            player = new Player
            {
                UserId = userId,
                Client = client,
                Name = name ?? client,
                UserAgent = userAgent,
                IpAddress = ipAddress,
                LastSeenAt = now,
                ScrobbleEnabled = true,
                CreatedAt = now
            };

            scopedContext.Players.Add(player);
            await scopedContext.SaveChangesAsync(cancellationToken);

            Logger.Information("[{ServiceName}] Created new player [{PlayerId}] [{Client}] for user [{UserId}]",
                nameof(DeviceIdentificationService), player.Id, client, userId);
        }

        return player;
    }

    /// <summary>
    /// Generate a stable hash from a string for fallback device IDs
    /// </summary>
    private static string GetStableHashFromString(string input)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input ?? string.Empty));
        return Convert.ToHexString(hashBytes)[..16]; // Take first 16 characters
    }
}
