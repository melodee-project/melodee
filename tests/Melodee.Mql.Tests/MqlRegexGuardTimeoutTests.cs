using FluentAssertions;
using Melodee.Mql.Security;

namespace Melodee.Mql.Tests;

public class MqlRegexGuardTimeoutTests
{
    [Fact]
    public void SafeMatch_ValidPattern_CompletesSuccessfully()
    {
        var guard = new MqlRegexGuard();
        var pattern = @"^[a-z]+$";
        var testString = "pinkfloyd";

        var result = guard.SafeMatch(pattern, testString, TimeSpan.FromMilliseconds(500));

        result.IsValid.Should().BeTrue();
        result.IsBlocked.Should().BeFalse();
    }

    [Fact]
    public void SafeMatch_WithDefaultTimeout_Uses500Ms()
    {
        var guard = new MqlRegexGuard();
        var pattern = @"^[a-zA-Z0-9_.-]+$";
        var testString = "valid_input-123";

        var result = guard.SafeMatch(pattern, testString);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void SafeMatch_InvalidPatternMapsToMqlRegexInvalid()
    {
        var guard = new MqlRegexGuard();
        var pattern = "[unclosed";
        var testString = "test";

        var result = guard.SafeMatch(pattern, testString, TimeSpan.FromMilliseconds(500));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("MQL_REGEX_INVALID");
    }

    [Fact]
    public void SafeMatch_DangerousPatternMapsToMqlRegexDangerous()
    {
        var guard = new MqlRegexGuard();
        var pattern = "(a+)+b";
        var testString = "aaaaab";

        var result = guard.SafeMatch(pattern, testString, TimeSpan.FromMilliseconds(500));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("MQL_REGEX_DANGEROUS");
    }

    [Fact]
    public void SafeMatch_ProhibitedPatternMapsToMqlRegexProhibited()
    {
        var guard = new MqlRegexGuard();
        var pattern = "(.*)*";
        var testString = "test";

        var result = guard.SafeMatch(pattern, testString, TimeSpan.FromMilliseconds(500));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().BeOneOf("MQL_REGEX_DANGEROUS", "MQL_REGEX_PROHIBITED");
    }

    [Fact]
    public void SafeMatch_EmptyPatternMapsToMqlEmptyPattern()
    {
        var guard = new MqlRegexGuard();
        var pattern = "";
        var testString = "test";

        var result = guard.SafeMatch(pattern, testString, TimeSpan.FromMilliseconds(500));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("MQL_EMPTY_PATTERN");
    }

    [Fact]
    public void SafeMatch_TooLongPatternMapsToMqlRegexTooLong()
    {
        var guard = new MqlRegexGuard();
        var pattern = new string('a', 101);
        var testString = "test";

        var result = guard.SafeMatch(pattern, testString, TimeSpan.FromMilliseconds(500));

        result.IsValid.Should().BeFalse();
        result.ErrorCode.Should().Be("MQL_REGEX_TOO_LONG");
    }
}
