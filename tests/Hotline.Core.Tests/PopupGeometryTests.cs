using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class PopupGeometryTests
{
    [Fact]
    public void Centers_in_primary_work_area_at_half()
        => Assert.Equal(new RectI(640, 260, 640, 520),
            PopupGeometry.Place(new RectI(0, 0, 1920, 1040), 640, 520, 1.0, verticalPosition: 0.5));

    [Fact]
    public void Applies_dpi_scale()
        => Assert.Equal(new RectI(1440, 330, 960, 780),
            PopupGeometry.Place(new RectI(0, 0, 3840, 1440), 640, 520, 1.5, verticalPosition: 0.5));

    [Fact]
    public void Handles_monitor_left_of_primary_with_negative_coordinates()
        => Assert.Equal(new RectI(-1280, 280, 640, 520),
            PopupGeometry.Place(new RectI(-1920, 0, 1920, 1080), 640, 520, 1.0, verticalPosition: 0.5));

    [Fact]
    public void Clamps_to_work_area_when_too_big()
        => Assert.Equal(new RectI(100, 50, 800, 600),
            PopupGeometry.Place(new RectI(100, 50, 800, 600), 2000, 2000, 1.0, verticalPosition: 0.8));

    [Theory]
    [InlineData(0.0, 0)]      // top edge of the work area
    [InlineData(1.0, 920)]    // bottom edge (1040 - 120)
    [InlineData(0.8, 736)]    // default: lower part of the screen, like Copilot's quick view
    public void Vertical_position_is_a_fraction_of_free_space(double position, int expectedY)
        => Assert.Equal(expectedY, PopupGeometry.Place(new RectI(0, 0, 1920, 1040), 560, 120, 1.0, position).Y);

    [Theory]
    [InlineData(-3.0, 0)]
    [InlineData(7.0, 920)]
    public void Out_of_range_vertical_position_is_clamped(double position, int expectedY)
        => Assert.Equal(expectedY, PopupGeometry.Place(new RectI(0, 0, 1920, 1040), 560, 120, 1.0, position).Y);
}
