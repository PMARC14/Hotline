using Hotline.Core.Activation;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class KeyActionResolverTests
{
    [Theory]
    [InlineData(KeyEvent.Tap, KeyAction.TogglePopup)]
    [InlineData(KeyEvent.HoldStart, KeyAction.NewChat)]
    [InlineData(KeyEvent.HoldStop, KeyAction.None)] // releasing a hold does nothing in v1
    public void Defaults_tap_toggles_and_hold_starts_new_chat(KeyEvent e, KeyAction expected)
        => Assert.Equal(expected, KeyActionResolver.Resolve(e, new ActivationSettings()));

    [Fact]
    public void Custom_mapping_is_respected()
    {
        var s = new ActivationSettings { Tap = KeyAction.NewChat, Hold = KeyAction.CaptureWindow };
        Assert.Equal(KeyAction.NewChat, KeyActionResolver.Resolve(KeyEvent.Tap, s));
        Assert.Equal(KeyAction.CaptureWindow, KeyActionResolver.Resolve(KeyEvent.HoldStart, s));
    }
}
