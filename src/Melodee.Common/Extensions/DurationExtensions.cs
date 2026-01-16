using NodaTime;

namespace Melodee.Common.Extensions;

public static class DurationExtensions
{
    public static string ToDurationString(this NodaTime.Duration duration)
    {
        var ts = duration.ToTimeSpan();
        return ts.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture);
    }
}