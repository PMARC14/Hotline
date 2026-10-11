using System.Globalization;

namespace Hotline.Core.Text.Localization;

/// <summary>
/// The interface languages Hotline ships, and the "language" setting: "system" (default) follows the Windows display
/// language; a tag picks one. Only the interface text changes; numbers and dates keep following Windows.
/// </summary>
public static class Languages
{
    public const string System = "system";

    /// <summary>English first (the fallback), then the translations (Strings.&lt;tag&gt;.resx).</summary>
    public static IReadOnlyList<string> Supported { get; } =
        ["en-US", "es", "fr", "de", "it", "pt-BR", "nl", "pl", "ru", "uk", "tr", "ja", "ko", "zh-Hans", "zh-Hant", "hi", "id", "vi"];

    /// <summary>The culture a setting asks for, or null to follow Windows (also for an unknown tag).</summary>
    public static CultureInfo? Resolve(string? setting)
    {
        if (string.IsNullOrWhiteSpace(setting) || setting.Trim().Equals(System, StringComparison.OrdinalIgnoreCase)) return null;
        var tag = Supported.FirstOrDefault(t => t.Equals(setting.Trim(), StringComparison.OrdinalIgnoreCase));
        return tag is null ? null : CultureInfo.GetCultureInfo(tag);
    }

    /// <summary>Applies the setting to this process's interface text (call once at startup, before any window).</summary>
    public static CultureInfo? Apply(string? setting)
    {
        if (Resolve(setting) is not { } culture) return null;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }

    /// <summary>A language's own name ("Deutsch", "日本語"), capitalised, for the language picker.</summary>
    public static string NativeName(string tag)
    {
        var name = CultureInfo.GetCultureInfo(tag).NativeName;
        return name.Length == 0 ? tag : char.ToUpper(name[0], CultureInfo.GetCultureInfo(tag)) + name[1..];
    }
}

/// <summary>Interface text lookups for keys held as data (settings schema) and formatted messages.</summary>
public static class L
{
    /// <summary>The text for a resource key in the current interface language (English if missing; the key if unknown).</summary>
    public static string Get(string key) => Strings.ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    /// <summary>An effort level's name ("low" → "Low", translated); levels without a translation show as they are.</summary>
    public static string Effort(string level) =>
        Strings.ResourceManager.GetString("Effort_" + level.ToLowerInvariant(), CultureInfo.CurrentUICulture) ?? level;

    /// <summary>A whole-sentence message with {0}… placeholders, filled in with the current formats.</summary>
    public static string F(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
