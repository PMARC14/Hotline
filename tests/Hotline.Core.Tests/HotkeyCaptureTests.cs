using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

/// <summary>Setting the fallback hotkey by pressing it (Settings › General).</summary>
public class HotkeyCaptureTests
{
    private const uint Esc = 0x1B, Backspace = 0x08, Delete = 0x2E, Ctrl = 0x11, Shift = 0x10, LWin = 0x5B, H = 'H', F13 = 0x7C, F5 = 0x74;

    [Fact]
    public void A_key_with_a_modifier_is_bound()
    {
        var r = HotkeyCapture.Press(HotkeyModifiers.Control | HotkeyModifiers.Alt, H);
        Assert.Equal(HotkeyCapture.Outcome.Bound, r.Outcome);
        Assert.Equal("Ctrl+Alt+H", r.Text);
    }

    [Fact]
    public void F13_to_F24_work_alone()
        => Assert.Equal("F13", HotkeyCapture.Press(HotkeyModifiers.None, F13).Text);

    [Theory]
    [InlineData(Ctrl)]
    [InlineData(Shift)]
    [InlineData(LWin)]
    public void Modifiers_alone_keep_waiting(uint vk)
        => Assert.Equal(HotkeyCapture.Outcome.Waiting, HotkeyCapture.Press(HotkeyModifiers.Control, vk).Outcome);

    [Fact]
    public void Esc_cancels() => Assert.Equal(HotkeyCapture.Outcome.Cancel, HotkeyCapture.Press(HotkeyModifiers.None, Esc).Outcome);

    [Theory]
    [InlineData(Backspace)]
    [InlineData(Delete)]
    public void Backspace_or_Delete_clears(uint vk)
        => Assert.Equal(HotkeyCapture.Outcome.Clear, HotkeyCapture.Press(HotkeyModifiers.None, vk).Outcome);

    [Fact]
    public void A_plain_key_needs_a_modifier() // it would swallow normal typing in every app
        => Assert.Equal(HotkeyCapture.Outcome.NeedsModifier, HotkeyCapture.Press(HotkeyModifiers.None, F5).Outcome);

    [Fact]
    public void A_key_settings_json_cannot_name_is_refused() // the file must be able to hold it
        => Assert.Equal(HotkeyCapture.Outcome.Unsupported, HotkeyCapture.Press(HotkeyModifiers.Control, Delete).Outcome);

    [Fact]
    public void A_bound_shortcut_round_trips_through_the_setting()
    {
        var r = HotkeyCapture.Press(HotkeyModifiers.Win | HotkeyModifiers.Shift, 'K');
        Assert.True(Hotkey.TryParse(r.Text, out var parsed));
        Assert.Equal(new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift, 'K'), parsed);
    }
}
