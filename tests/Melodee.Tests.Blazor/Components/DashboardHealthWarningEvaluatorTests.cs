using System.Security.Claims;
using FluentAssertions;
using Melodee.Blazor.Components.Pages;
using Melodee.Common.Constants;
using Moq;
using BlazorDoctorService = Melodee.Blazor.Services.IDoctorService;
using DoctorCheckResult = Melodee.Common.Services.Doctor.DoctorCheckResult;

namespace Melodee.Tests.Blazor.Components;

public class DashboardHealthWarningEvaluatorTests
{
    [Fact]
    public async Task ShouldShowAsync_WhenUserIsAdminAndDoctorNeedsAttention_ReturnsTrue()
    {
        var doctorService = new Mock<BlazorDoctorService>();
        doctorService.Setup(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await DashboardHealthWarningEvaluator.ShouldShowAsync(CreateUser(isAdmin: true), doctorService.Object);

        result.Should().BeTrue();
        doctorService.Verify(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShouldShowAsync_WhenUserIsAdminAndDoctorIsHealthy_ReturnsFalse()
    {
        var doctorService = new Mock<BlazorDoctorService>();
        doctorService.Setup(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await DashboardHealthWarningEvaluator.ShouldShowAsync(CreateUser(isAdmin: true), doctorService.Object);

        result.Should().BeFalse();
        doctorService.Verify(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ShouldShowAsync_WhenUserIsNotAdmin_DoesNotCallDoctor()
    {
        var doctorService = new Mock<BlazorDoctorService>(MockBehavior.Strict);

        var result = await DashboardHealthWarningEvaluator.ShouldShowAsync(CreateUser(isAdmin: false), doctorService.Object);

        result.Should().BeFalse();
        doctorService.Verify(service => service.NeedsAttentionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetIssuesAsync_WhenUserIsAdmin_ReturnsAttentionChecks()
    {
        var expected = new[]
        {
            new DoctorCheckResult("MusicBrainzDatabase", false, "unsupported DecentDB file format version 11", TimeSpan.Zero)
        };
        var doctorService = new Mock<BlazorDoctorService>();
        doctorService.Setup(service => service.GetAttentionChecksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var result = await DashboardHealthWarningEvaluator.GetIssuesAsync(CreateUser(isAdmin: true), doctorService.Object);

        result.Should().BeEquivalentTo(expected);
        doctorService.Verify(service => service.GetAttentionChecksAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetIssuesAsync_WhenUserIsNotAdmin_DoesNotCallDoctor()
    {
        var doctorService = new Mock<BlazorDoctorService>(MockBehavior.Strict);

        var result = await DashboardHealthWarningEvaluator.GetIssuesAsync(CreateUser(isAdmin: false), doctorService.Object);

        result.Should().BeEmpty();
        doctorService.Verify(service => service.GetAttentionChecksAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void HasUnsupportedDecentDbIssue_WhenMusicBrainzUnsupportedFormat_ReturnsTrue()
    {
        var issues = new[]
        {
            new DoctorCheckResult(
                "MusicBrainzDatabase",
                false,
                "MusicBrainz DecentDB database uses a file format that is not supported by the current DecentDB provider.",
                TimeSpan.Zero)
        };

        var result = DashboardHealthWarningEvaluator.HasUnsupportedDecentDbIssue(issues);

        result.Should().BeTrue();
    }

    [Fact]
    public void HasUnsupportedDecentDbIssue_WhenDifferentDoctorIssue_ReturnsFalse()
    {
        var issues = new[]
        {
            new DoctorCheckResult("PostgresDatabase", false, "Unable to connect to the primary database", TimeSpan.Zero)
        };

        var result = DashboardHealthWarningEvaluator.HasUnsupportedDecentDbIssue(issues);

        result.Should().BeFalse();
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
