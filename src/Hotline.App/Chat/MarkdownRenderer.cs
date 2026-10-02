using Hotline.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Hotline.App.Chat;

internal sealed record RenderStyle(double FontSize, FontFamily Font, Brush Muted, Brush CodeBackground, Brush Accent, double Radius, Action<string> OpenLink);

/// <summary>Renders the Core markdown block model as native, selectable WinUI text.</summary>
internal static class MarkdownRenderer
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    /// <summary>
    /// Appends one markdown block to a single RichTextBlock so a whole answer can be selected and copied in one
    /// drag. Code blocks, tables and rules are embedded boxes (they keep their own copy/scroll). Returns how
    /// many paragraphs were added (the presenter uses it to replace only the streaming tail).
    /// </summary>
    public static int AppendBlock(RichTextBlock rtb, MdBlock block, RenderStyle s, double contentWidth, int indent = 0, Brush? foreground = null)
    {
        Paragraph Para(IReadOnlyList<MdInline> inlines, double size, bool bold, string? marker = null)
        {
            var p = new Paragraph { FontSize = size, Margin = new Thickness(indent * 18, 0, 0, 8) };
            if (foreground is not null) p.Foreground = foreground;
            if (marker is not null) p.Inlines.Add(new Run { Text = marker, Foreground = s.Muted });
            foreach (var inline in inlines) p.Inlines.Add(ToInline(inline, s, bold));
            return p;
        }
        Paragraph Boxed(UIElement element)
        {
            if (element is FrameworkElement fe) fe.Width = Math.Max(160, contentWidth - indent * 18);
            var p = new Paragraph { Margin = new Thickness(indent * 18, 0, 0, 8) };
            p.Inlines.Add(new InlineUIContainer { Child = element });
            return p;
        }

        switch (block)
        {
            case MdParagraph para:
                rtb.Blocks.Add(Para(para.Inlines, s.FontSize, bold: false));
                return 1;
            case MdHeading h:
                rtb.Blocks.Add(Para(h.Inlines, s.FontSize + h.Level switch { 1 => 6, 2 => 4, _ => 2 }, bold: true));
                return 1;
            case MdCode c:
            {
                // Header row (language + copy) is a small shaded control; the code itself is selectable monospace text.
                rtb.Blocks.Add(Boxed(CodeHeader(c, s)));
                var code = new Paragraph { FontFamily = Mono, FontSize = s.FontSize - 1, Margin = new Thickness(indent * 18 + 10, 0, 0, 10) };
                var lines = c.Code.TrimEnd('\n', '\r').Replace("\r\n", "\n").Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0) code.Inlines.Add(new LineBreak());
                    code.Inlines.Add(new Run { Text = lines[i].Length == 0 ? " " : lines[i] });
                }
                rtb.Blocks.Add(code);
                return 2;
            }
            case MdTable t:
                rtb.Blocks.Add(TableText(t, s, indent));
                return 1;
            case MdRule:
                rtb.Blocks.Add(Boxed(new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = s.Muted, Opacity = 0.4 }));
                return 1;
            case MdQuote q:
                var quoted = 0;
                foreach (var child in q.Blocks) quoted += AppendBlock(rtb, child, s, contentWidth, indent + 1, s.Muted);
                return quoted;
            case MdList l:
                var count = 0;
                for (var i = 0; i < l.Items.Count; i++)
                {
                    var marker = l.Ordered ? $"{l.Start + i}. " : "• ";
                    var first = true;
                    foreach (var child in l.Items[i])
                    {
                        if (first && child is MdParagraph itemPara)
                        {
                            var p = Para(itemPara.Inlines, s.FontSize, bold: false, marker);
                            p.Margin = new Thickness(indent * 18 + 4, 0, 0, 4);
                            rtb.Blocks.Add(p);
                            count++;
                        }
                        else count += AppendBlock(rtb, child, s, contentWidth, indent + 1, foreground);
                        first = false;
                    }
                }
                return count;
            default:
                return 0;
        }
    }

    /// <summary>Language label + copy button above a code block (the code itself stays selectable text).</summary>
    private static UIElement CodeHeader(MdCode code, RenderStyle s)
    {
        var copy = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new FontIcon { Glyph = "\uE8C8", FontSize = 11 }, new TextBlock { Text = "Copy", FontSize = 11 } } },
            Padding = new Thickness(6, 2, 6, 2), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right, Opacity = 0.8,
        };
        ToolTipService.SetToolTip(copy, "Copy code");
        copy.Click += (_, _) =>
        {
            try
            {
                var package = new DataPackage();
                package.SetText(code.Code);
                Clipboard.SetContent(package);
            }
            catch (Exception) { /* clipboard busy (another app holds it): ignore rather than crash */ }
        };
        var grid = new Grid { Background = s.CodeBackground, CornerRadius = new CornerRadius(s.Radius, s.Radius, 0, 0), Padding = new Thickness(10, 0, 2, 0) };
        grid.Children.Add(new TextBlock { Text = code.Language ?? "code", FontSize = 11, Foreground = s.Muted, VerticalAlignment = VerticalAlignment.Center });
        grid.Children.Add(copy);
        return grid;
    }

    /// <summary>Tables as aligned monospace text so they select and copy with the rest of the answer.</summary>
    private static Paragraph TableText(MdTable table, RenderStyle s, int indent)
    {
        static string Plain(IReadOnlyList<MdInline> inlines) => string.Concat(inlines.Select(i => i switch
        {
            MdText t => t.Text,
            MdLink l => string.Concat(l.Inlines.OfType<MdText>().Select(x => x.Text)),
            MdBreak => " ",
            _ => "",
        }));
        var rows = table.Rows.Select(r => r.Select(Plain).ToList()).ToList();
        var columns = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        var widths = Enumerable.Range(0, columns).Select(c => rows.Max(r => c < r.Count ? r[c].Length : 0)).ToList();
        var p = new Paragraph { FontFamily = Mono, FontSize = s.FontSize - 1, Margin = new Thickness(indent * 18, 0, 0, 10) };
        for (var r = 0; r < rows.Count; r++)
        {
            if (r > 0) p.Inlines.Add(new LineBreak());
            var line = string.Join("  ", Enumerable.Range(0, columns).Select(c => (c < rows[r].Count ? rows[r][c] : "").PadRight(widths[c]))).TrimEnd();
            var run = new Run { Text = line };
            if (table.HasHeader && r == 0) run.FontWeight = FontWeights.SemiBold;
            p.Inlines.Add(run);
            if (table.HasHeader && r == 0)
            {
                p.Inlines.Add(new LineBreak());
                p.Inlines.Add(new Run { Text = string.Join("  ", widths.Select(w => new string('─', Math.Max(1, w)))), Foreground = s.Muted });
            }
        }
        return p;
    }

    private static Inline ToInline(MdInline inline, RenderStyle s, bool bold) => inline switch
    {
        MdText t => ToRun(t, bold),
        MdBreak => new LineBreak(),
        MdLink l => ToLink(l, s, bold),
        _ => new Run(),
    };

    private static Run ToRun(MdText t, bool bold)
    {
        var run = new Run { Text = t.Text };
        if (bold || t.Style.HasFlag(MdStyle.Bold)) run.FontWeight = FontWeights.SemiBold;
        if (t.Style.HasFlag(MdStyle.Italic)) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
        if (t.Style.HasFlag(MdStyle.Strike)) run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
        if (t.Style.HasFlag(MdStyle.Code)) run.FontFamily = Mono;
        return run;
    }

    private static Hyperlink ToLink(MdLink link, RenderStyle s, bool bold)
    {
        var h = new Hyperlink();
        foreach (var child in link.Inlines.OfType<MdText>()) h.Inlines.Add(ToRun(child, bold));
        h.Click += (_, _) => s.OpenLink(link.Url);
        ToolTipService.SetToolTip(h, link.Url);
        return h;
    }

}
