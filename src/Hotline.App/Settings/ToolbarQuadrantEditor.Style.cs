using Hotline.Core.Theming;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Hotline.App.Settings;

/// <summary>The bar's look (width, outline, its colour, filled) and each item's own look (a flyout from its button).</summary>
internal sealed partial class ToolbarQuadrantEditor
{
    private ComboBox? _barSize, _barColor;
    private ToggleSwitch? _barOutline, _barFilled;
    /// <summary>Set while controls are filled from the model, so their change events don't edit it.</summary>
    private bool _syncing;

    /// <summary>A colour combo's first entry (the accent for the bar, "like the whole bar" for an item): no colour of its own.</summary>
    private const string NoColor = "";

    private Border BarStyleCard()
    {
        _barSize = new ComboBox { Header = Strings.Bar_Width, MinWidth = 140 };
        foreach (var width in ToolbarEditorView.Widths) _barSize.Items.Add(new ComboBoxItem { Content = ToolbarEditorView.WidthName(width), Tag = width });
        _barOutline = new ToggleSwitch { Header = Strings.Bar_Outline, OnContent = "", OffContent = "", MinWidth = 0 };
        _barFilled = new ToggleSwitch { Header = Strings.Bar_Filled, OnContent = "", OffContent = "", MinWidth = 0 };
        _barColor = ColorBox(Strings.Bar_ColorAccent, null);
        Automation(_barSize, "bar-size", Strings.Bar_Width);
        Automation(_barOutline, "bar-outline", Strings.Bar_Outline);
        Automation(_barFilled, "bar-filled", Strings.Bar_Filled);
        Automation(_barColor, "bar-outline-color", Strings.Bar_OutlineColor);
        // A width for the whole bar widens every button (the items' own widths go).
        // "Custom" only shows that the widths differ: choosing it changes nothing.
        _barSize.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _barSize.SelectedItem is ComboBoxItem { Tag: double width }) Edit(m => ToolbarEditor.SetBarSize(m, width), null);
        };
        _barOutline.Toggled += (_, _) => { if (!_syncing) Edit(m => ToolbarEditor.SetStyle(m, null, m.Style with { Outline = _barOutline.IsOn }), null); };
        _barFilled.Toggled += (_, _) => { if (!_syncing) Edit(m => ToolbarEditor.SetStyle(m, null, m.Style with { Filled = _barFilled.IsOn }), null); };
        _barColor.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _barColor.SelectedItem is ComboBoxItem { Tag: string hex })
                Edit(m => ToolbarEditor.SetStyle(m, null, m.Style with { OutlineColor = hex == NoColor ? null : hex }), null);
        };
        SyncBarStyle();
        return new Border
        {
            Style = _cardStyle,
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = Strings.Bar_BarStyle },
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { _barSize, _barOutline, _barFilled, _barColor } },
                },
            },
        };
    }

    private void SyncBarStyle()
    {
        if (_barSize is null || _barOutline is null || _barFilled is null || _barColor is null) return;
        _syncing = true;
        try
        {
            var style = _model.Style;
            // "Custom" is listed (after the widths) only while it is the state: the items' drawn widths differ from the bar's.
            var width = ToolbarEditorView.BarWidth(_model);
            var custom = _barSize.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is BarSizeChoice.Custom);
            if (width is null && custom is null)
                _barSize.Items.Add(custom = new ComboBoxItem { Content = Strings.Bar_SizeCustom, Tag = BarSizeChoice.Custom });
            if (width is null) _barSize.SelectedItem = custom;
            else
            {
                if (custom is not null) _barSize.Items.Remove(custom);
                _barSize.SelectedIndex = ToolbarEditorView.Widths.ToList().IndexOf(width.Value);
            }
            _barOutline.IsOn = style.Outline == true;
            _barFilled.IsOn = style.Filled == true;
            SelectColor(_barColor, style.OutlineColor);
            _barColor.IsEnabled = style.Outline == true;
        }
        finally { _syncing = false; }
    }

    private static void Automation(UIElement element, string automationId, string name)
    {
        AutomationProperties.SetAutomationId(element, automationId);
        AutomationProperties.SetName(element, name);
    }

    /// <summary>The outline colour choices: <paramref name="first"/> (no colour of its own, drawn as <paramref name="firstSwatch"/>
    /// or the accent), then the swatches, each named for screen readers.</summary>
    private ComboBox ColorBox(string first, string? firstSwatch)
    {
        var box = new ComboBox { Header = Strings.Bar_OutlineColor, MinWidth = 160 };
        box.Items.Add(ColorItem(NoColor, first, firstSwatch));
        foreach (var hex in ToolbarEditorView.OutlineColors) box.Items.Add(ColorItem(hex, ToolbarEditorView.ColorName(hex), hex));
        return box;
    }

    private ComboBoxItem ColorItem(string tag, string name, string? swatch)
    {
        // The swatch from parsed bytes (a file's colour is validated by Resolve); no colour of its own shows the colour the bar really
        // draws (the panel theme's accent, or a contrast theme's highlight: ToolbarAccent, the resolver the renderer uses).
        Brush fill = swatch is not null && ThemeColor.TryParse(swatch, out var c)
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, c.R, c.G, c.B))
            : new SolidColorBrush(ToolbarAccent.Current);
        var item = new ComboBoxItem
        {
            Tag = tag,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 8,
                Children =
                {
                    new Border
                    {
                        Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = fill, VerticalAlignment = VerticalAlignment.Center,
                        BorderThickness = new Thickness(1),
                        BorderBrush = Application.Current.Resources.TryGetValue("ControlStrokeColorDefaultBrush", out var stroke) ? stroke as Brush : null,
                    },
                    new TextBlock { Text = name }, // plain text: a colour set by hand shows as its hex
                },
            },
        };
        AutomationProperties.SetName(item, name);
        return item;
    }

    /// <summary>Selects <paramref name="hex"/> (null: the first entry); a colour set by hand in the file gets its own entry.</summary>
    private void SelectColor(ComboBox box, string? hex)
    {
        var tag = hex ?? NoColor;
        var index = box.Items.OfType<ComboBoxItem>().ToList().FindIndex(i => i.Tag as string == tag);
        if (index < 0)
        {
            box.Items.Add(ColorItem(tag, ToolbarEditorView.ColorName(hex), hex));
            index = box.Items.Count - 1;
        }
        box.SelectedIndex = index;
    }

    /// <summary>The item's look: width (or the bar's; 0.5, 1, 2, 3 or 4 cells, always one cell high), outline and its colour (or the bar's),
    /// filled, and "use the whole bar's look". A picker (<paramref name="picker"/>) has the look only: its width is its span, next to its
    /// name. Each change is saved at once; the editor is rebuilt when the flyout closes.</summary>
    private Button StyleButton(string id, string itemName, bool picker = false)
    {
        var automationId = "bar-style-" + id;
        var button = SmallButton("", Strings.Bar_ItemStyle, itemName, automationId, true, null);
        var flyout = new Flyout();
        flyout.Opening += (_, _) => flyout.Content = ItemStylePanel(id, itemName, picker);
        flyout.Opened += (_, _) => FlyoutOpened();
        flyout.Closed += (_, _) =>
        {
            if (_rebuildPending) _focus = (automationId, automationId); // only when a rebuild follows
            FlyoutClosed();
        };
        button.Flyout = flyout;
        return button;
    }

    private StackPanel ItemStylePanel(string id, string itemName, bool picker)
    {
        var width = new ComboBox { Header = Strings.Bar_Width, MinWidth = 200 };
        width.Items.Add(new ComboBoxItem { Content = Strings.Bar_AsBar });
        foreach (var w in ToolbarEditorView.Widths) width.Items.Add(new ComboBoxItem { Content = ToolbarEditorView.WidthName(w), Tag = w });
        var outline = new CheckBox { Content = Strings.Bar_Outline };
        var color = ColorBox(Strings.Bar_AsBar, _model.Style.OutlineColor);
        var filled = new CheckBox { Content = Strings.Bar_Filled };
        var reset = new Button { Content = Strings.Bar_UseBarLook };
        Automation(width, "bar-look-width", $"{Strings.Bar_Width}: {itemName}");
        Automation(outline, "bar-look-outline", $"{Strings.Bar_Outline}: {itemName}");
        Automation(color, "bar-look-outline-color", $"{Strings.Bar_OutlineColor}: {itemName}");
        Automation(filled, "bar-look-filled", $"{Strings.Bar_Filled}: {itemName}");
        Automation(reset, "bar-look-reset", $"{Strings.Bar_UseBarLook}: {itemName}");

        void Sync()
        {
            _syncing = true;
            try
            {
                var own = _model.Overrides.GetValueOrDefault(id) ?? new ButtonStyle();
                var effective = _model.EffectiveStyle(id);
                width.SelectedIndex = own.Width is { } w && ToolbarEditorView.Widths.ToList().IndexOf(w) is var at and >= 0 ? at + 1 : 0;
                outline.IsChecked = effective.Outline == true;
                SelectColor(color, own.OutlineColor);
                color.IsEnabled = effective.Outline == true; // set without a size change: the flyout stays open
                filled.IsChecked = effective.Filled == true;
            }
            finally { _syncing = false; }
        }
        void Set(Func<ButtonStyle, ButtonStyle> change)
        {
            if (_syncing) return;
            Edit(m => ToolbarEditor.SetStyle(m, id, change(m.Overrides.GetValueOrDefault(id) ?? new ButtonStyle())), id);
            Sync(); // what was saved (clamped), or the last good look when saving failed
        }

        width.SelectionChanged += (_, _) => Set(o => o with { Width = width.SelectedItem is ComboBoxItem { Tag: double w } ? w : null });
        outline.Click += (_, _) => Set(o => o with { Outline = outline.IsChecked == true });
        color.SelectionChanged += (_, _) =>
        {
            if (color.SelectedItem is ComboBoxItem { Tag: string hex }) Set(o => o with { OutlineColor = hex == NoColor ? null : hex });
        };
        filled.Click += (_, _) => Set(o => o with { Filled = filled.IsChecked == true });
        reset.Click += (_, _) => Set(_ => new ButtonStyle());
        Sync();
        var panel = new StackPanel
        {
            Spacing = 8, MinWidth = 220,
            Children =
            {
                new TextBlock { Text = itemName, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] },
                outline, color, filled, reset,
            },
        };
        if (!picker) panel.Children.Insert(1, width); // a picker's width is its span, beside its name
        return panel;
    }
}
