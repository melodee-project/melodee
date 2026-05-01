using FluentAssertions;

namespace Melodee.Tests.Cli.CommandSettings;

/// <summary>
/// Tests for LibraryMoveOkSettings validation and behavior
/// </summary>
public class LibraryMoveOkSettingsTests : IDisposable
{
    private readonly string _tempDirectory;

    public LibraryMoveOkSettingsTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"melodee_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, true);
        }
    }

    [Fact]
    public void Validate_WithValidLibraryNames_ReturnsSuccess()
    {
        var settings = new LibraryMoveOkSettings
        {
            LibraryName = "SourceLibrary",
            ToLibraryName = "DestinationLibrary"
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyLibraryName_ReturnsError()
    {
        var settings = new LibraryMoveOkSettings
        {
            LibraryName = string.Empty,
            ToLibraryName = "DestinationLibrary"
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Library name is required");
    }

    [Fact]
    public void Validate_WithEmptyToLibraryName_ReturnsError()
    {
        var settings = new LibraryMoveOkSettings
        {
            LibraryName = "SourceLibrary",
            ToLibraryName = string.Empty
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("To library name is required");
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var settings = new LibraryMoveOkSettings();

        settings.LibraryName.Should().BeNull();
        settings.ToLibraryName.Should().BeNull();
        settings.FromPath.Should().BeNull();
        settings.ToPath.Should().BeNull();
        settings.IsPathBasedMode.Should().BeFalse();
        settings.Verbose.Should().BeFalse();
    }

    [Fact]
    public void IsPathBasedMode_WithBothPathsSet_ReturnsTrue()
    {
        var settings = new LibraryMoveOkSettings
        {
            FromPath = "/some/from/path",
            ToPath = "/some/to/path"
        };

        settings.IsPathBasedMode.Should().BeTrue();
    }

    [Fact]
    public void IsPathBasedMode_WithOnlyFromPath_ReturnsFalse()
    {
        var settings = new LibraryMoveOkSettings
        {
            FromPath = "/some/from/path",
            ToPath = null
        };

        settings.IsPathBasedMode.Should().BeFalse();
    }

    [Fact]
    public void IsPathBasedMode_WithOnlyToPath_ReturnsFalse()
    {
        var settings = new LibraryMoveOkSettings
        {
            FromPath = null,
            ToPath = "/some/to/path"
        };

        settings.IsPathBasedMode.Should().BeFalse();
    }

    [Fact]
    public void IsPathBasedMode_WithNoPaths_ReturnsFalse()
    {
        var settings = new LibraryMoveOkSettings
        {
            FromPath = null,
            ToPath = null
        };

        settings.IsPathBasedMode.Should().BeFalse();
    }

    [Fact]
    public void Validate_PathBasedMode_WithValidPaths_ReturnsSuccess()
    {
        var fromPath = Path.Combine(_tempDirectory, "from");
        var toPath = Path.Combine(_tempDirectory, "to");
        Directory.CreateDirectory(fromPath);
        Directory.CreateDirectory(toPath);

        var settings = new LibraryMoveOkSettings
        {
            FromPath = fromPath,
            ToPath = toPath
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Validate_PathBasedMode_WithNonExistentFromPath_ReturnsError()
    {
        var toPath = Path.Combine(_tempDirectory, "to");
        Directory.CreateDirectory(toPath);

        var settings = new LibraryMoveOkSettings
        {
            FromPath = "/nonexistent/from/path",
            ToPath = toPath
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("From path does not exist");
    }

    [Fact]
    public void Validate_PathBasedMode_WithNonExistentToPath_ReturnsError()
    {
        var fromPath = Path.Combine(_tempDirectory, "from");
        Directory.CreateDirectory(fromPath);

        var settings = new LibraryMoveOkSettings
        {
            FromPath = fromPath,
            ToPath = "/nonexistent/to/path"
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("To path does not exist");
    }

    [Fact]
    public void Validate_PathBasedMode_WithSamePaths_ReturnsError()
    {
        var samePath = Path.Combine(_tempDirectory, "same");
        Directory.CreateDirectory(samePath);

        var settings = new LibraryMoveOkSettings
        {
            FromPath = samePath,
            ToPath = samePath
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("From path and To path cannot be the same");
    }

    [Fact]
    public void Validate_WithOnlyFromPath_ReturnsError()
    {
        var fromPath = Path.Combine(_tempDirectory, "from");
        Directory.CreateDirectory(fromPath);

        var settings = new LibraryMoveOkSettings
        {
            FromPath = fromPath,
            ToPath = null
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Both --from-path and --to-path must be provided together");
    }

    [Fact]
    public void Validate_WithOnlyToPath_ReturnsError()
    {
        var toPath = Path.Combine(_tempDirectory, "to");
        Directory.CreateDirectory(toPath);

        var settings = new LibraryMoveOkSettings
        {
            FromPath = null,
            ToPath = toPath
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Both --from-path and --to-path must be provided together");
    }

    [Fact]
    public void Validate_PathBasedMode_IgnoresLibraryNames()
    {
        var fromPath = Path.Combine(_tempDirectory, "from");
        var toPath = Path.Combine(_tempDirectory, "to");
        Directory.CreateDirectory(fromPath);
        Directory.CreateDirectory(toPath);

        var settings = new LibraryMoveOkSettings
        {
            LibraryName = string.Empty,
            ToLibraryName = string.Empty,
            FromPath = fromPath,
            ToPath = toPath
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Theory]
    [InlineData("ValidSource", "ValidDestination")]
    [InlineData("Source with spaces", "Destination with spaces")]
    [InlineData("Source123", "Dest456")]
    public void Validate_WithVariousValidLibraryNames_ReturnsSuccess(string sourceName, string destName)
    {
        var settings = new LibraryMoveOkSettings
        {
            LibraryName = sourceName,
            ToLibraryName = destName
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }
}
