using Hotline.Core.Text;

namespace Hotline.Core.Tests;

/// <summary>Model- and clipboard-supplied SVG/markdown is untrusted: no external references, no unbounded work.</summary>
public sealed class SvgMarkdownLimitsTests
{
    private static string Wrap(string inner) => $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10">{inner}</svg>""";

    // ---- R1: style / presentation values ----

    [Theory]
    [InlineData("""<rect width="10" height="10" style="fill:\75rl(https://evil.tld/x);background:image-set('https://evil.tld/p.png' 1x)"/>""")]
    [InlineData("""<rect width="10" height="10" style="fill:url/**/(https://evil.tld/x)"/>""")]
    [InlineData("""<rect width="10" height="10" style="fill:red;background:image-set('https://evil.tld/p.png' 1x)"/>""")]
    [InlineData("""<rect width="10" height="10" style="@import 'https://evil.tld/a.css'"/>""")]
    [InlineData("""<rect width="10" height="10" fill="\75rl(https://evil.tld/x)"/>""")]
    [InlineData("""<rect width="10" height="10" fill="url/**/(https://evil.tld/x)"/>""")]
    [InlineData("""<rect width="10" height="10" style="fill:url(https://evil.tld/x)"/>""")]
    [InlineData("""<rect width="10" height="10" style="behavior:url(https://evil.tld/x.htc)"/>""")]
    public void Svg_style_cannot_reach_external_resources(string shape)
    {
        var clean = SvgSanitizer.Sanitize(Wrap(shape));
        Assert.NotNull(clean);
        Assert.Contains("<rect", clean);
        Assert.DoesNotContain("evil.tld", clean);
        Assert.DoesNotContain("image-set", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@import", clean, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\", clean);
    }

    [Fact]
    public void Svg_plain_style_declarations_still_work()
    {
        var clean = SvgSanitizer.Sanitize(Wrap("""<defs><linearGradient id="g"/></defs><rect width="10" height="10" style="fill:#336699; stroke:rgb(1,2,3);stroke-width:2;fill-opacity:.5;font-family:'Segoe UI', sans-serif"/><circle r="1" style="fill:url(#g)"/>"""))!;
        Assert.Contains("fill:#336699", clean);
        Assert.Contains("stroke:rgb(1,2,3)", clean);
        Assert.Contains("stroke-width:2", clean);
        Assert.Contains("fill:url(#g)", clean);
        Assert.Contains("Segoe UI", clean);
    }

    // ---- R2: limits ----

    [Fact]
    public void Svg_nested_20000_deep_is_rejected_without_throwing()
    {
        var deep = "<svg>" + string.Concat(Enumerable.Repeat("<g>", 20_000)) + string.Concat(Enumerable.Repeat("</g>", 20_000)) + "</svg>";
        Assert.Null(SvgSanitizer.Sanitize(deep));
    }

    [Fact]
    public void Svg_with_too_many_elements_is_rejected()
    {
        Assert.Null(SvgSanitizer.Sanitize(Wrap(string.Concat(Enumerable.Repeat("<circle r=\"1\"/>", 20_000)))));
        Assert.NotNull(SvgSanitizer.Sanitize(Wrap(string.Concat(Enumerable.Repeat("<circle r=\"1\"/>", 500)))));
    }

    [Fact]
    public void Svg_reasonable_nesting_is_kept()
    {
        var nested = Wrap(string.Concat(Enumerable.Repeat("<g>", 20)) + "<circle r=\"1\"/>" + string.Concat(Enumerable.Repeat("</g>", 20)));
        Assert.Contains("<circle", SvgSanitizer.Sanitize(nested));
    }

    [Fact]
    public void Svg_document_over_the_size_cap_is_rejected()
    {
        Assert.Null(SvgSanitizer.Sanitize(Wrap("<text>" + new string('a', 2_000_000) + "</text>")));
    }

    [Fact]
    public void Svg_use_keeps_only_present_local_targets_and_never_cycles()
    {
        var clean = SvgSanitizer.Sanitize(Wrap("""<defs><circle id="c" r="1"/></defs><use href="#c"/><use href="#missing"/><use href="https://x.example/y.svg#c"/>"""))!;
        Assert.Equal(1, clean.Split("<use").Length - 1);

        var cyclic = SvgSanitizer.Sanitize(Wrap("""<defs><g id="a"><use href="#b"/></g><g id="b"><use href="#a"/></g></defs><use href="#a"/><g id="s"><use href="#s"/></g>"""))!;
        var doc = System.Xml.Linq.XElement.Parse(cyclic);
        foreach (var use in doc.Descendants().Where(e => e.Name.LocalName == "use"))
        {
            var id = use.Attribute("href")!.Value[1..];
            var target = doc.Descendants().Single(e => (string?)e.Attribute("id") == id);
            Assert.DoesNotContain(target.DescendantsAndSelf(), e => e.Name.LocalName == "use"); // one level of reuse: no cycles
        }
    }

    [Fact]
    public void Svg_use_count_is_capped()
    {
        var clean = SvgSanitizer.Sanitize(Wrap("""<circle id="c" r="1"/>""" + string.Concat(Enumerable.Repeat("""<use href="#c"/>""", 1000))))!;
        Assert.True(clean.Split("<use").Length - 1 <= 100);
    }

    private static int Depth(MdBlock b) => b switch
    {
        MdQuote q => 1 + q.Blocks.Select(Depth).DefaultIfEmpty(0).Max(),
        MdList l => 1 + l.Items.SelectMany(i => i).Select(Depth).DefaultIfEmpty(0).Max(),
        _ => 0,
    };

    private static string AllText(IEnumerable<MdBlock> blocks) => string.Concat(blocks.Select(b => b switch
    {
        MdParagraph p => string.Concat(p.Inlines.OfType<MdText>().Select(t => t.Text)),
        MdQuote q => AllText(q.Blocks),
        MdList l => AllText(l.Items.SelectMany(i => i)),
        _ => "",
    }));

    [Theory]
    [InlineData(">", " deep text")]
    [InlineData("- ", "deep text")]
    public void Markdown_quotes_and_lists_20000_deep_do_not_throw(string prefix, string text)
    {
        Assert.Contains("deep text", AllText(MarkdownModel.Parse(string.Concat(Enumerable.Repeat(prefix, 20_000)) + text)));
    }

    [Fact]
    public void Markdown_quotes_and_lists_deeper_than_the_cap_are_flattened()
    {
        foreach (var md in new[] { string.Concat(Enumerable.Repeat(">", 100)) + " deep text", string.Concat(Enumerable.Repeat("- ", 100)) + "deep text" })
        {
            var blocks = MarkdownModel.Parse(md);
            Assert.True(blocks.Max(Depth) <= 17);
            Assert.Contains("deep text", AllText(blocks));
        }
    }

    [Fact]
    public void Markdown_nested_emphasis_and_links_do_not_throw()
    {
        MarkdownModel.Parse(string.Concat(Enumerable.Repeat("*a _a ", 10_000)) + "x" + string.Concat(Enumerable.Repeat("_ *", 10_000)));
        MarkdownModel.Parse(string.Concat(Enumerable.Repeat("[a ", 500)) + "x" + string.Concat(Enumerable.Repeat("](https://example.com)", 500)));
        MarkdownModel.Parse(string.Concat(Enumerable.Repeat("**a ", 10_000)) + "x");
        // Deep but inside Markdig's own limit: the inline walk caps it.
        var inlines = MarkdownModel.Parse(string.Concat(Enumerable.Repeat("*a _a ", 50)) + "x" + string.Concat(Enumerable.Repeat("_ *", 50)));
        Assert.Contains("x", AllText(inlines));
    }

    [Fact]
    public void Markdown_moderate_nesting_is_kept()
    {
        var blocks = MarkdownModel.Parse("> > > quoted");
        Assert.Equal(3, blocks.Max(Depth));
    }

    // ---- R3: markdown + svg scanning ----

    [Theory]
    [InlineData("<svg ", 40_000, 0)]
    [InlineData("<svg><circle r=\"1\"/></svg>\n\n", 5_000, 5_000)]
    public void Many_unclosed_or_closed_svg_blocks_parse_in_linear_time(string snippet, int repeat, int expectedDrawings)
    {
        var md = string.Concat(Enumerable.Repeat(snippet, repeat));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var blocks = MarkdownModel.Parse(md);
        Assert.True(watch.ElapsedMilliseconds < 2000, $"{watch.ElapsedMilliseconds} ms");
        if (expectedDrawings > 0) Assert.Equal(expectedDrawings, blocks.OfType<MdSvg>().Count());
    }

    [Theory]
    [InlineData("```\n~~~\n<svg viewBox=\"0 0 1 1\"><circle r=\"1\"/></svg>\n```")]
    [InlineData("~~~\n```\n<svg viewBox=\"0 0 1 1\"><circle r=\"1\"/></svg>\n~~~")]
    [InlineData("````\n```\n<svg viewBox=\"0 0 1 1\"><circle r=\"1\"/></svg>\n````")]
    public void Svg_source_inside_a_fence_stays_code_whatever_the_fence_lines_inside(string md)
    {
        Assert.DoesNotContain(MarkdownModel.Parse(md), b => b is MdSvg);
    }

    [Fact]
    public void Svg_after_a_closed_fence_is_still_a_drawing()
    {
        var blocks = MarkdownModel.Parse("```\ncode\n```\n\n<svg viewBox=\"0 0 1 1\"><circle r=\"1\"/></svg>");
        Assert.Single(blocks.OfType<MdSvg>());
    }

    [Theory]
    [InlineData("HOTLINESVGBLOCK-1")]
    [InlineData("HOTLINESVGBLOCK99")]
    [InlineData("HOTLINESVGBLOCK+0")]
    public void Svg_token_lookalike_text_does_not_throw(string text)
    {
        var blocks = MarkdownModel.Parse("<svg><circle r=\"1\"/></svg>\n\n" + text);
        Assert.Single(blocks.OfType<MdSvg>());
        Assert.Contains(text, AllText(blocks));
    }

    [Fact]
    public void Streaming_open_svg_is_incomplete_and_complete_one_is_not()
    {
        Assert.False(Assert.IsType<MdSvg>(Assert.Single(MarkdownModel.Parse("<svg viewBox=\"0 0 1 1\"><circle"))).Complete);
        Assert.True(Assert.IsType<MdSvg>(Assert.Single(MarkdownModel.Parse("<svg><circle r=\"1\"/></svg>"))).Complete);
    }
}
