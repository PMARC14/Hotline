using Hotline.Core.Activation;
using Hotline.Core.Text.Localization;
using Hotline.Core.Theming;

namespace Hotline.Core.Settings;

public enum SettingsPage { General, Appearance, Window, Chat, Advanced }

public abstract record SettingItem(SettingsPage Page, string Header, string? Description, bool RequiresRestart);

public sealed record ToggleItem(SettingsPage Page, string Header, string? Description, Func<HotlineSettings, bool> Get,
    Action<HotlineSettings, bool> Set, bool RequiresRestart = false) : SettingItem(Page, Header, Description, RequiresRestart)
{
    /// <summary>Other controls on the page show what this one changes, so the page is rebuilt after a change.</summary>
    public bool RefreshPage { get; init; }
}

public sealed record NumberItem(SettingsPage Page, string Header, string? Description, double Min, double Max, double Step, string? Unit,
    Func<HotlineSettings, double> Get, Action<HotlineSettings, double> Set, bool RequiresRestart = false)
    : SettingItem(Page, Header, Description, RequiresRestart);

public sealed record ChoiceOption(string Value, string Label);

public sealed record ChoiceItem(SettingsPage Page, string Header, string? Description, IReadOnlyList<ChoiceOption> Options,
    Func<HotlineSettings, string> Get, Action<HotlineSettings, string> Set, bool RequiresRestart = false)
    : SettingItem(Page, Header, Description, RequiresRestart)
{
    /// <summary>Other controls on the page show what this one changes, so the page is rebuilt after a change.</summary>
    public bool RefreshPage { get; init; }
}

public sealed record TextItem(SettingsPage Page, string Header, string? Description, string? Placeholder,
    Func<HotlineSettings, string?> Get, Action<HotlineSettings, string?> Set, bool RequiresRestart = false)
    : SettingItem(Page, Header, Description, RequiresRestart)
{
    /// <summary>Rejects a value before it's saved (the settings window shows the problem instead).</summary>
    public Func<string, bool>? Validate { get; init; }
    /// <summary>What to say when <see cref="Validate"/> rejects a value.</summary>
    public string? Invalid { get; init; }
}

/// <summary>
/// A global hotkey, set by pressing it (Settings shows a key binder; see Activation.HotkeyCapture). The value is the
/// text settings.json holds, e.g. "Ctrl+Alt+H"; null = none.
/// </summary>
public sealed record HotkeyItem(SettingsPage Page, string Header, string? Description, Func<HotlineSettings, string?> Get,
    Action<HotlineSettings, string?> Set, bool RequiresRestart = false) : SettingItem(Page, Header, Description, RequiresRestart);

public static class SettingsSchema
{
    private static ChoiceItem Choice<T>(SettingsPage page, string header, string? description, (T Value, string Label)[] options,
        Func<HotlineSettings, T> get, Action<HotlineSettings, T> set, bool restart = false) where T : struct, Enum
        => new(page, header, description, options.Select(o => new ChoiceOption(o.Value.ToString(), o.Label)).ToList(),
            s => get(s).ToString(), (s, v) => set(s, Enum.Parse<T>(v)), restart);

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>The accent picker's "Custom" entry (settings.json stores the colour itself).</summary>
    private const string CustomAccent = "custom";

    private static readonly (KeyAction, string)[] KeyActions =
    [
        (KeyAction.TogglePopup, nameof(Strings.Set_Key_Toggle)), (KeyAction.ShowPopup, nameof(Strings.Set_Key_Open)),
        (KeyAction.NewChat, nameof(Strings.Set_Key_NewChat)), (KeyAction.CaptureWindow, nameof(Strings.Set_Key_CaptureWindow)),
        (KeyAction.RegionSelect, nameof(Strings.Set_Key_CaptureRegion)), (KeyAction.Voice, nameof(Strings.Set_Key_Voice)),
        (KeyAction.None, nameof(Strings.Set_Key_None)),
    ];

    /// <summary>"Use Windows language", then each shipped language under its own name (never translated).</summary>
    private static List<ChoiceOption> LanguageOptions() =>
        [new(Languages.System, nameof(Strings.Set_Language_System)), .. Languages.Supported.Select(t => new ChoiceOption(t, Languages.NativeName(t)))];

    /// <summary>
    /// Every user-facing setting, described once; the settings window is generated from this list. Header, Description,
    /// option labels, Placeholder, Invalid and Unit are resource keys (Strings.resx), looked up when a page is drawn
    /// (<see cref="L.Get"/>; text that isn't a key, like a language's own name or "px", is shown as is).
    /// </summary>
    public static IReadOnlyList<SettingItem> Items { get; } =
    [
        new ChoiceItem(SettingsPage.General, nameof(Strings.Set_Language), nameof(Strings.Set_Language_Desc), LanguageOptions(),
            s => Languages.Resolve(s.General.Language) is null ? Languages.System : s.General.Language.Trim(),
            (s, v) => s.General.Language = v, RequiresRestart: true),
        Choice(SettingsPage.General, nameof(Strings.Set_Tap), null, KeyActions, s => s.Activation.Tap, (s, v) => s.Activation.Tap = v),
        Choice(SettingsPage.General, nameof(Strings.Set_Hold), null, KeyActions, s => s.Activation.Hold, (s, v) => s.Activation.Hold = v),
        new ToggleItem(SettingsPage.General, nameof(Strings.Set_Voice), nameof(Strings.Set_Voice_Desc),
            s => s.Activation.Hold == KeyAction.Voice,
            (s, v) => { if (v) s.Activation.Hold = KeyAction.Voice; else if (s.Activation.Hold == KeyAction.Voice) s.Activation.Hold = KeyAction.NewChat; })
        { RefreshPage = true },
        new HotkeyItem(SettingsPage.General, nameof(Strings.Set_Hotkey), nameof(Strings.Set_Hotkey_Press_Desc),
            s => s.Activation.FallbackHotkey, (s, v) => s.Activation.FallbackHotkey = Blank(v), RequiresRestart: true),
        new ToggleItem(SettingsPage.General, nameof(Strings.Set_HideOnBlur), nameof(Strings.Set_HideOnBlur_Desc),
            s => s.Window.HideOnBlur, (s, v) => s.Window.HideOnBlur = v),
        new ToggleItem(SettingsPage.General, nameof(Strings.Set_OnTop), null, s => s.Window.AlwaysOnTop, (s, v) => s.Window.AlwaysOnTop = v),

        Choice(SettingsPage.Appearance, nameof(Strings.Set_Theme), nameof(Strings.Set_Theme_Desc),
            [(ThemeChoice.System, nameof(Strings.Set_Theme_System)), (ThemeChoice.Light, nameof(Strings.Set_Theme_Light)), (ThemeChoice.Dark, nameof(Strings.Set_Theme_Dark)),
             (ThemeChoice.HighContrast, nameof(Strings.Set_Theme_HighContrast))],
            s => s.Window.Theme, (s, v) => s.Window.Theme = v),
        new ChoiceItem(SettingsPage.Appearance, nameof(Strings.Set_Accent), nameof(Strings.Set_Accent_Desc),
            [new(AccentChoice.Hotline, nameof(Strings.Set_Accent_Hotline)), new(AccentChoice.Windows, nameof(Strings.Set_Accent_Windows)),
             new(CustomAccent, nameof(Strings.Set_Accent_Custom))],
            s => AccentChoice.IsCustomColor(s.Window.Accent) ? CustomAccent
                : string.Equals(s.Window.Accent?.Trim(), AccentChoice.Windows, StringComparison.OrdinalIgnoreCase) ? AccentChoice.Windows : AccentChoice.Hotline,
            (s, v) =>
            {
                if (v != CustomAccent) s.Window.Accent = v;
                else if (!AccentChoice.IsCustomColor(s.Window.Accent)) s.Window.Accent = Theming.ThemeTokens.Dark.Accent.Replace("#FF", "#"); // start from the purple
            })
        { RefreshPage = true },
        new TextItem(SettingsPage.Appearance, nameof(Strings.Set_AccentColor), nameof(Strings.Set_AccentColor_Desc), "#E81123",
            s => AccentChoice.IsCustomColor(s.Window.Accent) ? s.Window.Accent.Trim().ToUpperInvariant() : null,
            (s, v) =>
            {
                if (AccentChoice.IsCustomColor(v)) s.Window.Accent = v!.Trim().ToUpperInvariant();
                else if (AccentChoice.IsCustomColor(s.Window.Accent)) s.Window.Accent = AccentChoice.Hotline; // cleared: back to purple
                // otherwise ("hotline" / "windows" chosen above) an empty box changes nothing
            })
        { Validate = AccentChoice.IsCustomColor, Invalid = nameof(Strings.Set_AccentColor_Invalid) },
        Choice(SettingsPage.Appearance, nameof(Strings.Set_Backdrop), nameof(Strings.Set_Backdrop_Desc),
            [(BackdropKind.Acrylic, nameof(Strings.Set_Backdrop_Acrylic)), (BackdropKind.AcrylicThin, nameof(Strings.Set_Backdrop_AcrylicThin)),
             (BackdropKind.Mica, nameof(Strings.Set_Backdrop_Mica)), (BackdropKind.Solid, nameof(Strings.Set_Backdrop_Solid))],
            s => s.Window.Backdrop, (s, v) => s.Window.Backdrop = v),
        new NumberItem(SettingsPage.Appearance, nameof(Strings.Set_Tint), nameof(Strings.Set_Tint_Desc), 0, 1, 0.05, null,
            s => s.Window.TintOpacity, (s, v) => s.Window.TintOpacity = v),
        new NumberItem(SettingsPage.Appearance, nameof(Strings.Set_Luminosity), nameof(Strings.Set_Luminosity_Desc), 0, 1, 0.05, null,
            s => s.Window.LuminosityOpacity, (s, v) => s.Window.LuminosityOpacity = v),
        new NumberItem(SettingsPage.Appearance, nameof(Strings.Set_TextSize), nameof(Strings.Set_TextSize_Desc), 10, 32, 1, "px",
            s => s.Window.FontSize ?? Theming.ThemeTokens.DefaultFontSize, (s, v) => s.Window.FontSize = (int)v == Theming.ThemeTokens.DefaultFontSize ? null : (int)v),
        new TextItem(SettingsPage.Appearance, nameof(Strings.Set_Font), nameof(Strings.Set_Font_Desc), "Segoe UI Variable",
            s => s.Window.FontFamily, (s, v) => s.Window.FontFamily = Blank(v)),
        Choice(SettingsPage.Appearance, nameof(Strings.Set_Scrollbar), null,
            [(ScrollbarStyle.Auto, nameof(Strings.Set_Scrollbar_Auto)), (ScrollbarStyle.Visible, nameof(Strings.Set_Scrollbar_Always)), (ScrollbarStyle.Hidden, nameof(Strings.Set_Scrollbar_Never))],
            s => s.Window.Scrollbar, (s, v) => s.Window.Scrollbar = v),

        new NumberItem(SettingsPage.Window, nameof(Strings.Set_Width), nameof(Strings.Set_Width_Desc), 20, 90, 1, "%", s => s.Window.WidthPercent, (s, v) => s.Window.WidthPercent = v),
        new NumberItem(SettingsPage.Window, nameof(Strings.Set_MinWidth), null, 320, 4000, 10, "DIP", s => s.Window.MinWidth, (s, v) => s.Window.MinWidth = (int)v),
        new NumberItem(SettingsPage.Window, nameof(Strings.Set_MaxWidth), null, 320, 4000, 10, "DIP", s => s.Window.MaxWidth, (s, v) => s.Window.MaxWidth = (int)v),
        new NumberItem(SettingsPage.Window, nameof(Strings.Set_MinHeight), nameof(Strings.Set_MinHeight_Desc), 0, 4000, 10, "DIP",
            s => s.Window.Height, (s, v) => s.Window.Height = (int)v),
        new NumberItem(SettingsPage.Window, nameof(Strings.Set_MaxHeight), nameof(Strings.Set_MaxHeight_Desc), 30, 95, 1, "%",
            s => s.Window.MaxHeightPercent, (s, v) => s.Window.MaxHeightPercent = v),
        new NumberItem(SettingsPage.Window, nameof(Strings.Set_Position), nameof(Strings.Set_Position_Desc), 0, 1, 0.05, null,
            s => s.Window.Anchor.Y, (s, v) => s.Window.Anchor.Y = v),
        Choice(SettingsPage.Window, nameof(Strings.Set_Growth), null, [(GrowMode.Grow, nameof(Strings.Set_Growth_Fit)), (GrowMode.Full, nameof(Strings.Set_Growth_Full))],
            s => s.Chat.GrowMode, (s, v) => s.Chat.GrowMode = v),
        new NumberItem(SettingsPage.Window, nameof(Strings.Set_PositionX), nameof(Strings.Set_PositionX_Desc), 0, 1, 0.05, null,
            s => s.Window.Anchor.X, (s, v) => s.Window.Anchor.X = v),
        new TextItem(SettingsPage.Window, nameof(Strings.Set_Monitor), nameof(Strings.Set_Monitor_Desc), PanelAnchor.Active,
            s => s.Window.Anchor.Monitor, (s, v) => s.Window.Anchor.Monitor = string.IsNullOrWhiteSpace(v) ? PanelAnchor.Active : v.Trim()),
        new NumberItem(SettingsPage.Window, nameof(Strings.Set_FixedWidth), nameof(Strings.Set_FixedWidth_Desc), 0, 8000, 10, "DIP",
            s => s.Window.Width ?? 0, (s, v) => s.Window.Width = v <= 0 ? null : Math.Clamp((int)Math.Round(v), 320, 8000)),
        new NumberItem(SettingsPage.Window, nameof(Strings.Set_ChatHeight), nameof(Strings.Set_ChatHeight_Desc), 0, 8000, 10, "DIP",
            s => s.Window.ChatHeight ?? 0, (s, v) => s.Window.ChatHeight = v <= 0 ? null : Math.Clamp((int)Math.Round(v), 60, 8000)),
        Choice(SettingsPage.Window, nameof(Strings.Set_Direction), nameof(Strings.Set_Direction_Desc),
            [(GrowDirection.Auto, nameof(Strings.Set_Direction_Auto)), (GrowDirection.Up, nameof(Strings.Set_Direction_Up)), (GrowDirection.Down, nameof(Strings.Set_Direction_Down))],
            s => s.Window.GrowDirection, (s, v) => s.Window.GrowDirection = v),

        new ToggleItem(SettingsPage.Chat, nameof(Strings.Set_NotifyDone), nameof(Strings.Set_NotifyDone_Desc), s => s.Chat.NotifyWhenDone, (s, v) => s.Chat.NotifyWhenDone = v),
        new ToggleItem(SettingsPage.Chat, nameof(Strings.Set_Thinking), nameof(Strings.Set_Thinking_Desc),
            s => s.Chat.ThinkingIndicator, (s, v) => s.Chat.ThinkingIndicator = v),
        new ToggleItem(SettingsPage.Chat, nameof(Strings.Set_History), nameof(Strings.Set_History_Desc), s => s.Chat.SaveHistory, (s, v) => s.Chat.SaveHistory = v, RequiresRestart: true),
        new NumberItem(SettingsPage.Chat, nameof(Strings.Set_Retention), null, 1, 3650, 1, nameof(Strings.Set_Unit_Days), s => s.Chat.HistoryRetentionDays,
            (s, v) => s.Chat.HistoryRetentionDays = (int)v, RequiresRestart: true),
        Choice(SettingsPage.Chat, nameof(Strings.Set_Selection), nameof(Strings.Set_Selection_Desc),
            [(AttachSelectionMode.Auto, nameof(Strings.Set_Selection_Auto)), (AttachSelectionMode.Clipboard, nameof(Strings.Set_Selection_Clipboard)), (AttachSelectionMode.Off, nameof(Strings.Set_Selection_Off))],
            s => s.Chat.AttachSelection, (s, v) => s.Chat.AttachSelection = v),
        new TextItem(SettingsPage.Chat, nameof(Strings.Set_Stop), nameof(Strings.Set_Stop_Desc),
            "Esc", s => s.Chat.StopShortcut, (s, v) => s.Chat.StopShortcut = string.IsNullOrWhiteSpace(v) ? "Esc" : v.Trim())
        { Validate = v => Activation.Hotkey.TryParseShortcut(v, out _), Invalid = nameof(Strings.Set_Stop_Invalid) },
        new ToggleItem(SettingsPage.Chat, nameof(Strings.Set_Memory), nameof(Strings.Set_Memory_Desc),
            s => s.Chat.Memory, (s, v) => s.Chat.Memory = v),
        Choice(SettingsPage.Chat, nameof(Strings.Set_MemoryTool), nameof(Strings.Set_MemoryTool_Desc),
            [(MemoryToolMode.Ask, nameof(Strings.Set_MemoryTool_Ask)), (MemoryToolMode.Allow, nameof(Strings.Set_MemoryTool_Allow)), (MemoryToolMode.Off, nameof(Strings.Set_MemoryTool_Off))],
            s => s.Chat.MemoryTool, (s, v) => s.Chat.MemoryTool = v),
        new ToggleItem(SettingsPage.Chat, nameof(Strings.Set_VoiceSend), nameof(Strings.Set_VoiceSend_Desc),
            s => s.Chat.VoiceAutoSend, (s, v) => s.Chat.VoiceAutoSend = v),
        Choice(SettingsPage.Chat, nameof(Strings.Set_Ocr), nameof(Strings.Set_Ocr_Desc),
            [(OcrMode.Auto, nameof(Strings.Set_Ocr_Auto)), (OcrMode.Always, nameof(Strings.Set_Ocr_Always)), (OcrMode.Off, nameof(Strings.Set_Ocr_Off))],
            s => s.Chat.Ocr, (s, v) => s.Chat.Ocr = v),
        new NumberItem(SettingsPage.Chat, nameof(Strings.Set_ImageSize), nameof(Strings.Set_ImageSize_Desc), 256, 8192, 128, "px",
            s => s.Chat.MaxImagePixels, (s, v) => s.Chat.MaxImagePixels = (int)v),

        new ToggleItem(SettingsPage.Advanced, nameof(Strings.Set_Logging), nameof(Strings.Set_Logging_Desc), s => s.Diagnostics.VerboseLogging,
            (s, v) => s.Diagnostics.VerboseLogging = v),
    ];
}
