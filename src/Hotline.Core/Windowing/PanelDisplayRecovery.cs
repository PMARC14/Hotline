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
    /// The rect to answer Windows' WM_DPICHANGED with. Windows suggests the window scaled by the DPI ratio; when the panel
    /// was already placed for the new DPI (opening on a monitor of another scale), scaling it again would make it 1.5x
    /// too big or too small, so the planned rect stands. A drag across monitors, or a DPI change the panel wasn't placed
    /// for (a scaling change under a pinned panel), takes the suggestion; RecoverDisplay then rebuilds around it.
    /// </summary>
    public static RectI RectForDpiChange(double plannedScale, uint newDpi, RectI? planned, RectI suggested, bool userMoving) =>
        !userMoving && planned is { } p && Math.Abs(newDpi / 96.0 - plannedScale) < 0.001 ? p : suggested;
}
