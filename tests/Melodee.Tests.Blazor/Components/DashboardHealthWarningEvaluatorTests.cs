using System.Security.Claims;
using FluentAssertions;
using Melodee.Blazor.Components.Pages;
using Melodee.Blazor.Services;
using Melodee.Common.Constants;
using Moq;

namespace Melodee.Tests.Blazor.Components;

public class DashboardHealthWarningEvaluatorTests
{
    [Fact]
    public async Task ShouldShowAsync_WhenUserIsAdminAndDoctorNeedsAttention_ReturnsTrue()
    {
        var doctorService = new Mock<IDoctorService>();
        doctorService.Setup(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await DashboardHealthWarningEvaluator.ShouldShowAsync(CreateUser(isAdmin: true), doctorService.Object);

        result.Should().BeTrue();
        doctorService.Verify(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShouldShowAsync_WhenUserIsAdminAndDoctorIsHealthy_ReturnsFalse()
    {
        var doctorService = new Mock<IDoctorService>();
        doctorService.Setup(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await DashboardHealthWarningEvaluator.ShouldShowAsync(CreateUser(isAdmin: true), doctorService.Object);

        result.Should().BeFalse();
        doctorService.Verify(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShouldShowAsync_WhenUserIsNotAdmin_DoesNotCallDoctor()
    {
        var doctorService = new Mock<IDoctorService>(MockBehavior.Strict);

        var result = await DashboardHealthWarningEvaluator.ShouldShowAsync(CreateUser(isAdmin: false), doctorService.Object);

        result.Should().BeFalse();
        doctorService.Verify(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ClaimsPrincipal CreateUser(bool isAdmin)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "1") };
        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, RoleNameRegistry.Administrator));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}
