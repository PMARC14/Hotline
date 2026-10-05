using Hotline.Core.Settings;
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

    public static event Action? Changed;

    private static UISettings CreateUi()
    {
        var ui = new UISettings();
        ui.ColorValuesChanged += (_, _) => Changed?.Invoke();
        return ui;
    }

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
        ThemeChoice.Dark => true,
        _ => IsDark,
    };

    public static ElementTheme Resolve(ThemeChoice choice) => ResolveDark(choice) ? ElementTheme.Dark : ElementTheme.Light;

    /// <summary>Caption buttons and title bar in the same theme as the content (Windows draws them white otherwise).</summary>
    public static void ApplyTitleBar(AppWindow window, ThemeChoice choice) =>
        window.TitleBar.PreferredTheme = ResolveDark(choice) ? TitleBarTheme.Dark : TitleBarTheme.Light;
}
