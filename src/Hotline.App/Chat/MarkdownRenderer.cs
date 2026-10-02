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
    private static readonly FontFamily MathFont = new("Cambria Math, Segoe UI Symbol");

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
            case MdMath m:
            {
                var math = new Paragraph { FontFamily = MathFont, FontSize = s.FontSize + 2, TextAlignment = TextAlignment.Center, Margin = new Thickness(indent * 18, 2, 0, 10) };
                var lines = m.Text.Split('\n');
                for (var i = 0; i < lines.Length; i++)
                {
                    if (i > 0) math.Inlines.Add(new LineBreak());
                    math.Inlines.Add(new Run { Text = lines[i] });
                }
                rtb.Blocks.Add(math);
                return 1;
            }
            case MdTable t:
                rtb.Blocks.Add(Boxed(TableGrid(t, s)));
                return 1;
            case MdSvg svg:
                rtb.Blocks.Add(Boxed(SvgCard(svg, s, contentWidth)));
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
                    var marker = l.Ordered ? $"{l.Start + i}. " : (indent % 3) switch { 0 => "• ", 1 => "◦ ", _ => "▪ " };
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

    private static string PlainText(IReadOnlyList<MdInline> inlines) => string.Concat(inlines.Select(i => i switch
    {
        MdText t => t.Text,
        MdLink l => string.Concat(l.Inlines.OfType<MdText>().Select(x => x.Text)),
        MdBreak => " ",
        _ => "",
    }));

    /// <summary>
    /// A real table: aligned columns that scroll sideways when wide (instead of wrapping), selectable cells, a header
    /// row and a Copy button (tab-separated, pastes into spreadsheets).
    /// </summary>
    private static UIElement TableGrid(MdTable table, RenderStyle s)
    {
        var columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count);
        var grid = new Grid { ColumnSpacing = 18, RowSpacing = 0 };
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r < table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = table.HasHeader && r == 0;
            for (var c = 0; c < table.Rows[r].Count; c++)
            {
                var cell = new RichTextBlock { IsTextSelectionEnabled = true, FontSize = s.FontSize, FontFamily = s.Font, Margin = new Thickness(0, 4, 0, 4) };
                var p = new Paragraph();
                foreach (var inline in table.Rows[r][c]) p.Inlines.Add(ToInline(inline, s, header));
                cell.Blocks.Add(p);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
            if (r < table.Rows.Count - 1)
            {
                var line = new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = s.Muted, Opacity = header ? 0.5 : 0.18 };
                Grid.SetRow(line, r);
                Grid.SetColumnSpan(line, Math.Max(1, columns));
                grid.Children.Add(line);
            }
        }
        var scroll = new ScrollViewer
        {
            Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 0, 6),
        };
        var tsv = string.Join("\n", table.Rows.Select(row => string.Join("\t", row.Select(PlainText))));
        var copy = SmallButton("\uE8C8", "Copy", "Copy table (pastes into spreadsheets)", () => SetClipboardText(tsv));
        copy.HorizontalAlignment = HorizontalAlignment.Right;
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(copy);
        stack.Children.Add(scroll);
        return stack;
    }

    /// <summary>
    /// An SVG drawing from the model, drawn natively. Windows' SVG renderer skips text labels and some effects, so
    /// "Open" shows the full drawing in the default viewer (scripts are stripped from the saved file first).
    /// </summary>
    private static UIElement SvgCard(MdSvg svg, RenderStyle s, double contentWidth)
    {
        var card = new StackPanel { Spacing = 4 };
        if (!svg.Complete)
        {
            card.Children.Add(new TextBlock { Text = "Drawing an image…", Foreground = s.Muted, FontStyle = Windows.UI.Text.FontStyle.Italic });
            return card;
        }
        var image = new Image { MaxHeight = 360, MaxWidth = Math.Max(120, contentWidth), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        var source = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource();
        image.Source = source;
        _ = LoadSvgAsync(source, svg.Markup);
        var open = SmallButton("\uE8A7", "Open image", "Open the full drawing (with labels) in your default viewer", () => OpenSvg(svg.Markup));
        card.Children.Add(new Border { Child = image, CornerRadius = new CornerRadius(s.Radius), HorizontalAlignment = HorizontalAlignment.Left });
        card.Children.Add(open);
        return card;
    }

    private static async Task LoadSvgAsync(Microsoft.UI.Xaml.Media.Imaging.SvgImageSource source, string markup)
    {
        try
        {
            using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using (var writer = new Windows.Storage.Streams.DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteString(SafeSvg(markup));
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);
            await source.SetSourceAsync(stream);
        }
        catch (Exception) { /* an SVG Windows can't draw: the Open button still works */ }
    }

    private static string SafeSvg(string markup)
    {
        var noScripts = System.Text.RegularExpressions.Regex.Replace(markup, @"<script\b[\s\S]*?</script\s*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        noScripts = System.Text.RegularExpressions.Regex.Replace(noScripts, @"\son\w+\s*=\s*(""[^""]*""|'[^']*')", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return noScripts.Contains("xmlns", StringComparison.Ordinal) ? noScripts : noScripts.Replace("<svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"");
    }

    private static void OpenSvg(string markup)
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), $"hotline-drawing-{(uint)markup.GetHashCode():x8}.svg");
            File.WriteAllText(path, SafeSvg(markup));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception) { /* no viewer for .svg */ }
    }

    private static Button SmallButton(string glyph, string text, string tooltip, Action click)
    {
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { new FontIcon { Glyph = glyph, FontSize = 11 }, new TextBlock { Text = text, FontSize = 11 } } },
            Padding = new Thickness(6, 2, 6, 2), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), Opacity = 0.8,
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => click();
        return button;
    }

    private static void SetClipboardText(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception) { /* clipboard busy (another app holds it): ignore rather than crash */ }
    }

    private static Inline ToInline(MdInline inline, RenderStyle s, bool bold) => inline switch
    {
        MdText t => ToRun(t, bold, s),
        MdBreak => new LineBreak(),
        MdLink l => ToLink(l, s, bold),
        _ => new Run(),
    };

    private static Run ToRun(MdText t, bool bold, RenderStyle s)
    {
        var run = new Run { Text = t.Text };
        if (bold || t.Style.HasFlag(MdStyle.Bold)) run.FontWeight = FontWeights.SemiBold;
        if (t.Style.HasFlag(MdStyle.Italic)) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
        if (t.Style.HasFlag(MdStyle.Strike)) run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
        if (t.Style.HasFlag(MdStyle.Code)) run.FontFamily = Mono;
        if (t.Style.HasFlag(MdStyle.Math)) run.FontFamily = MathFont;
        if (t.Style.HasFlag(MdStyle.Underline)) run.TextDecorations |= Windows.UI.Text.TextDecorations.Underline;
        if (t.Style.HasFlag(MdStyle.Mark)) { run.Foreground = s.Accent; run.FontWeight = FontWeights.SemiBold; } // ==marked== (no run backgrounds in WinUI)
        if (t.Style.HasFlag(MdStyle.Superscript)) Typography.SetVariants(run, FontVariants.Superscript);
        return run;
    }

    private static Hyperlink ToLink(MdLink link, RenderStyle s, bool bold)
    {
        var h = new Hyperlink();
        foreach (var child in link.Inlines.OfType<MdText>()) h.Inlines.Add(ToRun(child, bold, s));
        h.Click += (_, _) => s.OpenLink(link.Url);
        ToolTipService.SetToolTip(h, link.Url);
        return h;
    }

}
