using Hotline.Core.Windowing;
using Xunit;

namespace Hotline.Core.Tests;

public class WindowSizingTests
{
    [Fact]
    public void Centered_clamps_scaled_settings_to_work_area()
    {
        var work = new RectI(1366, 0, 1366, 720);
        var target = WindowSizing.Centered(work, 1000, 740, 1.5);
        Assert.Equal(new RectI(1366, 0, 1366, 720), target);
    }

    [Fact]
    public void Centered_clamps_height_and_centres_width_at_100_percent()
    {
        var work = new RectI(1366, 0, 1366, 720);
        var target = WindowSizing.Centered(work, 1000, 740, 1.0);
        Assert.Equal(new RectI(1549, 0, 1000, 720), target);
    }

    [Fact]
    public void Centered_centres_window_inside_larger_work_area()
    {
        var work = new RectI(0, 0, 1920, 1080);
        var target = WindowSizing.Centered(work, 1000, 740, 1.0);
        Assert.Equal(new RectI(460, 170, 1000, 740), target);
    }

    [Fact]
    public void Centered_supports_negative_origins()
    {
        var work = new RectI(-1920, -200, 1920, 1040);
        var target = WindowSizing.Centered(work, 1000, 740, 1.0);
        Assert.Equal(new RectI(-1460, -50, 1000, 740), target);
    }

    [Fact]
    public void Centered_clamps_to_tiny_work_area()
    {
        var work = new RectI(100, 100, 300, 200);
        var target = WindowSizing.Centered(work, 1000, 740, 1.0);
        Assert.Equal(new RectI(100, 100, 300, 200), target);
    }
}
