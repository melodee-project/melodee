using FluentAssertions;

namespace Melodee.Tests.Blazor.Services;

public class MainLayoutProxyServiceTests
{
    [Fact]
    public void SetSpinnerVisible_WhenStateChanges_RaisesSpinnerVisibleChanged()
    {
        var service = new MainLayoutProxyService();
        var eventCount = 0;
        service.SpinnerVisibleChanged += (_, _) => eventCount++;

        service.SetSpinnerVisible(true);

        service.ShowSpinner.Should().BeTrue();
        eventCount.Should().Be(1);
    }

    [Fact]
    public void SetSpinnerVisible_WhenStateDoesNotChange_DoesNotRaiseSpinnerVisibleChanged()
    {
        var service = new MainLayoutProxyService();
        var eventCount = 0;
        service.SetSpinnerVisible(true);
        service.SpinnerVisibleChanged += (_, _) => eventCount++;

        service.SetSpinnerVisible(true);

        service.ShowSpinner.Should().BeTrue();
        eventCount.Should().Be(0);
    }

    [Fact]
    public void ToggleSpinnerVisible_WithForceState_UsesSetStateSemantics()
    {
        var service = new MainLayoutProxyService();
        var eventCount = 0;
        service.SpinnerVisibleChanged += (_, _) => eventCount++;

        service.ToggleSpinnerVisible(true);
        service.ToggleSpinnerVisible(false);

        service.ShowSpinner.Should().BeFalse();
        eventCount.Should().Be(2);
    }
}
