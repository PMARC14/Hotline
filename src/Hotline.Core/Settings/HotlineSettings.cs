using Hotline.Core.Activation;
using Hotline.Core.Windowing;
using System.Text.Json.Serialization;

namespace Hotline.Core.Settings;

/// <summary>Limits shared by file validation, normalization and the numeric settings controls.</summary>
public sealed record SettingRange(double Min, double Max, double? Default, bool ZeroToo = false)
{
    public double Clamp(double value) => ZeroToo && (!double.IsFinite(value) || value <= 0) ? 0 : Math.Clamp(value, Min, Max);
    public double ClampFinite(double value) => double.IsFinite(value) ? Clamp(value) : Default ?? Min;
    public int Clamp(int value) => (int)Clamp((double)value);
}

public static class SettingsRanges
{
    public static readonly IReadOnlyDictionary<string, SettingRange> All =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, SettingRange>(new Dictionary<string, SettingRange>
        {
            ["ChatSettings.MaxImagePixels"] = new(256, 8192, SettingsDefaults.Number("chat", "maxImagePixels")),
            ["ChatSettings.HistoryRetentionDays"] = new(1, 3650, SettingsDefaults.Number("chat", "historyRetentionDays")),
            ["ChatSettings.HistoryListSize"] = new(10, 5000, SettingsDefaults.Number("chat", "historyListSize")),
            ["ChatSettings.ThinkingDelayMs"] = new(0, 10000, SettingsDefaults.Number("chat", "thinkingDelayMs")),
            ["WindowSettings.FontSize"] = new(10, 32, SettingsDefaults.Number("window", "fontSize")),
            ["WindowSettings.ChatHeight"] = new(60, 8000, SettingsDefaults.Number("window", "chatHeight")),
            ["WindowSettings.TintOpacity"] = new(0, 1, SettingsDefaults.Number("window", "tintOpacity")),
            ["WindowSettings.LuminosityOpacity"] = new(0, 1, SettingsDefaults.Number("window", "luminosityOpacity")),
            ["WindowSettings.MinWidth"] = new(200, 4000, SettingsDefaults.Number("window", "minWidth")),
            ["WindowSettings.MaxWidth"] = new(200, 4000, SettingsDefaults.Number("window", "maxWidth")),
            ["WindowSettings.MinHeight"] = new(0, 4000, SettingsDefaults.Number("window", "minHeight")),
            ["WindowSettings.MaxHeightPercent"] = new(30, 95, SettingsDefaults.Number("window", "maxHeightPercent")),
            ["PanelAnchor.X"] = new(0, 1, SettingsDefaults.Anchor<double>("x")),
            ["PanelAnchor.Y"] = new(0, 1, SettingsDefaults.Anchor<double>("y")),
        });
}

public sealed class HotlineSettings
{
    public const int CurrentSchemaVersion = SettingsMigrations.CurrentVersion;
    [JsonPropertyName("version")]
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public GeneralSettings General { get; set; } = new();
    public ActivationSettings Activation { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public DiagnosticsSettings Diagnostics { get; set; } = new();
    public ChatSettings Chat { get; set; } = new();
    public ToolsSettings Tools { get; set; } = new();
}

public sealed class GeneralSettings
{
    /// <summary>Interface language: "system" follows Windows, or a tag from Languages.Supported (e.g. "de"). Applies on restart.</summary>
    public string Language { get; set; } = SettingsDefaults.Get<string>("general", "language");

    /// <summary>User preference for showing the support reminder.</summary>
    public bool SupportReminder { get; set; } = SettingsDefaults.Get<bool>("general", "supportReminder");
}

public sealed class DiagnosticsSettings
{
    /// <summary>Write DEBUG lines to the log and show the key-status line in the popup. Debug builds force this on.</summary>
    public bool VerboseLogging { get; set; } = SettingsDefaults.Get<bool>("diagnostics", "verboseLogging");
    /// <summary>Allow diagnostic links (hotline://demo, hotline://selftest, hotline://grips). File-only; off by default.</summary>
    public bool TestLinks { get; set; } = SettingsDefaults.Get<bool>("diagnostics", "testLinks");
}

public sealed class ActivationSettings
{
    public KeyAction Tap { get; set; } = SettingsDefaults.Get<KeyAction>("activation", "tap");
    /// <summary>Long press: start a new chat (and open the popup).</summary>
    public KeyAction Hold { get; set; } = SettingsDefaults.Get<KeyAction>("activation", "hold");
    /// <summary>Optional extra hotkey, e.g. "Ctrl+Alt+H". Null or empty = off.</summary>
    public string? FallbackHotkey { get; set; } = SettingsDefaults.Get<string?>("activation", "fallbackHotkey");
}

/// <summary>HighContrast: Hotline's own white-on-black theme (a Windows contrast theme is followed automatically either way).</summary>
public enum ThemeChoice { System, Light, Dark, HighContrast }

/// <summary>Window backdrop material.</summary>
public enum BackdropKind { Acrylic, AcrylicThin, Mica, Solid }

public sealed class WindowSettings
{
    public PanelWidth Width { get; set; } = SettingsDefaults.Get<PanelWidth>("window", "width");
    public PanelWidth? ChatWidth { get; set; } = SettingsDefaults.Get<PanelWidth?>("window", "chatWidth");
    public int MinWidth { get; set; } = SettingsDefaults.Get<int>("window", "minWidth");
    public int MaxWidth { get; set; } = SettingsDefaults.Get<int>("window", "maxWidth");
    public int MinHeight { get; set; } = SettingsDefaults.Get<int>("window", "minHeight");
    public double MaxHeightPercent { get; set; } = SettingsDefaults.Get<double>("window", "maxHeightPercent");
    public PanelAnchor Anchor { get; set; } = new();
    public GrowMode GrowMode { get; set; } = SettingsDefaults.Get<GrowMode>("window", "growMode");
    /// <summary>Most the chat grows to, in DIPs; null = MaxHeightPercent of the screen.</summary>
    public int? ChatHeight { get; set; } = SettingsDefaults.Get<int?>("window", "chatHeight");
    /// <summary>Which way the chat grows from the bar: Auto (up unless there's no room above), or locked Up/Down.</summary>
    public GrowDirection GrowDirection { get; set; } = SettingsDefaults.Get<GrowDirection>("window", "growDirection");
    /// <summary>Sound when the panel opens: "off", "windows:&lt;event&gt;" (the Windows sound scheme) or a .wav in ~/.hotline/sounds.</summary>
    public string OpenSound { get; set; } = SettingsDefaults.Get<string>("window", "openSound");
    /// <summary>Sound when the panel closes (same values as OpenSound).</summary>
    public string CloseSound { get; set; } = SettingsDefaults.Get<string>("window", "closeSound");
    public bool HideOnBlur { get; set; } = SettingsDefaults.Get<bool>("window", "hideOnBlur");
    public bool AlwaysOnTop { get; set; } = SettingsDefaults.Get<bool>("window", "alwaysOnTop");
    public ThemeChoice Theme { get; set; } = SettingsDefaults.Get<ThemeChoice>("window", "theme");
    /// <summary>"hotline" (the built-in purple), "windows" (follows the Windows accent) or a "#RRGGBB" colour.</summary>
    public string Accent { get; set; } = SettingsDefaults.Get<string>("window", "accent");
    /// <summary>Chat text size in px (10–32); null = the Windows default (scaled by Settings › Accessibility › Text size).</summary>
    public int? FontSize { get; set; } = SettingsDefaults.Get<int?>("window", "fontSize");
    /// <summary>Chat font family, e.g. "Cascadia Code"; null = Segoe UI Variable.</summary>
    public string? FontFamily { get; set; } = SettingsDefaults.Get<string?>("window", "fontFamily");
    public ScrollbarStyle Scrollbar { get; set; } = SettingsDefaults.Get<ScrollbarStyle>("window", "scrollbar");
    public BackdropKind Backdrop { get; set; } = SettingsDefaults.Get<BackdropKind>("window", "backdrop");
    /// <summary>Acrylic tint strength, 0 (clear) to 1 (opaque tint).</summary>
    public double TintOpacity { get; set; } = SettingsDefaults.Get<double>("window", "tintOpacity");
    /// <summary>Acrylic luminosity layer, 0 (most see-through) to 1.</summary>
    public double LuminosityOpacity { get; set; } = SettingsDefaults.Get<double>("window", "luminosityOpacity");
}

public enum BackendType { Antigravity, Gemini, OpenAiCompatible, ClaudeCode, Anthropic, Local, Codex }

/// <summary>ChatOnly: answers only (attachments readable). Inherit: the CLI's own tools and permission rules.</summary>
public enum ToolMode { ChatOnly, Inherit }

public sealed class BackendProfile
{
    [JsonIgnore]
    public string Id { get; set; } = "";
    public int Version { get; set; } = 1;
    public BackendType Type { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Model override (agy: e.g. "gemini-3.8-flash-low"; null = backend default).</summary>
    public string? Model { get; set; }
    /// <summary>Reasoning effort (agy: low | medium | high | max; null = backend default).</summary>
    public string? Effort { get; set; }
    public string? Endpoint { get; set; }
    /// <summary>CLI executable path; null = auto-detect.</summary>
    public string? CliPath { get; set; }
    /// <summary>agy custom agent name (default "hotline").</summary>
    public string? Agent { get; set; }
    /// <summary>This connection's own "Continue this chat in" targets: null = the shared list (chat.continueIn), [] = none.</summary>
    public List<Backends.ContinueTarget>? ContinueIn { get; set; }
    /// <summary>Extra CLI arguments, passed as individual arguments.</summary>
    public List<string>? ExtraArgs { get; set; }
    /// <summary>
    /// Replaces Hotline's usual extra launch flags for this mode (null = defaults). Required and chat-only isolation flags are
    /// always added; flags that approve tools or skip permission checks are dropped: only ApproveAllTools turns them on.
    /// </summary>
    public List<string>? Args { get; set; }
    /// <summary>
    /// CLI connections: chats live in the CLI's own history (one conversation per Hotline chat, resumable there and from
    /// Recent chats). Off: Hotline keeps the chat and replays it (Claude Code then keeps nothing; agy always records).
    /// </summary>
    public bool KeepCliSessions { get; set; } = true;
    /// <summary>CLI connections with keepCliSessions: also keep the messages in Hotline's own history (default: only a record).</summary>
    public bool KeepInHotline { get; set; }
    /// <summary>Anthropic API: if a safety check declines a request, let the API re-serve it with a suitable model.</summary>
    public bool RefusalFallback { get; set; } = true;
    /// <summary>Tool use: CLI connections (agy, Claude Code, Codex) use their own tools; API connections Hotline's MCP tools.</summary>
    public ToolMode Tools { get; set; } = ToolMode.ChatOnly;
    /// <summary>
    /// DANGEROUS. Inherit mode only: auto-approve every tool request, shell commands and file edits included, with no prompt
    /// (--dangerously-skip-permissions / --dangerously-bypass-approvals-and-sandbox). Off: the CLI's own permission rules apply.
    /// </summary>
    public bool ApproveAllTools { get; set; }
    /// <summary>Folder the CLI works in when Tools = Inherit. Null or missing = your user folder.</summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>System prompt name (file ~/.hotline/prompts/&lt;name&gt;.md). Null = chat.defaultPrompt.</summary>
    public string? Prompt { get; set; }
    /// <summary>API connections: most tokens one answer may use. Null = the model's default (Claude API: 64000).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }
    /// <summary>Gemini, OpenAI-compatible and local connections: sampling temperature (0–2). Null = the model's default.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }
    /// <summary>Whether the model reads images. Null = the type's default (local: no; others: yes).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? Images { get; set; }
}

public sealed class ChatSettings
{
    /// <summary>The connection the panel uses. Empty = not chosen yet: a new install asks on first open (and runs no CLI before).</summary>
    [JsonPropertyName("defaultConnection")]
    public string DefaultBackend { get; set; } = SettingsDefaults.Get<string>("chat", "defaultConnection");
    /// <summary>System prompt used by connections that don't pick their own.</summary>
    public string DefaultPrompt { get; set; } = SettingsDefaults.Get<string>("chat", "defaultPrompt");
    /// <summary>The shared "Continue this chat in" targets; null = the default (the llama.cpp web UI of a Local connection).</summary>
    public List<Backends.ContinueTarget>? ContinueIn { get; set; } = SettingsDefaults.Get<List<Backends.ContinueTarget>?>("chat", "continueIn");
    /// <summary>
    /// The connections in dropdown order, stored one file each in connections/&lt;id&gt;.json.
    /// </summary>
    [JsonIgnore]
    public List<BackendProfile> Backends { get; set; } = [];
    /// <summary>Dropdown order: connection ids. Connections not listed follow, by name.</summary>
    public List<string> Order { get; set; } = SettingsDefaults.Get<List<string>>("chat", "order");
    public bool SaveHistory { get; set; } = SettingsDefaults.Get<bool>("chat", "saveHistory");
    /// <summary>A Windows notification when an answer finishes while the panel is hidden or another app is in front.</summary>
    public bool NotifyWhenDone { get; set; } = SettingsDefaults.Get<bool>("chat", "notifyWhenDone");
    /// <summary>Include answer text in completion notifications.</summary>
    public bool NotifyShowsText { get; set; } = SettingsDefaults.Get<bool>("chat", "notifyShowsText");
    /// <summary>How many saved chats the history list reads (each is opened for its title). settings.json only.</summary>
    public int HistoryListSize { get; set; } = SettingsDefaults.Get<int>("chat", "historyListSize");
    /// <summary>While an answer streams and no text has come for <see cref="ThinkingDelayMs"/>, the cursor spins.</summary>
    public bool ThinkingIndicator { get; set; } = SettingsDefaults.Get<bool>("chat", "thinkingIndicator");
    /// <summary>How long without new text before the thinking indicator shows (ms, 0–10000). settings.json only.</summary>
    public int ThinkingDelayMs { get; set; } = SettingsDefaults.Get<int>("chat", "thinkingDelayMs");
    /// <summary>"/resume" and a space opens the history list right away, searching as you type. settings.json only.</summary>
    public bool ResumeWhileTyping { get; set; } = SettingsDefaults.Get<bool>("chat", "resumeWhileTyping");
    /// <summary>
    /// A reopened CLI chat whose messages Hotline didn't keep shows them from the CLI's own history (agy, Claude Code,
    /// Codex). settings.json only.
    /// </summary>
    public bool CliTranscripts { get; set; } = SettingsDefaults.Get<bool>("chat", "cliTranscripts");
    public int HistoryRetentionDays { get; set; } = SettingsDefaults.Get<int>("chat", "historyRetentionDays");
    /// <summary>Longest edge for attached/captured images (pixels).</summary>
    public int MaxImagePixels { get; set; } = SettingsDefaults.Get<int>("chat", "maxImagePixels");
    /// <summary>Selected text in the app you came from becomes an attachment when the key opens the panel.</summary>
    public AttachSelectionMode AttachSelection { get; set; } = SettingsDefaults.Get<AttachSelectionMode>("chat", "attachSelection");
    /// <summary>Screenshot text for models that can't read images (Windows' built-in OCR, offline).</summary>
    public OcrMode Ocr { get; set; } = SettingsDefaults.Get<OcrMode>("chat", "ocr");
    /// <summary>Voice key action: send the message when the key is released (off = review it first).</summary>
    public bool VoiceAutoSend { get; set; } = SettingsDefaults.Get<bool>("chat", "voiceAutoSend");
    [System.Text.Json.Serialization.JsonIgnore]
    public Windowing.ToolbarModel Toolbar { get; set; } = Windowing.ToolbarModel.Defaults;
    /// <summary>Add ~/.hotline/memory.md to every chat's system prompt.</summary>
    public bool Memory { get; set; } = SettingsDefaults.Get<bool>("chat", "memory");
    /// <summary>Whether API models may save to memory.md with the remember tool (Ask = approve each one in the panel).</summary>
    public MemoryToolMode MemoryTool { get; set; } = SettingsDefaults.Get<MemoryToolMode>("chat", "memoryTool");
}

/// <summary>Auto = thin and transparent until hovered; Visible = always shown; Hidden = never shown (still scrolls).</summary>
public enum ScrollbarStyle { Auto, Visible, Hidden }

public enum GrowMode { Grow, Full }

/// <summary>Auto = up unless the chat has no room above (then down); Up/Down lock the direction.</summary>
public enum GrowDirection { Auto, Up, Down }

/// <summary>
/// The panel's home spot. Monitor: "active" (where you're working), "primary", or a monitor's device name (falls back
/// to active when it isn't connected). X/Y: fractions of the monitor's free work area (0 = left/top, 1 = right/bottom).
/// </summary>
public sealed class PanelAnchor
{
    public const string Active = "active", Primary = "primary";
    public string Monitor { get; set; } = SettingsDefaults.Anchor<string>("monitor");
    public double X { get; set; } = SettingsDefaults.Anchor<double>("x");
    public double Y { get; set; } = SettingsDefaults.Anchor<double>("y");
}

/// <summary>
/// Auto = read the selection through UI Automation (no keystrokes, clipboard untouched; some apps don't expose it);
/// Off; Clipboard = also try Ctrl+C and put the clipboard back afterwards (works in more apps, briefly uses the clipboard).
/// </summary>
public enum AttachSelectionMode { Auto, Off, Clipboard }

/// <summary>Auto = OCR images for connections that can't read them; Always = also add the text for image models; Off.</summary>
public enum OcrMode { Auto, Always, Off }

public enum MemoryToolMode { Ask, Allow, Off }

public sealed class ToolsSettings
{
    public Dictionary<string, string> Approvals { get; set; } = SettingsDefaults.Get<Dictionary<string, string>>("tools", "approvals");
}
