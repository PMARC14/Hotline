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
        // lift them out first, parse, then put each back as one image block.
        var svgs = new List<MdSvg>();
        var source = markdown ?? "";
        var text = SvgElement().Replace(source, m =>
        {
            if (InsideCodeFence(source, m.Index)) return m.Value; // SVG shown as source code stays code
            svgs.Add(new MdSvg(m.Value, Complete: true));
            return $"\n\n{SvgToken}{svgs.Count - 1}\n\n";
        });
        var open = SvgStart().Matches(text).LastOrDefault(m => !InsideCodeFence(text, m.Index)
            && !text.AsSpan(m.Index).Contains("</svg", StringComparison.OrdinalIgnoreCase));
        if (open is not null) // still streaming: the closing tag hasn't arrived
        {
            svgs.Add(new MdSvg(text[open.Index..], Complete: false));
            text = text[..open.Index] + $"\n\n{SvgToken}{svgs.Count - 1}\n\n";
        }
        var blocks = Blocks(Markdown.Parse(text, Pipeline));
        return svgs.Count == 0 ? blocks : new Seq<MdBlock>(blocks.Select(b => b is MdParagraph { Inlines: [MdText { Text: var t }] } && t.StartsWith(SvgToken, StringComparison.Ordinal)
            && int.TryParse(t.AsSpan(SvgToken.Length), out var i) && i < svgs.Count ? svgs[i] : b));
    }

    /// <summary>True when a ``` / ~~~ fence is open at <paramref name="index"/> (odd number of fence lines before it).</summary>
    private static bool InsideCodeFence(string text, int index)
        => CodeFence().Matches(text[..index]).Count % 2 == 1;

    [GeneratedRegex(@"^[ \t]{0,3}(```|~~~)", RegexOptions.Multiline)]
    private static partial Regex CodeFence();

    [GeneratedRegex(@"<svg\b[\s\S]*?</svg\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex SvgElement();

    [GeneratedRegex(@"<svg\b", RegexOptions.IgnoreCase)]
    private static partial Regex SvgStart();

    /// <summary>Layout-only HTML (div/center/p wrappers, comments) adds nothing to a chat answer: hidden.</summary>
    [GeneratedRegex(@"^(\s*(</?(div|center|p|span|section|figure|figcaption|br)\b[^>]*>|<!--[\s\S]*?-->))*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex WrapperHtml();

    /// <summary>Index of the first block that differs (streaming: everything before it can stay rendered).</summary>
    public static int FirstChangedIndex(IReadOnlyList<MdBlock> previous, IReadOnlyList<MdBlock> current)
    {
        var i = 0;
        while (i < previous.Count && i < current.Count && previous[i].Equals(current[i])) i++;
        return i;
    }

    private static Seq<MdBlock> Blocks(ContainerBlock container) => new(container.Select(Block).OfType<MdBlock>());

    private static MdBlock? Block(Block block) => block switch
    {
        MathBlock m => new MdMath(LatexText.ToUnicode(m.Lines.ToString())), // before FencedCodeBlock: MathBlock derives from it
        HeadingBlock h => new MdHeading(h.Level, Inlines(h.Inline)),
        FencedCodeBlock f => new MdCode(string.IsNullOrWhiteSpace(f.Info) ? null : f.Info, f.Lines.ToString()),
        CodeBlock c => new MdCode(null, c.Lines.ToString()),
        ParagraphBlock p when DisplayMath(p) is { } math => new MdMath(LatexText.ToUnicode(math)),
        ParagraphBlock p => new MdParagraph(Inlines(p.Inline)),
        ListBlock l => new MdList(l.IsOrdered, int.TryParse(l.OrderedStart, out var s) ? s : 1,
            new Seq<IReadOnlyList<MdBlock>>(l.OfType<ListItemBlock>().Select(i => (IReadOnlyList<MdBlock>)Blocks(i)))),
        QuoteBlock q => new MdQuote(Blocks(q)),
        ThematicBreakBlock => new MdRule(),
        HtmlBlock html when WrapperHtml().IsMatch(html.Lines.ToString()) => null,
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
        Walk(container, MdStyle.None, result);
        return new Seq<MdInline>(Merge(result));
    }

    private static void Walk(ContainerInline? container, MdStyle baseStyle, List<MdInline> output)
    {
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
                    Walk(em, style | added, output);
                    break;
                case LinkInline { IsImage: false } link when IsSafe(link.Url):
                    var children = new List<MdInline>();
                    Walk(link, style, children);
                    output.Add(new MdLink(link.Url!, new Seq<MdInline>(Merge(children))));
                    break;
                case LinkInline link:
                    Walk(link, style, output); // unsafe scheme or image: keep the visible text only
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
                    Walk(nested, style, output);
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

/// <summary>Read-only list with value equality (so records containing lists compare structurally).</summary>
public sealed class Seq<T>(IEnumerable<T> items) : IReadOnlyList<T>, IEquatable<IReadOnlyList<T>>
{
    private readonly T[] _items = items.ToArray();
    public T this[int index] => _items[index];
    public int Count => _items.Length;
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    public bool Equals(IReadOnlyList<T>? other) => other is not null && this.SequenceEqual(other);
    public override bool Equals(object? obj) => obj is IReadOnlyList<T> other && Equals(other);
    public override int GetHashCode() => _items.Aggregate(0, (h, x) => HashCode.Combine(h, x));
}
