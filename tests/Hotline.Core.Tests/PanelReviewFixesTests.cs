using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

/// <summary>Findings of the Argon review of the panel-placement branch (2026-10-17).</summary>
public sealed class PanelReviewFixesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
    private static readonly RectI Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Resize_from_a_rect_partly_off_the_work_area_keeps_the_minimum_size()
    {
        var r = PanelPlacement.Resize(new RectI(-100, 300, 350, 400), ResizeEdge.Left, -1, 0, 320, 100, Work);
        Assert.True(r.Width >= 320 && r.Height == 400, r.ToString());
        Assert.True(r.X >= Work.X);
    }

    [Fact]
    public void Number_boxes_step_up_from_automatic_and_clamp_instead_of_switching_off()
    {
        var width = SettingsSchema.Items.OfType<NumberItem>().Single(i => i.Header == nameof(Strings.Set_FixedWidth));
        var chat = SettingsSchema.Items.OfType<NumberItem>().Single(i => i.Header == nameof(Strings.Set_ChatHeight));
        var s = new HotlineSettings();
        width.Set(s, 10);
        chat.Set(s, 10);
        Assert.Equal((320, 60), (s.Window.Width, s.Window.ChatHeight));
        width.Set(s, 0);
        Assert.Null(s.Window.Width);
    }

    [Fact]
    public void A_null_sound_in_the_file_reads_as_off()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), """{ "window": { "openSound": null, "closeSound": "  " } }""");
        var s = new SettingsStore(_dir).Load();
        Assert.Equal(("off", "off"), (s.Window.OpenSound, s.Window.CloseSound));
    }

    [Fact]
    public void Windows_sound_events_may_have_spaces_and_bare_file_names_resolve_in_the_media_folder()
    {
        var media = Path.Combine(_dir, "Media");
        Directory.CreateDirectory(media);
        File.WriteAllBytes(Path.Combine(media, "Windows Logon.wav"), [1]);
        var choice = Sounds.Parse("windows:Windows Logon");
        Assert.Equal(Path.Combine(media, "Windows Logon.wav"), Sounds.Resolve(_dir, choice, _ => "Windows Logon.wav", media));
        Assert.Null(Sounds.Resolve(_dir, Sounds.Parse(@"windows:a\b"), _ => "x.wav", media)); // not an event name
        Assert.Null(Sounds.Resolve(_dir, choice, _ => @"\\server\share\x.wav", media)); // no network paths
        File.WriteAllBytes(Path.Combine(media, "x.mp3"), [1]);
        Assert.Null(Sounds.Resolve(_dir, choice, _ => Path.Combine(media, "x.mp3"), media)); // .wav only
    }

    [Fact]
    public void Moving_a_spacer_between_rows_moves_only_that_one()
    {
        var (top, items) = ToolbarConfig.ToRow(["spacer", "pin"], ["spacer", "settings", "spacer"], index: 2, fromTop: false);
        Assert.Equal(["spacer", "pin", "spacer"], top);
        Assert.Equal(["spacer", "settings"], items);
    }
}
