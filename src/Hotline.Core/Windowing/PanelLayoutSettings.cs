using Hotline.Core.Settings;

namespace Hotline.Core.Windowing;

/// <summary>
/// Value snapshot of the <see cref="WindowSettings"/> fields that place or size the panel's home bar and chat bounds.
/// Appearance fields (theme, backdrop, accent, fonts, sounds, scrollbar) are excluded so changing them never re-places an open panel.
/// </summary>
public readonly record struct PanelLayoutSettings(
    string AnchorMonitor,
    double AnchorX,
    double AnchorY,
    int? Width,
    double WidthPercent,
    int MinWidth,
    int MaxWidth,
    int Height,
    int? ChatWidth,
    double ChatWidthPercent,
    int? ChatHeight,
    double MaxHeightPercent,
    GrowDirection GrowDirection)
{
    public static PanelLayoutSettings From(WindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var anchor = settings.Anchor;
        return new PanelLayoutSettings(
            anchor?.Monitor ?? PanelAnchor.Active,
            anchor?.X ?? 0.5,
            anchor?.Y ?? 0.8,
            settings.Width,
            settings.WidthPercent,
            settings.MinWidth,
            settings.MaxWidth,
            settings.Height,
            settings.ChatWidth,
            settings.ChatWidthPercent,
            settings.ChatHeight,
            settings.MaxHeightPercent,
            settings.GrowDirection);
    }
}
