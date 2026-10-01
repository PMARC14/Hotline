using Hotline.Core.Text;

namespace Hotline.Core.Tests;

public class MarkdownModelTests
{
    private static MdText T(string text, MdStyle style = MdStyle.None) => new(text, style);

    [Fact]
    public void Paragraph_with_styles()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("a **b** *c* ~~d~~ `e`")));
        Assert.Equal(
            [T("a "), T("b", MdStyle.Bold), T(" "), T("c", MdStyle.Italic), T(" "), T("d", MdStyle.Strike), T(" "), T("e", MdStyle.Code)],
            p.Inlines);
    }

    [Fact]
    public void Nested_emphasis_combines_styles()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("***both***")));
        Assert.Equal([T("both", MdStyle.Bold | MdStyle.Italic)], p.Inlines);
    }

    [Fact]
    public void Heading_levels()
    {
        var h = Assert.IsType<MdHeading>(Assert.Single(MarkdownModel.Parse("## Title")));
        Assert.Equal(2, h.Level);
        Assert.Equal([T("Title")], h.Inlines);
    }

    [Fact]
    public void Fenced_code_keeps_language_and_text()
    {
        var c = Assert.IsType<MdCode>(Assert.Single(MarkdownModel.Parse("```cs\nvar x = 1;\nvar y = 2;\n```")));
        Assert.Equal("cs", c.Language);
        Assert.Equal("var x = 1;\nvar y = 2;", c.Code);
    }

    [Fact]
    public void Lists_ordered_and_unordered()
    {
        var blocks = MarkdownModel.Parse("- a\n- b\n\n3. x\n4. y");
        var ul = Assert.IsType<MdList>(blocks[0]);
        Assert.False(ul.Ordered);
        Assert.Equal(2, ul.Items.Count);
        var ol = Assert.IsType<MdList>(blocks[1]);
        Assert.True(ol.Ordered);
        Assert.Equal(3, ol.Start);
        Assert.Equal([T("x")], Assert.IsType<MdParagraph>(ol.Items[0][0]).Inlines);
    }

    [Fact]
    public void Quote_rule_and_table()
    {
        var blocks = MarkdownModel.Parse("> q\n\n---\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");
        Assert.IsType<MdQuote>(blocks[0]);
        Assert.IsType<MdRule>(blocks[1]);
        var t = Assert.IsType<MdTable>(blocks[2]);
        Assert.True(t.HasHeader);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal([T("2")], t.Rows[1][1]);
    }

    [Fact]
    public void Safe_links_are_links()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("see [docs](https://x.y/z) or <mailto:a@b.c>")));
        var link = Assert.IsType<MdLink>(p.Inlines[1]);
        Assert.Equal("https://x.y/z", link.Url);
        Assert.Equal([T("docs")], link.Inlines);
        Assert.Contains(p.Inlines, i => i is MdLink { Url: "mailto:a@b.c" });
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](file:///C:/Windows/win.ini)")]
    [InlineData("[x](ms-settings:privacy)")]
    public void Unsafe_link_schemes_render_as_text(string md)
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse(md)));
        Assert.DoesNotContain(p.Inlines, i => i is MdLink);
        Assert.Contains(p.Inlines, i => i is MdText { Text: "x" });
    }

    [Fact]
    public void Bare_filenames_are_not_links()
        => Assert.DoesNotContain(Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("open main.py"))).Inlines, i => i is MdLink);

    [Fact]
    public void Line_breaks_and_html_as_text()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("one  \ntwo <b>x</b>")));
        Assert.Contains(p.Inlines, i => i is MdBreak);
        Assert.Contains(p.Inlines, i => i is MdText t && t.Text.Contains("<b>"));
    }

    [Fact]
    public void Large_table_is_preserved()
    {
        var md = "| a | b |\n|---|---|\n" + string.Concat(Enumerable.Range(0, 300).Select(i => $"| {i} | x |\n"));
        Assert.Equal(301, Assert.IsType<MdTable>(Assert.Single(MarkdownModel.Parse(md))).Rows.Count);
    }

    [Fact]
    public void Unclosed_code_fence_while_streaming_is_still_code()
        => Assert.Equal("partial", Assert.IsType<MdCode>(Assert.Single(MarkdownModel.Parse("```py\npartial"))).Code);

    [Fact]
    public void Empty_input_gives_no_blocks()
        => Assert.Empty(MarkdownModel.Parse(""));

    [Fact]
    public void Raw_html_blocks_are_kept_as_text_not_dropped()
    {
        var c = Assert.IsType<MdCode>(Assert.Single(MarkdownModel.Parse("<details>\n<summary>More</summary>\nhidden text\n</details>")));
        Assert.Contains("hidden text", c.Code);
    }

    [Fact]
    public void First_changed_block_index_finds_streaming_tail()
    {
        var a = MarkdownModel.Parse("# T\n\npara one\n\npara two");
        var b = MarkdownModel.Parse("# T\n\npara one\n\npara two grows");
        Assert.Equal(2, MarkdownModel.FirstChangedIndex(a, b));
        Assert.Equal(3, MarkdownModel.FirstChangedIndex(a, a));
        Assert.Equal(0, MarkdownModel.FirstChangedIndex([], b));
    }
}
