using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class ActivationPlannerTests
{
    // uri null = no URI at all. A cold-start key press arrives as ProtocolForResults and is parsed the same way.
    [Theory]
    [InlineData(ActivationKind.Protocol, "hotline://key?state=Tap", false, KeyEvent.Tap, false, false, true)]
    [InlineData(ActivationKind.ProtocolForResults, "hotline://key?state=Down", true, KeyEvent.HoldStart, false, false, true)]
    // any web page can launch hotline://open: show the popup, never read the selection
    [InlineData(ActivationKind.Protocol, "hotline://open", false, null, true, false, false)]
    [InlineData(ActivationKind.Protocol, null, false, null, true, false, false)]
    [InlineData(ActivationKind.Protocol, "hotline://answer", false, null, true, false, false)] // answer notification click
    [InlineData(ActivationKind.Protocol, "hotline://settings", false, null, false, true, true)]
    [InlineData(ActivationKind.Protocol, "hotline://tray", false, null, false, false, true)]
    [InlineData(ActivationKind.ProtocolForResults, "hotline://tray", true, null, false, false, true)]
    [InlineData(ActivationKind.StartupTask, null, true, null, false, false, true)] // sign-in startup stays in the tray
    [InlineData(ActivationKind.Launch, null, true, null, true, false, true)]
    [InlineData(ActivationKind.Launch, null, false, null, true, false, true)]
    [InlineData(ActivationKind.Other, null, false, null, true, false, true)]
    [InlineData(ActivationKind.StartupTask, null, false, null, true, false, true)]
    public void Activation_requests_map_to_plans(ActivationKind kind, string? uri, bool first, KeyEvent? key, bool showPopup, bool openSettings, bool readSelection)
        => Assert.Equal(new ActivationPlan(key, showPopup, openSettings, ReadSelection: readSelection),
            ActivationPlanner.Plan(new ActivationRequest(kind, uri is null ? null : new Uri(uri), first)));
}
