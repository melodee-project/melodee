using System.Security.Claims;
using Melodee.Common.Configuration;
using Melodee.Common.Constants;
using Melodee.Common.Models;
using Moq;
using Xunit;

namespace Melodee.Tests.Common.Models;

public class UserInfoClaimsTests
{
    private const string PasswordEncryptedClaimType = "passwordencrypted";

    [Fact]
    public void ToClaimsPrincipal_DoesNotIncludePasswordEncryptedClaim()
    {
        var userInfo = new UserInfo(
            Id: 1,
            ApiKey: Guid.NewGuid(),
            UserName: "testuser",
            Email: "test@example.com",
            PublicKey: "test-public-key",
            TimeZoneId: "UTC",
            PasswordEncrypted: "encrypted-password-value"
        );

        var mockConfig = new Mock<IMelodeeConfiguration>();
        var claims = userInfo.ToClaimsPrincipal(mockConfig.Object, "/avatars").Claims.ToList();

        var passwordEncryptedClaim = claims.FirstOrDefault(c => c.Type == PasswordEncryptedClaimType);
        Assert.Null(passwordEncryptedClaim);

        Assert.DoesNotContain(claims, c => c.Value.Contains("encrypted-password"));
    }

    [Fact]
    public void ToClaimsPrincipal_DoesNotIncludeUserTokenDerivedFromPassword()
    {
        var userInfo = new UserInfo(
            Id: 1,
            ApiKey: Guid.NewGuid(),
            UserName: "testuser",
            Email: "test@example.com",
            PublicKey: "test-public-key",
            TimeZoneId: "UTC",
            PasswordEncrypted: "secret-encrypted-password"
        );

        var mockConfig = new Mock<IMelodeeConfiguration>();
        var claims = userInfo.ToClaimsPrincipal(mockConfig.Object, "/avatars").Claims.ToList();

        Assert.DoesNotContain(claims, c => c.Type == ClaimTypeRegistry.UserToken);
    }

    [Fact]
    public void FromClaimsPrincipal_DoesNotThrow_ForLegacyPasswordEncryptedClaim()
    {
        var claimsPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.PrimarySid, "1"),
            new Claim(ClaimTypes.Sid, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.Name, "testuser"),
            new Claim(ClaimTypes.Email, "test@example.com"),
            new Claim(ClaimTypeRegistry.UserPublicKey, "public-key"),
            new Claim(ClaimTypeRegistry.UserTimeZoneId, "UTC"),
            new Claim(PasswordEncryptedClaimType, "should-be-ignored")
        }, "Melodee"));

        var userInfo = UserInfo.FromClaimsPrincipal(claimsPrincipal);

        Assert.NotNull(userInfo);
    }

    [Fact]
    public void ToClaimsPrincipal_IncludesExpectedStandardClaims()
    {
        var userInfo = new UserInfo(
            Id: 42,
            ApiKey: Guid.Parse("12345678-1234-1234-1234-123456789abc"),
            UserName: "testuser",
            Email: "test@example.com",
            PublicKey: "public-key-123",
            TimeZoneId: "America/New_York"
        );

        var mockConfig = new Mock<IMelodeeConfiguration>();
        var claims = userInfo.ToClaimsPrincipal(mockConfig.Object, "/avatars").Claims.ToList();

        Assert.Equal("42", claims.First(c => c.Type == ClaimTypes.PrimarySid).Value);
        Assert.Equal("12345678-1234-1234-1234-123456789abc", claims.First(c => c.Type == ClaimTypes.Sid).Value);
        Assert.Equal("testuser", claims.First(c => c.Type == ClaimTypes.Name).Value);
        Assert.Equal("test@example.com", claims.First(c => c.Type == ClaimTypes.Email).Value);
        Assert.Equal("public-key-123", claims.First(c => c.Type == ClaimTypeRegistry.UserPublicKey).Value);
        Assert.Equal("America/New_York", claims.First(c => c.Type == ClaimTypeRegistry.UserTimeZoneId).Value);
        Assert.NotNull(claims.First(c => c.Type == ClaimTypeRegistry.UserSalt).Value);
    }
}
