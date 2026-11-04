using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class FlashCoreTests : TempDirTest
{
    [Fact]
    public void Missing_connection_type_is_rejected_and_left_untouched()
    {
        var store = new SettingsStore(_dir);
        store.Load();
        Directory.CreateDirectory(store.ConnectionsDirectory);
        var path = store.ConnectionPath("missing");
        const string text = """{ "name": "Remote Server" }""";
        File.WriteAllText(path, text);
        Assert.Empty(store.Load().Chat.Backends);
        Assert.Contains(path, store.BrokenConnectionFiles);
        Assert.Single(store.SettingsProblems);
        Assert.Equal(text, File.ReadAllText(path));
    }

    [Theory]
    [InlineData(0, 200)]
    [InlineData(-1080, -880)]
    public void Grow_up_caps_height_at_the_fixed_bottom_edge(int workY, int barY)
    {
        var bar = new RectI(400, barY, 600, 50);
        var grown = PopupGeometry.GrowUp(bar, 500, 800, new RectI(0, workY, 1920, 1080));
        Assert.Equal(bar.Y + bar.Height, grown.Y + grown.Height);
        Assert.Equal(workY, grown.Y);
    }

    [Theory]
    [InlineData("con")]
    [InlineData("AUX")]
    [InlineData("com1.extra")]
    [InlineData("LPT9")]
    public void Connection_paths_do_not_address_DOS_devices(string id)
    {
        var stem = Path.GetFileName(new SettingsStore(_dir).ConnectionPath(id)).Split('.')[0];
        Assert.Equal("_" + id.Split('.')[0], stem);
    }
}
