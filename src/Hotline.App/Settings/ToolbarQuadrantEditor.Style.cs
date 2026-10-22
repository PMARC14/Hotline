using Hotline.Core.Theming;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Hotline.App.Settings;

/// <summary>The bar's look (size, outline, its colour, filled) and each item's own look (a flyout from its button).</summary>
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
        _barSize = new ComboBox { Header = Strings.Bar_Size, MinWidth = 140 };
        foreach (var size in ToolbarEditorView.Sizes) _barSize.Items.Add(new ComboBoxItem { Content = ToolbarEditorView.SizeName(size), Tag = size });
        _barOutline = new ToggleSwitch { Header = Strings.Bar_Outline, OnContent = "", OffContent = "", MinWidth = 0 };
        _barFilled = new ToggleSwitch { Header = Strings.Bar_Filled, OnContent = "", OffContent = "", MinWidth = 0 };
        _barColor = ColorBox(Strings.Bar_ColorAccent, null);
        Automation(_barSize, "bar-size", Strings.Bar_Size);
        Automation(_barOutline, "bar-outline", Strings.Bar_Outline);
        Automation(_barFilled, "bar-filled", Strings.Bar_Filled);
        Automation(_barColor, "bar-outline-color", Strings.Bar_OutlineColor);
        // A size for the whole bar sizes every button (the items' own sizes go; a width or height set by hand stays).
        // "Custom" only shows that the items differ: choosing it changes nothing.
        _barSize.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _barSize.SelectedItem is ComboBoxItem { Tag: ButtonSize size }) Edit(m => ToolbarEditor.SetBarSize(m, size), null);
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
            // "Custom" is listed (after the sizes) only while it is the state: some item has a size of its own.
            var choice = ToolbarEditorView.BarSize(_model);
            var custom = _barSize.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is BarSizeChoice.Custom);
            if (choice == BarSizeChoice.Custom && custom is null)
                _barSize.Items.Add(custom = new ComboBoxItem { Content = Strings.Bar_SizeCustom, Tag = BarSizeChoice.Custom });
            if (choice == BarSizeChoice.Custom) _barSize.SelectedItem = custom;
            else
            {
                if (custom is not null) _barSize.Items.Remove(custom);
                _barSize.SelectedIndex = ToolbarEditorView.Sizes.ToList().IndexOf((ButtonSize)choice);
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
    private static ComboBox ColorBox(string first, string? firstSwatch)
    {
        var box = new ComboBox { Header = Strings.Bar_OutlineColor, MinWidth = 160 };
        box.Items.Add(ColorItem(NoColor, first, firstSwatch));
        foreach (var hex in ToolbarEditorView.OutlineColors) box.Items.Add(ColorItem(hex, ToolbarEditorView.ColorName(hex), hex));
        return box;
    }

    private static ComboBoxItem ColorItem(string tag, string name, string? swatch)
    {
        // The swatch from parsed bytes (a file's colour is validated by Resolve); the accent's from the theme.
        Brush? fill = swatch is not null && ThemeColor.TryParse(swatch, out var c)
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, c.R, c.G, c.B))
            : Application.Current.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent) ? accent as Brush : null;
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
    private static void SelectColor(ComboBox box, string? hex)
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

    /// <summary>The item's look: size (or the bar's), width and height (20-80, empty = from the size), outline and its colour
    /// (or the bar's), filled, and "use the whole bar's look". Each change is saved at once; the editor is rebuilt when the
    /// flyout closes.</summary>
    private Button StyleButton(string id, string itemName)
    {
        var automationId = "bar-style-" + id;
        var button = SmallButton("", Strings.Bar_ItemStyle, itemName, automationId, true, null);
        var flyout = new Flyout();
        flyout.Opening += (_, _) => flyout.Content = ItemStylePanel(id, itemName);
        flyout.Opened += (_, _) => FlyoutOpened();
        flyout.Closed += (_, _) =>
        {
            if (_rebuildPending) _focus = (automationId, automationId); // only when a rebuild follows
            FlyoutClosed();
        };
        button.Flyout = flyout;
        return button;
    }

    private StackPanel ItemStylePanel(string id, string itemName)
    {
        var size = new ComboBox { Header = Strings.Bar_Size, MinWidth = 200 };
        size.Items.Add(new ComboBoxItem { Content = Strings.Bar_AsBar });
        foreach (var s in ToolbarEditorView.Sizes) size.Items.Add(new ComboBoxItem { Content = ToolbarEditorView.SizeName(s), Tag = s });
        NumberBox Dip(string header) => new()
        {
            Header = header, Minimum = ToolbarPlan.MinDip, Maximum = ToolbarPlan.MaxDip, SmallChange = 1, LargeChange = 10, Width = 96, PlaceholderText = Strings.Bar_Auto,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        };
        var (width, height) = (Dip(Strings.Bar_Width), Dip(Strings.Bar_Height));
        var outline = new CheckBox { Content = Strings.Bar_Outline };
        var color = ColorBox(Strings.Bar_AsBar, _model.Style.OutlineColor);
        var filled = new CheckBox { Content = Strings.Bar_Filled };
        var reset = new Button { Content = Strings.Bar_UseBarLook };
        Automation(size, "bar-look-size", $"{Strings.Bar_Size}: {itemName}");
        Automation(width, "bar-look-width", $"{Strings.Bar_Width}: {itemName}");
        Automation(height, "bar-look-height", $"{Strings.Bar_Height}: {itemName}");
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
                size.SelectedIndex = own.Size is { } s ? ToolbarEditorView.Sizes.ToList().IndexOf(s) + 1 : 0;
                width.Value = own.Width ?? double.NaN;
                height.Value = own.Height ?? double.NaN;
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
        static int? Number(double v) => double.IsNaN(v) ? null : (int)Math.Round(v);

        size.SelectionChanged += (_, _) => Set(o => o with { Size = size.SelectedItem is ComboBoxItem { Tag: ButtonSize s } ? s : null });
        width.ValueChanged += (_, e) => Set(o => o with { Width = Number(e.NewValue) });
        height.ValueChanged += (_, e) => Set(o => o with { Height = Number(e.NewValue) });
        outline.Click += (_, _) => Set(o => o with { Outline = outline.IsChecked == true });
        color.SelectionChanged += (_, _) =>
        {
            if (color.SelectedItem is ComboBoxItem { Tag: string hex }) Set(o => o with { OutlineColor = hex == NoColor ? null : hex });
        };
        filled.Click += (_, _) => Set(o => o with { Filled = filled.IsChecked == true });
        reset.Click += (_, _) => Set(_ => new ButtonStyle());
        Sync();
        return new StackPanel
        {
            Spacing = 8, MinWidth = 220,
            Children =
            {
                new TextBlock { Text = itemName, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] },
                size,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { width, height } },
                outline, color, filled, reset,
            },
        };
    }
}
