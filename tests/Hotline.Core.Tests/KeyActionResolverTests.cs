using Hotline.Core.Activation;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class KeyActionResolverTests
{
    [Fact]
    public void Defaults_tap_toggles_and_hold_starts_new_chat()
    {
        var s = new ActivationSettings();
        Assert.Equal(KeyAction.TogglePopup, KeyActionResolver.Resolve(KeyEvent.Tap, s));
        Assert.Equal(KeyAction.NewChat, KeyActionResolver.Resolve(KeyEvent.HoldStart, s));
    }

    [Fact]
    public void Hold_release_does_nothing_in_v1()
        => Assert.Equal(KeyAction.None, KeyActionResolver.Resolve(KeyEvent.HoldStop, new ActivationSettings()));

    [Fact]
    public void Custom_mapping_is_respected()
    {
        var s = new ActivationSettings { Tap = KeyAction.NewChat, Hold = KeyAction.CaptureWindow };
        Assert.Equal(KeyAction.NewChat, KeyActionResolver.Resolve(KeyEvent.Tap, s));
        Assert.Equal(KeyAction.CaptureWindow, KeyActionResolver.Resolve(KeyEvent.HoldStart, s));
    }
}
