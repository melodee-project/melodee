using System.Collections;
using System.Diagnostics;
using Melodee.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace Melodee.Common.Configuration;

public sealed class MelodeeConfigurationFactory(IDbContextFactory<MelodeeDbContext> contextFactory)
    : IMelodeeConfigurationFactory
{
    private static readonly Lazy<Dictionary<string, object?>> EnvironmentVariables = new(() =>
        Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .ToDictionary(
                entry => entry.Key.ToString()!,
                entry => entry.Value,
                StringComparer.OrdinalIgnoreCase));

    private IMelodeeConfiguration? _configuration;

    /// <summary>
    /// Event raised when configuration has been reset and components should reload their configuration.
    /// </summary>
    public event EventHandler? ConfigurationChanged;

    public void Reset()
    {
        _configuration = null;

        // Notify all subscribers that configuration has changed
        ConfigurationChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IMelodeeConfiguration> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        if (_configuration == null)
        {
            await using (var scopedContext = await contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false))
            {
                var settings = await scopedContext
                    .Settings
                    .ToDictionaryAsync(
                        x => x.Key,
                        object? (x) => x.Value,
                        StringComparer.OrdinalIgnoreCase,
                        cancellationToken)
                    .ConfigureAwait(false);

                _configuration = new MelodeeConfiguration(UpdateWithEnvironmentVariables(settings));
            }
        }

        return _configuration!;
    }

    public static bool IsSetViaEnvironmentVariable(string key)
    {
        return EnvironmentVariablesSettings().ContainsKey(key);
    }

    public static Dictionary<string, object?> EnvironmentVariablesSettings()
    {
        return EnvironmentVariables.Value;
    }


    public static Dictionary<string, object?> UpdateWithEnvironmentVariables(Dictionary<string, object?> settings)
    {
        var allEnvVars = EnvironmentVariablesSettings();
        foreach (var (key, value) in allEnvVars)
        {
            var kk = key.Replace("_", ".");
            var keyForLogging = ConfigurationLogRedactor.SanitizeKey(kk);
            var valueForLogging = ConfigurationLogRedactor.RedactValue(key, value);
            if (settings.ContainsKey(kk) && settings[kk] != value)
            {
                settings[kk] = value;
                Trace.WriteLine($"[{nameof(MelodeeConfigurationFactory)}] Overriding setting [{keyForLogging}] with environment variable value [{valueForLogging}]");
            }
            else
            {
                settings.Add(kk, value);
                Trace.WriteLine($"[{nameof(MelodeeConfigurationFactory)}] Added setting [{keyForLogging}] with environment variable value [{valueForLogging}]");
            }
        }

        return settings;
    }
}
