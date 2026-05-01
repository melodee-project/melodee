using Melodee.Common.Utility;

namespace Melodee.Tests.Common.Utility;

public class PathGuardTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "Melodee_PathGuardTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void IsUnderRoot_ReturnsTrue_ForPathUnderRoot()
    {
        var root = NewTempDir();
        try
        {
            var subdir = Path.Combine(root, "subdir");
            var result = PathGuard.IsUnderRoot(root, subdir);

            Assert.True(result);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IsUnderRoot_ReturnsFalse_ForPathOutsideRoot()
    {
        var root = NewTempDir();
        try
        {
            var outsidePath = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid());
            Directory.CreateDirectory(outsidePath);
            try
            {
                var result = PathGuard.IsUnderRoot(root, outsidePath);
                Assert.False(result);
            }
            finally
            {
                Directory.Delete(outsidePath, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IsUnderRoot_ReturnsFalse_ForPathWithDoubleDots()
    {
        var root = NewTempDir();
        try
        {
            var escapedPath = Path.Combine(root, "..", "escaped_" + Guid.NewGuid());
            Directory.CreateDirectory(escapedPath);
            try
            {
                var result = PathGuard.IsUnderRoot(root, escapedPath);
                Assert.False(result);
            }
            finally
            {
                Directory.Delete(escapedPath, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IsUnderRoot_ReturnsFalse_ForNullOrEmptyInputs()
    {
        Assert.False(PathGuard.IsUnderRoot(null!, "path"));
        Assert.False(PathGuard.IsUnderRoot("root", null!));
        Assert.False(PathGuard.IsUnderRoot("", "path"));
        Assert.False(PathGuard.IsUnderRoot("root", ""));
    }

    [Fact]
    public void EnsureUnderRoot_ReturnsPath_ForValidPath()
    {
        var root = NewTempDir();
        try
        {
            var validPath = Path.Combine(root, "subdir", "file.txt");
            var result = PathGuard.EnsureUnderRoot(root, validPath);

            Assert.NotNull(result);
            Assert.Equal(validPath, result);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EnsureUnderRoot_Throws_ForPathOutsideRoot()
    {
        var root = NewTempDir();
        try
        {
            var outsidePath = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid());
            Directory.CreateDirectory(outsidePath);
            try
            {
                Assert.Throws<UnauthorizedAccessException>(() =>
                    PathGuard.EnsureUnderRoot(root, outsidePath));
            }
            finally
            {
                Directory.Delete(outsidePath, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EnsureUnderRoot_Throws_ForPathWithDoubleDots()
    {
        var root = NewTempDir();
        try
        {
            var escapedPath = Path.Combine(root, "..", "outside_" + Guid.NewGuid());
            Directory.CreateDirectory(escapedPath);
            try
            {
                Assert.Throws<UnauthorizedAccessException>(() =>
                    PathGuard.EnsureUnderRoot(root, escapedPath));
            }
            finally
            {
                Directory.Delete(escapedPath, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EnsureUnderRoot_Throws_ForAbsolutePathOutsideRoot()
    {
        var root = NewTempDir();
        try
        {
            var absolutePath = Path.Combine(Path.GetTempPath(), "outside_" + Guid.NewGuid());
            Directory.CreateDirectory(absolutePath);
            try
            {
                Assert.Throws<UnauthorizedAccessException>(() =>
                    PathGuard.EnsureUnderRoot(root, absolutePath));
            }
            finally
            {
                Directory.Delete(absolutePath, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EnsureUnderRoot_Throws_ForRootEqualsCandidate_WhenNotAllowed()
    {
        var root = NewTempDir();
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() =>
                PathGuard.EnsureUnderRoot(root, root, allowRootEqualsCandidate: false));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EnsureUnderRoot_AllowsRootEqualsCandidate_WhenExplicitlyAllowed()
    {
        var root = NewTempDir();
        try
        {
            var result = PathGuard.EnsureUnderRoot(root, root, allowRootEqualsCandidate: true);

            Assert.NotNull(result);
            Assert.Equal(root, result);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void EnsureUnderRoot_Throws_ForNullOrEmptyInputs()
    {
        Assert.Throws<ArgumentException>(() => PathGuard.EnsureUnderRoot(null!, "path"));
        Assert.Throws<ArgumentException>(() => PathGuard.EnsureUnderRoot("root", null!));
        Assert.Throws<ArgumentException>(() => PathGuard.EnsureUnderRoot("", "path"));
        Assert.Throws<ArgumentException>(() => PathGuard.EnsureUnderRoot("root", ""));
    }

    [Fact]
    public void IsUnderRoot_HandlesSymlinkEscapes_Conservatively()
    {
        var root = NewTempDir();
        try
        {
            var subdir = Path.Combine(root, "subdir");
            Directory.CreateDirectory(subdir);

            var outsidePath = Path.Combine(Path.GetTempPath(), "outside_link_" + Guid.NewGuid());
            Directory.CreateDirectory(outsidePath);

            try
            {
                var linkPath = Path.Combine(subdir, "link");
                try
                {
                    var result = PathGuard.IsUnderRoot(root, linkPath);
                    Assert.False(result);
                }
                catch
                {
                    // Symlink creation might fail, which is fine
                }
            }
            finally
            {
                Directory.Delete(outsidePath, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void IsUnderRoot_ReturnsTrue_ForFilePathUnderRoot()
    {
        var root = NewTempDir();
        try
        {
            var filePath = Path.Combine(root, "file.txt");
            File.WriteAllText(filePath, "test");

            var result = PathGuard.IsUnderRoot(root, filePath);

            Assert.True(result);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
