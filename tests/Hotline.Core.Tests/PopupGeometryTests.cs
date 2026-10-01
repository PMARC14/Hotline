using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class PopupGeometryTests
{
    [Fact]
    public void Centers_in_primary_work_area()
        => Assert.Equal(new RectI(640, 260, 640, 520),
            PopupGeometry.CenterIn(new RectI(0, 0, 1920, 1040), 640, 520, 1.0));

    [Fact]
    public void Applies_dpi_scale()
        => Assert.Equal(new RectI(1440, 330, 960, 780),
            PopupGeometry.CenterIn(new RectI(0, 0, 3840, 1440), 640, 520, 1.5));

    [Fact]
    public void Handles_monitor_left_of_primary_with_negative_coordinates()
        => Assert.Equal(new RectI(-1280, 280, 640, 520),
            PopupGeometry.CenterIn(new RectI(-1920, 0, 1920, 1080), 640, 520, 1.0));

    [Fact]
    public void Clamps_to_work_area_when_too_big()
        => Assert.Equal(new RectI(100, 50, 800, 600),
            PopupGeometry.CenterIn(new RectI(100, 50, 800, 600), 2000, 2000, 1.0));
}
