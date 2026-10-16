using Hotline.Core.Settings;
using Hotline.Core.Theming;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace Hotline.App;

/// <summary>
/// Windows' light/dark app mode, read live (Application.RequestedTheme is fixed at startup), so "System" follows the
/// Windows setting while Hotline runs. <see cref="Changed"/> fires on a background thread when the mode or colours change.
/// </summary>
internal static class SystemTheme
{
    private static readonly UISettings Ui = CreateUi();
    // Read only: its HighContrastChanged event can't be subscribed outside a CoreWindow (0x80070490), but turning a
    // contrast theme on or off changes the system colours, so ColorValuesChanged reports it.
    private static readonly AccessibilitySettings Accessibility = new();

    public static event Action? Changed;

    private static UISettings CreateUi()
    {
        var ui = new UISettings();
        ui.ColorValuesChanged += (_, _) => Changed?.Invoke(); // light/dark, accent colour, contrast themes
        ui.AdvancedEffectsEnabledChanged += (_, _) => Changed?.Invoke(); // Transparency effects
        return ui;
    }

    private static bool HighContrastOn
    {
        get
        {
            try { return Accessibility.HighContrast; }
            catch (Exception) { return false; } // outside a CoreWindow WinRT may fail in any form: no contrast theme then
        }
    }

    /// <summary>Windows' accent shades, contrast theme colours and transparency switch, read now.</summary>
    public static WindowsColors Colors
    {
        get
        {
            ContrastColors? contrast = null;
            if (HighContrastOn)
            {
#pragma warning disable CS0618 // UIElementColor is the only WinRT source of the contrast theme's system colours
                string E(UIElementType t) => Hex(Ui.UIElementColor(t));
                contrast = new ContrastColors(E(UIElementType.Window), E(UIElementType.WindowText), E(UIElementType.Highlight),
                    E(UIElementType.HighlightText), E(UIElementType.Hotlight), E(UIElementType.GrayText), E(UIElementType.ButtonFace),
                    E(UIElementType.ButtonText));
#pragma warning restore CS0618
            }
            return new WindowsColors(Hex(Ui.GetColorValue(UIColorType.AccentLight2)), Hex(Ui.GetColorValue(UIColorType.AccentDark1)),
                contrast, Ui.AdvancedEffectsEnabled);
        }
    }

    private static string Hex(Windows.UI.Color c) => $"#FF{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>True when Windows' app mode is dark (its app background colour is dark).</summary>
    public static bool IsDark
    {
        get
        {
            var bg = Ui.GetColorValue(UIColorType.Background);
            return bg.R + bg.G + bg.B < 384;
        }
    }

    public static bool ResolveDark(ThemeChoice choice) => choice switch
    {
        ThemeChoice.Light => false,
        ThemeChoice.Dark or ThemeChoice.HighContrast => true, // Hotline's high contrast is white on black
        _ => IsDark,
    };

    public static ElementTheme Resolve(ThemeChoice choice) => ResolveDark(choice) ? ElementTheme.Dark : ElementTheme.Light;

    /// <summary>Caption buttons and title bar in the same theme as the content (Windows draws them white otherwise).</summary>
    public static void ApplyTitleBar(AppWindow window, ThemeChoice choice)
    {
        if (window.TitleBar is { } bar) bar.PreferredTheme = ResolveDark(choice) ? TitleBarTheme.Dark : TitleBarTheme.Light;
    }
}
