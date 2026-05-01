namespace Melodee.Cli.Configuration;

/// <summary>
/// Resolved remote mode options after applying precedence rules:
/// 1. Command line flags (--server, --token, --profile)
/// 2. Environment variables (MELODEE_SERVER, MELODEE_TOKEN, MELODEE_PROFILE)
/// 3. Config file profile
/// </summary>
public class RemoteModeOptions
{
    public string? Server { get; set; }
    public string? Token { get; set; }
    public bool IsRemoteMode => !string.IsNullOrWhiteSpace(Server);

    /// <summary>
    /// Resolve remote mode options from multiple sources with proper precedence.
    /// </summary>
    public static RemoteModeOptions Resolve(
        string? cliServer,
        string? cliToken,
        string? cliProfile)
    {
        // Check environment variables
        var envServer = Environment.GetEnvironmentVariable("MELODEE_SERVER");
        var envToken = Environment.GetEnvironmentVariable("MELODEE_TOKEN");
        var envProfile = Environment.GetEnvironmentVariable("MELODEE_PROFILE");

        // Determine which profile to use (CLI > Env > Config default)
        var profileName = cliProfile ?? envProfile;

        // Load config file
        var config = McliConfigurationLoader.Load();

        // If no explicit profile specified, use default from config
        if (string.IsNullOrWhiteSpace(profileName) && config.Defaults?.Profile != null)
        {
            profileName = config.Defaults.Profile;
        }

        // Get profile from config
        McliProfile? profile = null;
        if (!string.IsNullOrWhiteSpace(profileName) && config.Profiles.TryGetValue(profileName, out var p))
        {
            profile = p;
        }

        // Apply precedence: CLI > Env > Profile
        var resolvedServer = cliServer ?? envServer ?? profile?.Server;
        var resolvedToken = cliToken ?? envToken ?? profile?.Token;

        return new RemoteModeOptions
        {
            Server = resolvedServer,
            Token = resolvedToken
        };
    }

    /// <summary>
    /// Normalize the server URL to ensure it's ready for API calls.
    /// Removes trailing slashes and ensures proper base URL format.
    /// </summary>
    public string GetNormalizedBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(Server))
        {
            throw new InvalidOperationException("Server URL is not set");
        }

        var url = Server.TrimEnd('/');

        // Ensure we don't have /api/v1 already appended
        if (url.EndsWith("/api/v1", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^7]; // Remove /api/v1
        }
        else if (url.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4]; // Remove /api
        }

        return url;
    }

    /// <summary>
    /// Get the API base URL (server + /api/v1)
    /// </summary>
    public string GetApiBaseUrl()
    {
        return $"{GetNormalizedBaseUrl()}/api/v1";
    }

    /// <summary>
    /// Mask the token for display purposes.
    /// Format: ********-****-****-****-************
    /// </summary>
    public static string MaskToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        // If it looks like a GUID, mask it with the standard GUID format
        if (Guid.TryParse(token, out _))
        {
            return "********-****-****-****-************";
        }

        // Otherwise, just show first and last 4 characters
        if (token.Length <= 8)
        {
            return new string('*', token.Length);
        }

        return $"{token[..4]}{'*'.ToString().PadLeft(token.Length - 8, '*')}{token[^4..]}";
    }
}
