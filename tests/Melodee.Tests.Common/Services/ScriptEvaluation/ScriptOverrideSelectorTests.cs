using FluentAssertions;
using Melodee.Common.Models.Scripting;
using Melodee.Common.Services.ScriptEvaluation;

namespace Melodee.Tests.Common.Services.ScriptEvaluation;

public class ScriptOverrideSelectorTests
{
    [Fact]
    public void SelectOverride_LibraryAndLongestPrefix_Wins()
    {
        var config = new ScriptConfig
        {
            Overrides =
            [
                new ScriptOverrideConfig
                {
                    LibraryId = 1,
                    PathPrefix = "Incoming/",
                    OnDeny = "skip",
                    Body = "function check(ctx, scriptConfig) { return true; }"
                },
                new ScriptOverrideConfig
                {
                    LibraryId = 1,
                    PathPrefix = "Incoming/Albums/",
                    OnDeny = "delete",
                    Body = "function check(ctx, scriptConfig) { return true; }"
                }
            ]
        };

        var selected = ScriptOverrideSelector.SelectOverride(config, 1, "Incoming/Albums/Test");

        selected.Should().NotBeNull();
        selected!.OnDeny.Should().Be("delete");
        selected.PathPrefix.Should().Be("Incoming/Albums/");
    }

    [Fact]
    public void SelectOverride_LibraryMatch_WinsOverPathOnly()
    {
        var config = new ScriptConfig
        {
            Overrides =
            [
                new ScriptOverrideConfig
                {
                    PathPrefix = "Incoming/",
                    OnDeny = "delete",
                    Body = "function check(ctx, scriptConfig) { return true; }"
                },
                new ScriptOverrideConfig
                {
                    LibraryId = 1,
                    OnDeny = "skip",
                    Body = "function check(ctx, scriptConfig) { return true; }"
                }
            ]
        };

        var selected = ScriptOverrideSelector.SelectOverride(config, 1, "Incoming/Anything");

        selected.Should().NotBeNull();
        selected!.LibraryId.Should().Be(1);
    }

    [Fact]
    public void SelectOverride_PathOnlyLongestPrefix_Wins()
    {
        var config = new ScriptConfig
        {
            Overrides =
            [
                new ScriptOverrideConfig
                {
                    PathPrefix = "Incoming/",
                    OnDeny = "skip",
                    Body = "function check(ctx, scriptConfig) { return true; }"
                },
                new ScriptOverrideConfig
                {
                    PathPrefix = "Incoming/Albums/",
                    OnDeny = "delete",
                    Body = "function check(ctx, scriptConfig) { return true; }"
                }
            ]
        };

        var selected = ScriptOverrideSelector.SelectOverride(config, 99, "Incoming/Albums/Test");

        selected.Should().NotBeNull();
        selected!.PathPrefix.Should().Be("Incoming/Albums/");
    }
}

