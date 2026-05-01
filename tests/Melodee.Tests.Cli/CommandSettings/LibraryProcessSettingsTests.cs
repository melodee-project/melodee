using FluentAssertions;

namespace Melodee.Tests.Cli.CommandSettings;

/// <summary>
/// Tests for LibraryProcessSettings validation and behavior
/// </summary>
public class LibraryProcessSettingsTests : IDisposable
{
    private readonly string _tempDirectory;

    public LibraryProcessSettingsTests()
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
    public void Validate_WithValidLibraryName_ReturnsSuccess()
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = "TestLibrary"
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyLibraryName_ReturnsError()
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = string.Empty
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Library name is required");
    }

    [Fact]
    public void Validate_WithNullLibraryName_ReturnsError()
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = null!
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Library name is required");
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var settings = new LibraryProcessSettings();

        settings.CopyMode.Should().BeFalse();
        settings.ForceMode.Should().BeFalse();
        settings.ProcessLimit.Should().BeNull();
        settings.PreDiscoveryScript.Should().BeNull();
        settings.Verbose.Should().BeFalse();
        settings.InboundPath.Should().BeNull();
        settings.StagingPath.Should().BeNull();
        settings.IsPathBasedMode.Should().BeFalse();
    }

    [Fact]
    public void CopyMode_CanBeSetToFalse()
    {
        var settings = new LibraryProcessSettings
        {
            CopyMode = false
        };

        settings.CopyMode.Should().BeFalse();
    }

    [Fact]
    public void ForceMode_CanBeSetToFalse()
    {
        var settings = new LibraryProcessSettings
        {
            ForceMode = false
        };

        settings.ForceMode.Should().BeFalse();
    }

    [Fact]
    public void ProcessLimit_CanBeSetToSpecificValue()
    {
        var settings = new LibraryProcessSettings
        {
            ProcessLimit = 100
        };

        settings.ProcessLimit.Should().Be(100);
    }

    [Fact]
    public void ProcessLimit_CanBeSetToZero()
    {
        var settings = new LibraryProcessSettings
        {
            ProcessLimit = 0
        };

        settings.ProcessLimit.Should().Be(0);
    }

    [Fact]
    public void PreDiscoveryScript_CanBeSet()
    {
        var settings = new LibraryProcessSettings
        {
            PreDiscoveryScript = "/path/to/script.sh"
        };

        settings.PreDiscoveryScript.Should().Be("/path/to/script.sh");
    }

    [Theory]
    [InlineData("ValidLibrary")]
    [InlineData("Library with spaces")]
    [InlineData("Library123")]
    [InlineData("UPPERCASE_LIBRARY")]
    [InlineData("lowercase-library")]
    public void Validate_WithVariousValidLibraryNames_ReturnsSuccess(string libraryName)
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = libraryName
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    public void Validate_WithEmptyLibraryName_ReturnsError_Theory(string libraryName)
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = libraryName
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Library name is required");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Validate_WithWhitespaceLibraryNames_PassesValidation(string libraryName)
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = libraryName
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void IsPathBasedMode_WithBothPathsSet_ReturnsTrue()
    {
        var settings = new LibraryProcessSettings
        {
            InboundPath = "/some/inbound/path",
            StagingPath = "/some/staging/path"
        };

        settings.IsPathBasedMode.Should().BeTrue();
    }

    [Fact]
    public void IsPathBasedMode_WithOnlyInboundPath_ReturnsFalse()
    {
        var settings = new LibraryProcessSettings
        {
            InboundPath = "/some/inbound/path",
            StagingPath = null
        };

        settings.IsPathBasedMode.Should().BeFalse();
    }

    [Fact]
    public void IsPathBasedMode_WithOnlyStagingPath_ReturnsFalse()
    {
        var settings = new LibraryProcessSettings
        {
            InboundPath = null,
            StagingPath = "/some/staging/path"
        };

        settings.IsPathBasedMode.Should().BeFalse();
    }

    [Fact]
    public void IsPathBasedMode_WithNoPaths_ReturnsFalse()
    {
        var settings = new LibraryProcessSettings
        {
            InboundPath = null,
            StagingPath = null
        };

        settings.IsPathBasedMode.Should().BeFalse();
    }

    [Fact]
    public void Validate_PathBasedMode_WithValidPaths_ReturnsSuccess()
    {
        var inboundPath = Path.Combine(_tempDirectory, "inbound");
        var stagingPath = Path.Combine(_tempDirectory, "staging");
        Directory.CreateDirectory(inboundPath);
        Directory.CreateDirectory(stagingPath);

        var settings = new LibraryProcessSettings
        {
            InboundPath = inboundPath,
            StagingPath = stagingPath
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Validate_PathBasedMode_WithNonExistentInboundPath_ReturnsError()
    {
        var stagingPath = Path.Combine(_tempDirectory, "staging");
        Directory.CreateDirectory(stagingPath);

        var settings = new LibraryProcessSettings
        {
            InboundPath = "/nonexistent/inbound/path",
            StagingPath = stagingPath
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Inbound path does not exist");
    }

    [Fact]
    public void Validate_PathBasedMode_WithNonExistentStagingPath_ReturnsError()
    {
        var inboundPath = Path.Combine(_tempDirectory, "inbound");
        Directory.CreateDirectory(inboundPath);

        var settings = new LibraryProcessSettings
        {
            InboundPath = inboundPath,
            StagingPath = "/nonexistent/staging/path"
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Staging path does not exist");
    }

    [Fact]
    public void Validate_WithOnlyInboundPath_ReturnsError()
    {
        var inboundPath = Path.Combine(_tempDirectory, "inbound");
        Directory.CreateDirectory(inboundPath);

        var settings = new LibraryProcessSettings
        {
            InboundPath = inboundPath,
            StagingPath = null
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Both --inbound and --staging must be provided together");
    }

    [Fact]
    public void Validate_WithOnlyStagingPath_ReturnsError()
    {
        var stagingPath = Path.Combine(_tempDirectory, "staging");
        Directory.CreateDirectory(stagingPath);

        var settings = new LibraryProcessSettings
        {
            InboundPath = null,
            StagingPath = stagingPath
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Both --inbound and --staging must be provided together");
    }

    [Fact]
    public void Validate_PathBasedMode_IgnoresLibraryName()
    {
        var inboundPath = Path.Combine(_tempDirectory, "inbound");
        var stagingPath = Path.Combine(_tempDirectory, "staging");
        Directory.CreateDirectory(inboundPath);
        Directory.CreateDirectory(stagingPath);

        var settings = new LibraryProcessSettings
        {
            LibraryName = string.Empty,
            InboundPath = inboundPath,
            StagingPath = stagingPath
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }
}
