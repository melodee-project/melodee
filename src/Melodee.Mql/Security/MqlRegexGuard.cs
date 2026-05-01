using System.Text.RegularExpressions;

namespace Melodee.Mql.Security;

/// <summary>
/// Default implementation of regex pattern guards with ReDoS protection.
/// </summary>
public sealed class MqlRegexGuard : IMqlRegexGuard
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(500);
    private const int MaxPatternLength = 100;
    private const int MaxTestStringLength = 10000;

    private static readonly string[] ProhibitedPatterns = new[]
    {
        @"(.*)*",
        @"(.*)+",
        @"(.+)+",
        @"(\d*)*",
        @"(\d+)+",
        @"([a-z]*)*",
        @"([a-z]+)+",
        @"([A-Z]*)*",
        @"([A-Z]+)+",
        @"(\w*)*",
        @"(\w+)+",
        @"(.?)*",
        @"(.?)+",
        @"(a+)+",
        @"(aa+)+",
        @"(aaa+)+",
        @"(a{2,})+",
        @"(a{1,3})+",
        @"(x+x+)+y",
        @"(x+x+y)+",
        @"(a?)+",
        @"(a*)+",
        @"(ab*)*c",
        @"(ab*)+c",
        @"((a+)?)+",
        @"((a*)?)+",
        @"((a{1,3}){1,3})+",
        @"(a+|b*)*c",
        @"(a+|b+)+c",
        @"(a|b)*c",
        @"(a|b)+c",
        @"((?:a|b)+)+",
        @"(?:a+|b*)*",
        @"(?:a+|b+)?",
        @"(?=a+)+",
        @"(?!a+)+",
        @"(?<=a+)+",
        @"(?<!a+)+"
    };

    private static readonly Regex ProhibitedRegex;

    static MqlRegexGuard()
    {
        var pattern = string.Join("|", ProhibitedPatterns.Select(EscapeForRegex));
        ProhibitedRegex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    public RegexValidationResult ValidatePattern(string pattern)
    {
        var startTime = DateTime.UtcNow;

        if (string.IsNullOrEmpty(pattern))
        {
            return new RegexValidationResult
            {
                IsValid = false,
                IsBlocked = true,
                ErrorCode = "MQL_EMPTY_PATTERN",
                ErrorMessage = "Regex pattern cannot be empty",
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }

        if (pattern.Length > MaxPatternLength)
        {
            return new RegexValidationResult
            {
                IsValid = false,
                IsBlocked = true,
                ErrorCode = "MQL_REGEX_TOO_LONG",
                ErrorMessage = $"Regex pattern exceeds maximum length of {MaxPatternLength} characters",
                SafePattern = pattern[..MaxPatternLength],
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }

        if (MqlTextSanitizer.ContainsRedosPattern(pattern))
        {
            return new RegexValidationResult
            {
                IsValid = false,
                IsBlocked = true,
                ErrorCode = "MQL_REGEX_DANGEROUS",
                ErrorMessage = "Regex pattern contains potential ReDoS vulnerability",
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }

        if (ProhibitedRegex.IsMatch(pattern))
        {
            return new RegexValidationResult
            {
                IsValid = false,
                IsBlocked = true,
                ErrorCode = "MQL_REGEX_PROHIBITED",
                ErrorMessage = "Regex pattern matches prohibited patterns",
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }

        try
        {
            var options = GetRegexOptions();
            _ = new Regex(pattern, options, TimeSpan.FromMilliseconds(100));

            var safePattern = MqlTextSanitizer.SanitizeForRegex(pattern);

            return new RegexValidationResult
            {
                IsValid = true,
                IsBlocked = false,
                SafePattern = safePattern,
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
        catch (ArgumentException ex)
        {
            return new RegexValidationResult
            {
                IsValid = false,
                IsBlocked = true,
                ErrorCode = "MQL_REGEX_INVALID",
                ErrorMessage = $"Invalid regex pattern: {ex.Message}",
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
    }

    public RegexValidationResult SafeMatch(string pattern, string testString, TimeSpan? timeout = null)
    {
        var actualTimeout = timeout ?? DefaultTimeout;
        var startTime = DateTime.UtcNow;

        var validationResult = ValidatePattern(pattern);
        if (!validationResult.IsValid)
        {
            return validationResult;
        }

        if (string.IsNullOrEmpty(testString) || testString.Length > MaxTestStringLength)
        {
            testString = testString?.Length > MaxTestStringLength
                ? testString[..MaxTestStringLength]
                : string.Empty;
        }

        try
        {
            var options = GetRegexOptions();
            var regex = new Regex(pattern, options, actualTimeout);
            var match = regex.Match(testString);

            return new RegexValidationResult
            {
                IsValid = true,
                IsBlocked = false,
                SafePattern = validationResult.SafePattern,
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
        catch (RegexMatchTimeoutException)
        {
            return new RegexValidationResult
            {
                IsValid = false,
                IsBlocked = true,
                ErrorCode = "MQL_REGEX_TIMEOUT",
                ErrorMessage = $"Regex evaluation exceeded timeout of {actualTimeout.TotalMilliseconds}ms",
                SafePattern = validationResult.SafePattern,
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
        catch (ArgumentException ex)
        {
            return new RegexValidationResult
            {
                IsValid = false,
                IsBlocked = true,
                ErrorCode = "MQL_REGEX_INVALID",
                ErrorMessage = $"Invalid regex pattern: {ex.Message}",
                SafePattern = validationResult.SafePattern,
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
        catch (Exception ex)
        {
            return new RegexValidationResult
            {
                IsValid = false,
                IsBlocked = true,
                ErrorCode = "MQL_REGEX_ERROR",
                ErrorMessage = $"Error during regex evaluation: {ex.Message}",
                SafePattern = validationResult.SafePattern,
                EvaluationTimeMs = (long)(DateTime.UtcNow - startTime).TotalMilliseconds
            };
        }
    }

    private static RegexOptions GetRegexOptions()
    {
        var options = RegexOptions.Compiled | RegexOptions.IgnoreCase;

        if (RegexOptions.NonBacktracking.GetType()
                .GetField("value__", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
                .GetValue(RegexOptions.NonBacktracking) != null)
        {
            options |= RegexOptions.NonBacktracking;
        }

        return options;
    }

    private static string EscapeForRegex(string pattern)
    {
        return Regex.Escape(pattern);
    }
}
