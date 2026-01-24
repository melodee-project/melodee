using Melodee.Common.Models.Scripting;

namespace Melodee.Common.Services.ScriptEvaluation;

public static class ScriptOverrideSelector
{
    public static ScriptOverrideConfig? SelectOverride(
        ScriptConfig config,
        int libraryId,
        string relativePath)
    {
        var normalizedRelativePath = NormalizePath(relativePath);
        var candidates = config.Overrides.Where(o => o.Enabled).ToList();

        if (!candidates.Any())
        {
            return null;
        }

        var libraryMatches = candidates
            .Where(o => o.LibraryId == libraryId)
            .ToList();

        var pathMatches = candidates
            .Where(o => !string.IsNullOrEmpty(o.PathPrefix) &&
                        normalizedRelativePath.StartsWith(NormalizePath(o.PathPrefix!), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var libraryMatchWithPath = libraryMatches
            .Where(o => !string.IsNullOrEmpty(o.PathPrefix) &&
                        normalizedRelativePath.StartsWith(NormalizePath(o.PathPrefix!), StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (libraryMatchWithPath.Any())
        {
            return libraryMatchWithPath
                .OrderByDescending(o => o.PathPrefix?.Length ?? 0)
                .First();
        }

        if (libraryMatches.Any())
        {
            return libraryMatches.First();
        }

        if (pathMatches.Any())
        {
            return pathMatches
                .OrderByDescending(o => o.PathPrefix?.Length ?? 0)
                .First();
        }

        return null;
    }

    private static string NormalizePath(string path)
    {
        return path
            .Replace('\\', '/')
            .TrimStart('/')
            .Trim();
    }
}
