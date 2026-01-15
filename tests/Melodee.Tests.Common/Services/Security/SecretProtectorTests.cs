using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Services.Security;
using Moq;

namespace Melodee.Tests.Common.Services.Security;

public class SecretProtectorTests
{
    private const string TestSecretKey = "ThisIsAVeryLongSecretKeyForTesting1234567890";
    private readonly ISecretProtector _protector;

    public SecretProtectorTests()
    {
        var mockConfiguration = new Mock<IMelodeeConfiguration>();
        mockConfiguration.Setup(c => c.GetValue<string>(SettingRegistry.SecuritySecretKey, null))
            .Returns(TestSecretKey);

        var mockConfigFactory = new Mock<IMelodeeConfigurationFactory>();
        mockConfigFactory.Setup(f => f.GetConfigurationAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockConfiguration.Object);

        _protector = new SecretProtector(mockConfigFactory.Object);
    }

    [Fact]
    public void Protect_ReturnsFormattedString()
    {
        var secret = "my-secret-value";
        var protectedData = _protector.Protect(secret);

        Assert.NotNull(protectedData);
        Assert.NotEmpty(protectedData);
        Assert.StartsWith("v1:gcm:", protectedData);
    }

    [Fact]
    public void Unprotect_RestoresOriginalSecret()
    {
        var originalSecret = "my-original-secret-12345";
        var protectedData = _protector.Protect(originalSecret);
        var restoredSecret = _protector.Unprotect(protectedData);

        Assert.Equal(originalSecret, restoredSecret);
    }

    [Fact]
    public void Protect_ProducesDifferentOutput_ForSameInput()
    {
        var secret = "same-secret";
        var protected1 = _protector.Protect(secret);
        var protected2 = _protector.Protect(secret);

        Assert.NotEqual(protected1, protected2);
    }

    [Fact]
    public void Unprotect_Fails_ForTamperedCiphertext()
    {
        var secret = "original-secret";
        var protectedData = _protector.Protect(secret);

        var chars = protectedData.ToCharArray();
        chars[10] = chars[10] == 'A' ? 'B' : 'A';
        var tamperedData = new string(chars);

        Assert.ThrowsAny<Exception>(() => _protector.Unprotect(tamperedData));
    }

    [Fact]
    public void Unprotect_Fails_ForInvalidPrefix()
    {
        var invalidFormat = "invalid-prefix:" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("data"));

        Assert.Throws<ArgumentException>(() => _protector.Unprotect(invalidFormat));
    }

    [Fact]
    public void Unprotect_Fails_ForInvalidBase64()
    {
        var invalidData = "v1:gcm:not-valid-base64!!!";

        Assert.Throws<FormatException>(() => _protector.Unprotect(invalidData));
    }

    [Fact]
    public void Protect_Throws_ForNullSecret()
    {
        Assert.Throws<ArgumentException>(() => _protector.Protect(null!));
    }

    [Fact]
    public void Protect_Throws_ForEmptySecret()
    {
        Assert.Throws<ArgumentException>(() => _protector.Protect(string.Empty));
    }

    [Fact]
    public void Unprotect_Throws_ForNullData()
    {
        Assert.Throws<ArgumentException>(() => _protector.Unprotect(null!));
    }

    [Fact]
    public void Unprotect_Throws_ForEmptyData()
    {
        Assert.Throws<ArgumentException>(() => _protector.Unprotect(string.Empty));
    }

    [Fact]
    public void Unprotect_Fails_ForTruncatedData()
    {
        var secret = "secret";
        var protectedData = _protector.Protect(secret);
        var truncatedData = protectedData.Substring(0, protectedData.Length - 5);

        Assert.ThrowsAny<Exception>(() => _protector.Unprotect(truncatedData));
    }

    [Fact]
    public void Roundtrip_Works_ForLongSecrets()
    {
        var longSecret = new string('x', 1000);
        var protectedData = _protector.Protect(longSecret);
        var restored = _protector.Unprotect(protectedData);

        Assert.Equal(longSecret, restored);
    }

    [Fact]
    public void Roundtrip_Works_ForSpecialCharacters()
    {
        var specialSecret = "secret!@#$%^&*()_+-=[]{}|;':\",./<>?`~";
        var protectedData = _protector.Protect(specialSecret);
        var restored = _protector.Unprotect(protectedData);

        Assert.Equal(specialSecret, restored);
    }

    [Fact]
    public void Roundtrip_Works_ForUnicodeCharacters()
    {
        var unicodeSecret = "secret-日本語-emoji-🔐-café";
        var protectedData = _protector.Protect(unicodeSecret);
        var restored = _protector.Unprotect(protectedData);

        Assert.Equal(unicodeSecret, restored);
    }
}
