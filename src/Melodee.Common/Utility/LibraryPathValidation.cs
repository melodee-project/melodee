namespace Melodee.Common.Utility;

public static class LibraryPathValidation
{
    public const int RecommendedMaxPathLength = 255;

    public static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (OperatingSystem.IsWindows() && IsUncPath(expanded))
        {
            return true;
        }

        return Path.IsPathRooted(expanded);
    }

    public static bool IsUncPath(string path)
    {
        return path.StartsWith(@"\\", StringComparison.Ordinal);
    }

    public static bool ContainsTraversal(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        var normalized = expanded.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Any(segment => segment is "." or "..");
    }

    public static bool TryNormalizePath(string path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string GetCanonicalPath(string path)
    {
        if (!TryNormalizePath(path, out var normalized))
        {
            return path;
        }

        return ResolveSymlinkTarget(normalized);
    }

    public static string NormalizeForComparison(string path)
    {
        var canonical = GetCanonicalPath(path);
        var collapsed = canonical.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        return collapsed.TrimEnd(Path.DirectorySeparatorChar);
    }

    public static bool PathsOverlap(string path1, string path2, StringComparison comparison)
    {
        if (string.IsNullOrWhiteSpace(path1) || string.IsNullOrWhiteSpace(path2))
        {
            return false;
        }

        var normalizedPath1 = NormalizeForComparison(path1);
        var normalizedPath2 = NormalizeForComparison(path2);

        if (string.Equals(normalizedPath1, normalizedPath2, comparison))
        {
            return true;
        }

        var separator = Path.DirectorySeparatorChar;
        if (normalizedPath1.Length > normalizedPath2.Length)
        {
            return normalizedPath1.StartsWith(normalizedPath2 + separator, comparison);
        }

        return normalizedPath2.StartsWith(normalizedPath1 + separator, comparison);
    }

    public static bool IsPathLengthRecommended(string path)
    {
        if (!TryNormalizePath(path, out var normalized))
        {
            return false;
        }

        return normalized.Length <= RecommendedMaxPathLength;
    }

    public static StringComparison GetPathComparison()
    {
        return OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private static string ResolveSymlinkTarget(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var dirInfo = new DirectoryInfo(path);
                return ResolveLinkTarget(dirInfo) ?? dirInfo.FullName;
            }

            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                return ResolveLinkTarget(fileInfo) ?? fileInfo.FullName;
            }
        }
        catch
        {
            return path;
        }

        return path;
    }

    private static string? ResolveLinkTarget(FileSystemInfo info)
    {
        if (!info.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            return info.FullName;
        }

        try
        {
            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName;
        }
        catch
        {
            return info.FullName;
        }
    }
}
