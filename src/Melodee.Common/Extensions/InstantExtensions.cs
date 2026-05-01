using NodaTime;

namespace Melodee.Common.Extensions;

public static class InstantExtensions
{
    public static string ToEtag(this Instant instant)
    {
        return instant.ToUnixTimeTicks().ToString();
    }

    public static string ToIso8601String(this Instant instant)
    {
        return instant.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", null);
    }

    public static string? ToIso8601String(this Instant? instant)
    {
        return instant?.ToString("yyyy-MM-ddTHH:mm:ss.fffffff'Z'", null);
    }
}
