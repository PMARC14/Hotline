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

    public static UIElement Render(IReadOnlyList<MdBlock> blocks, RenderStyle style)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var block in blocks) panel.Children.Add(RenderBlock(block, style));
        return panel;
    }

    public static UIElement RenderBlock(MdBlock block, RenderStyle s) => block switch
    {
        MdParagraph p => Rich(p.Inlines, s, s.FontSize, bold: false),
        MdHeading h => Rich(h.Inlines, s, s.FontSize + h.Level switch { 1 => 6, 2 => 4, _ => 2 }, bold: true),
        MdCode c => Code(c, s),
        MdList l => List(l, s),
        MdQuote q => new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0),
            BorderBrush = s.Accent, Child = Render(q.Blocks, s),
        },
        MdRule => new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = s.Muted, Opacity = 0.4 },
        MdTable t => Table(t, s),
        _ => new TextBlock(),
    };

    private static RichTextBlock Rich(IReadOnlyList<MdInline> inlines, RenderStyle s, double size, bool bold)
    {
        var rtb = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = size, FontFamily = s.Font };
        var paragraph = new Paragraph();
        foreach (var inline in inlines) paragraph.Inlines.Add(ToInline(inline, s, bold));
        rtb.Blocks.Add(paragraph);
        return rtb;
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

    private static UIElement Code(MdCode code, RenderStyle s)
    {
        var text = new TextBlock { Text = code.Code, FontFamily = Mono, FontSize = s.FontSize - 1, IsTextSelectionEnabled = true };
        var scroll = new ScrollViewer
        {
            Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(10, 8, 40, 8),
        };
        var copy = new Button
        {
            Content = new FontIcon { Glyph = "\uE8C8", FontSize = 13 }, Padding = new Thickness(6), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4),
        };
        ToolTipService.SetToolTip(copy, "Copy");
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
        var grid = new Grid();
        grid.Children.Add(scroll);
        grid.Children.Add(copy);
        var label = code.Language is null ? null : new TextBlock { Text = code.Language, FontSize = 11, Foreground = s.Muted, Margin = new Thickness(10, 6, 0, 0) };
        var stack = new StackPanel();
        if (label is not null) stack.Children.Add(label);
        stack.Children.Add(grid);
        return new Border { Background = s.CodeBackground, CornerRadius = new CornerRadius(s.Radius), Child = stack };
    }

    private static UIElement List(MdList list, RenderStyle s)
    {
        var panel = new StackPanel { Spacing = 4 };
        for (var i = 0; i < list.Items.Count; i++)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var marker = new TextBlock { Text = list.Ordered ? $"{list.Start + i}." : "•", FontSize = s.FontSize, FontFamily = s.Font, Foreground = s.Muted };
            var content = Render(list.Items[i], s);
            Grid.SetColumn((FrameworkElement)content, 1);
            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static UIElement Table(MdTable table, RenderStyle s)
    {
        var columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count);
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 4 };
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r < table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < table.Rows[r].Count; c++)
            {
                var cell = Rich(table.Rows[r][c], s, s.FontSize, bold: table.HasHeader && r == 0);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return new ScrollViewer
        {
            Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
    }
}
