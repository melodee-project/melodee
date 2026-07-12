using FluentAssertions;
using Melodee.Common.Configuration;

namespace Melodee.Tests.Common.Configuration;

public class ConfigurationLogRedactorTests
{
    [Theory]
    [InlineData("DB_PASSWORD")]
    [InlineData("ConnectionStrings__DefaultConnection")]
    [InlineData("Jwt__Key")]
    [InlineData("MELODEE_AUTH_TOKEN")]
    [InlineData("searchEngine_spotify_apiKey")]
    [InlineData("AWS_SECRET_ACCESS_KEY")]
    [InlineData("DB_PASSWORD_PATH")]
    [InlineData("register_privateCode")]
    [InlineData("mpd_password")]
    [InlineData("THIRD_PARTY_TOKEN_HOURS")]
    public void RedactValue_SensitiveKey_RedactsValue(string key)
    {
        const string secretValue = "do-not-log-this-secret";

        var result = ConfigurationLogRedactor.RedactValue(key, secretValue);

        result.Should().Be(ConfigurationLogRedactor.RedactedValue);
        result.Should().NotContain(secretValue);
    }

    [Theory]
    [InlineData("DB_MIN_POOL_SIZE", "10")]
    [InlineData("MELODEE_STORAGE_PATH", "/app/storage")]
    [InlineData("ASPNETCORE_ENVIRONMENT", "Production")]
    [InlineData("conversion_enabled", "true")]
    [InlineData("jobs_libraryProcess_cronExpression", "0 */5 * * * ?")]
    [InlineData("MELODEE_IMAGE", "ghcr.io/melodee-project/melodee:2.2.0")]
    [InlineData("MELODEE_AUTH_TOKEN_HOURS", "24")]
    [InlineData("system_baseUrl", "https://music.example.com")]
    public void RedactValue_SafeOperationalKey_PreservesValue(string key, string value)
    {
        var result = ConfigurationLogRedactor.RedactValue(key, value);

        result.Should().Be(value);
    }

    [Fact]
    public void RedactValue_UnrecognizedKey_RedactsValueByDefault()
    {
        const string secretValue = "an-unclassified-value";

        var result = ConfigurationLogRedactor.RedactValue("THIRD_PARTY_CUSTOM_SETTING", secretValue);

        result.Should().Be(ConfigurationLogRedactor.RedactedValue);
    }

    [Fact]
    public void RedactValue_PostgreSqlConnectionString_DoesNotExposePassword()
    {
        const string connectionString =
            "Host=melodee-db;Database=melodeedb;Username=melodeeuser;Password=database-secret";

        var result = ConfigurationLogRedactor.RedactValue("ConnectionStrings__DefaultConnection", connectionString);

        result.Should().Be(ConfigurationLogRedactor.RedactedValue);
        result.Should().NotContain("database-secret");
    }

    [Theory]
    [InlineData("THIRD_PARTY_SUPPORT")]
    [InlineData("THIRD_PARTY_CONVERSION")]
    [InlineData("THIRD_PARTY_GHOST")]
    [InlineData("THIRD_PARTY_PROTOTYPE")]
    public void RedactValue_UnrecognizedKeyEndingWithPartialSafeWord_RedactsValue(string key)
    {
        var result = ConfigurationLogRedactor.RedactValue(key, "unclassified-value");

        result.Should().Be(ConfigurationLogRedactor.RedactedValue);
    }

    [Theory]
    [InlineData("https://admin:password@music.example.com")]
    [InlineData("https://music.example.com?access_token=secret")]
    [InlineData("https://music.example.com/#secret")]
    public void RedactValue_SafeUrlKeyContainingCredentialsOrParameters_RedactsValue(string value)
    {
        var result = ConfigurationLogRedactor.RedactValue("system_baseUrl", value);

        result.Should().Be(ConfigurationLogRedactor.RedactedValue);
    }

    [Fact]
    public void RedactValue_SafeValueContainingLineEnding_EscapesLineEnding()
    {
        var result = ConfigurationLogRedactor.RedactValue("MELODEE_STORAGE_PATH", "/app/storage\nforged-entry");

        result.Should().Be("/app/storage[LF]forged-entry");
    }

    [Fact]
    public void SanitizeKey_KeyContainingLineEnding_EscapesLineEnding()
    {
        var result = ConfigurationLogRedactor.SanitizeKey("MELODEE_STORAGE_PATH\r\nforged-entry");

        result.Should().Be("MELODEE_STORAGE_PATH[CR][LF]forged-entry");
    }
}
