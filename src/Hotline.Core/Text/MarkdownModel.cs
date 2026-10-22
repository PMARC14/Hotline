using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Hotline.Core.Text;

public abstract record MdBlock;
public sealed record MdParagraph(IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdHeading(int Level, IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdCode(string? Language, string Code) : MdBlock;
public sealed record MdList(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<MdBlock>> Items) : MdBlock;
public sealed record MdQuote(IReadOnlyList<MdBlock> Blocks) : MdBlock;
public sealed record MdRule : MdBlock;
/// <summary>Display math ($$...$$), already converted to Unicode text.</summary>
public sealed record MdMath(string Text) : MdBlock;
/// <summary>An SVG drawing the model wrote inline (rendered as an image). Complete = closing tag received.</summary>
public sealed record MdSvg(string Markup, bool Complete) : MdBlock;
public sealed record MdTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows, bool HasHeader) : MdBlock;

public abstract record MdInline;
public sealed record MdText(string Text, MdStyle Style) : MdInline;
public sealed record MdLink(string Url, IReadOnlyList<MdInline> Inlines) : MdInline;
public sealed record MdBreak : MdInline;

[Flags]
public enum MdStyle { None = 0, Bold = 1, Italic = 2, Code = 4, Strike = 8, Math = 16, Superscript = 32, Mark = 64, Underline = 128 }

/// <summary>
/// Markdown → a small, UI-agnostic block model the App renders natively. Records compare by value
/// in tests; lists are materialized as arrays wrapped in <see cref="Seq{T}"/> for structural equality.
/// </summary>
public static partial class MarkdownModel
{
    // No single-~ subscript: chat answers write "~5 minutes" for "about 5".
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough | EmphasisExtraOptions.Superscript | EmphasisExtraOptions.Marked | EmphasisExtraOptions.Inserted)
        .UseMathematics().UseAutoLinks().UseTaskLists().Build();

    private const string SvgToken = "HOTLINESVGBLOCK";

    public static IReadOnlyList<MdBlock> Parse(string markdown)
    {
        // Inline SVG drawings contain blank lines and comments that markdown would split into many HTML blocks:
        // lift them out first, parse, then put each back as one image block. One linear scan (a regex that restarts
        // at every unclosed "<svg" is quadratic, and this runs on every streamed update).
        var svgs = new List<MdSvg>();
        var source = markdown ?? "";
        var fences = FenceRanges(source);
        var text = new System.Text.StringBuilder(source.Length);
        var copied = 0; // source[..copied] is already in text
        var scan = 0;
        while (NextSvgStart(source, scan) is var start && start >= 0)
        {
            if (InsideCodeFence(fences, start)) { scan = start + 4; continue; } // SVG shown as source code stays code
            var end = SvgEnd(source, start);
            if (end < 0) break; // no closing tag anywhere after this: nothing later can be complete either
            svgs.Add(new MdSvg(source[start..end], Complete: true));
            text.Append(source, copied, start - copied).Append($"\n\n{SvgToken}{svgs.Count - 1}\n\n");
            copied = scan = end;
        }
        var open = LastOpenSvg(source, copied, fences);
        if (open >= 0) // still streaming: the closing tag hasn't arrived
        {
            svgs.Add(new MdSvg(source[open..], Complete: false));
            text.Append(source, copied, open - copied).Append($"\n\n{SvgToken}{svgs.Count - 1}\n\n");
            copied = source.Length;
        }
        text.Append(source, copied, source.Length - copied);
        MarkdownDocument document;
        try { document = Markdown.Parse(text.ToString(), Pipeline); }
        catch (ArgumentException) { return [new MdParagraph([new MdText(source, MdStyle.None)])]; } // Markdig's own nesting limit: show the text as it is
        var blocks = Blocks(document, 0);
        // NumberStyles.None: only plain digits (a "-1" typed in the text must not index the list).
        return svgs.Count == 0 ? blocks : new Seq<MdBlock>(blocks.Select(b => b is MdParagraph { Inlines: [MdText { Text: var t }] } && t.StartsWith(SvgToken, StringComparison.Ordinal)
            && int.TryParse(t.AsSpan(SvgToken.Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var i) && i < svgs.Count ? svgs[i] : b));
    }

    /// <summary>Start of the next "&lt;svg" tag at or after <paramref name="from"/> (not "&lt;svgx"), or -1.</summary>
    private static int NextSvgStart(string text, int from)
    {
        while (from < text.Length && text.IndexOf("<svg", from, StringComparison.OrdinalIgnoreCase) is var i && i >= 0)
        {
            if (!IsWordChar(i + 4 < text.Length ? text[i + 4] : ' ')) return i;
            from = i + 4;
        }
        return -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>Index just past the first "&lt;/svg&gt;" (whitespace allowed before the &gt;) after <paramref name="start"/>, or -1.</summary>
    private static int SvgEnd(string text, int start)
    {
        for (var from = start + 4; from < text.Length && text.IndexOf("</svg", from, StringComparison.OrdinalIgnoreCase) is var i && i >= 0;)
        {
            var j = i + 5;
            while (j < text.Length && char.IsWhiteSpace(text[j])) j++;
            if (j < text.Length && text[j] == '>') return j + 1;
            from = i + 5;
        }
        return -1;
    }

    /// <summary>The last "&lt;svg" at or after <paramref name="from"/> outside a code fence with no "&lt;/svg" after it, or -1.</summary>
    private static int LastOpenSvg(string text, int from, List<(int Start, int End)> fences)
    {
        for (var at = text.Length; at > from && text.LastIndexOf("<svg", at - 1, at - from, StringComparison.OrdinalIgnoreCase) is var i && i >= 0; at = i)
        {
            if (IsWordChar(i + 4 < text.Length ? text[i + 4] : ' ') || InsideCodeFence(fences, i)) continue;
            // Any earlier "<svg" has this closing tag after it too, so only the last unfenced one can be open.
            return text.AsSpan(i).Contains("</svg", StringComparison.OrdinalIgnoreCase) ? -1 : i;
        }
        return -1;
    }

    private static bool InsideCodeFence(List<(int Start, int End)> fences, int index)
    {
        int lo = 0, hi = fences.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (index < fences[mid].Start) hi = mid - 1;
            else if (index >= fences[mid].End) lo = mid + 1;
            else return true;
        }
        return false;
    }

    /// <summary>
    /// The text ranges covered by ``` / ~~~ fences, as markdown reads them: a fence closes only with the same character,
    /// at least as long as the opener; any other fence-looking line inside it is code. An unclosed fence runs to the end.
    /// </summary>
    private static List<(int Start, int End)> FenceRanges(string text)
    {
        var ranges = new List<(int, int)>();
        var openStart = -1;
        char marker = '\0';
        var length = 0;
        for (var lineStart = 0; lineStart < text.Length;)
        {
            var newline = text.IndexOf('\n', lineStart);
            var lineEnd = newline < 0 ? text.Length : newline;
            var next = newline < 0 ? text.Length : newline + 1;
            var line = text.AsSpan(lineStart, lineEnd - lineStart);
            var indent = 0;
            while (indent < line.Length && indent < 3 && line[indent] is ' ' or '\t') indent++;
            var rest = line[indent..];
            if (rest.Length >= 3 && rest[0] is '`' or '~')
            {
                var run = 1;
                while (run < rest.Length && rest[run] == rest[0]) run++;
                if (run >= 3)
                {
                    if (openStart < 0)
                    {
                        // A backtick fence's info text can't hold a backtick (that line is inline code, not a fence).
                        if (rest[0] == '~' || !rest[run..].Contains('`')) { openStart = lineStart; marker = rest[0]; length = run; }
                    }
                    else if (rest[0] == marker && run >= length && rest[run..].IsWhiteSpace())
                    {
                        ranges.Add((openStart, next));
                        openStart = -1;
                    }
                }
            }
            lineStart = next;
        }
        if (openStart >= 0) ranges.Add((openStart, text.Length));
        return ranges;
    }

    /// <summary>Layout-only HTML (div/center/p wrappers, comments) adds nothing to a chat answer: hidden.</summary>
    private static bool IsWrapperHtml(string html)
    {
        // Strip wrapper tags and complete comments, then require nothing but whitespace (no nested quantifiers).
        var rest = HtmlComment().Replace(WrapperTag().Replace(html, ""), "");
        return string.IsNullOrWhiteSpace(rest);
    }

    [GeneratedRegex(@"</?(?:div|center|p|span|section|figure|figcaption|br)\b[^<>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex WrapperTag();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();

    /// <summary>Index of the first block that differs (streaming: everything before it can stay rendered).</summary>
    public static int FirstChangedIndex(IReadOnlyList<MdBlock> previous, IReadOnlyList<MdBlock> current)
    {
        var i = 0;
        while (i < previous.Count && i < current.Count && previous[i].Equals(current[i])) i++;
        return i;
    }

    // Quotes/lists nest through recursive code here and in the renderer, and a stack overflow can't be caught: past
    // these depths the text is shown flat. Real answers rarely nest more than three or four deep.
    private const int MaxBlockDepth = 16;
    private const int MaxInlineDepth = 32;

    private static Seq<MdBlock> Blocks(ContainerBlock container, int depth) => new(container.Select(b => Block(b, depth)).OfType<MdBlock>());

    /// <summary>The text of everything under a block or inline, without recursion, for content nested too deeply to model.</summary>
    private static string FlatText(MarkdownObject root) => root switch
    {
        ContainerInline inline => string.Concat(inline.Descendants().Select(InlineText)),
        _ => string.Join("\n", root.Descendants<LeafBlock>().Select(l => l.Inline is { } inline ? FlatText(inline) : l.Lines.ToString())),
    };

    private static string InlineText(MarkdownObject inline) => inline switch
    {
        LiteralInline lit => lit.Content.ToString(),
        CodeInline code => code.Content,
        LineBreakInline => " ",
        _ => "",
    };

    private static MdBlock? Block(Block block, int depth) => block switch
    {
        QuoteBlock or ListBlock when depth >= MaxBlockDepth => new MdParagraph([new MdText(FlatText(block), MdStyle.None)]),
        MathBlock m => new MdMath(LatexText.ToUnicode(m.Lines.ToString())), // before FencedCodeBlock: MathBlock derives from it
        HeadingBlock h => new MdHeading(h.Level, Inlines(h.Inline)),
        FencedCodeBlock f => new MdCode(string.IsNullOrWhiteSpace(f.Info) ? null : f.Info, f.Lines.ToString()),
        CodeBlock c => new MdCode(null, c.Lines.ToString()),
        ParagraphBlock p when DisplayMath(p) is { } math => new MdMath(LatexText.ToUnicode(math)),
        ParagraphBlock p => new MdParagraph(Inlines(p.Inline)),
        ListBlock l => new MdList(l.IsOrdered, int.TryParse(l.OrderedStart, out var s) ? s : 1,
            new Seq<IReadOnlyList<MdBlock>>(l.OfType<ListItemBlock>().Select(i => (IReadOnlyList<MdBlock>)Blocks(i, depth + 1)))),
        QuoteBlock q => new MdQuote(Blocks(q, depth + 1)),
        ThematicBreakBlock => new MdRule(),
        HtmlBlock html when IsWrapperHtml(html.Lines.ToString()) => null,
        HtmlBlock html => new MdCode("html", html.Lines.ToString()), // show other raw HTML rather than silently dropping it
        Table t => new MdTable(
            new Seq<IReadOnlyList<IReadOnlyList<MdInline>>>(t.OfType<TableRow>().Select(r =>
                (IReadOnlyList<IReadOnlyList<MdInline>>)new Seq<IReadOnlyList<MdInline>>(r.OfType<TableCell>().Select(CellInlines)))),
            t.OfType<TableRow>().FirstOrDefault()?.IsHeader ?? false),
        _ => null,
    };

    /// <summary>A paragraph that is only $$...$$ (written on one line) is display math.</summary>
    private static string? DisplayMath(ParagraphBlock p)
    {
        var parts = p.Inline?.Where(i => !(i is LiteralInline lit && lit.Content.IsEmptyOrWhitespace()) && i is not LineBreakInline).ToList();
        return parts is [MathInline { DelimiterCount: >= 2 } math] ? math.Content.ToString() : null;
    }

    private static IReadOnlyList<MdInline> CellInlines(TableCell cell) =>
        new Seq<MdInline>(cell.OfType<ParagraphBlock>().SelectMany(p => Inlines(p.Inline)));

    private static Seq<MdInline> Inlines(ContainerInline? container)
    {
        var result = new List<MdInline>();
        Walk(container, MdStyle.None, result, 0);
        return new Seq<MdInline>(Merge(result));
    }

    private static void Walk(ContainerInline? container, MdStyle baseStyle, List<MdInline> output, int depth)
    {
        if (container is not null && depth >= MaxInlineDepth) { output.Add(new MdText(FlatText(container), baseStyle)); return; }
        // Inline HTML like <kbd>Ctrl</kbd> arrives as separate open/close tags around ordinary text:
        // track which known tags are open and apply their style; unknown tags are hidden (text kept).
        var openTags = new List<MdStyle>();
        MdStyle style = baseStyle;
        for (var inline = container?.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    output.Add(new MdText(lit.Content.ToString(), style));
                    break;
                case MathInline math:
                    output.Add(new MdText(LatexText.ToUnicode(math.Content.ToString()), style | MdStyle.Math));
                    break;
                case TaskList task:
                    output.Add(new MdText(task.Checked ? "☑ " : "☐ ", style));
                    break;
                case CodeInline code:
                    output.Add(new MdText(code.Content, style | MdStyle.Code));
                    break;
                case EmphasisInline em:
                    var added = em.DelimiterChar switch
                    {
                        '~' => MdStyle.Strike,
                        '^' => MdStyle.Superscript,
                        '=' => MdStyle.Mark,
                        '+' => MdStyle.Underline,
                        _ => em.DelimiterCount >= 2 ? MdStyle.Bold : MdStyle.Italic,
                    };
                    if (em.DelimiterCount >= 3 && em.DelimiterChar is '*' or '_') added = MdStyle.Bold | MdStyle.Italic;
                    Walk(em, style | added, output, depth + 1);
                    break;
                case LinkInline { IsImage: false } link when IsSafe(link.Url):
                    var children = new List<MdInline>();
                    Walk(link, style, children, depth + 1);
                    output.Add(new MdLink(link.Url!, new Seq<MdInline>(Merge(children))));
                    break;
                case LinkInline link:
                    Walk(link, style, output, depth + 1); // unsafe scheme or image: keep the visible text only
                    break;
                case AutolinkInline auto when IsSafe(auto.Url):
                    output.Add(new MdLink(auto.Url, new Seq<MdInline>([new MdText(auto.Url, style)])));
                    break;
                case AutolinkInline auto:
                    output.Add(new MdText(auto.Url, style));
                    break;
                case LineBreakInline { IsHard: true }:
                    output.Add(new MdBreak());
                    break;
                case LineBreakInline:
                    output.Add(new MdText(" ", style));
                    break;
                case HtmlInline html:
                    var (name, closing) = TagName(html.Tag);
                    if (name == "br") { output.Add(new MdBreak()); break; }
                    if (HtmlStyles.TryGetValue(name, out var tagStyle))
                    {
                        if (closing) openTags.Remove(tagStyle); else openTags.Add(tagStyle);
                        style = openTags.Aggregate(baseStyle, (acc, s2) => acc | s2);
                    }
                    break; // other tags (span, sup, div...) are dropped; their text stays
                case HtmlEntityInline entity:
                    output.Add(new MdText(entity.Transcoded.ToString(), style));
                    break;
                case ContainerInline nested:
                    Walk(nested, style, output, depth + 1);
                    break;
            }
        }
    }

    private static readonly Dictionary<string, MdStyle> HtmlStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["b"] = MdStyle.Bold, ["strong"] = MdStyle.Bold, ["i"] = MdStyle.Italic, ["em"] = MdStyle.Italic,
        ["s"] = MdStyle.Strike, ["del"] = MdStyle.Strike, ["strike"] = MdStyle.Strike,
        ["code"] = MdStyle.Code, ["kbd"] = MdStyle.Code, ["samp"] = MdStyle.Code, ["tt"] = MdStyle.Code,
    };

    private static (string Name, bool Closing) TagName(string tag)
    {
        var t = tag.Trim('<', '>', ' ', '/');
        var closing = tag.StartsWith("</", StringComparison.Ordinal);
        var end = t.IndexOfAny([' ', '\t', '\n', '/']);
        return ((end < 0 ? t : t[..end]).ToLowerInvariant(), closing);
    }

    /// <summary>Joins adjacent text runs with the same style ("a" + " " → "a ").</summary>
    private static IEnumerable<MdInline> Merge(List<MdInline> inlines)
    {
        MdText? pending = null;
        foreach (var i in inlines)
        {
            if (i is MdText t && pending is not null && pending.Style == t.Style) { pending = pending with { Text = pending.Text + t.Text }; continue; }
            if (pending is not null) yield return pending;
            pending = i as MdText;
            if (pending is null) yield return i;
        }
        if (pending is not null) yield return pending;
    }

    private static bool IsSafe(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http" || u.Scheme == "mailto");
}
