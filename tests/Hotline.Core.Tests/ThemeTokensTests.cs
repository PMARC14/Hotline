using Hotline.Core.Theming;

namespace Hotline.Core.Tests;

public class ThemeTokensTests
{
    [Fact]
    public void Css_declares_every_token_as_custom_property()
    {
        var css = ThemeTokens.Dark.ToCss();
        Assert.StartsWith(":root{", css);
        foreach (var name in new[] { "--hl-text", "--hl-muted", "--hl-accent", "--hl-surface", "--hl-surface-strong",
                                     "--hl-border", "--hl-user-bubble", "--hl-code-bg", "--hl-font", "--hl-font-size", "--hl-radius" })
            Assert.Contains(name + ":", css);
        Assert.Contains("--hl-font-size:14px", css);
    }

    [Fact]
    public void Light_and_dark_differ()
        => Assert.NotEqual(ThemeTokens.Dark.ToCss(), ThemeTokens.Light.ToCss());

    [Theory]
    [InlineData("red;}body{display:none")]
    [InlineData("</style><script>")]
    public void Unsafe_values_are_rejected(string value)
        => Assert.Throws<ArgumentException>(() => (ThemeTokens.Dark with { Accent = value }).ToCss());
}
