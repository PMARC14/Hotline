using Hotline.Core.Activation;

namespace Hotline.Core.Settings;

public sealed class HotlineSettings
{
    public const int CurrentSchemaVersion = 7;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ActivationSettings Activation { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public DiagnosticsSettings Diagnostics { get; set; } = new();
    public ChatSettings Chat { get; set; } = new();
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

public enum ThemeChoice { System, Light, Dark }

/// <summary>Window backdrop material. Acrylic (translucent) is the default look.</summary>
public enum BackdropKind { Acrylic, AcrylicThin, Mica, Solid }

public sealed class WindowSettings
{
    public PopupLayout Layout { get; set; } = PopupLayout.QuickView;
    /// <summary>Panel width as a percentage of the monitor's work area (20–90), clamped to [MinWidth, MaxWidth] DIPs.</summary>
    public double WidthPercent { get; set; } = 40;
    public int MinWidth { get; set; } = 600;
    public int MaxWidth { get; set; } = 1000;
    /// <summary>Minimum panel height in DIPs. 0 = fit the message bar exactly; the panel grows upward with text and replies.</summary>
    public int Height { get; set; }
    /// <summary>The panel grows upward with the conversation to at most this share of the screen height (30–95).</summary>
    public double MaxHeightPercent { get; set; } = 70;
    /// <summary>Where the popup sits vertically: 0 = top, 0.5 = centered, 1 = bottom of the free space.</summary>
    public double VerticalPosition { get; set; } = 0.8;
    public bool HideOnBlur { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    /// <summary>Chat text size in px (10–32); null = the Windows default (scaled by Settings › Accessibility › Text size).</summary>
    public int? FontSize { get; set; }
    /// <summary>Chat font family, e.g. "Cascadia Code"; null = Segoe UI Variable.</summary>
    public string? FontFamily { get; set; }
    public ScrollbarStyle Scrollbar { get; set; } = ScrollbarStyle.Auto;
    public BackdropKind Backdrop { get; set; } = BackdropKind.Acrylic;
    /// <summary>Acrylic tint strength, 0 (clear) to 1 (opaque tint).</summary>
    public double TintOpacity { get; set; } = 0.15;
    /// <summary>Acrylic luminosity layer, 0 (most see-through) to 1.</summary>
    public double LuminosityOpacity { get; set; } = 0.35;
}

public enum BackendType { Antigravity, Gemini, OpenAiCompatible, ClaudeCode, Anthropic, Local }

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
    /// Replaces Hotline's built-in launch flags for this connection's mode (e.g. agy's --agent, Claude Code's chat-only
    /// isolation). The flags Hotline needs to talk to the CLI (print mode, stream-json, no permission prompts) are
    /// always added; dangerous flags only come from ApproveAllTools. Null = Hotline's defaults.
    /// </summary>
    public List<string>? Args { get; set; }
    /// <summary>Let the CLI keep its own copy of these chats (agy always does; Claude Code only when true).</summary>
    public bool KeepCliSessions { get; set; }
    /// <summary>Anthropic API: if a safety check declines a request, let the API re-serve it with a suitable model.</summary>
    public bool RefusalFallback { get; set; } = true;
    /// <summary>Tool use for CLI connections (agy, Claude Code).</summary>
    public ToolMode Tools { get; set; } = ToolMode.ChatOnly;
    /// <summary>
    /// DANGEROUS. Inherit mode only: auto-approve every tool request (agy --dangerously-skip-permissions), including
    /// shell commands and file edits, with no prompt. Off: the CLI's own permission rules apply (in the background,
    /// anything that would ask is denied).
    /// </summary>
    public bool ApproveAllTools { get; set; }
    /// <summary>Folder the CLI works in when Tools = Inherit. Null or missing = your user folder.</summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>System prompt name (file ~/.hotline/prompts/&lt;name&gt;.md). Null = chat.defaultPrompt.</summary>
    public string? Prompt { get; set; }
}

public sealed class ChatSettings
{
    public string DefaultBackend { get; set; } = "agy";
    /// <summary>System prompt used by connections that don't pick their own.</summary>
    public string DefaultPrompt { get; set; } = "default";
    /// <summary>
    /// The connections, in dropdown order. Stored one file each in ~/.hotline/connections/&lt;id&gt;.json (not in
    /// settings.json; an old settings.json "backends" list is moved there once).
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<BackendProfile> Backends { get; set; } = DefaultBackends();
    /// <summary>Dropdown order: connection ids. Connections not listed follow, by name.</summary>
    public List<string> Order { get; set; } = [];
    /// <summary>Grow = fit the conversation (up to the maximum height); Full = jump to the maximum height once there are messages.</summary>
    public GrowMode GrowMode { get; set; } = GrowMode.Grow;
    public bool SaveHistory { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 30;
    /// <summary>Longest edge for attached/captured images (pixels).</summary>
    public int MaxImagePixels { get; set; } = 2048;

    public static List<BackendProfile> DefaultBackends() =>
    [
        new BackendProfile { Id = "agy", Type = BackendType.Antigravity, Name = "Gemini (Antigravity)", Agent = "hotline" },
    ];
}

/// <summary>Auto = thin and transparent until hovered; Visible = always shown; Hidden = never shown (still scrolls).</summary>
public enum ScrollbarStyle { Auto, Visible, Hidden }

public enum GrowMode { Grow, Full }
