namespace Melodee.Common.Models.Scripting;

public static class DirectoryContextHelpers
{
    public static bool DetectTrackNumberGaps(IEnumerable<int> trackNumbers)
    {
        var sorted = trackNumbers.OrderBy(x => x).Distinct().ToList();
        if (!sorted.Any() || sorted[0] != 1)
        {
            return true;
        }

        for (int i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] != sorted[i - 1] + 1)
            {
                return true;
            }
        }

        return false;
    }

    public static double CalculateDurationMinutes(TimeSpan duration)
    {
        return Math.Round(duration.TotalMinutes, 2);
    }
}
