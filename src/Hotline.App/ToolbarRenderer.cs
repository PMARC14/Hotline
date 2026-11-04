using System.Buffers.Binary;
using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.App.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Hotline.Core.Theming;

namespace Hotline.App;

/// <summary>
/// Draws toolbar.json (a resolved <see cref="ToolbarModel"/>) into the panel's two button rows. Each row is
/// <c>[left group…] [*] [right group…]</c>; the three pickers are items like the buttons, each placed where the model lists it. Built-in buttons are the XAML ones,
/// custom buttons are made anew on every apply (with their picture when they have one, see <see cref="CustomButtonPicture"/>). When a row is too narrow its last left buttons move into a "⋯" menu
/// (<see cref="Fit"/>, run after layout by ProviderBar). What goes where is decided in <see cref="ToolbarPlan"/>.
/// </summary>
internal sealed class ToolbarRenderer
{
    private sealed class Row(Grid grid, Button more)
    {
        public Grid Grid { get; } = grid;
        public Button More { get; } = more;
        public RowPlan Plan { get; set; } = new([], [], []);
        /// <summary>The buttons in the "⋯" menu, in bar order: left, centre, then the right group's.</summary>
        public IReadOnlyList<string> Dropped { get; set; } = [];
        /// <summary>The buttons the user put in the more button's menu, when this row holds that button (they come first in its menu).</summary>
        public IReadOnlyList<string> UserMenu { get; set; } = [];
        public (int LeftShown, int RightDropped, bool Overflow, string HiddenPickers)? Arranged { get; set; }
    }

    private readonly PopupWindow _popup;
    private readonly Action<CustomButton> _custom;
    private readonly FileLog _log;
    private readonly Dictionary<string, ButtonBase> _builtIn;
    private readonly Dictionary<string, (Button Button, CustomButton Definition)> _customButtons = [];
    private readonly Row _top, _bottom;
    /// <summary>The placed "more" item (toolbar.json): the menu button of the row that holds it, in place of that row's automatic "⋯".</summary>
    private readonly Button _moreItem = MoreButton();

    /// <summary>The model the rows show; set only once a whole apply has succeeded.</summary>
    public ToolbarModel Model { get; private set; } = ToolbarModel.Defaults;

    /// <summary>The last fit's room and available pickers: an apply fits to them at once, so a changed bar isn't drawn too wide
    /// for a frame before the queued layout (which then fits to the current width).</summary>
    private (double Room, IReadOnlyCollection<string> Available)? _lastFit;

    private readonly Dictionary<string, ComboBox> _pickers;

    public ToolbarRenderer(PopupWindow popup, Action<CustomButton> custom, FileLog log)
    {
        (_popup, _custom, _log) = (popup, custom, log);
        _builtIn = new Dictionary<string, ButtonBase>
        {
            ["pin"] = popup.PinButton, ["captureWindow"] = popup.CaptureWindowButton, ["captureScreen"] = popup.CaptureScreenButton,
            ["captureRegion"] = popup.CaptureRegionButton, ["prompt"] = popup.PromptButton, ["recent"] = popup.RecentButton,
            ["continueIn"] = popup.ContinueInButton, ["newChat"] = popup.NewChatButton, ["settings"] = popup.SettingsButton,
            [ToolbarModel.MoreId] = _moreItem,
            ["copyLastAnswer"] = popup.CopyLastAnswerButton,
        };
        // Clicking a bar button leaves keyboard focus in the message box: with 26H2 the Copilot key's keystrokes reached a
        // focused pin button and turned pinning off.
        foreach (var b in _builtIn.Values) b.AllowFocusOnInteraction = false;
        _pickers = new Dictionary<string, ComboBox> { ["effort"] = popup.EffortBox, ["model"] = popup.ModelBox, ["provider"] = popup.ProviderBox };
        // Every item is a cell of the grid with its padding inside (ToolbarPlan.CellGap / CellInset): the rows have no column gap.
        popup.TopToolbar.ColumnSpacing = popup.Toolbar.ColumnSpacing = ToolbarPlan.CellGap;
        // A picker is as tall as a button: the cell's height less the inset (PopupWindow.xaml's ComboBox styles carry the same value).
        foreach (var (id, picker) in _pickers)
        {
            (picker.Margin, picker.Height) = (new Thickness(ToolbarPlan.CellInset), ToolbarPlan.Inner(ToolbarPlan.BaseHeight));
            OwnLookBrushes(id, picker);
        }
        // A theme change (light, dark, contrast) changes the fill the filled look takes from the theme's button brush.
        popup.Root.ActualThemeChanged += (_, _) => { if (Model is { } m) Restyle(m); };
        _top = new Row(popup.TopToolbar, MoreButton());
        _bottom = new Row(popup.Toolbar, MoreButton());
        _top.More.Flyout = OverflowMenu(() => _top);
        _bottom.More.Flyout = OverflowMenu(() => _bottom);
        _moreItem.Flyout = OverflowMenu(() => new[] { _top, _bottom }.FirstOrDefault(r => r.Plan.HasMore));
    }

    private static Button MoreButton()
    {
        var more = new Button { Content = Glyphs.More, AllowFocusOnInteraction = false, Margin = new Thickness(ToolbarPlan.CellInset) };
        ToolTipService.SetToolTip(more, Strings.Transcript_More);
        AutomationProperties.SetName(more, Strings.Transcript_More);
        return more;
    }

    private Hotline.Core.Settings.ChatSettings _chatSettings = new();
    private IReadOnlyList<string> _shortcutWarnings = [];
    private IReadOnlyDictionary<string, string> _shortcuts = Hotline.Core.Settings.ChatShortcuts.Defaults;
    public IReadOnlyDictionary<string, string> Shortcuts => _shortcuts;

    public string ShortcutTip(string id, string text) => _shortcuts.TryGetValue(id, out var combo) && combo.Length > 0
        ? text + "\n" + L.F(Strings.Shortcuts_Tooltip, combo) : text;

    public void SetShortcuts(Hotline.Core.Settings.ChatSettings settings)
    {
        _chatSettings = settings;
        var resolved = Model.ResolveShortcuts(settings);
        var warnings = resolved.Warnings.Except(Model.Warnings).ToList();
        if (!warnings.SequenceEqual(_shortcutWarnings))
        {
            _shortcutWarnings = warnings;
            foreach (var warning in warnings) _log.Info("toolbar.json: " + warning);
        }
        var shortcuts = Model.EffectiveShortcuts(settings.StopShortcut);
        foreach (var b in resolved.Buttons)
            if (b.Shortcut is { } combo) shortcuts[b.Id] = combo;
        _shortcuts = shortcuts;
        foreach (var (id, button) in _builtIn)
            ShortcutHelp(button, id, AutomationProperties.GetName(button));
        foreach (var (id, picker) in _pickers)
        {
            var text = AutomationProperties.GetName(picker);
            if (id != "effort" && picker.SelectedItem is string name && name.Trim().Length > 0) text += ": " + name.Trim();
            ShortcutHelp(picker, id, text);
        }
        foreach (var row in new[] { _top, _bottom })
            ShortcutHelp(row.More, "more", Strings.Transcript_More);
        foreach (var (id, custom) in _customButtons)
            ShortcutHelp(custom.Button, id, custom.Definition.Label);
    }

    private void ShortcutHelp(DependencyObject element, string id, string text)
    {
        ToolTipService.SetToolTip(element, ShortcutTip(id, text));
        AutomationProperties.SetAcceleratorKey(element, _shortcuts.GetValueOrDefault(id, ""));
    }

    /// <summary>Uses the same Click/toggle/flyout path as the bar and overflow. Pickers cycle through their items.</summary>
    public void InvokeShortcut(string id)
    {
        if (_customButtons.TryGetValue(id, out var custom))
        {
            if (custom.Button.IsEnabled) Invoke(custom.Button, Anchor(custom.Button));
            return;
        }
        if (_pickers.TryGetValue(id, out var picker))
        {
            if (picker.IsEnabled && picker.Items.Count > 0)
                picker.SelectedIndex = (picker.SelectedIndex + 1) % picker.Items.Count;
            return;
        }
        if (id == "more")
        {
            var rows = new[] { _top, _bottom };
            var row = rows.FirstOrDefault(r => r.Plan.HasMore) ?? rows.FirstOrDefault(r => r.Dropped.Count > 0);
            if (row is not null) Invoke(MenuButton(row), MenuButton(row));
            return;
        }
        if (!_builtIn.TryGetValue(id, out var button) || !button.IsEnabled) return;
        if (button is ToggleButton toggle) toggle.IsChecked = toggle.IsChecked != true;
        else Invoke((Button)button, Anchor(button));
    }

    /// <summary>Rebuilds both rows from the model and fits them to the last known width (the queued layout follows).</summary>
    public void Apply(ToolbarModel model)
    {
        if (ToolbarPlan.Unknown(model) is { Count: > 0 } unknown) // only a model built in memory can have these
            _log.Error($"button rows: no such item {string.Join(", ", unknown)}; left out");
        foreach (var row in new[] { _top, _bottom }) { row.Grid.Children.Clear(); row.Grid.ColumnDefinitions.Clear(); }
        foreach (var (button, _) in _customButtons.Values) button.Click -= OnCustomClick;
        _customButtons.Clear();
        foreach (var b in model.Buttons)
        {
            var button = new Button { Content = Glyph(b.Icon), AllowFocusOnInteraction = false, Tag = b.Id };
            if (CustomButtonPicture.Read(b, _log.Info) is { } picture)
            {
                // Its picture instead of the glyph, sized from the style (never measured): the glyph comes back if it can't be drawn.
                var size = ToolbarPlan.Metrics(model, b.Id);
                button.Content = CustomButtonPicture.View(picture, Math.Round(Math.Min(ToolbarPlan.Inner(size.Width), ToolbarPlan.Inner(size.Height)) * 0.6), () =>
                {
                    _log.Info($"button rows: picture {b.IconFile} can't be drawn; its icon is used");
                    button.Content = Glyph(b.Icon);
                });
            }
            ToolTipService.SetToolTip(button, b.Label); // plain text: never markup
            AutomationProperties.SetName(button, b.Label);
            button.Click += OnCustomClick;
            _customButtons[b.Id] = (button, b);
        }
        foreach (var (row, top) in new[] { (_top, true), (_bottom, false) })
        {
            row.Plan = ToolbarPlan.Row(model, top);
            row.UserMenu = row.Plan.HasMore ? ToolbarPlan.MenuItems(model) : [];
            row.Arranged = null;
            Arrange(row, row.Plan.Droppable.Count(id => !ToolbarPlan.Stays(id)), rightDropped: 0, overflow: false, hiddenPickers: []); // as Fit counts them
            var visibility = row.Plan.Left.Count + row.Plan.Center.Count + row.Plan.Right.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (row.Grid.Visibility != visibility) row.Grid.Visibility = visibility;
        }
        // The look, after placing; only values that differ are set. The pickers take the outline and fill but not the width (that is ProviderBar's).
        // Sizes come from the same Metrics as Fit's widths (every button one cell high), so what is drawn is what was fitted.
        Restyle(model);
        Model = model;
        SetShortcuts(_chatSettings);
        // Not inside a layout pass (App's file timer, the self-test): only moves buttons between a row and its menu.
        if (_lastFit is { } last) Fit(last.Room, last.Available);
    }

    private void OnCustomClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && _customButtons.TryGetValue(id, out var custom)) _custom(custom.Definition);
    }

    /// <summary>A validated icon code (E000-F8FF, see ToolbarModel) as its Segoe Fluent Icons character.</summary>
    private static string Glyph(string hex) =>
        char.ConvertFromUtf32(int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code) ? code : Convert.ToInt32(ToolbarModel.DefaultIcon, 16));

    private FrameworkElement Element(string id) =>
        _pickers.TryGetValue(id, out var picker) ? picker
        : _builtIn.TryGetValue(id, out var b) ? b
        : _customButtons[id].Button;

    /// <summary>Lays a row out: the left items kept (every picker stays in its group), "⋯" when some buttons moved into it, a flexible
    /// gap, the centre group (between two flexible gaps, so it sits in the free space), the right group (without the buttons it
    /// gave up). A picker in <paramref name="hiddenPickers"/> (no room, or nothing to show) is left out of the grid, so it costs
    /// no column gap.</summary>
    private void Arrange(Row row, int leftShown, int rightDropped, bool overflow, IReadOnlyCollection<string> hiddenPickers)
    {
        var hiddenKey = string.Join(",", hiddenPickers.Order());
        if (row.Arranged == (leftShown, rightDropped, overflow, hiddenKey)) return;
        row.Arranged = (leftShown, rightDropped, overflow, hiddenKey);
        var (shown, dropped) = ToolbarPlan.Split(row.Plan.Droppable, leftShown); // the left group, then the centre: the centre goes first
        var (rightShown, rightGone) = ToolbarPlan.SplitRight(row.Plan.Right, rightDropped);
        row.Dropped = [.. dropped, .. rightGone];
        var grid = row.Grid;
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        void Add(FrameworkElement element)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(element, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(element);
        }
        void Gap() => grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var id in shown.Where(row.Plan.Left.Contains).Where(Visible)) Add(Element(id));
        if (overflow && !row.Plan.HasMore) Add(row.More); // a row with the more item has that as its menu button
        Gap();
        var centre = shown.Where(row.Plan.Center.Contains).Where(Visible).ToList();
        if (centre.Count > 0)
        {
            foreach (var id in centre) Add(Element(id));
            Gap();
        }
        foreach (var id in rightShown.Where(Visible)) Add(Element(id));
        // A row whose items are all hidden pickers has no children: collapse it, or its margin stays as a blank gap.
        var rowVisibility = grid.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (grid.Visibility != rowVisibility) grid.Visibility = rowVisibility;

        bool Visible(string id) => !hiddenPickers.Contains(id);
    }

    /// <summary>
    /// Moves the buttons that don't fit (the left ones, and only when the right group alone is too wide, its first ones) in <paramref name="room"/> into each row's "⋯" menu (and back), keeping
    /// the pickers' minimum when it can (see <see cref="ToolbarPlan.PlanRow"/>). Widths come from the resolved sizes, never
    /// from measuring (a moved button measures 0), so the result doesn't depend on the last pass. Returns each picker's width
    /// (0 = hidden, and then it is not in its row at all); <paramref name="availablePickers"/> are the ones with something to show.
    /// </summary>
    public IReadOnlyDictionary<string, double> Fit(double room, IReadOnlyCollection<string> availablePickers)
    {
        _lastFit = (room, availablePickers);
        var widths = new Dictionary<string, double>();
        foreach (var (row, top) in new[] { (_top, true), (_bottom, false) })
        {
            if (row.Plan.Droppable.Count + row.Plan.Right.Count == 0) continue;
            // The same call the tests make: which buttons stay, and the widths of the row's pickers from what they leave. The
            // row's column gap lies between any two items, pickers too.
            var layout = ToolbarPlan.PlanRow(Model, top, room, row.Grid.ColumnSpacing, availablePickers);
            var hidden = layout.PickerWidths.Where(p => p.Value <= 0).Select(p => p.Key).ToList();
            Arrange(row, layout.Fit.LeftShown, layout.Fit.RightDropped, layout.Fit.Overflow, hidden);
            foreach (var (id, width) in layout.PickerWidths) widths[id] = width;
        }
        return widths;
    }

    /// <summary>The row's menu button: its placed more item, else its automatic "⋯".</summary>
    private Button MenuButton(Row row) => row.Plan.HasMore ? _moreItem : row.More;

    /// <summary>Where a menu that belongs to a bar button opens: the button, or the row's menu button while it is in that menu (dropped
    /// from the row, or listed in the more menu).</summary>
    public FrameworkElement Anchor(FrameworkElement button)
    {
        foreach (var row in new[] { _top, _bottom })
            if (row.Dropped.Concat(row.UserMenu).Any(id => ReferenceEquals(_builtIn.GetValueOrDefault(id), button))) return MenuButton(row);
        return button.Parent is not null && button.ActualWidth > 0 ? button : _popup.Input;
    }

    // ---- style ----

    /// <summary>The panel theme's accent (#RRGGBB: Hotline's purple, the Windows accent when matching is on, or the user's
    /// own); the default outline colour. Set by the theme (<see cref="SetAccent"/>), never read from Windows here.</summary>
    private string? _accent;

    /// <summary>Takes the theme's accent and redraws the outlines when it changed.</summary>
    public void SetAccent(string? hex)
    {
        if (_accent == hex) return;
        _accent = ToolbarAccent.Theme = hex;
        if (Model is { } model) Restyle(model);
    }

    private void Restyle(ToolbarModel model)
    {
        var accent = AccentOutline();
        foreach (var (id, button) in _builtIn) ApplyStyle(button, model.EffectiveStyle(id), ToolbarPlan.Metrics(model, id), accent);
        foreach (var (id, (button, _)) in _customButtons) ApplyStyle(button, model.EffectiveStyle(id), ToolbarPlan.Metrics(model, id), accent);
        foreach (var (id, box) in _pickers) ApplyPickerStyle(id, box, model.EffectiveStyle(id), accent);
        // The automatic "⋯" button is always one cell (BaseWidth x one cell high), whatever the bar's width.
        var overflowStyle = model.Style with { Width = 1 };
        ApplyStyle(_top.More, overflowStyle, ToolbarPlan.Metrics(overflowStyle), accent);
        ApplyStyle(_bottom.More, overflowStyle, ToolbarPlan.Metrics(overflowStyle), accent);
    }

    /// <summary>The outline's thickness: drawn inside the button's fixed width and height, so it never changes the layout.</summary>
    private static readonly Thickness OutlineThickness = new(1.5);

    private void ApplyStyle(ButtonBase button, ButtonStyle style, ButtonMetrics size, Windows.UI.Color accent)
    {
        var key = (button is ToggleButton ? "ToolbarToggle" : "ToolbarButton") + (style.Outline == true ? "Outline" : "") + (style.Filled == true ? "Filled" : "");
        if (_popup.Root.Resources[key] is Style look && button.Style != look) button.Style = look;
        // The size is the item's whole cell; the button is that less the inset on each side, so touching cells keep a visible gap.
        var (width, height) = (ToolbarPlan.Inner(size.Width), ToolbarPlan.Inner(size.Height));
        var inset = new Thickness(ToolbarPlan.CellInset);
        if (button.Margin != inset) button.Margin = inset;
        if (button.Width != width) button.Width = width;
        if (button.Height != height) button.Height = height;
        if (button.FontSize != size.FontSize) button.FontSize = size.FontSize;
        // The outline in a colour (the file's #RRGGBB, validated by Resolve, or the accent) over the style's grey border: a
        // brush made from parsed bytes, never markup. Hover and pressed keep the template's own states.
        if (style.Outline == true)
        {
            var color = style.OutlineColor is { } hex && ThemeColor.TryParse(hex, out var c) ? Windows.UI.Color.FromArgb(0xFF, c.R, c.G, c.B) : accent;
            if (button.ReadLocalValue(Control.BorderBrushProperty) is not SolidColorBrush brush || brush.Color != color) button.BorderBrush = new SolidColorBrush(color);
            if (button.BorderThickness != OutlineThickness) button.BorderThickness = OutlineThickness;
        }
        else
        {
            // Back to the style's own (no) border.
            if (button.ReadLocalValue(Control.BorderBrushProperty) != DependencyProperty.UnsetValue) button.ClearValue(Control.BorderBrushProperty);
            if (button.ReadLocalValue(Control.BorderThicknessProperty) != DependencyProperty.UnsetValue) button.ClearValue(Control.BorderThicknessProperty);
        }
    }

    // ---- the pickers' look ----

    /// <summary>Each picker's own fill and outline brushes. They are the picker's Background and BorderBrush for good, and also stand in
    /// for the theme brushes of its fill and border states (the picker's own resources), so the look survives hover, press and focus:
    /// a look change is a change of the brushes' colours (transparent = no look), never of the control's template or states.</summary>
    private readonly Dictionary<string, (SolidColorBrush Fill, SolidColorBrush Outline)> _pickerBrushes = [];

    private static readonly Windows.UI.Color NoColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

    private void OwnLookBrushes(string id, ComboBox box)
    {
        var (fill, outline) = (new SolidColorBrush(NoColor), new SolidColorBrush(NoColor));
        _pickerBrushes[id] = (fill, outline);
        box.Background = fill;
        box.BorderBrush = outline;
        // The bar's pickers have transparent theme brushes for these states (PopupWindow.xaml); their own take the look instead.
        // Hover and press keep the theme's own background (ComboBoxBackgroundPointerOver / Pressed).
        foreach (var key in new[] { "ComboBoxBackground", "ComboBoxBackgroundFocused", "ComboBoxBackgroundUnfocused" }) box.Resources[key] = fill;
        foreach (var key in new[] { "ComboBoxBorderBrush", "ComboBoxBorderBrushPointerOver", "ComboBoxBorderBrushPressed", "ComboBoxBorderBrushFocused" }) box.Resources[key] = outline;
    }

    /// <summary>A picker's outline (colour, or the accent; <see cref="OutlineThickness"/> inside its fixed height) and fill (the theme's button
    /// background, like a filled button). Only values that differ are set.</summary>
    private void ApplyPickerStyle(string id, ComboBox box, ButtonStyle style, Windows.UI.Color accent)
    {
        if (!_pickerBrushes.TryGetValue(id, out var brushes)) return;
        var fill = style.Filled == true ? FillColor() : NoColor;
        if (brushes.Fill.Color != fill) brushes.Fill.Color = fill;
        var outline = style.Outline == true
            ? style.OutlineColor is { } hex && ThemeColor.TryParse(hex, out var c) ? Windows.UI.Color.FromArgb(0xFF, c.R, c.G, c.B) : accent
            : NoColor;
        if (brushes.Outline.Color != outline) brushes.Outline.Color = outline;
        var thickness = style.Outline == true ? OutlineThickness : new Thickness(0);
        if (box.BorderThickness != thickness) box.BorderThickness = thickness;
    }

    /// <summary>The colour the filled buttons have (the theme's ButtonBackground), for a filled picker.</summary>
    private static Windows.UI.Color FillColor() =>
        Application.Current.Resources.TryGetValue("ButtonBackground", out var v) && v is SolidColorBrush { Color: var c } ? c
        : Windows.UI.Color.FromArgb(0x30, 0x80, 0x80, 0x80);

    /// <summary>The default outline colour: <see cref="ToolbarAccent"/> (shared with Settings' swatches).</summary>
    private Windows.UI.Color AccentOutline() => ToolbarAccent.Resolve(_accent);

    // ---- the "⋯" menu ----

    /// <summary>The menu of a row's menu button: the user's more-menu entries, a separator, then what the row dropped. Empty (the placed more
    /// item with nothing in it): one disabled "Nothing here" entry.</summary>
    private MenuFlyout OverflowMenu(Func<Row?> rowOf)
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft };
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            if (rowOf() is not { } row) return;
            foreach (var id in row.UserMenu) menu.Items.Add(Item(id, row));
            if (row.UserMenu.Count > 0 && row.Dropped.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
            foreach (var id in row.Dropped) menu.Items.Add(Item(id, row));
            if (menu.Items.Count == 0) menu.Items.Add(new MenuFlyoutItem { Text = Strings.Bar_NothingHere, IsEnabled = false });
        };
        IDisposable? hold = null;
        menu.Opened += (_, _) => hold = _popup.HoldResize(); // a resize would close the menu
        menu.Closed += (_, _) => { hold?.Dispose(); hold = null; };
        return menu;
    }

    /// <summary>A menu entry doing what the button does (through the button's own handlers).</summary>
    private MenuFlyoutItemBase Item(string id, Row row)
    {
        if (_customButtons.TryGetValue(id, out var custom))
        {
            var item = new MenuFlyoutItem { Text = custom.Definition.Label, Icon = new FontIcon { Glyph = Glyph(custom.Definition.Icon) } };
            ShortcutHelp(item, id, custom.Definition.Label);
            if (CustomButtonPicture.Read(custom.Definition, _log.Info) is { } picture)
            {
                var image = new ImageIcon();
                item.Icon = image;
                CustomButtonPicture.Load(picture, 16, bitmap => image.Source = bitmap,
                    () => item.Icon = new FontIcon { Glyph = Glyph(custom.Definition.Icon) });
            }
            item.Click += (_, _) => _custom(custom.Definition);
            return item;
        }
        var button = _builtIn[id];
        var text = L.Get("Bar_" + id);
        var icon = new FontIcon { Glyph = button.Content as string ?? "" };
        if (button is ToggleButton toggle)
        {
            var item = new ToggleMenuFlyoutItem { Text = text, Icon = icon, IsChecked = toggle.IsChecked == true };
            ShortcutHelp(item, id, text);
            item.Click += (_, _) => toggle.IsChecked = toggle.IsChecked != true; // its Checked/Unchecked handlers do the rest
            return item;
        }
        var entry = new MenuFlyoutItem { Text = text, Icon = icon };
        ShortcutHelp(entry, id, text);
        // After this menu has closed, so a menu the button opens isn't closed with it.
        entry.Click += (_, _) => _popup.DispatcherQueue.TryEnqueue(() =>
        {
            // An exception escaping a DispatcherQueue callback ends the process.
            try { Invoke((Button)button, MenuButton(row)); }
            catch (Exception ex) { _log.Error($"bar item {id} from the overflow menu failed", ex); }
        });
        return entry;
    }

    private void Invoke(Button button, FrameworkElement anchor)
    {
        if (button.Flyout is { } flyout)
        {
            // Its own menu (the prompt list), opened at the row's menu button: the button itself isn't on screen.
            var hold = _popup.HoldResize();
            void Closed(object? s, object e) { flyout.Closed -= Closed; hold.Dispose(); }
            flyout.Closed += Closed;
            try { flyout.ShowAt(anchor); }
            catch
            {
                Closed(null, EventArgs.Empty); // it never opened: release the hold now
                throw;
            }
            return;
        }
        new ButtonAutomationPeer(button).Invoke(); // raises its Click: the same handlers as the button
    }
}

/// <summary>
/// A custom button's picture (toolbar.json "iconFile", in icons\): read and checked by <see cref="CustomIcons"/> each time,
/// then drawn from those same bytes (no file URI: nothing changes between the check and the drawing, and no image cache
/// keeps an old picture), decoded at the size it is shown. Used by the bar and by Settings.
/// </summary>
internal static class CustomButtonPicture
{
    public static string Folder => CustomIcons.Folder(App.DataDirectory);

    /// <summary>The checked bytes of <paramref name="b"/>'s picture, or null when it has none or it can't be used (logged
    /// with the file name and the reason only).</summary>
    public static byte[]? Read(CustomButton b, Action<string> log)
    {
        if (b.IconFile is null || CustomIcons.IconPath(Folder, b.IconFile) is not { } path) return null;
        var (bytes, problem) = CustomIcons.Read(path);
        if (bytes is null) log($"button rows: picture {b.IconFile} not shown ({problem}); its icon is used");
        return bytes;
    }

    /// <summary>An Image of <paramref name="size"/> DIP square showing a checked PNG (aspect kept); <paramref name="failed"/>
    /// runs on the UI thread if it can't be decoded after all (a damaged pixel stream passes the header check).</summary>
    public static Image View(byte[] png, double size, Action failed)
    {
        var image = new Image { Width = size, Height = size, Stretch = Stretch.Uniform };
        Load(png, size, bitmap => image.Source = bitmap, failed);
        return image;
    }

    /// <summary>Decodes a checked PNG at most <paramref name="size"/> DIP on its longer side, then hands it to
    /// <paramref name="loaded"/>, or calls <paramref name="failed"/>. Never throws (it runs as a queued continuation).</summary>
    public static void Load(byte[] png, double size, Action<BitmapImage> loaded, Action failed) => _ = LoadAsync(png, size, loaded, failed);

    private static async Task LoadAsync(byte[] png, double size, Action<BitmapImage> loaded, Action failed)
    {
        var done = false;
        void Fail()
        {
            if (done) return;
            done = true;
            try { failed(); }
            catch (Exception) { } // the glyph fallback itself failed: the button stays as it is
        }
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            // Only the longer side is bounded, so the decoder keeps the aspect (IHDR width and height: bytes 16-23, checked).
            var wide = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16)) >= BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20));
            var decode = Math.Max(1, (int)Math.Ceiling(size));
            var bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Logical };
            if (wide) bitmap.DecodePixelWidth = decode;
            else bitmap.DecodePixelHeight = decode;
            bitmap.ImageFailed += (_, _) => Fail(); // a decode error can arrive as this event instead of an exception
            await bitmap.SetSourceAsync(stream);
            loaded(bitmap);
        }
        catch (Exception) { Fail(); }
    }
}
