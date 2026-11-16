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
    PanelWidth Width,
    int MinWidth,
    int MaxWidth,
    int MinHeight,
    PanelWidth? ChatWidth,
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
            settings.MinWidth,
            settings.MaxWidth,
            settings.MinHeight,
            settings.ChatWidth,
            settings.ChatHeight,
            settings.MaxHeightPercent,
            settings.GrowDirection);
    }
}
