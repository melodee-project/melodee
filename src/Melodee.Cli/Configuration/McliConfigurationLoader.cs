using System.Runtime.InteropServices;
using System.Text.Json;

namespace Melodee.Cli.Configuration;

/// <summary>
/// Loads mcli configuration from the standard config file location.
/// </summary>
public static class McliConfigurationLoader
{
    private const string ConfigFileName = "mcli.json";
    private const string ConfigDirectoryName = "melodee";

    /// <summary>
    /// Get the platform-specific configuration directory path.
    /// Linux/macOS: ~/.config/melodee/
    /// Windows: %APPDATA%\melodee\
    /// </summary>
    public static string GetConfigDirectory()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, ConfigDirectoryName);
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config");
            return Path.Combine(configHome, ConfigDirectoryName);
        }
    }

    /// <summary>
    /// Get the full path to the configuration file.
    /// </summary>
    public static string GetConfigFilePath()
    {
        return Path.Combine(GetConfigDirectory(), ConfigFileName);
    }

    /// <summary>
    /// Load configuration from the standard location.
    /// Returns an empty configuration if the file doesn't exist.
    /// </summary>
    public static McliConfiguration Load()
    {
        var configPath = GetConfigFilePath();

        if (!File.Exists(configPath))
        {
            return new McliConfiguration();
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<McliConfiguration>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return config ?? new McliConfiguration();
        }
        catch
        {
            // If we can't parse the config, return empty rather than failing
            return new McliConfiguration();
        }
    }

    /// <summary>
    /// Save configuration to the standard location.
    /// Creates the directory if it doesn't exist.
    /// </summary>
    public static void Save(McliConfiguration configuration)
    {
        var configDir = GetConfigDirectory();
        Directory.CreateDirectory(configDir);

        var configPath = GetConfigFilePath();
        var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(configPath, json);
    }
}
