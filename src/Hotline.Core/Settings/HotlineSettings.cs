using Hotline.Core.Activation;

namespace Hotline.Core.Settings;

public sealed class HotlineSettings
{
    public const int CurrentSchemaVersion = 14;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public GeneralSettings General { get; set; } = new();
    public ActivationSettings Activation { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public DiagnosticsSettings Diagnostics { get; set; } = new();
    public ChatSettings Chat { get; set; } = new();
}

public sealed class GeneralSettings
{
    /// <summary>Interface language: "system" follows Windows, or a tag from Languages.Supported (e.g. "de"). Applies on restart.</summary>
    public string Language { get; set; } = Text.Localization.Languages.System;

    /// <summary>Show the "Enjoying Hotline?" donation note once, the next time the panel opens after an answer (then it turns itself off).</summary>
    public bool SupportReminder { get; set; } = true;
}

public sealed class DiagnosticsSettings
{
    /// <summary>Write DEBUG lines to the log and show the key-status line in the popup. Debug builds force this on.</summary>
    public bool VerboseLogging { get; set; }
}

public sealed class ActivationSettings
{
    public KeyAction Tap { get; set; } = KeyAction.TogglePopup;
    /// <summary>Long press: start a new chat (and open the popup).</summary>
    public KeyAction Hold { get; set; } = KeyAction.NewChat;
    /// <summary>Optional extra hotkey, e.g. "Ctrl+Alt+H". Null or empty = off.</summary>
    public string? FallbackHotkey { get; set; }
}

public enum PopupLayout { QuickView, CommandBar, SidePanel }

/// <summary>HighContrast: Hotline's own white-on-black theme (a Windows contrast theme is followed automatically either way).</summary>
public enum ThemeChoice { System, Light, Dark, HighContrast }

/// <summary>Window backdrop material. Acrylic (translucent) is the default look.</summary>
public enum BackdropKind { Acrylic, AcrylicThin, Mica, Solid }

public sealed class WindowSettings
{
    public PopupLayout Layout { get; set; } = PopupLayout.QuickView;
    /// <summary>Panel width as a percentage of the monitor's work area (20–90), clamped to [MinWidth, MaxWidth] DIPs.</summary>
    public double WidthPercent { get; set; } = 40;
    public int MinWidth { get; set; } = 680;
    public int MaxWidth { get; set; } = 1000;
    /// <summary>Minimum panel height in DIPs. 0 = fit the message bar exactly; the panel grows upward with text and replies.</summary>
    public int Height { get; set; }
    /// <summary>The panel grows upward with the conversation to at most this share of the screen height (30–95).</summary>
    public double MaxHeightPercent { get; set; } = 70;
    /// <summary>Where the panel opens (its saved default spot): monitor and fractions of the free work area.</summary>
    public PanelAnchor Anchor { get; set; } = new();
    /// <summary>Panel width in DIPs; null = WidthPercent clamped to [MinWidth, MaxWidth].</summary>
    public int? Width { get; set; }
    /// <summary>Most the chat grows to, in DIPs; null = MaxHeightPercent of the screen.</summary>
    public int? ChatHeight { get; set; }
    /// <summary>Which way the chat grows from the bar: Auto (up unless there's no room above), or locked Up/Down.</summary>
    public GrowDirection GrowDirection { get; set; } = GrowDirection.Auto;
    /// <summary>Sound when the panel opens: "off", "windows:&lt;event&gt;" (the Windows sound scheme) or a .wav in ~/.hotline/sounds.</summary>
    public string OpenSound { get; set; } = "off";
    /// <summary>Sound when the panel closes (same values as OpenSound).</summary>
    public string CloseSound { get; set; } = "off";
    /// <summary>Before v13: the vertical position, now Anchor.Y (read once, then dropped from the file).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public double? VerticalPosition { get; set; }
    public bool HideOnBlur { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    /// <summary>"hotline" (the built-in purple), "windows" (follows the Windows accent) or a "#RRGGBB" colour.</summary>
    public string Accent { get; set; } = Theming.AccentChoice.Hotline;
    /// <summary>Chat text size in px (10–32); null = the Windows default (scaled by Settings › Accessibility › Text size).</summary>
    public int? FontSize { get; set; }
    /// <summary>Chat font family, e.g. "Cascadia Code"; null = Segoe UI Variable.</summary>
    public string? FontFamily { get; set; }
    public ScrollbarStyle Scrollbar { get; set; } = ScrollbarStyle.Auto;
    public BackdropKind Backdrop { get; set; } = BackdropKind.AcrylicThin;
    /// <summary>Acrylic tint strength, 0 (clear) to 1 (opaque tint).</summary>
    public double TintOpacity { get; set; } = 0.15;
    /// <summary>Acrylic luminosity layer, 0 (most see-through) to 1.</summary>
    public double LuminosityOpacity { get; set; } = 0.35;
}

public enum BackendType { Antigravity, Gemini, OpenAiCompatible, ClaudeCode, Anthropic, Local, Codex }

/// <summary>ChatOnly: answers only (attachments readable). Inherit: the CLI's own tools and permission rules.</summary>
public enum ToolMode { ChatOnly, Inherit }

public sealed class BackendProfile
{
    public string Id { get; set; } = "";
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
    /// <summary>Extra CLI arguments, space separated.</summary>
    public string? ExtraArgs { get; set; }
    /// <summary>
    /// Replaces Hotline's usual extra launch flags for this connection's mode. The flags Hotline needs to talk to the CLI
    /// (print mode, stream-json) and, in chat-only mode, the isolation flags that keep it from using its own tools are always
    /// added; flags that approve tools or skip permission checks are dropped from this list: only ApproveAllTools turns them
    /// on. Null = Hotline's defaults.
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
    /// DANGEROUS. Inherit mode only: auto-approve every tool request (agy/Claude Code --dangerously-skip-permissions, Codex
    /// --dangerously-bypass-approvals-and-sandbox), including
    /// shell commands and file edits, with no prompt. Off: the CLI's own permission rules apply (in the background,
    /// anything that would ask is denied).
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
    public string DefaultBackend { get; set; } = "";
    /// <summary>System prompt used by connections that don't pick their own.</summary>
    public string DefaultPrompt { get; set; } = "default";
    /// <summary>
    /// The connections, in dropdown order. Stored one file each in ~/.hotline/connections/&lt;id&gt;.json (not in
    /// settings.json; an old settings.json "backends" list is moved there once).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<BackendProfile> Backends { get; set; } = [];
    /// <summary>Dropdown order: connection ids. Connections not listed follow, by name.</summary>
    public List<string> Order { get; set; } = [];
    /// <summary>Grow = fit the conversation (up to the maximum height); Full = jump to the maximum height once there are messages.</summary>
    public GrowMode GrowMode { get; set; } = GrowMode.Grow;
    public bool SaveHistory { get; set; } = true;
    /// <summary>A Windows notification when an answer finishes while the panel is hidden or another app is in front.</summary>
    public bool NotifyWhenDone { get; set; } = true;
    /// <summary>How many saved chats the history list reads (each is opened for its title). settings.json only.</summary>
    public int HistoryListSize { get; set; } = 300;
    /// <summary>While an answer streams and no text has come for <see cref="ThinkingDelayMs"/>, the cursor spins.</summary>
    public bool ThinkingIndicator { get; set; } = true;
    /// <summary>How long without new text before the thinking indicator shows (ms, 0–10000). settings.json only.</summary>
    public int ThinkingDelayMs { get; set; } = 700;
    /// <summary>"/resume" and a space opens the history list right away, searching as you type. settings.json only.</summary>
    public bool ResumeWhileTyping { get; set; } = true;
    /// <summary>
    /// A reopened CLI chat whose messages Hotline didn't keep shows them from the CLI's own history (agy, Claude Code,
    /// Codex). settings.json only.
    /// </summary>
    public bool CliTranscripts { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 30;
    /// <summary>Longest edge for attached/captured images (pixels).</summary>
    public int MaxImagePixels { get; set; } = 2048;
    /// <summary>Selected text in the app you came from becomes an attachment when the key opens the panel.</summary>
    public AttachSelectionMode AttachSelection { get; set; } = AttachSelectionMode.Auto;
    /// <summary>Screenshot text for models that can't read images (Windows' built-in OCR, offline).</summary>
    public OcrMode Ocr { get; set; } = OcrMode.Auto;
    /// <summary>Voice key action: send the message when the key is released (off = review it first).</summary>
    public bool VoiceAutoSend { get; set; }
    /// <summary>Stops a streaming answer: a key alone ("Esc") or a combo ("Ctrl+."). Enter never stops an answer.</summary>
    public string StopShortcut { get; set; } = "Esc";
    /// <summary>Add ~/.hotline/memory.md to every chat's system prompt.</summary>
    public bool Memory { get; set; } = true;
    /// <summary>Whether API models may save to memory.md with the remember tool (Ask = approve each one in the panel).</summary>
    public MemoryToolMode MemoryTool { get; set; } = MemoryToolMode.Ask;

    /// <summary>
    /// The agy connection settings files before v12 got when they had none of their own (no connection files). New
    /// installs start with no connection and ask; only those older files keep it.
    /// </summary>
    public static List<BackendProfile> LegacyDefaultBackends() =>
    [
        new BackendProfile { Id = "agy", Type = BackendType.Antigravity, Name = "Gemini (Antigravity)", Agent = "hotline" },
    ];
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
    public string Monitor { get; set; } = Active;
    public double X { get; set; } = 0.5;
    public double Y { get; set; } = 0.8;
}

/// <summary>
/// Auto = read the selection through UI Automation (no keystrokes, clipboard untouched; some apps don't expose it);
/// Off; Clipboard = also try Ctrl+C and put the clipboard back afterwards (works in more apps, briefly uses the clipboard).
/// </summary>
public enum AttachSelectionMode { Auto, Off, Clipboard }

/// <summary>Auto = OCR images for connections that can't read them; Always = also add the text for image models; Off.</summary>
public enum OcrMode { Auto, Always, Off }

public enum MemoryToolMode { Ask, Allow, Off }
