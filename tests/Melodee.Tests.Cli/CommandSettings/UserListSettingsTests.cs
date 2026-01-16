using FluentAssertions;

namespace Melodee.Tests.Cli.CommandSettings;

/// <summary>
/// Tests for UserListSettings validation and behavior
/// </summary>
public class UserListSettingsTests
{
    [Fact]
    public void DefaultValues_AreSetCorrectly()
    {
        var settings = new UserListSettings();

        settings.Limit.Should().Be(50);
        settings.ReturnRaw.Should().BeFalse();
        settings.Verbose.Should().BeFalse();
    }

    [Fact]
    public void Limit_CanBeSetToCustomValue()
    {
        var settings = new UserListSettings
        {
            Limit = 100
        };

        settings.Limit.Should().Be(100);
    }

    [Fact]
    public void ReturnRaw_CanBeSetToTrue()
    {
        var settings = new UserListSettings
        {
            ReturnRaw = true
        };

        settings.ReturnRaw.Should().BeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(1000)]
    public void Limit_AcceptsVariousValues(int limit)
    {
        var settings = new UserListSettings
        {
            Limit = limit
        };

        settings.Limit.Should().Be(limit);
    }

    [Fact]
    public void AllSettings_CanBeCombined()
    {
        var settings = new UserListSettings
        {
            Limit = 25,
            ReturnRaw = true,
            Verbose = true
        };

        settings.Limit.Should().Be(25);
        settings.ReturnRaw.Should().BeTrue();
        settings.Verbose.Should().BeTrue();
    }
}
