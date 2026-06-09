namespace Melodee.Benchmarks;

internal sealed class MusicBrainzProbeOptions
{
    private readonly Dictionary<string, string?> values;

    private MusicBrainzProbeOptions(Dictionary<string, string?> values)
    {
        this.values = values;
    }

    public static MusicBrainzProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = arg[2..];
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[i + 1];
                i++;
            }
            else
            {
                values[key] = "true";
            }
        }

        return new MusicBrainzProbeOptions(values);
    }

    public string? GetString(string key) =>
        values.TryGetValue(key, out var value) ? value : null;

    public string RequireString(string key)
    {
        var value = GetString(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"Missing required --{key} argument.");
        }

        return value;
    }

    public bool GetBool(string key) =>
        values.TryGetValue(key, out var value) &&
        (value is null || bool.TryParse(value, out var result) && result);

    public int GetInt32(string key, int defaultValue)
    {
        var value = GetString(key);
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
