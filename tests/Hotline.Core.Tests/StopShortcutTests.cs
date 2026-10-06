using Hotline.Core.Activation;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>chat.stopShortcut: the key that stops a streaming answer (Enter never does).</summary>
public sealed class StopShortcutTests
{
    [Theory]
    [InlineData("Esc", HotkeyModifiers.None, 0x1Bu)]
    [InlineData("Ctrl+.", HotkeyModifiers.Control, 0xBEu)]
    [InlineData("ctrl+shift+backspace", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x08u)]
    [InlineData("Alt+S", HotkeyModifiers.Alt, (uint)'S')]
    public void Panel_shortcuts_may_be_bare_keys_or_combos(string text, HotkeyModifiers mods, uint vk)
    {
        Assert.True(Hotkey.TryParseShortcut(text, out var key));
        Assert.Equal(new Hotkey(mods, vk), key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Bogus")]
    [InlineData("Ctrl+")]
    [InlineData("Enter")] // Enter types/sends; it must never stop an answer
    public void Bad_or_reserved_shortcuts_are_rejected(string text) => Assert.False(Hotkey.TryParseShortcut(text, out _));

    [Fact]
    public void The_global_hotkey_still_refuses_bare_keys() => Assert.False(Hotkey.TryParse("Esc", out _));

    [Fact]
    public void A_shortcut_matches_only_its_exact_modifiers()
    {
        Hotkey.TryParseShortcut("Ctrl+.", out var key);
        Assert.True(key.Matches(0xBE, HotkeyModifiers.Control));
        Assert.False(key.Matches(0xBE, HotkeyModifiers.Control | HotkeyModifiers.Shift));
        Assert.False(key.Matches(0xBE, HotkeyModifiers.None));
    }

    [Fact]
    public void Defaults_to_esc_and_is_editable_in_settings()
    {
        Assert.Equal("Esc", new ChatSettings().StopShortcut);
        var item = Assert.IsType<TextItem>(SettingsSchema.Items.Single(i => i.Header == "Stop an answer"));
        Assert.NotNull(item.Validate);
        Assert.True(item.Validate!("Ctrl+."));
        Assert.False(item.Validate!("Enter"));
    }

    [Fact]
    public void An_invalid_value_in_the_file_falls_back_to_esc()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, SettingsStore.FileName), """{ "chat": { "stopShortcut": "Ctrl+Bogus" } }""");
            Assert.Equal("Esc", new SettingsStore(dir).Load().Chat.StopShortcut);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
