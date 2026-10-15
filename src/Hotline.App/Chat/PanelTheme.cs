using Hotline.Core.Settings;
using Hotline.Core.Theming;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Hotline.App.Chat;

/// <summary>The panel's current look (sizes, fonts, colours), recomputed from the live settings. Shared by its parts.</summary>
internal sealed class PanelTheme(HotlineSettings settings, Action<string> openLink)
{
    public ThemeTokens Tokens { get; private set; } = ThemeTokens.Dark;
    public RenderStyle Style { get; private set; } = null!;
    public bool Dark { get; private set; }

    /// <summary>A key that changes whenever already-rendered content must be rebuilt.</summary>
    public string Look => $"{Tokens.FontSizePx}|{Tokens.Font}|{Dark}|{Backdrop}|{Tokens.Accent}|{Tokens.HighContrast}";

    /// <summary>The backdrop drawn: the chosen one, or Solid when Windows turns transparency off or a contrast theme is on.</summary>
    public BackdropKind Backdrop { get; private set; } = BackdropKind.Acrylic;

    public void Update()
    {
        Dark = settings.Window.Theme switch
        {
            ThemeChoice.Dark or ThemeChoice.HighContrast => true,
            ThemeChoice.Light => false,
            _ => SystemTheme.IsDark, // live, not the theme Hotline started with
        };
        var windows = SystemTheme.Colors;
        Tokens = ThemeTokens.For(Dark, settings.Window, windows);
        Backdrop = ThemeTokens.EffectiveBackdrop(settings.Window.Backdrop, windows, settings.Window.Theme);
        Style = new RenderStyle(Tokens.FontSizePx, new FontFamily(Tokens.Font), Brush(Tokens.Muted), Brush(Tokens.CodeBackground),
            Brush(Tokens.Accent), Tokens.RadiusPx, openLink);
    }

    public static SolidColorBrush Brush(string hex)
    {
        var (a, r, g, b) = ThemeColor.Parse(hex);
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }
}
