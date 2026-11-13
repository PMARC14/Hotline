using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class PanelLayoutSettingsTests
{
    [Fact]
    public void Theme_change_keeps_layout_equal()
    {
        var settings = new HotlineSettings();
        settings.Chat.Backends.Add(new BackendProfile { Id = "agy", Type = BackendType.Antigravity, Model = "gemini-3.8-flash-low" });
        settings.Chat.DefaultBackend = "agy";

        var before = PanelLayoutSettings.From(settings.Window);

        settings.Window.Theme = ThemeChoice.Dark;
        settings.Window.Backdrop = BackdropKind.Mica;
        settings.Window.TintOpacity = 0.5;
        settings.Window.LuminosityOpacity = 0.7;
        settings.Window.Accent = "#123456";
        settings.Window.FontSize = 18;
        settings.Window.FontFamily = "Cascadia Code";
        settings.Window.Scrollbar = ScrollbarStyle.Visible;
        settings.Window.AlwaysOnTop = false;
        settings.Window.HideOnBlur = false;
        settings.Window.OpenSound = "chime.wav";
        settings.Window.CloseSound = "chime.wav";
        settings.Chat.Backends[0].Model = "gemini-3.8-flash-high";
        settings.Chat.Backends[0].Effort = "high";

        var after = PanelLayoutSettings.From(settings.Window);

        Assert.Equal(before, after);
        Assert.True(before == after);
        Assert.False(before != after);
    }

    public static TheoryData<string, Action<WindowSettings>> LayoutFieldMutations => new()
    {
        { "Anchor.Monitor", w => w.Anchor.Monitor = PanelAnchor.Primary },
        { "Anchor.X", w => w.Anchor.X = 0.25 },
        { "Anchor.Y", w => w.Anchor.Y = 0.35 },
        { "Width", w => w.Width = 900 },
        { "WidthPercent", w => w.WidthPercent = 55 },
        { "MinWidth", w => w.MinWidth = 500 },
        { "MaxWidth", w => w.MaxWidth = 1600 },
        { "Height", w => w.Height = 120 },
        { "ChatWidth", w => w.ChatWidth = 1100 },
        { "ChatWidthPercent", w => w.ChatWidthPercent = 65 },
        { "ChatHeight", w => w.ChatHeight = 700 },
        { "MaxHeightPercent", w => w.MaxHeightPercent = 75 },
        { "GrowDirection", w => w.GrowDirection = GrowDirection.Down },
    };

    [Theory]
    [MemberData(nameof(LayoutFieldMutations))]
    public void Mutating_each_layout_field_makes_snapshot_unequal(string field, Action<WindowSettings> mutate)
    {
        var window = new WindowSettings();
        var before = PanelLayoutSettings.From(window);

        mutate(window);
        var after = PanelLayoutSettings.From(window);

        Assert.True(before != after, $"Expected mutating {field} to produce an unequal PanelLayoutSettings snapshot.");
        Assert.NotEqual(before, after);
    }
}
