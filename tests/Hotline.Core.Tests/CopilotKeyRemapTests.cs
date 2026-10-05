using Hotline.Core.Activation;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>The Copilot key as Right Ctrl: the key sends Win+Shift+F23; F23 becomes Right Ctrl and Win/Shift are let go.</summary>
public sealed class CopilotKeyRemapTests
{
    private static KeyStroke Down(ushort vk) => new(vk, Up: false);
    private static KeyStroke Up(ushort vk) => new(vk, Up: true);

    [Fact]
    public void Press_turns_into_right_ctrl_and_lets_go_of_win_and_shift()
    {
        var remap = new CopilotKeyRemap();
        var (suppress, inject) = remap.OnKey(CopilotKeyRemap.F23, up: false);
        Assert.True(suppress);
        // The mask key first, so letting go of Win doesn't open Start.
        Assert.Equal([Down(CopilotKeyRemap.Mask), Up(CopilotKeyRemap.Mask), Up(CopilotKeyRemap.LShift), Up(CopilotKeyRemap.LWin), Down(CopilotKeyRemap.RCtrl)], inject);
        Assert.True(remap.Held);
    }

    [Fact]
    public void Release_lets_go_of_right_ctrl()
    {
        var remap = new CopilotKeyRemap();
        remap.OnKey(CopilotKeyRemap.F23, up: false);
        var (suppress, inject) = remap.OnKey(CopilotKeyRemap.F23, up: true);
        Assert.True(suppress);
        Assert.Equal([Up(CopilotKeyRemap.RCtrl)], inject);
        Assert.False(remap.Held);
    }

    [Fact]
    public void Auto_repeat_is_swallowed_without_more_presses()
    {
        var remap = new CopilotKeyRemap();
        remap.OnKey(CopilotKeyRemap.F23, up: false);
        var (suppress, inject) = remap.OnKey(CopilotKeyRemap.F23, up: false);
        Assert.True(suppress);
        Assert.Empty(inject);
    }

    [Fact]
    public void Other_keys_pass_through_untouched()
    {
        var remap = new CopilotKeyRemap();
        Assert.Equal((false, []), remap.OnKey(0x43 /* C */, up: false));
        Assert.Equal((false, []), remap.OnKey(CopilotKeyRemap.LWin, up: true));
    }

    [Fact]
    public void A_stray_release_is_swallowed() => Assert.Equal((true, []), new CopilotKeyRemap().OnKey(CopilotKeyRemap.F23, up: true));

    [Fact]
    public void Setting_defaults_to_opening_hotline_and_is_offered_on_the_general_page()
    {
        Assert.Equal(CopilotKeyMode.Hotline, new ActivationSettings().CopilotKey);
        var choice = Assert.IsType<ChoiceItem>(SettingsSchema.Items.Single(i => i.Page == SettingsPage.General && i.Header == "Copilot key"));
        Assert.Contains(choice.Options, o => o.Value == nameof(CopilotKeyMode.RightCtrl));
    }
}
