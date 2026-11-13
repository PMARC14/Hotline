namespace Hotline.Core.Windowing;

/// <summary>Rebuilds cached geometry around the OS's actual rectangle, which is already DPI scaled.</summary>
public static class PanelDisplayRecovery
{
    public sealed record Result(RectI Rect, RectI Bar, int? FixedHeight);

    public static Result Calculate(RectI oldWork, RectI newWork, double oldScale, double newScale,
        RectI actual, RectI cachedBar, int? fixedHeight, bool growsUp)
    {
        var rect = PanelPlacement.Clamp(actual, newWork);
        var ratio = newScale / oldScale;
        var height = Math.Min(newWork.Height, Math.Max(1, (int)Math.Round(cachedBar.Height * ratio)));
        // Width settings are physical pixels; only DIP-sized cached heights change with DPI.
        var width = Math.Min(cachedBar.Width, rect.Width);
        var bar = PanelPlacement.Clamp(new RectI(rect.X + (rect.Width - width) / 2,
            growsUp ? rect.Y + rect.Height - height : rect.Y, width, height), newWork);
        int? fixedPx = fixedHeight is { } f ? Math.Min(newWork.Height, Math.Max(1, (int)Math.Round(f * ratio))) : null;
        return new Result(rect, bar, fixedPx);
    }
}
