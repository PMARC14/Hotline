using Hotline.Core.Settings;
using Hotline.Core.Theming;

namespace Hotline.Core.Tests;

public class ThemeTokensTests
{
    [Theory]
    [InlineData("#FF8B7CFF", 255, 0x8B, 0x7C, 0xFF)]
    [InlineData("#0FFFFFFF", 0x0F, 255, 255, 255)]
    [InlineData("#F3F3F3", 255, 0xF3, 0xF3, 0xF3)]
    public void Parses_hex_colors(string hex, int a, int r, int g, int b)
        => Assert.Equal(((byte)a, (byte)r, (byte)g, (byte)b), ThemeColor.Parse(hex));

    [Theory]
    [InlineData("red")]
    [InlineData("rgba(1,2,3,0.5)")]
    [InlineData("#12345")]
    [InlineData("#GG000000")]
    public void Rejects_non_hex(string value)
        => Assert.Throws<FormatException>(() => ThemeColor.Parse(value));

    [Fact]
    public void Builtin_tokens_are_valid_and_differ()
    {
        foreach (var t in new[] { ThemeTokens.Dark, ThemeTokens.Light })
            foreach (var c in new[] { t.Text, t.Muted, t.Accent, t.Surface, t.SurfaceStrong, t.Border, t.UserBubble, t.CodeBackground })
                ThemeColor.Parse(c);
        Assert.NotEqual(ThemeTokens.Dark.Text, ThemeTokens.Light.Text);
    }

    [Fact]
    public void Font_is_a_xaml_family_list()
        => Assert.Equal("Segoe UI Variable Text, Segoe UI", ThemeTokens.Dark.Font);
}
