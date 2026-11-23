using System.Globalization;
using Hotline.Core.Settings;

namespace Hotline.Core.State;

/// <summary>One-time UI notices tracked in <c>state.json</c>.</summary>
public enum ShownFlag
{
    FirstRun,
    SupportNote,
    UpgradeNotice,
}

/// <summary>Timestamps of when one-time notices were shown (<c>null</c> = not shown yet).</summary>
public sealed record ShownState(
    DateTimeOffset? FirstRun = null,
    DateTimeOffset? SupportNote = null,
    DateTimeOffset? UpgradeNotice = null);

/// <summary>A <c>settings.json</c> <c>window.width</c> value recorded as the baseline (<c>over</c>) for a saved panel width.</summary>
public readonly record struct WidthOver
{
    public int? Pixels { get; }
    public string? Percent { get; }

    private WidthOver(int pixels) => Pixels = pixels;

    private WidthOver(string percent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(percent);
        Percent = percent.Trim();
    }

    public static WidthOver FromPixels(int pixels) => new(pixels);
    public static WidthOver FromPercent(string percent) => new(percent);

    public static implicit operator WidthOver(int pixels) => FromPixels(pixels);
    public static implicit operator WidthOver(string percent) => FromPercent(percent);

    public override string ToString() => Percent ?? Pixels?.ToString(CultureInfo.InvariantCulture) ?? "";
}

/// <summary>A saved panel setting along with the <c>settings.json</c> value (<see cref="Over"/>) it overrides while equal.</summary>
public sealed record PanelOverride<TValue, TOver>(TValue Value, TOver Over);

/// <summary>The panel spot saved by "Save position and size as default" in <c>state.json</c>.</summary>
public sealed record PanelSpot(
    PanelOverride<int, WidthOver?>? Width = null,
    PanelOverride<int?, int?>? ChatHeight = null,
    PanelOverride<PanelAnchor, PanelAnchor?>? Anchor = null);

/// <summary>Effective panel spot after applying the "applies while equal" rule against current <c>settings.json</c> values.</summary>
public sealed record ResolvedPanelSpot(
    int? Width,
    int? ChatHeight,
    PanelAnchor Anchor,
    bool WidthApplied,
    bool ChatHeightApplied,
    bool AnchorApplied);

/// <summary>An "Always allow" MCP tool approval stored in <c>state.json</c>, scoped to a server's launch hash.</summary>
public sealed record ToolGrant(string Server, string Tool, string Launch, DateTimeOffset At);

/// <summary>Record of the one-time pre-freeze upgrade.</summary>
public sealed record UpgradeRecord(DateTimeOffset At, string? Backup);

/// <summary>Store for per-tool "Always allow" grants backed by <c>state.json</c>.</summary>
public interface IGrantStore
{
    IReadOnlyList<ToolGrant> GetGrants();
    bool IsGranted(string server, string tool, string launchHash);
    bool AddGrant(string server, string tool, string launchHash, DateTimeOffset? at = null);
    bool RevokeGrant(string server, string tool, string? launchHash = null);
}
