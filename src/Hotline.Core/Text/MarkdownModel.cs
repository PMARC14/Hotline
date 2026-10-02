using Markdig;
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
public sealed record MdTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows, bool HasHeader) : MdBlock;

public abstract record MdInline;
public sealed record MdText(string Text, MdStyle Style) : MdInline;
public sealed record MdLink(string Url, IReadOnlyList<MdInline> Inlines) : MdInline;
public sealed record MdBreak : MdInline;

[Flags]
public enum MdStyle { None = 0, Bold = 1, Italic = 2, Code = 4, Strike = 8, Math = 16 }

/// <summary>
/// Markdown → a small, UI-agnostic block model the App renders natively. Records compare by value
/// in tests; lists are materialized as arrays wrapped in <see cref="Seq{T}"/> for structural equality.
/// </summary>
public static class MarkdownModel
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().UseMathematics().Build();

    public static IReadOnlyList<MdBlock> Parse(string markdown) => Blocks(Markdown.Parse(markdown ?? "", Pipeline));

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
        ParagraphBlock p => new MdParagraph(Inlines(p.Inline)),
        ListBlock l => new MdList(l.IsOrdered, int.TryParse(l.OrderedStart, out var s) ? s : 1,
            new Seq<IReadOnlyList<MdBlock>>(l.OfType<ListItemBlock>().Select(i => (IReadOnlyList<MdBlock>)Blocks(i)))),
        QuoteBlock q => new MdQuote(Blocks(q)),
        ThematicBreakBlock => new MdRule(),
        HtmlBlock html => new MdCode("html", html.Lines.ToString()), // show raw HTML rather than silently dropping it
        Table t => new MdTable(
            new Seq<IReadOnlyList<IReadOnlyList<MdInline>>>(t.OfType<TableRow>().Select(r =>
                (IReadOnlyList<IReadOnlyList<MdInline>>)new Seq<IReadOnlyList<MdInline>>(r.OfType<TableCell>().Select(CellInlines)))),
            t.OfType<TableRow>().FirstOrDefault()?.IsHeader ?? false),
        _ => null,
    };

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
                case CodeInline code:
                    output.Add(new MdText(code.Content, style | MdStyle.Code));
                    break;
                case EmphasisInline em:
                    var added = em.DelimiterChar == '~' ? MdStyle.Strike : em.DelimiterCount >= 2 ? MdStyle.Bold : MdStyle.Italic;
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
