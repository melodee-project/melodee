using FluentAssertions;

namespace Melodee.Tests.Cli;

public class ProjectReferenceTest
{
    [Fact]
    public void CanReference_CliAssembly()
    {
        // This test verifies we can reference the CLI project
        var assembly = typeof(Melodee.Cli.Program).Assembly;
        assembly.Should().NotBeNull();
    }

    [Fact]
    public void CanCreate_LibrarySettings()
    {
        var settings = new Melodee.Cli.CommandSettings.LibrarySettings
        {
            LibraryName = "Test"
        };

        settings.LibraryName.Should().Be("Test");
    }

    [Fact]
    public void CanCreate_ProcessSettings()
    {
        var settings = new Melodee.Cli.CommandSettings.LibraryProcessSettings
        {
            LibraryName = "Test"
        };

        settings.LibraryName.Should().Be("Test");
        // Note: [DefaultValue] attribute is CLI metadata, not actual initialization
        settings.CopyMode.Should().BeFalse(); // bool default is false
    }

    [Fact]
    public void CanValidate_ProcessSettings()
    {
        var settings = new Melodee.Cli.CommandSettings.LibraryProcessSettings
        {
            LibraryName = "ValidName"
        };

        var result = settings.Validate();
        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Validation_Fails_WithEmptyLibraryName()
    {
        var settings = new Melodee.Cli.CommandSettings.LibraryProcessSettings
        {
            LibraryName = ""
        };

        var result = settings.Validate();
        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Library name is required");
    }
}
