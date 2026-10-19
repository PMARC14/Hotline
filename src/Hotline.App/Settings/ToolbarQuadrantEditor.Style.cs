using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Settings;

/// <summary>The bar's look (size, outline, filled) and each item's own look (a flyout from its button).</summary>
internal sealed partial class ToolbarQuadrantEditor
{
    private ComboBox? _barSize;
    private ToggleSwitch? _barOutline, _barFilled;
    /// <summary>Set while controls are filled from the model, so their change events don't edit it.</summary>
    private bool _syncing;

    private static readonly (ButtonSize Size, string Label)[] Sizes =
        [(ButtonSize.Compact, Strings.Bar_SizeCompact), (ButtonSize.Normal, Strings.Bar_SizeNormal), (ButtonSize.Wide, Strings.Bar_SizeWide)];

    private Border BarStyleCard()
    {
        _barSize = new ComboBox { Header = Strings.Bar_Size, MinWidth = 140 };
        foreach (var (size, label) in Sizes) _barSize.Items.Add(new ComboBoxItem { Content = label, Tag = size });
        _barOutline = new ToggleSwitch { Header = Strings.Bar_Outline, OnContent = "", OffContent = "", MinWidth = 0 };
        _barFilled = new ToggleSwitch { Header = Strings.Bar_Filled, OnContent = "", OffContent = "", MinWidth = 0 };
        Automation(_barSize, "bar-size", Strings.Bar_Size);
        Automation(_barOutline, "bar-outline", Strings.Bar_Outline);
        Automation(_barFilled, "bar-filled", Strings.Bar_Filled);
        // "with": a width or height set by hand for the whole bar stays.
        _barSize.SelectionChanged += (_, _) =>
        {
            if (!_syncing && _barSize.SelectedItem is ComboBoxItem { Tag: ButtonSize size }) Edit(m => ToolbarEditor.SetStyle(m, null, m.Style with { Size = size }), null);
        };
        _barOutline.Toggled += (_, _) => { if (!_syncing) Edit(m => ToolbarEditor.SetStyle(m, null, m.Style with { Outline = _barOutline.IsOn }), null); };
        _barFilled.Toggled += (_, _) => { if (!_syncing) Edit(m => ToolbarEditor.SetStyle(m, null, m.Style with { Filled = _barFilled.IsOn }), null); };
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
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { _barSize, _barOutline, _barFilled } },
                },
            },
        };
    }

    private void SyncBarStyle()
    {
        if (_barSize is null || _barOutline is null || _barFilled is null) return;
        _syncing = true;
        try
        {
            var style = _model.Style;
            _barSize.SelectedIndex = Array.FindIndex(Sizes, s => s.Size == (style.Size ?? ButtonSize.Normal));
            _barOutline.IsOn = style.Outline == true;
            _barFilled.IsOn = style.Filled == true;
        }
        finally { _syncing = false; }
    }

    private static void Automation(UIElement element, string automationId, string name)
    {
        AutomationProperties.SetAutomationId(element, automationId);
        AutomationProperties.SetName(element, name);
    }

    /// <summary>The item's look: size (or the bar's), width and height (20-80, empty = from the size), outline, filled, and
    /// "use the whole bar's look". Each change is saved at once; the editor is rebuilt when the flyout closes.</summary>
    private Button StyleButton(string id, string itemName)
    {
        var automationId = "bar-style-" + id;
        var button = SmallButton("\uE790", Strings.Bar_ItemStyle, itemName, automationId, true, null);
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
        foreach (var (s, label) in Sizes) size.Items.Add(new ComboBoxItem { Content = label, Tag = s });
        NumberBox Dip(string header) => new()
        {
            Header = header, Minimum = 20, Maximum = 80, SmallChange = 1, LargeChange = 10, Width = 96, PlaceholderText = Strings.Bar_Auto,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        };
        var (width, height) = (Dip(Strings.Bar_Width), Dip(Strings.Bar_Height));
        var outline = new CheckBox { Content = Strings.Bar_Outline };
        var filled = new CheckBox { Content = Strings.Bar_Filled };
        var reset = new Button { Content = Strings.Bar_UseBarLook };
        Automation(size, "bar-look-size", $"{Strings.Bar_Size}: {itemName}");
        Automation(width, "bar-look-width", $"{Strings.Bar_Width}: {itemName}");
        Automation(height, "bar-look-height", $"{Strings.Bar_Height}: {itemName}");
        Automation(outline, "bar-look-outline", $"{Strings.Bar_Outline}: {itemName}");
        Automation(filled, "bar-look-filled", $"{Strings.Bar_Filled}: {itemName}");
        Automation(reset, "bar-look-reset", $"{Strings.Bar_UseBarLook}: {itemName}");

        void Sync()
        {
            _syncing = true;
            try
            {
                var own = _model.Overrides.GetValueOrDefault(id) ?? new ButtonStyle();
                var effective = _model.EffectiveStyle(id);
                size.SelectedIndex = own.Size is { } s ? Array.FindIndex(Sizes, x => x.Size == s) + 1 : 0;
                width.Value = own.Width ?? double.NaN;
                height.Value = own.Height ?? double.NaN;
                outline.IsChecked = effective.Outline == true;
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
                outline, filled, reset,
            },
        };
    }
}
