using FluentAssertions;

namespace Melodee.Tests.Cli.CommandSettings;

/// <summary>
/// Tests for UserCreateSettings validation and behavior
/// </summary>
public class UserCreateSettingsTests
{
    [Fact]
    public void Validate_WithValidData_ReturnsSuccess()
    {
        var settings = new UserCreateSettings
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = "Password123!"
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithEmptyUsername_ReturnsError()
    {
        var settings = new UserCreateSettings
        {
            Username = string.Empty,
            Email = "test@example.com",
            Password = "Password123!"
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Username is required");
    }

    [Fact]
    public void Validate_WithWhitespaceUsername_ReturnsError()
    {
        var settings = new UserCreateSettings
        {
            Username = "   ",
            Email = "test@example.com",
            Password = "Password123!"
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Username is required");
    }

    [Fact]
    public void Validate_WithEmptyEmail_ReturnsError()
    {
        var settings = new UserCreateSettings
        {
            Username = "testuser",
            Email = string.Empty,
            Password = "Password123!"
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Email is required");
    }

    [Fact]
    public void Validate_WithWhitespaceEmail_ReturnsError()
    {
        var settings = new UserCreateSettings
        {
            Username = "testuser",
            Email = "   ",
            Password = "Password123!"
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Email is required");
    }

    [Fact]
    public void Validate_WithEmptyPassword_ReturnsError()
    {
        var settings = new UserCreateSettings
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = string.Empty
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Password is required");
    }

    [Fact]
    public void Validate_WithShortPassword_ReturnsError()
    {
        var settings = new UserCreateSettings
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = "short"
        };

        var result = settings.Validate();

        result.Successful.Should().BeFalse();
        result.Message.Should().Contain("Password must be at least 8 characters");
    }

    [Fact]
    public void Validate_WithExactly8CharPassword_ReturnsSuccess()
    {
        var settings = new UserCreateSettings
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = "12345678"
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var settings = new UserCreateSettings();

        settings.Username.Should().Be(string.Empty);
        settings.Email.Should().Be(string.Empty);
        settings.Password.Should().Be(string.Empty);
        settings.Force.Should().BeFalse();
        settings.Verbose.Should().BeFalse();
    }

    [Theory]
    [InlineData("user1")]
    [InlineData("test_user")]
    [InlineData("User-Name")]
    [InlineData("user.name")]
    public void Validate_WithVariousValidUsernames_ReturnsSuccess(string username)
    {
        var settings = new UserCreateSettings
        {
            Username = username,
            Email = "test@example.com",
            Password = "Password123!"
        };

        var result = settings.Validate();

        result.Successful.Should().BeTrue();
    }

    [Fact]
    public void Force_CanBeSetToTrue()
    {
        var settings = new UserCreateSettings
        {
            Username = "testuser",
            Email = "test@example.com",
            Password = "Password123!",
            Force = true
        };

        settings.Force.Should().BeTrue();
    }
}
