using FluentAssertions;

namespace Melodee.Tests.Cli;

public class WorkingTests
{
    [Fact]
    public void BasicTest_ShouldPass()
    {
        var result = 2 + 2;
        result.Should().Be(4);
    }

    [Fact]
    public void LibraryProcessSettings_CanBeCreated()
    {
        var settings = new Melodee.Cli.CommandSettings.LibraryProcessSettings
        {
            LibraryName = "TestLibrary"
        };

        settings.LibraryName.Should().Be("TestLibrary");
    }

    [Fact]
    public void LibraryProcessSettings_Validation_RequiresLibraryName()
    {
        var settings = new Melodee.Cli.CommandSettings.LibraryProcessSettings
        {
            LibraryName = ""
        };

        var result = settings.Validate();
        result.Successful.Should().BeFalse();
    }

    [Fact]
    public void ConfigurationSetCommand_CanBeCreated()
    {
        var command = new Melodee.Cli.Command.ConfigurationSetCommand();
        command.Should().NotBeNull();
    }

    [Fact]
    public void ProcessInboundCommand_CanBeCreated()
    {
        var command = new Melodee.Cli.Command.ProcessInboundCommand();
        command.Should().NotBeNull();
    }
}
