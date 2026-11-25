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
        // Widths are scaled pixels (S1) like heights, so the cached bar width follows the new scale too.
        var width = Math.Min(rect.Width, Math.Max(1, (int)Math.Round(cachedBar.Width * ratio)));
        var bar = PanelPlacement.Clamp(new RectI(rect.X + (rect.Width - width) / 2,
            growsUp ? rect.Y + rect.Height - height : rect.Y, width, height), newWork);
        int? fixedPx = fixedHeight is { } f ? Math.Min(newWork.Height, Math.Max(1, (int)Math.Round(f * ratio))) : null;
        return new Result(rect, bar, fixedPx);
    }

    /// <summary>
    /// The panel opened on a monitor of another scale: placement already planned for the new scale, then Windows' DPI change
    /// scaled the window rect once more. True when the raster scale just changed (<paramref name="observedScale"/> was the last
    /// one seen) while the planned scale already matches it, and the window no longer matches the planned rect.
    /// </summary>
    public static bool NeedsRestore(double observedScale, double scale, RectI actual, RectI? planned) =>
        Math.Abs(observedScale - scale) > 0.001 && planned is { } p && actual != p;
}
