using Hotline.Core.Activation;

namespace Hotline.Core.Settings;

public enum SettingsPage { General, Appearance, Window, Chat, Advanced }

public abstract record SettingItem(SettingsPage Page, string Header, string? Description, bool RequiresRestart);

public sealed record ToggleItem(SettingsPage Page, string Header, string? Description, Func<HotlineSettings, bool> Get,
    Action<HotlineSettings, bool> Set, bool RequiresRestart = false) : SettingItem(Page, Header, Description, RequiresRestart);

public sealed record NumberItem(SettingsPage Page, string Header, string? Description, double Min, double Max, double Step, string? Unit,
    Func<HotlineSettings, double> Get, Action<HotlineSettings, double> Set, bool RequiresRestart = false)
    : SettingItem(Page, Header, Description, RequiresRestart);

public sealed record ChoiceOption(string Value, string Label);

public sealed record ChoiceItem(SettingsPage Page, string Header, string? Description, IReadOnlyList<ChoiceOption> Options,
    Func<HotlineSettings, string> Get, Action<HotlineSettings, string> Set, bool RequiresRestart = false)
    : SettingItem(Page, Header, Description, RequiresRestart);

public sealed record TextItem(SettingsPage Page, string Header, string? Description, string? Placeholder,
    Func<HotlineSettings, string?> Get, Action<HotlineSettings, string?> Set, bool RequiresRestart = false)
    : SettingItem(Page, Header, Description, RequiresRestart);

/// <summary>Every user-facing setting, described once; the settings window is generated from this list.</summary>
public static class SettingsSchema
{
    private static ChoiceItem Choice<T>(SettingsPage page, string header, string? description, (T Value, string Label)[] options,
        Func<HotlineSettings, T> get, Action<HotlineSettings, T> set, bool restart = false) where T : struct, Enum
        => new(page, header, description, options.Select(o => new ChoiceOption(o.Value.ToString(), o.Label)).ToList(),
            s => get(s).ToString(), (s, v) => set(s, Enum.Parse<T>(v)), restart);

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static readonly (KeyAction, string)[] KeyActions =
    [
        (KeyAction.TogglePopup, "Open / close Hotline"), (KeyAction.ShowPopup, "Open Hotline"), (KeyAction.NewChat, "Start a new chat"),
        (KeyAction.CaptureWindow, "Capture the current window"), (KeyAction.None, "Do nothing"),
    ];

    public static IReadOnlyList<SettingItem> Items { get; } =
    [
        Choice(SettingsPage.General, "Short press of the Copilot key", null, KeyActions, s => s.Activation.Tap, (s, v) => s.Activation.Tap = v),
        Choice(SettingsPage.General, "Long press of the Copilot key", null, KeyActions, s => s.Activation.Hold, (s, v) => s.Activation.Hold = v),
        new TextItem(SettingsPage.General, "Extra hotkey", "Also opens Hotline, e.g. Ctrl+Alt+H. Leave empty for none.", "Ctrl+Alt+H",
            s => s.Activation.FallbackHotkey, (s, v) => s.Activation.FallbackHotkey = Blank(v), RequiresRestart: true),
        new ToggleItem(SettingsPage.General, "Hide when I click elsewhere", "Pin the panel (📌) to keep it open temporarily.",
            s => s.Window.HideOnBlur, (s, v) => s.Window.HideOnBlur = v),
        new ToggleItem(SettingsPage.General, "Keep on top of other windows", null, s => s.Window.AlwaysOnTop, (s, v) => s.Window.AlwaysOnTop = v),

        Choice(SettingsPage.Appearance, "Theme", null, [(ThemeChoice.System, "Use Windows setting"), (ThemeChoice.Light, "Light"), (ThemeChoice.Dark, "Dark")],
            s => s.Window.Theme, (s, v) => s.Window.Theme = v),
        Choice(SettingsPage.Appearance, "Background", "Acrylic is translucent; Solid is opaque.",
            [(BackdropKind.Acrylic, "Acrylic"), (BackdropKind.AcrylicThin, "Acrylic (thin)"), (BackdropKind.Mica, "Mica"), (BackdropKind.Solid, "Solid")],
            s => s.Window.Backdrop, (s, v) => s.Window.Backdrop = v),
        new NumberItem(SettingsPage.Appearance, "Acrylic tint", "0 = clear, 1 = strongly tinted.", 0, 1, 0.05, null,
            s => s.Window.TintOpacity, (s, v) => s.Window.TintOpacity = v),
        new NumberItem(SettingsPage.Appearance, "Acrylic luminosity", "Lower is more see-through.", 0, 1, 0.05, null,
            s => s.Window.LuminosityOpacity, (s, v) => s.Window.LuminosityOpacity = v),
        new NumberItem(SettingsPage.Appearance, "Text size", "Default follows Windows (Settings › Accessibility › Text size).", 10, 32, 1, "px",
            s => s.Window.FontSize ?? Theming.ThemeTokens.DefaultFontSize, (s, v) => s.Window.FontSize = (int)v == Theming.ThemeTokens.DefaultFontSize ? null : (int)v),
        new TextItem(SettingsPage.Appearance, "Font", "Any installed font, e.g. Cascadia Code. Empty = Segoe UI Variable.", "Segoe UI Variable",
            s => s.Window.FontFamily, (s, v) => s.Window.FontFamily = Blank(v)),
        Choice(SettingsPage.Appearance, "Scrollbar", null, [(ScrollbarStyle.Auto, "Show when scrolling"), (ScrollbarStyle.Visible, "Always"), (ScrollbarStyle.Hidden, "Never")],
            s => s.Window.Scrollbar, (s, v) => s.Window.Scrollbar = v),

        new NumberItem(SettingsPage.Window, "Width", "Share of the screen width.", 20, 90, 1, "%", s => s.Window.WidthPercent, (s, v) => s.Window.WidthPercent = v),
        new NumberItem(SettingsPage.Window, "Minimum width", null, 320, 4000, 10, "DIP", s => s.Window.MinWidth, (s, v) => s.Window.MinWidth = (int)v),
        new NumberItem(SettingsPage.Window, "Maximum width", null, 320, 4000, 10, "DIP", s => s.Window.MaxWidth, (s, v) => s.Window.MaxWidth = (int)v),
        new NumberItem(SettingsPage.Window, "Minimum height", "0 = just the message bar; the panel grows upward as you type and chat.", 0, 4000, 10, "DIP",
            s => s.Window.Height, (s, v) => s.Window.Height = (int)v),
        new NumberItem(SettingsPage.Window, "Maximum height", "How much of the screen it may grow to.", 30, 95, 1, "%",
            s => s.Window.MaxHeightPercent, (s, v) => s.Window.MaxHeightPercent = v),
        new NumberItem(SettingsPage.Window, "Vertical position", "0 = top, 0.5 = centre, 1 = bottom.", 0, 1, 0.05, null,
            s => s.Window.VerticalPosition, (s, v) => s.Window.VerticalPosition = v),
        Choice(SettingsPage.Window, "Growth", null, [(GrowMode.Grow, "Fit the conversation"), (GrowMode.Full, "Jump to maximum height")],
            s => s.Chat.GrowMode, (s, v) => s.Chat.GrowMode = v),

        new ToggleItem(SettingsPage.Chat, "Save chat history", "Text only, in your .hotline folder.", s => s.Chat.SaveHistory, (s, v) => s.Chat.SaveHistory = v, RequiresRestart: true),
        new NumberItem(SettingsPage.Chat, "Keep history for", null, 1, 3650, 1, "days", s => s.Chat.HistoryRetentionDays,
            (s, v) => s.Chat.HistoryRetentionDays = (int)v, RequiresRestart: true),
        new NumberItem(SettingsPage.Chat, "Image size limit", "Attached and captured images are scaled to this longest edge.", 256, 8192, 128, "px",
            s => s.Chat.MaxImagePixels, (s, v) => s.Chat.MaxImagePixels = (int)v),

        new ToggleItem(SettingsPage.Advanced, "Detailed logging", "Writes extra diagnostics to the log.", s => s.Diagnostics.VerboseLogging,
            (s, v) => s.Diagnostics.VerboseLogging = v),
    ];
}
