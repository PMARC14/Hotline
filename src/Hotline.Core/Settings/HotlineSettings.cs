using Hotline.Core.Activation;

namespace Hotline.Core.Settings;

public sealed class HotlineSettings
{
    public const int CurrentSchemaVersion = 3;
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
    /// <summary>Size in device-independent pixels (scaled by monitor DPI).</summary>
    /// <remarks>Compact input bar like Copilot's quick view; grows with the conversation (later plan).</remarks>
    public int Width { get; set; } = 560;
    public int Height { get; set; } = 120;
    /// <summary>Where the popup sits vertically: 0 = top, 0.5 = centered, 1 = bottom of the free space.</summary>
    public double VerticalPosition { get; set; } = 0.8;
    public bool HideOnBlur { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    /// <summary>Chat text size in px (10–32).</summary>
    public int FontSize { get; set; } = 14;
    /// <summary>Chat font family, e.g. "Cascadia Code"; null = Segoe UI Variable.</summary>
    public string? FontFamily { get; set; }
    public ScrollbarStyle Scrollbar { get; set; } = ScrollbarStyle.Auto;
    public BackdropKind Backdrop { get; set; } = BackdropKind.Acrylic;
    /// <summary>Acrylic tint strength, 0 (clear) to 1 (opaque tint).</summary>
    public double TintOpacity { get; set; } = 0.15;
    /// <summary>Acrylic luminosity layer, 0 (most see-through) to 1.</summary>
    public double LuminosityOpacity { get; set; } = 0.35;
}

public enum BackendType { Antigravity, Gemini, OpenAiCompatible, ClaudeCode }

public sealed class BackendProfile
{
    public string Id { get; set; } = "";
    public BackendType Type { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Model override (agy: e.g. "gemini-3.8-flash-low"; null = backend default).</summary>
    public string? Model { get; set; }
    public string? Endpoint { get; set; }
    /// <summary>CLI executable path; null = auto-detect.</summary>
    public string? CliPath { get; set; }
    /// <summary>agy custom agent name (default "hotline").</summary>
    public string? Agent { get; set; }
    /// <summary>Extra CLI arguments, space separated.</summary>
    public string? ExtraArgs { get; set; }
}

public sealed class ChatSettings
{
    public string DefaultBackend { get; set; } = "agy";
    public List<BackendProfile> Backends { get; set; } = DefaultBackends();
    /// <summary>Popup grows upward from the bar to at most this height (DIPs).</summary>
    public int MaxHeight { get; set; } = 560;
    /// <summary>Grow = fit the conversation (up to MaxHeight); Full = jump to MaxHeight once there are messages.</summary>
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
