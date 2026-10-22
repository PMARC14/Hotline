using System.Text.RegularExpressions;

namespace Hotline.Core.Theming;

/// <summary>The <c>window.accent</c> setting: "hotline" (the built-in purple), "windows" (the Windows accent) or "#RRGGBB".</summary>
public static partial class AccentChoice
{
    public const string Hotline = "hotline";
    public const string Windows = "windows";

    public static bool IsCustomColor(string? value) => value is not null && Hex().IsMatch(value.Trim());

    /// <summary>The accent colour to draw, or null for the built-in one.</summary>
    public static string? Resolve(string? value, bool dark, WindowsColors? windows)
    {
        if (string.Equals(value?.Trim(), Windows, StringComparison.OrdinalIgnoreCase))
            return windows is null ? null : dark ? windows.AccentLight2 : windows.AccentDark1;
        return IsCustomColor(value) ? "#FF" + value!.Trim()[1..].ToUpperInvariant() : null;
    }

    [GeneratedRegex("^#[0-9A-Fa-f]{6}$")]
    private static partial Regex Hex();
}
