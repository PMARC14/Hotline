using Hotline.Core.Activation;

namespace Hotline.Core.Settings;

public sealed class HotlineSettings
{
    public const int CurrentSchemaVersion = 2;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ActivationSettings Activation { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public DiagnosticsSettings Diagnostics { get; set; } = new();
}

public sealed class DiagnosticsSettings
{
    /// <summary>Write DEBUG lines to the log and show the key-status line in the popup. Debug builds force this on.</summary>
    public bool VerboseLogging { get; set; }
}

public sealed class ActivationSettings
{
    public KeyAction Tap { get; set; } = KeyAction.TogglePopup;
    public KeyAction Hold { get; set; } = KeyAction.ShowPopup;
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
    public bool HideOnBlur { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
    public BackdropKind Backdrop { get; set; } = BackdropKind.Acrylic;
    /// <summary>Acrylic tint strength, 0 (clear) to 1 (opaque tint).</summary>
    public double TintOpacity { get; set; } = 0.15;
    /// <summary>Acrylic luminosity layer, 0 (most see-through) to 1.</summary>
    public double LuminosityOpacity { get; set; } = 0.35;
}
