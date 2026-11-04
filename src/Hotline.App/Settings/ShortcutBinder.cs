using Hotline.Core.Activation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Windows.System;

namespace Hotline.App.Settings;

/// <summary>The shared key capture control for global hotkeys and panel shortcuts.</summary>
internal static class ShortcutBinder
{
    public static Button Create(Func<string?> read, Action<string?> save, Func<string, bool>? accept = null, bool panel = false)
    {
        var button = new Button { MinWidth = 200, HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetLiveSetting(button, AutomationLiveSetting.Polite);
        var capturing = false;
        void ShowValue() { capturing = false; button.Content = read() is { Length: > 0 } v ? v : Strings.Hotkey_NotSet; }
        void Save(string? value) { save(value); ShowValue(); }
        ShowValue();
        button.Click += (_, _) => { capturing = true; button.Content = Strings.Hotkey_Press; };
        button.LostFocus += (_, _) => { if (capturing) ShowValue(); };
        button.PreviewKeyDown += (_, e) =>
        {
            if (!capturing) return;
            e.Handled = true;
            var held = HotkeyModifiers.None;
            if (Down(VirtualKey.Control)) held |= HotkeyModifiers.Control;
            if (Down(VirtualKey.Menu)) held |= HotkeyModifiers.Alt;
            if (Down(VirtualKey.Shift)) held |= HotkeyModifiers.Shift;
            if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) held |= HotkeyModifiers.Win;
            var key = e.Key != VirtualKey.None ? e.Key : e.OriginalKey;
            var result = HotkeyCapture.Press(held, (uint)key, panel);
            switch (result.Outcome)
            {
                case HotkeyCapture.Outcome.Waiting: return;
                case HotkeyCapture.Outcome.Cancel: ShowValue(); return;
                case HotkeyCapture.Outcome.Clear: Save(null); return;
                case HotkeyCapture.Outcome.NeedsModifier: button.Content = panel ? Strings.Shortcuts_NeedsModifier : Strings.Hotkey_NeedsModifier; return;
                case HotkeyCapture.Outcome.Unsupported: button.Content = Strings.Hotkey_Unsupported; return;
            }
            if (accept is not null && !accept(result.Text!)) { button.Content = Strings.Hotkey_InUse; return; }
            Save(result.Text);
        };
        return button;
    }

    private static bool Down(VirtualKey key) => Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)
        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
}
