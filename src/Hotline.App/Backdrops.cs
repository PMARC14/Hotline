using Hotline.Core.Settings;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Hotline.App;

/// <summary>Builds the popup's backdrop from settings. Acrylic uses a tunable controller so it can be lighter/clearer than the stock material.</summary>
internal static class Backdrops
{
    public static SystemBackdrop? Create(WindowSettings s) => s.Backdrop switch
    {
        BackdropKind.Acrylic => new TunableAcrylicBackdrop(DesktopAcrylicKind.Base, s),
        BackdropKind.AcrylicThin => new TunableAcrylicBackdrop(DesktopAcrylicKind.Thin, s),
        BackdropKind.Mica => new MicaBackdrop(),
        _ => null, // Solid: the XAML background brush shows instead
    };

    private sealed class TunableAcrylicBackdrop(DesktopAcrylicKind kind, WindowSettings settings) : SystemBackdrop
    {
        private DesktopAcrylicController? _controller;

        protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
        {
            base.OnTargetConnected(target, xamlRoot);
            var config = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
            var dark = config.Theme == SystemBackdropTheme.Dark;
            _controller = new DesktopAcrylicController
            {
                Kind = kind,
                TintColor = dark ? Color.FromArgb(255, 0x20, 0x20, 0x20) : Color.FromArgb(255, 0xF3, 0xF3, 0xF3),
                TintOpacity = (float)settings.TintOpacity,
                LuminosityOpacity = (float)settings.LuminosityOpacity,
                FallbackColor = dark ? Color.FromArgb(255, 0x2C, 0x2C, 0x2C) : Color.FromArgb(255, 0xF9, 0xF9, 0xF9),
            };
            _controller.AddSystemBackdropTarget(target);
            _controller.SetSystemBackdropConfiguration(config);
        }

        protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
        {
            base.OnTargetDisconnected(target);
            _controller?.RemoveSystemBackdropTarget(target);
            _controller?.Dispose();
            _controller = null;
        }
    }
}
