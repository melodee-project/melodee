using FluentAssertions;

namespace Melodee.Tests.Cli.CommandSettings;

/// <summary>
/// Tests for UserDeleteSettings validation and behavior
/// </summary>
public class UserDeleteSettingsTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var settings = new UserDeleteSettings();

        settings.UserId.Should().Be(0);
        settings.SkipConfirmation.Should().BeFalse();
        settings.Verbose.Should().BeFalse();
    }

    [Fact]
    public void UserId_CanBeSet()
    {
        var settings = new UserDeleteSettings
        {
            UserId = 42
        };

        settings.UserId.Should().Be(42);
    }

    [Fact]
    public void SkipConfirmation_CanBeSetToTrue()
    {
        var settings = new UserDeleteSettings
        {
            UserId = 1,
            SkipConfirmation = true
        };

        settings.SkipConfirmation.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(999999)]
    public void UserId_AcceptsVariousPositiveValues(int userId)
    {
        var settings = new UserDeleteSettings
        {
            UserId = userId
        };

        settings.UserId.Should().Be(userId);
    }

    [Fact]
    public void AllSettings_CanBeCombined()
    {
        var settings = new UserDeleteSettings
        {
            UserId = 123,
            SkipConfirmation = true,
            Verbose = true
        };

        settings.UserId.Should().Be(123);
        settings.SkipConfirmation.Should().BeTrue();
        settings.Verbose.Should().BeTrue();
    }
}
