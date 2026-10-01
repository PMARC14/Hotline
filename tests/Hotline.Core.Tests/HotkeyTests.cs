using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x48u)]
    [InlineData("alt + space", HotkeyModifiers.Alt, 0x20u)]
    [InlineData("Win+Shift+F23", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x86u)]
    [InlineData("Control+1", HotkeyModifiers.Control, 0x31u)]
    [InlineData("F13", HotkeyModifiers.None, 0x7Cu)]
    public void Parses_valid_hotkeys(string text, HotkeyModifiers mods, uint vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(new Hotkey(mods, vk), hk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Alt+")]
    [InlineData("Banana")]
    [InlineData("Ctrl+Alt")]          // no key
    [InlineData("Ctrl+A+B")]          // two keys
    [InlineData("H")]                 // ordinary key without modifier would hijack typing
    [InlineData("F25")]
    public void Rejects_invalid_hotkeys(string? text)
        => Assert.False(Hotkey.TryParse(text, out _));

    [Theory]
    [InlineData("alt+ctrl+h", "Ctrl+Alt+H")]
    [InlineData("shift+win+f23", "Shift+Win+F23")]
    [InlineData("Alt+Space", "Alt+Space")]
    public void ToString_is_canonical_and_round_trips(string input, string expected)
    {
        Assert.True(Hotkey.TryParse(input, out var hk));
        Assert.Equal(expected, hk.ToString());
        Assert.True(Hotkey.TryParse(hk.ToString(), out var again));
        Assert.Equal(hk, again);
    }
}
