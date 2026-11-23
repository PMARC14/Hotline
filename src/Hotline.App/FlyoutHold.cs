using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Hotline.App;

/// <summary>Owns the panel holds for one flyout, including refused or failed opens.</summary>
internal sealed class FlyoutHold
{
    private readonly FlyoutBase _flyout;
    private IDisposable? _resize, _modal;

    public FlyoutHold(PopupWindow popup, FlyoutBase flyout)
    {
        _flyout = flyout;
        flyout.Opened += (_, _) =>
        {
            try { _resize ??= popup.HoldResize(); _modal ??= popup.Modal(); }
            catch { Release(); throw; }
        };
        flyout.Closed += (_, _) => Release();
    }

    public void ShowAt(FrameworkElement anchor, FlyoutShowOptions? options = null)
    {
        try
        {
            if (options is null) _flyout.ShowAt(anchor);
            else _flyout.ShowAt(anchor, options);
            if (!_flyout.IsOpen) Release();
        }
        catch { Release(); throw; }
    }

    public void Release()
    {
        var modal = _modal;
        var resize = _resize;
        _modal = _resize = null;
        try { modal?.Dispose(); }
        finally { resize?.Dispose(); }
    }
}
