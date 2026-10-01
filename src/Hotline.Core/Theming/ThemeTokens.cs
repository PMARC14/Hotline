using System.Globalization;
using System.Text;

namespace Hotline.Core.Theming;

/// <summary>Design tokens for the chat view, emitted as CSS custom properties (spec: Theming).</summary>
public sealed record ThemeTokens(
    string Text, string Muted, string Accent, string Surface, string SurfaceStrong, string Border,
    string UserBubble, string CodeBackground, string Font, int FontSizePx, int RadiusPx)
{
    private const string FontStack = "'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif";

    public static ThemeTokens Dark { get; } = new(
        "#F3F3F3", "#A8A8A8", "#8B7CFF", "rgba(255,255,255,0.06)", "rgba(255,255,255,0.10)", "rgba(255,255,255,0.12)",
        "rgba(139,124,255,0.22)", "rgba(0,0,0,0.35)", FontStack, 14, 8);

    public static ThemeTokens Light { get; } = new(
        "#1A1A1A", "#5C5C5C", "#5B4BF5", "rgba(0,0,0,0.04)", "rgba(0,0,0,0.07)", "rgba(0,0,0,0.10)",
        "rgba(91,75,245,0.14)", "rgba(0,0,0,0.05)", FontStack, 14, 8);

    public string ToCss()
    {
        var sb = new StringBuilder(":root{");
        void Add(string name, string value) => sb.Append(name).Append(':').Append(Safe(value)).Append(';');
        Add("--hl-text", Text);
        Add("--hl-muted", Muted);
        Add("--hl-accent", Accent);
        Add("--hl-surface", Surface);
        Add("--hl-surface-strong", SurfaceStrong);
        Add("--hl-border", Border);
        Add("--hl-user-bubble", UserBubble);
        Add("--hl-code-bg", CodeBackground);
        Add("--hl-font", Font);
        Add("--hl-font-size", FontSizePx.ToString(CultureInfo.InvariantCulture) + "px");
        Add("--hl-radius", RadiusPx.ToString(CultureInfo.InvariantCulture) + "px");
        return sb.Append('}').ToString();
    }

    private static string Safe(string value)
    {
        if (value.IndexOfAny([';', '{', '}', '<', '>', '"', '\\']) >= 0)
            throw new ArgumentException($"Unsafe theme token value: {value}");
        return value;
    }
}
