namespace Melodee.Common.Utility;

/// <summary>
/// Provides security checks for file path containment to prevent path traversal attacks.
/// All destructive file operations (delete, move) should use these guards before performing operations.
/// </summary>
public static class PathGuard
{
    /// <summary>
    /// Ensures that the candidate path is under the specified root directory.
    /// Returns the full normalized path if safe, or throws if the path would escape the root.
    /// </summary>
    /// <param name="root">The allowed root directory.</param>
    /// <param name="candidatePath">The path to validate.</param>
    /// <param name="allowRootEqualsCandidate">If true, allows the candidate to equal the root (for recursive operations).</param>
    /// <returns>The full normalized path under the root.</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when the path is outside the root.</exception>
    public static string EnsureUnderRoot(string root, string candidatePath, bool allowRootEqualsCandidate = false)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Root path cannot be null or empty.", nameof(root));
        }

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            throw new ArgumentException("Candidate path cannot be null or empty.", nameof(candidatePath));
        }

        var rootFullPath = Path.GetFullPath(root);
        var candidateFullPath = Path.GetFullPath(candidatePath);

        var normalizedRoot = rootFullPath.TrimEnd(Path.DirectorySeparatorChar);
        var normalizedCandidate = candidateFullPath.TrimEnd(Path.DirectorySeparatorChar);

        if (!normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Path '{candidateFullPath}' is not under the allowed root '{rootFullPath}'.");
        }

        if (!allowRootEqualsCandidate && string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException(
                $"Path '{candidateFullPath}' equals the root directory and is not allowed for this operation.");
        }

        return candidateFullPath;
    }

    /// <summary>
    /// Checks if the candidate path is under the specified root directory.
    /// </summary>
    /// <param name="root">The allowed root directory.</param>
    /// <param name="candidatePath">The path to validate.</param>
    /// <returns>True if the path is under the root; otherwise, false.</returns>
    public static bool IsUnderRoot(string root, string candidatePath)
    {
        if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(candidatePath))
        {
            return false;
        }

        try
        {
            var rootFullPath = Path.GetFullPath(root);
            var candidateFullPath = Path.GetFullPath(candidatePath);

            var normalizedRoot = rootFullPath.TrimEnd(Path.DirectorySeparatorChar);
            var normalizedCandidate = candidateFullPath.TrimEnd(Path.DirectorySeparatorChar);

            return normalizedCandidate.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalizedRoot, normalizedCandidate, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
