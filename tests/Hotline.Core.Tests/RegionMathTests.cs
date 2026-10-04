using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class RegionMathTests
{
    [Theory]
    [InlineData(10, 20, 110, 70, 10, 20, 100, 50)]   // drag down-right
    [InlineData(110, 70, 10, 20, 10, 20, 100, 50)]   // drag up-left: same rectangle
    [InlineData(-30, -30, 50, 40, 0, 0, 50, 40)]     // clamped to the screen
    [InlineData(150, 90, 400, 400, 150, 90, 50, 30)] // clamped at the far edges (200x120 screen)
    public void Selection_is_normalized_and_clamped(double x1, double y1, double x2, double y2, int x, int y, int w, int h)
        => Assert.Equal(new RectI(x, y, w, h), RegionMath.Selection(x1, y1, x2, y2, scale: 1, screenWidth: 200, screenHeight: 120));

    [Fact]
    public void Selection_converts_dips_to_pixels()
        => Assert.Equal(new RectI(15, 30, 150, 75), RegionMath.Selection(10, 20, 110, 70, scale: 1.5, screenWidth: 1000, screenHeight: 1000));

    [Fact]
    public void Tiny_selections_are_not_a_capture()
    {
        Assert.False(RegionMath.IsUsable(new RectI(0, 0, 3, 50)));
        Assert.True(RegionMath.IsUsable(new RectI(0, 0, 8, 8)));
    }

    [Fact]
    public void Crop_copies_the_rows_of_the_selection()
    {
        // 4x3 image; pixel value = index so the crop is easy to check.
        var src = new byte[4 * 3 * 4];
        for (var i = 0; i < 12; i++) for (var c = 0; c < 4; c++) src[i * 4 + c] = (byte)i;
        var crop = RegionMath.CropBgra(src, 4, 3, new RectI(1, 1, 2, 2));
        Assert.Equal(2 * 2 * 4, crop.Length);
        Assert.Equal([5, 6, 9, 10], Enumerable.Range(0, 4).Select(i => (int)crop[i * 4]));
    }

    [Fact]
    public void Crop_outside_the_image_is_rejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => RegionMath.CropBgra(new byte[16], 2, 2, new RectI(1, 1, 5, 5)));
}
