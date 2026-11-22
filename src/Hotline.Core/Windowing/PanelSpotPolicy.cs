using Hotline.Core.Settings;
using Hotline.Core.State;

namespace Hotline.Core.Windowing;

/// <summary>Effective panel spot and size after applying <c>state.json</c> overrides to <c>settings.json</c>.</summary>
public sealed record EffectivePanelSpot(
    PanelWidth Width,
    int? ChatHeight,
    PanelAnchor Anchor,
    bool WidthApplied,
    bool ChatHeightApplied,
    bool AnchorApplied)
{
    /// <summary>Opening width in real pixels on a monitor with <paramref name="workAreaPixels"/> and <paramref name="scale"/>.</summary>
    public int OpeningWidthPixels(WindowSettings settings, int workAreaPixels, double scale)
    {
        ArgumentNullException.ThrowIfNull(settings);
        // A saved width is a size the user dragged to (dragging ignores minWidth/maxWidth): only the range limits it.
        var range = SettingsRanges.All["WindowSettings.MinWidth"];
        var effective = new WindowSettings
        {
            Width = Width,
            MinWidth = WidthApplied ? (int)range.Min : settings.MinWidth,
            MaxWidth = WidthApplied ? (int)range.Max : settings.MaxWidth,
        };
        return PanelWidth.ResolvePixels(effective, workAreaPixels, scale);
    }
}

/// <summary>
/// Decision logic for "Save position and size as default" and opening placement: converts real pixels to scaled pixels
/// clamped to <see cref="SettingsRanges"/>, and applies saved overrides only while <c>settings.json</c> matches <c>over</c>.
/// </summary>
public static class PanelSpotPolicy
{
    public static WidthOver ToWidthOver(PanelWidth width) =>
        width.IsPercent
            ? WidthOver.FromPercent(width.ToString())
            : WidthOver.FromPixels((int)Math.Round(width.Value));

    public static EffectivePanelSpot Resolve(StateStore? state, WindowSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var currentAnchor = settings.Anchor ?? new PanelAnchor();
        if (state is null)
        {
            return new EffectivePanelSpot(
                settings.Width,
                settings.ChatHeight,
                CloneAnchor(currentAnchor),
                WidthApplied: false,
                ChatHeightApplied: false,
                AnchorApplied: false);
        }

        var resolved = state.ResolvePanelSpot(ToWidthOver(settings.Width), settings.ChatHeight, currentAnchor);
        var width = resolved.WidthApplied && resolved.Width is { } savedWidth
            ? new PanelWidth(SettingsRanges.All["WindowSettings.MinWidth"].Clamp(savedWidth))
            : settings.Width;
        var chatHeight = resolved.ChatHeightApplied
            ? (resolved.ChatHeight is { } savedChat ? SettingsRanges.All["WindowSettings.ChatHeight"].Clamp(savedChat) : (int?)null)
            : settings.ChatHeight;
        return new EffectivePanelSpot(
            width,
            chatHeight,
            resolved.Anchor,
            resolved.WidthApplied,
            resolved.ChatHeightApplied,
            resolved.AnchorApplied);
    }

    public static PanelSpot BuildSaveSpot(
        RectI bar,
        RectI work,
        double scale,
        int? fixedHeightPx,
        int barHeightPx,
        string? monitorDevice,
        WindowSettings settings,
        StateStore? state = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var safeScale = double.IsFinite(scale) && scale > 0 ? scale : 1.0;

        var widthDip = SettingsRanges.All["WindowSettings.MinWidth"].Clamp((int)Math.Round(bar.Width / safeScale));
        var widthOverride = new PanelOverride<int, WidthOver?>(widthDip, ToWidthOver(settings.Width));
        var chatOverride = BuildChatHeightOverride(fixedHeightPx, barHeightPx, safeScale, settings, state);

        var currentAnchor = settings.Anchor ?? new PanelAnchor();
        var anchor = PanelPlacement.AnchorFor(bar, work);
        anchor.Monitor = !string.IsNullOrWhiteSpace(monitorDevice) ? monitorDevice : currentAnchor.Monitor;
        var anchorOverride = new PanelOverride<PanelAnchor, PanelAnchor?>(anchor, CloneAnchor(currentAnchor));

        return new PanelSpot(widthOverride, chatOverride, anchorOverride);
    }

    private static PanelOverride<int?, int?>? BuildChatHeightOverride(
        int? fixedHeightPx,
        int barHeightPx,
        double scale,
        WindowSettings settings,
        StateStore? state)
    {
        var chatRange = SettingsRanges.All["WindowSettings.ChatHeight"];
        if (fixedHeightPx is { } f)
        {
            var chatDip = (f - barHeightPx) / scale;
            if (chatDip < chatRange.Min) return null;
            return new PanelOverride<int?, int?>(chatRange.Clamp((int)Math.Round(chatDip)), settings.ChatHeight);
        }

        if (state is null) return null;
        var current = Resolve(state, settings);
        if (!current.ChatHeightApplied || current.ChatHeight is not { } activeChat) return null;
        return new PanelOverride<int?, int?>(chatRange.Clamp(activeChat), settings.ChatHeight);
    }

    private static PanelAnchor CloneAnchor(PanelAnchor anchor) => new()
    {
        Monitor = string.IsNullOrWhiteSpace(anchor.Monitor) ? PanelAnchor.Active : anchor.Monitor,
        X = anchor.X,
        Y = anchor.Y,
    };
}
