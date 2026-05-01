using FluentAssertions;

namespace Melodee.Tests.Cli.Commands;

/// <summary>
/// Tests for LibraryValidateCommand and LibraryValidateSettings
/// </summary>
public class LibraryValidateCommandTests
{
    #region Settings Tests

    [Fact]
    public void LibraryValidateSettings_DefaultValues_AreCorrect()
    {
        var settings = new LibraryValidateSettings
        {
            LibraryName = "TestLibrary"
        };

        settings.Verbose.Should().BeFalse();
        settings.Json.Should().BeFalse();
        settings.Fix.Should().BeFalse();
        settings.LibraryName.Should().Be("TestLibrary");
    }

    [Fact]
    public void LibraryValidateSettings_FixMode_CanBeEnabled()
    {
        var settings = new LibraryValidateSettings
        {
            LibraryName = "Storage",
            Fix = true
        };

        settings.Fix.Should().BeTrue();
    }

    [Fact]
    public void LibraryValidateSettings_JsonMode_CanBeEnabled()
    {
        var settings = new LibraryValidateSettings
        {
            LibraryName = "Storage",
            Json = true
        };

        settings.Json.Should().BeTrue();
    }

    [Theory]
    [InlineData("Storage")]
    [InlineData("My Music Library")]
    [InlineData("library_01")]
    public void LibraryValidateSettings_LibraryName_AcceptsValidNames(string libraryName)
    {
        var settings = new LibraryValidateSettings
        {
            LibraryName = libraryName
        };

        settings.LibraryName.Should().Be(libraryName);
    }

    [Fact]
    public void LibraryValidateSettings_AllOptionsEnabled_ConfiguresCorrectly()
    {
        var settings = new LibraryValidateSettings
        {
            LibraryName = "TestLib",
            Verbose = true,
            Json = true,
            Fix = true
        };

        settings.LibraryName.Should().Be("TestLib");
        settings.Verbose.Should().BeTrue();
        settings.Json.Should().BeTrue();
        settings.Fix.Should().BeTrue();
    }

    [Fact]
    public void LibraryValidateSettings_Validate_WithValidLibraryName_ReturnsSuccess()
    {
        var settings = new LibraryValidateSettings
        {
            LibraryName = "Storage"
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void LibraryValidateSettings_Validate_WithEmptyLibraryName_ReturnsError()
    {
        var settings = new LibraryValidateSettings
        {
            LibraryName = string.Empty
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Library name is required");
    }

    [Fact]
    public void LibraryValidateSettings_Validate_WithNullLibraryName_ReturnsError()
    {
        var settings = new LibraryValidateSettings();

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
    }

    #endregion
}
