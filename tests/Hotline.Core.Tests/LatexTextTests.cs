using Hotline.Core.Text;

namespace Hotline.Core.Tests;

public class LatexTextTests
{
    [Theory]
    [InlineData(@"\pi", "π")]
    [InlineData(@"2\pi r", "2π r")]
    [InlineData(@"\alpha + \beta = \gamma", "α + β = γ")]
    [InlineData(@"x^2 + y^{10}", "x² + y¹⁰")]
    [InlineData(@"a_1 + a_{n}", "a₁ + aₙ")]
    [InlineData(@"\frac{1}{2}", "1⁄2")]
    [InlineData(@"\frac{a+b}{c}", "(a+b)/c")]
    [InlineData(@"\sqrt{2}", "√2")]
    [InlineData(@"\sqrt{x+1}", "√(x+1)")]
    [InlineData(@"a \times b \cdot c \div d", "a × b ⋅ c ÷ d")]
    [InlineData(@"x \le y \ge z \neq w \approx v", "x ≤ y ≥ z ≠ w ≈ v")]
    [InlineData(@"\sum_{i=1}^{n} i", "∑ᵢ₌₁ⁿ i")]
    [InlineData(@"\int_0^\infty e^{-x} dx", "∫₀^∞ e⁻ˣ dx")]
    [InlineData(@"90^\circ", "90°")]
    [InlineData(@"\text{area} = \pi r^2", "area = π r²")]
    [InlineData(@"\left( x \right)", "( x )")]
    [InlineData(@"A \to B \Rightarrow C", "A → B ⇒ C")]
    [InlineData(@"\mathbf{v} \in \mathbb{R}", "v ∈ ℝ")]
    [InlineData(@"x^{(n)}", "x⁽ⁿ⁾")]
    public void Converts_common_latex_to_unicode(string latex, string expected)
        => Assert.Equal(expected, LatexText.ToUnicode(latex));

    [Fact]
    public void Unknown_commands_keep_their_name_and_never_throw()
    {
        Assert.Equal("weirdx", LatexText.ToUnicode(@"\weird{x}"));
        Assert.Equal("", LatexText.ToUnicode(""));
        Assert.Equal("x^", LatexText.ToUnicode("x^"));
        Assert.Equal("1⁄", LatexText.ToUnicode(@"\frac{1}"));
    }

    [Fact]
    public void Markdown_math_becomes_math_text()
    {
        var blocks = MarkdownModel.Parse(@"Area is $\pi r^2$ here.");
        var p = Assert.IsType<MdParagraph>(Assert.Single(blocks));
        Assert.Contains(p.Inlines, i => i is MdText { Text: "π r²", Style: MdStyle.Math });
    }

    [Fact]
    public void Display_math_is_its_own_block()
    {
        var blocks = MarkdownModel.Parse("Euler:\n\n$$\ne^{i\\pi} + 1 = 0\n$$\n");
        Assert.Contains(blocks, b => b is MdMath { Text: "eⁱᵖ + 1 = 0" } or MdMath { Text: "e^(iπ) + 1 = 0" });
    }

    [Fact]
    public void Dollar_amounts_are_not_math()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("It costs $5 and $10.")));
        Assert.DoesNotContain(p.Inlines, i => i is MdText { Style: MdStyle.Math });
    }
}
