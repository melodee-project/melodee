using Melodee.Common.Services.Security;

namespace Melodee.Tests.Common.Services.Security;

public class PasswordHashServiceTests
{
    private readonly IPasswordHashService _passwordHashService;

    public PasswordHashServiceTests()
    {
        _passwordHashService = new PasswordHashService();
    }

    [Fact]
    public void Hash_ReturnsBCryptHashString()
    {
        var password = "testPassword123";
        var hash = _passwordHashService.Hash(password);

        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.StartsWith("$2a$", hash);
        Assert.True(hash.Length > 50);
    }

    [Fact]
    public void Hash_SamePasswordProducesDifferentHashes_DueToRandomSalt()
    {
        var password = "testPassword123";
        var hash1 = _passwordHashService.Hash(password);
        var hash2 = _passwordHashService.Hash(password);

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Verify_ReturnsTrue_ForCorrectPassword()
    {
        var password = "correctPassword";
        var hash = _passwordHashService.Hash(password);

        var result = _passwordHashService.Verify(password, hash);

        Assert.True(result);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForIncorrectPassword()
    {
        var password = "correctPassword";
        var wrongPassword = "wrongPassword";
        var hash = _passwordHashService.Hash(password);

        var result = _passwordHashService.Verify(wrongPassword, hash);

        Assert.False(result);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForNullPassword()
    {
        var hash = _passwordHashService.Hash("anyPassword");

        var result = _passwordHashService.Verify(null!, hash);

        Assert.False(result);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForNullHash()
    {
        var password = "anyPassword";

        var result = _passwordHashService.Verify(password, null!);

        Assert.False(result);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForEmptyPassword()
    {
        var hash = _passwordHashService.Hash("anyPassword");

        var result = _passwordHashService.Verify(string.Empty, hash);

        Assert.False(result);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForEmptyHash()
    {
        var password = "anyPassword";

        var result = _passwordHashService.Verify(password, string.Empty);

        Assert.False(result);
    }

    [Fact]
    public void Verify_ReturnsFalse_ForInvalidHash()
    {
        var password = "anyPassword";
        var invalidHash = "not-a-valid-bcrypt-hash";

        var result = _passwordHashService.Verify(password, invalidHash);

        Assert.False(result);
    }

    [Fact]
    public void Hash_ThrowsException_ForNullPassword()
    {
        Assert.Throws<ArgumentException>(() => _passwordHashService.Hash(null!));
    }

    [Fact]
    public void Hash_ThrowsException_ForEmptyPassword()
    {
        Assert.Throws<ArgumentException>(() => _passwordHashService.Hash(string.Empty));
    }

    [Fact]
    public void Hash_ThrowsException_ForWhitespacePassword()
    {
        Assert.Throws<ArgumentException>(() => _passwordHashService.Hash("   "));
    }

    [Fact]
    public void Verify_CanVerifyHashCreatedByDifferentInstance()
    {
        var password = "sharedPassword";
        var hash1 = new PasswordHashService().Hash(password);
        var service2 = new PasswordHashService();

        var result = service2.Verify(password, hash1);

        Assert.True(result);
    }
}
