using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class ActivationPlannerTests
{
    [Fact]
    public void Protocol_key_uri_becomes_a_key_event()
        => Assert.Equal(new ActivationPlan(KeyEvent.Tap, ShowPopup: false),
            ActivationPlanner.Plan(new ActivationRequest(ActivationKind.Protocol, new Uri("hotline://key?state=Tap"), IsFirstLaunch: false)));

    [Fact]
    public void Cold_start_key_press_arrives_as_protocol_for_results_and_is_parsed()
        => Assert.Equal(new ActivationPlan(KeyEvent.HoldStart, ShowPopup: false),
            ActivationPlanner.Plan(new ActivationRequest(ActivationKind.ProtocolForResults, new Uri("hotline://key?state=Down"), IsFirstLaunch: true)));

    [Fact]
    public void Protocol_without_key_state_just_shows_popup()
        => Assert.Equal(new ActivationPlan(null, ShowPopup: true),
            ActivationPlanner.Plan(new ActivationRequest(ActivationKind.Protocol, new Uri("hotline://open"), IsFirstLaunch: false)));

    [Fact]
    public void Protocol_with_missing_uri_shows_popup()
        => Assert.Equal(new ActivationPlan(null, ShowPopup: true),
            ActivationPlanner.Plan(new ActivationRequest(ActivationKind.Protocol, null, IsFirstLaunch: false)));

    [Fact]
    public void Sign_in_startup_stays_in_tray()
        => Assert.Equal(new ActivationPlan(null, ShowPopup: false),
            ActivationPlanner.Plan(new ActivationRequest(ActivationKind.StartupTask, null, IsFirstLaunch: true)));

    [Theory]
    [InlineData(ActivationKind.Launch, true)]
    [InlineData(ActivationKind.Launch, false)]
    [InlineData(ActivationKind.Other, false)]
    [InlineData(ActivationKind.StartupTask, false)]
    public void Other_activations_show_popup(ActivationKind kind, bool first)
        => Assert.Equal(new ActivationPlan(null, ShowPopup: true),
            ActivationPlanner.Plan(new ActivationRequest(kind, null, first)));

    [Theory]
    [InlineData(ActivationKind.Protocol, false)]
    [InlineData(ActivationKind.ProtocolForResults, true)]
    public void Tray_uri_starts_quietly_without_popup(ActivationKind kind, bool first)
        => Assert.Equal(new ActivationPlan(null, ShowPopup: false),
            ActivationPlanner.Plan(new ActivationRequest(kind, new Uri("hotline://tray"), first)));
}
