using System.Globalization;
using Hotline.Core.Text.Localization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Hotline.App.Settings;

/// <summary>
/// The navigation pane's width follows its longest label: some languages' labels ("Conversation et historique") are wider than the
/// pane's 210 DIP. What the item template takes beside a label (icon column, padding) is read after layout, never guessed; the pane
/// grows to the widest label plus that, up to <see cref="MaxPaneLength"/>.
/// </summary>
public sealed partial class SettingsWindow
{
    /// <summary>The widest the pane grows (DIP): the window is clamped to the work area, so the page keeps most of it.</summary>
    private const double MaxPaneLength = 320;

    /// <summary>After the first layout: widens the pane when a label would be trimmed. Not a size event, so changing the pane is safe here.</summary>
    private void FitNavPane()
    {
        var labels = NavLabels().ToList();
        if (labels.Count == 0) return;
        var overhead = Overhead(labels[0]);
        var needed = labels.Max(l => NaturalWidth(l.Label, l.Text)) + overhead + 1;
        var length = Math.Min(MaxPaneLength, Math.Ceiling(needed));
        if (length <= Nav.OpenPaneLength) return;
        _log.Info($"settings: navigation pane widened to {length} for its labels");
        Nav.OpenPaneLength = length;
    }

    /// <summary>What an item takes beside its label: the item's width less the label's layout slot (a TextBlock's ActualWidth is only
    /// its text's). The same at any pane width.</summary>
    private static double Overhead((NavigationViewItem Item, TextBlock Label, string Text) l) =>
        l.Item.ActualWidth - (LayoutInformation.GetLayoutSlot(l.Label).Width - l.Label.Margin.Left - l.Label.Margin.Right);

    /// <summary>Each navigation item with its label TextBlock (laid out) and text.</summary>
    private IEnumerable<(NavigationViewItem Item, TextBlock Label, string Text)> NavLabels()
    {
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
            if (item.Content is string text && item.ActualWidth > 0 && Find(item, text) is { } label) yield return (item, label, text);

        static TextBlock? Find(DependencyObject root, string text)
        {
            if (root is TextBlock { Text: var t } block && t == text) return block;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
                if (Find(VisualTreeHelper.GetChild(root, i), text) is { } found) return found;
            return null;
        }
    }

    /// <summary>The width <paramref name="text"/> takes on one line in <paramref name="like"/>'s font (DirectWrite, with its font fallback).</summary>
    private static double NaturalWidth(TextBlock like, string text)
    {
        var probe = new TextBlock
        {
            Text = text, FontSize = like.FontSize, FontFamily = like.FontFamily, FontWeight = like.FontWeight,
            FontStretch = like.FontStretch, CharacterSpacing = like.CharacterSpacing,
        };
        probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return probe.DesiredSize.Width;
    }

    /// <summary>
    /// Self-test: lays the (hidden) window out and checks every language's navigation labels against the widest pane
    /// (<see cref="MaxPaneLength"/> less the template's overhead). Logs the pane each language gets. Returns the problems; a hidden
    /// window that can't be laid out reports nothing (logged).
    /// </summary>
    internal IReadOnlyList<string> CheckNavLabels()
    {
        if (Content is not FrameworkElement root) return [];
        root.Measure(new Size(1000, 700));
        root.Arrange(new Rect(0, 0, 1000, 700));
        root.UpdateLayout();
        var labels = NavLabels().ToList();
        if (labels.Count == 0)
        {
            _log.Info("selftest settings: navigation labels not laid out in the hidden window; label widths not checked");
            return [];
        }
        var (_, sample, _) = labels[0];
        var overhead = Overhead(labels[0]);
        var room = MaxPaneLength - overhead;
        var keys = new[] { "General", "Connections", "Prompts", "Actions", "Appearance", "Window", "Chat", "Tools", "Advanced", "Backup", "Guide", "About" };
        var problems = new List<string>();
        var panes = new List<string>();
        foreach (var tag in Languages.Supported)
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            var widest = (Width: 0.0, Text: "");
            foreach (var key in keys)
            {
                var text = Strings.ResourceManager.GetString("Nav_" + key, culture) ?? "";
                var width = NaturalWidth(sample, text);
                if (width > widest.Width) widest = (width, text);
                if (width > room + 0.5) problems.Add($"navigation label {tag} \"{text}\" needs {width:0} DIP, the widest pane gives {room:0}");
            }
            panes.Add($"{tag} {Math.Max(Nav.OpenPaneLength, Math.Ceiling(widest.Width + overhead + 1)):0}");
        }
        _log.Info($"selftest settings: navigation overhead {overhead:0} DIP; pane per language: {string.Join(", ", panes)}");
        return problems;
    }
}
