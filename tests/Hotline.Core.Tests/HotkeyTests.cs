using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x48u, "Ctrl+Alt+H")]
    [InlineData("alt + space", HotkeyModifiers.Alt, 0x20u, "Alt+Space")]
    [InlineData("Win+Shift+F23", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x86u, "Shift+Win+F23")]
    [InlineData("Control+1", HotkeyModifiers.Control, 0x31u, "Ctrl+1")]
    [InlineData("F13", HotkeyModifiers.None, 0x7Cu, "F13")]
    [InlineData("alt+ctrl+h", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x48u, "Ctrl+Alt+H")]
    public void Parses_valid_hotkeys_and_prints_them_canonically(string text, HotkeyModifiers mods, uint vk, string canonical)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(new Hotkey(mods, vk), hk);
        Assert.Equal(canonical, hk.ToString());
        Assert.True(Hotkey.TryParse(canonical, out var again));
        Assert.Equal(hk, again);
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
}
