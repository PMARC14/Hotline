using Hotline.Core.Text;

namespace Hotline.Core.Tests;

/// <summary>Constructs seen in real Gemini answers (chat history, 2026-10-02) and common chat markdown.</summary>
public class MarkdownCoverageTests
{
    private static MdParagraph Para(string md) => Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse(md)));

    [Fact]
    public void Display_math_written_on_one_line_is_a_math_block()
        => Assert.Equal(new MdMath("Area = π r²"), Assert.Single(MarkdownModel.Parse(@"$$\text{Area} = \pi r^2$$")));

    [Fact]
    public void Svg_in_a_div_becomes_an_image_block_and_wrappers_vanish()
    {
        const string md = "Here is a diagram:\n\n<div align=\"center\">\n\n<svg viewBox=\"0 0 10 10\">\n  <!-- c -->\n\n  <circle cx=\"5\" cy=\"5\" r=\"4\"/>\n</svg>\n\n</div>\n";
        var blocks = MarkdownModel.Parse(md);
        Assert.IsType<MdParagraph>(blocks[0]);
        var svg = Assert.IsType<MdSvg>(Assert.Single(blocks.Skip(1)));
        Assert.True(svg.Complete);
        Assert.StartsWith("<svg", svg.Markup);
        Assert.EndsWith("</svg>", svg.Markup);
    }

    [Fact]
    public void Svg_still_streaming_is_an_incomplete_image_block()
    {
        var svg = Assert.IsType<MdSvg>(MarkdownModel.Parse("Drawing:\n\n<svg viewBox=\"0 0 10 10\">\n  <circle")[^1]);
        Assert.False(svg.Complete);
    }

    [Fact]
    public void Other_html_blocks_still_show_as_code()
        => Assert.IsType<MdCode>(Assert.Single(MarkdownModel.Parse("<table><tr><td>x</td></tr></table>")));

    [Theory]
    [InlineData("x^2^", "2", MdStyle.Superscript)]
    [InlineData("==important==", "important", MdStyle.Mark)]
    [InlineData("++added++", "added", MdStyle.Underline)]
    [InlineData("~~gone~~", "gone", MdStyle.Strike)]
    public void Emphasis_extras_map_to_their_own_styles(string md, string text, MdStyle style)
        => Assert.Contains(Para(md).Inlines, i => i is MdText t && t.Text == text && t.Style == style);

    [Fact]
    public void Single_tilde_is_approximately_not_subscript()
        => Assert.Equal([new MdText("about ~5 to ~10 minutes", MdStyle.None)], Para("about ~5 to ~10 minutes").Inlines);

    [Fact]
    public void Bare_urls_become_links()
        => Assert.Contains(Para("see https://example.com/page for more").Inlines, i => i is MdLink { Url: "https://example.com/page" });

    [Fact]
    public void Task_list_items_get_check_boxes()
    {
        var list = Assert.IsType<MdList>(Assert.Single(MarkdownModel.Parse("- [x] done\n- [ ] todo")));
        Assert.StartsWith("☑", ((MdText)((MdParagraph)list.Items[0][0]).Inlines[0]).Text);
        Assert.StartsWith("☐", ((MdText)((MdParagraph)list.Items[1][0]).Inlines[0]).Text);
    }

    [Fact]
    public void Svg_source_inside_a_code_block_stays_code()
    {
        var code = Assert.IsType<MdCode>(Assert.Single(MarkdownModel.Parse("```xml\n<svg viewBox=\"0 0 1 1\">\n\n</svg>\n```")));
        Assert.Contains("<svg", code.Code);
    }
}
