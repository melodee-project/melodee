using FluentAssertions;

namespace Melodee.Tests.Cli;

/// <summary>
/// Fixed command tests that should compile without complex dependencies
/// </summary>
public class FixedCommandTests
{
    [Fact]
    public void LibrarySettings_DefaultValues_AreCorrect()
    {
        var settings = new LibrarySettings();

        settings.LibraryName.Should().BeNull();
        // Note: [DefaultValue] attribute is CLI metadata, not actual initialization
        settings.Verbose.Should().BeFalse(); // bool default is false
    }

    [Fact]
    public void LibraryProcessSettings_DefaultValues_AreCorrect()
    {
        var settings = new LibraryProcessSettings();

        // Note: [DefaultValue] attribute is CLI metadata, not actual initialization
        settings.CopyMode.Should().BeFalse(); // bool default is false
        settings.ForceMode.Should().BeFalse(); // bool default is false
        settings.ProcessLimit.Should().BeNull();
        settings.PreDiscoveryScript.Should().BeNull();
    }

    [Fact]
    public void LibraryProcessSettings_WithValidName_PassesValidation()
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = "TestLibrary"
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void LibraryProcessSettings_WithEmptyName_FailsValidation()
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = ""
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Library name is required");
    }

    [Fact]
    public void ConfigurationSetSetting_DefaultValues_AreCorrect()
    {
        var settings = new ConfigurationSetSetting();

        // Note: [DefaultValue] attribute is CLI metadata, not actual initialization
        settings.Verbose.Should().BeFalse(); // bool default is false
        settings.Remove.Should().BeFalse();
        settings.Key.Should().Be(string.Empty);
        settings.Value.Should().Be(string.Empty);
    }

    [Fact]
    public void ConfigurationSetSetting_PropertiesCanBeSet()
    {
        var settings = new ConfigurationSetSetting
        {
            Key = "TestKey",
            Value = "TestValue",
            Remove = true,
            Verbose = false
        };

        settings.Key.Should().Be("TestKey");
        settings.Value.Should().Be("TestValue");
        settings.Remove.Should().BeTrue();
        settings.Verbose.Should().BeFalse();
    }

    [Fact]
    public void ProcessInboundCommand_CanBeCreated()
    {
        var command = new ProcessInboundCommand();

        command.Should().NotBeNull();
    }

    [Fact]
    public void ConfigurationSetCommand_CanBeCreated()
    {
        var command = new ConfigurationSetCommand();

        command.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Library1")]
    [InlineData("My Library")]
    [InlineData("LIBRARY_123")]
    [InlineData("test-library")]
    public void LibraryProcessSettings_WithValidNames_PassesValidation(string libraryName)
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = libraryName
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void LibraryProcessSettings_ProcessLimit_CanBeSet()
    {
        var settings = new LibraryProcessSettings
        {
            LibraryName = "Test",
            ProcessLimit = 100
        };

        settings.ProcessLimit.Should().Be(100);
        settings.LibraryName.Should().Be("Test");
    }
}
