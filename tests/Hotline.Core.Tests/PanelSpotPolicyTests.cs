using Hotline.Core.Settings;
using Hotline.Core.State;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class PanelSpotPolicyTests : TempDirTest
{
    private static readonly RectI Work = new(0, 0, 2880, 1700);
    private string StatePath => Path.Combine(_dir, StateStore.FileName);

    [Theory]
    [InlineData(640, 600, 100, 1.0, 640, 500)]
    [InlineData(1280, 1200, 200, 2.0, 640, 500)]
    [InlineData(960, 950, 200, 1.5, 640, 500)]
    [InlineData(100, null, 100, 1.0, 200, null)]
    [InlineData(5000, null, 100, 1.0, 4000, null)]
    [InlineData(640, 250, 200, 1.0, 640, null)]
    [InlineData(640, 9500, 100, 1.0, 640, 8000)]
    public void BuildSaveSpot_converts_real_pixels_to_scaled_pixels_and_clamps(
        int barWidthPx, int? fixedHeightPx, int barHeightPx, double scale, int expectedWidthDip, int? expectedChatDip)
    {
        var settings = new WindowSettings();
        var bar = new RectI(400, 900, barWidthPx, barHeightPx);

        var spot = PanelSpotPolicy.BuildSaveSpot(bar, Work, scale, fixedHeightPx, barHeightPx, monitorDevice: null, settings);

        Assert.NotNull(spot.Width);
        Assert.Equal(expectedWidthDip, spot.Width.Value);
        Assert.Equal(WidthOver.FromPercent("40%"), spot.Width.Over);

        if (expectedChatDip is { } chat)
        {
            Assert.NotNull(spot.ChatHeight);
            Assert.Equal(chat, spot.ChatHeight.Value);
            Assert.Null(spot.ChatHeight.Over);
        }
        else
        {
            Assert.Null(spot.ChatHeight);
        }

        Assert.NotNull(spot.Anchor);
        Assert.Equal(PanelAnchor.Active, spot.Anchor.Value.Monitor);
        Assert.Equal(PanelAnchor.Active, spot.Anchor.Over?.Monitor);
        Assert.Equal(0.5, spot.Anchor.Over?.X);
        Assert.Equal(0.8, spot.Anchor.Over?.Y);
    }

    [Fact]
    public void BuildSaveSpot_uses_named_monitor_when_provided_and_keeps_active_chat_height_until_collapsed()
    {
        var store = new StateStore(StatePath);
        var settings = new WindowSettings();
        var bar = new RectI(300, 800, 1280, 160);

        var initial = PanelSpotPolicy.BuildSaveSpot(bar, Work, 2.0, fixedHeightPx: 1160, barHeightPx: 160, @"\\.\DISPLAY2", settings, store);
        Assert.True(store.SavePanelSpot(initial));
        Assert.Equal(@"\\.\DISPLAY2", store.GetPanelSpot().Anchor?.Value.Monitor);
        Assert.Equal(500, store.GetPanelSpot().ChatHeight?.Value);

        // Moving without resizing height preserves the active saved chatHeight.
        var moved = PanelSpotPolicy.BuildSaveSpot(bar with { X = 500 }, Work, 2.0, fixedHeightPx: null, barHeightPx: 160, null, settings, store);
        Assert.True(store.SavePanelSpot(moved));
        Assert.Equal(500, store.GetPanelSpot().ChatHeight?.Value);

        // Sizing down to the bar alone clears the saved chatHeight override.
        var collapsed = PanelSpotPolicy.BuildSaveSpot(bar, Work, 2.0, fixedHeightPx: 200, barHeightPx: 160, null, settings, store);
        Assert.True(store.SavePanelSpot(collapsed));
        Assert.Null(store.GetPanelSpot().ChatHeight);
    }

    public static TheoryData<string, string, int?, string, double, double, int, int?, string, double, bool, bool, bool> ResolveCases() =>
        new()
        {
            { "all-equal", "40%", null, "active", 0.5, 0.8, 640, 500, @"\\.\DISPLAY2", 0.3, true, true, true },
            { "width-percent-changed", "50%", null, "active", 0.5, 0.8, 50, 500, @"\\.\DISPLAY2", 0.3, false, true, true },
            { "width-switched-to-px", "700", null, "active", 0.5, 0.8, 700, 500, @"\\.\DISPLAY2", 0.3, false, true, true },
            { "chat-height-changed", "40%", 420, "active", 0.5, 0.8, 640, 420, @"\\.\DISPLAY2", 0.3, true, false, true },
            { "anchor-monitor-changed", "40%", null, "primary", 0.5, 0.8, 640, 500, "primary", 0.5, true, true, false },
            { "anchor-x-changed", "40%", null, "active", 0.6, 0.8, 640, 500, "active", 0.6, true, true, false },
        };

    [Theory]
    [MemberData(nameof(ResolveCases))]
    public void Resolve_applies_saved_items_only_while_setting_equals_over(
        string _,
        string currentWidthText,
        int? currentChatHeight,
        string currentMonitor,
        double currentX,
        double currentY,
        int expectedWidthValue,
        int? expectedChatHeight,
        string expectedMonitor,
        double expectedX,
        bool expectedWidthApplied,
        bool expectedChatApplied,
        bool expectedAnchorApplied)
    {
        var store = new StateStore(StatePath);
        var baseline = new WindowSettings();
        var bar = new RectI(480, 800, 1280, 160);
        var savedSpot = PanelSpotPolicy.BuildSaveSpot(bar, Work, 2.0, fixedHeightPx: 1160, barHeightPx: 160, @"\\.\DISPLAY2", baseline, store);
        Assert.True(store.SavePanelSpot(savedSpot));

        Assert.True(PanelWidth.TryParse(currentWidthText, out var parsedWidth));
        var current = new WindowSettings
        {
            Width = parsedWidth,
            ChatHeight = currentChatHeight,
            Anchor = new PanelAnchor { Monitor = currentMonitor, X = currentX, Y = currentY },
        };

        var effective = PanelSpotPolicy.Resolve(store, current);

        Assert.Equal(expectedWidthApplied, effective.WidthApplied);
        Assert.Equal(expectedChatApplied, effective.ChatHeightApplied);
        Assert.Equal(expectedAnchorApplied, effective.AnchorApplied);
        Assert.Equal(expectedWidthValue, effective.Width.Value);
        Assert.Equal(expectedChatHeight, effective.ChatHeight);
        Assert.Equal(expectedMonitor, effective.Anchor.Monitor);
        Assert.Equal(expectedX, effective.Anchor.X, 6);
    }

    [Theory]
    [InlineData("40%", 2880, 2.0, true, 1280)]
    [InlineData("40%", 1920, 1.0, true, 640)]
    [InlineData("40%", 500, 1.0, true, 500)]
    [InlineData("50%", 2880, 2.0, false, 1440)]
    public void OpeningWidthPixels_uses_saved_scaled_width_until_setting_changes(
        string currentWidthText, int workWidthPx, double scale, bool expectedSavedApplied, int expectedWidthPx)
    {
        var store = new StateStore(StatePath);
        var baseline = new WindowSettings();
        var saved = PanelSpotPolicy.BuildSaveSpot(new RectI(0, 0, 1280, 120), Work, 2.0, null, 120, null, baseline, store);
        Assert.True(store.SavePanelSpot(saved));

        Assert.True(PanelWidth.TryParse(currentWidthText, out var currentWidth));
        var current = new WindowSettings { Width = currentWidth };
        var effective = PanelSpotPolicy.Resolve(store, current);

        Assert.Equal(expectedSavedApplied, effective.WidthApplied);
        Assert.Equal(expectedWidthPx, effective.OpeningWidthPixels(current, workWidthPx, scale));
    }

    [Fact]
    public void A_saved_width_beyond_maxWidth_opens_at_that_width()
    {
        var store = new StateStore(StatePath);
        var settings = new WindowSettings(); // maxWidth 800
        Assert.True(store.SavePanelSpot(PanelSpotPolicy.BuildSaveSpot(new RectI(0, 0, 1000, 120), Work, 1.0, null, 120, null, settings, store)));
        var effective = PanelSpotPolicy.Resolve(store, settings);
        Assert.True(effective.WidthApplied);
        Assert.Equal(1000, effective.OpeningWidthPixels(settings, 1920, 1.0));
    }

    [Fact]
    public void Resolve_handles_null_state_and_pixel_over_width()
    {
        var settings = new WindowSettings { Width = new PanelWidth(640) };
        var noState = PanelSpotPolicy.Resolve(null, settings);
        Assert.False(noState.WidthApplied);
        Assert.Equal(640, noState.Width.Value);

        var store = new StateStore(StatePath);
        var spot = PanelSpotPolicy.BuildSaveSpot(new RectI(0, 0, 720, 120), Work, 1.0, null, 120, null, settings, store);
        Assert.True(store.SavePanelSpot(spot));

        Assert.Equal(720, PanelSpotPolicy.Resolve(store, new WindowSettings { Width = new PanelWidth(640) }).Width.Value);
        Assert.Equal(600, PanelSpotPolicy.Resolve(store, new WindowSettings { Width = new PanelWidth(600) }).Width.Value);
    }

    [Fact]
    public void ToolbarShortcutSession_uses_passed_StateStore_for_lastWritten()
    {
        Directory.CreateDirectory(_dir);
        var toolbarPath = Path.Combine(_dir, "toolbar.json");
        var state = new StateStore(StatePath);
        var session = new ToolbarShortcutSession(toolbarPath, state: state);

        var (changedOnDisk, error) = session.SetShortcut("pin", "Ctrl+Q");
        Assert.False(changedOnDisk);
        Assert.Null(error);
        Assert.Equal(Hotline.Core.Files.SafeFile.Hash(File.ReadAllBytes(toolbarPath)), state.GetLastWritten("toolbar.json"));
    }
}
