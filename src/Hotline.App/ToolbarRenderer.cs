using Hotline.App.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Hotline.App;

/// <summary>
/// Draws toolbar.json (a resolved <see cref="ToolbarModel"/>) into the panel's two button rows. Each row is
/// <c>[left group…] [*] [right group…]</c>; the pickers are one block (PickersPanel). Built-in buttons are the XAML ones,
/// custom buttons are made anew on every apply. When a row is too narrow its last left buttons move into a "⋯" menu
/// (<see cref="Fit"/>, run after layout by ProviderBar). What goes where is decided in <see cref="ToolbarPlan"/>.
/// </summary>
internal sealed class ToolbarRenderer
{
    private sealed class Row(Grid grid, Button more)
    {
        public Grid Grid { get; } = grid;
        public Button More { get; } = more;
        public RowPlan Plan { get; set; } = new([], []);
        /// <summary>The left buttons in the "⋯" menu, in order.</summary>
        public IReadOnlyList<string> Dropped { get; set; } = [];
        public (int LeftShown, bool Overflow)? Arranged { get; set; }
    }

    private readonly PopupWindow _popup;
    private readonly Action<CustomButton> _custom;
    private readonly FileLog _log;
    private readonly Dictionary<string, ButtonBase> _builtIn;
    private readonly Dictionary<string, (Button Button, CustomButton Definition)> _customButtons = [];
    private readonly Row _top, _bottom;

    public ToolbarModel Model { get; private set; } = ToolbarModel.Defaults;

    public ToolbarRenderer(PopupWindow popup, Action<CustomButton> custom, FileLog log)
    {
        (_popup, _custom, _log) = (popup, custom, log);
        _builtIn = new Dictionary<string, ButtonBase>
        {
            ["pin"] = popup.PinButton, ["captureWindow"] = popup.CaptureWindowButton, ["captureScreen"] = popup.CaptureScreenButton,
            ["captureRegion"] = popup.CaptureRegionButton, ["prompt"] = popup.PromptButton, ["recent"] = popup.RecentButton,
            ["continueIn"] = popup.ContinueInButton, ["newChat"] = popup.NewChatButton, ["settings"] = popup.SettingsButton,
        };
        // Clicking a bar button leaves keyboard focus in the message box: with 26H2 the Copilot key's keystrokes reached a
        // focused pin button and turned pinning off.
        foreach (var b in _builtIn.Values) b.AllowFocusOnInteraction = false;
        _top = new Row(popup.TopToolbar, MoreButton());
        _bottom = new Row(popup.Toolbar, MoreButton());
        _top.More.Flyout = OverflowMenu(_top);
        _bottom.More.Flyout = OverflowMenu(_bottom);
    }

    private static Button MoreButton()
    {
        var more = new Button { Content = Glyphs.More, AllowFocusOnInteraction = false };
        ToolTipService.SetToolTip(more, Strings.Transcript_More);
        AutomationProperties.SetName(more, Strings.Transcript_More);
        return more;
    }

    /// <summary>Rebuilds both rows from the model, every item shown (the next <see cref="Fit"/> moves what doesn't fit).</summary>
    public void Apply(ToolbarModel model)
    {
        Model = model;
        foreach (var row in new[] { _top, _bottom }) { row.Grid.Children.Clear(); row.Grid.ColumnDefinitions.Clear(); }
        foreach (var (button, _) in _customButtons.Values) button.Click -= OnCustomClick;
        _customButtons.Clear();
        foreach (var b in model.Buttons)
        {
            var button = new Button { Content = Glyph(b.Icon), AllowFocusOnInteraction = false, Tag = b.Id };
            ToolTipService.SetToolTip(button, b.Label); // plain text: never markup
            AutomationProperties.SetName(button, b.Label);
            button.Click += OnCustomClick;
            _customButtons[b.Id] = (button, b);
        }
        // The picker boxes in the order listed; the block sits where the first one is.
        var boxes = new Dictionary<string, ComboBox> { ["effort"] = _popup.EffortBox, ["model"] = _popup.ModelBox, ["provider"] = _popup.ProviderBox };
        _popup.PickersPanel.Children.Clear();
        foreach (var name in ToolbarPlan.PickerOrder(model)) _popup.PickersPanel.Children.Add(boxes[name]);

        foreach (var (row, top) in new[] { (_top, true), (_bottom, false) })
        {
            row.Plan = ToolbarPlan.Row(model, top);
            row.Arranged = null;
            Arrange(row, row.Plan.Left.Count(id => !ToolbarPlan.IsPicker(id)), overflow: false); // as Fit counts them
            var visibility = row.Plan.Left.Count + row.Plan.Right.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (row.Grid.Visibility != visibility) row.Grid.Visibility = visibility;
        }
        // The look, after placing; only values that differ are set. The pickers take none of it (their widths are ProviderBar's).
        foreach (var (id, button) in _builtIn) ApplyStyle(button, model.EffectiveStyle(id));
        foreach (var (id, (button, _)) in _customButtons) ApplyStyle(button, model.EffectiveStyle(id));
        ApplyStyle(_top.More, model.Style);
        ApplyStyle(_bottom.More, model.Style);
    }

    private void OnCustomClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string id } && _customButtons.TryGetValue(id, out var custom)) _custom(custom.Definition);
    }

    /// <summary>A validated icon code (E000-F8FF, see ToolbarModel) as its Segoe Fluent Icons character.</summary>
    private static string Glyph(string hex) =>
        char.ConvertFromUtf32(int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code) ? code : Convert.ToInt32(ToolbarModel.DefaultIcon, 16));

    private FrameworkElement Element(string id) =>
        ToolbarPlan.IsPicker(id) ? _popup.PickersPanel
        : _builtIn.TryGetValue(id, out var b) ? b
        : _customButtons[id].Button;

    private double Width(string id) => ToolbarPlan.Metrics(Model.EffectiveStyle(id)).Width;

    private double MoreWidth => ToolbarPlan.Metrics(Model.Style).Width;

    /// <summary>Lays a row out: the left buttons kept (the pickers always), "⋯" when some moved into it, the flexible gap, the right group.</summary>
    private void Arrange(Row row, int leftShown, bool overflow)
    {
        if (row.Arranged == (leftShown, overflow)) return;
        row.Arranged = (leftShown, overflow);
        var (shown, dropped) = ToolbarPlan.Split(row.Plan.Left, leftShown);
        row.Dropped = dropped;
        var grid = row.Grid;
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        void Add(FrameworkElement element)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(element, grid.ColumnDefinitions.Count - 1);
            grid.Children.Add(element);
        }
        foreach (var id in shown) Add(Element(id));
        if (overflow) Add(row.More);
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var id in row.Plan.Right) Add(Element(id));
    }

    /// <summary>
    /// Moves the left buttons that don't fit in <paramref name="room"/> into each row's "⋯" menu (and back), keeping
    /// <paramref name="pickersMin"/> for the pickers when it can (see <see cref="ToolbarPlan.FitRow"/>). Widths come from the
    /// resolved sizes, never from measuring (a moved button measures 0), so the result doesn't depend on the last pass.
    /// Returns the width of everything but the pickers in the pickers' row (for <see cref="ToolbarLayout.Compute"/>).
    /// </summary>
    public double Fit(double room, double pickersMin)
    {
        var fixedWidth = 0.0;
        foreach (var row in new[] { _top, _bottom })
        {
            var (left, right) = (row.Plan.Left, row.Plan.Right);
            if (left.Count + right.Count == 0) continue;
            var spacing = row.Grid.ColumnSpacing;
            var hasPickers = left.Concat(right).Any(ToolbarPlan.IsPicker);
            var leftWidths = left.Where(id => !ToolbarPlan.IsPicker(id)).Select(Width).ToList();
            var rightWidths = right.Where(id => !ToolbarPlan.IsPicker(id)).Select(Width).ToList();
            // Fit doesn't count the flexible column's gap: take it off the room.
            var fit = ToolbarPlan.FitRow(room - spacing, spacing, leftWidths, rightWidths, hasPickers ? pickersMin : 0, MoreWidth);
            Arrange(row, fit.LeftShown, fit.Overflow);
            if (!hasPickers) continue;
            var buttons = leftWidths.Take(fit.LeftShown).Concat(rightWidths).ToList();
            var columns = buttons.Count + (fit.Overflow ? 1 : 0) + 2; // + the pickers and the flexible column
            fixedWidth = buttons.Sum() + (fit.Overflow ? MoreWidth : 0) + spacing * (columns - 1);
        }
        return fixedWidth;
    }

    /// <summary>Where a menu that belongs to a bar button opens: the button, or the "⋯" button while it is in that menu.</summary>
    public FrameworkElement Anchor(FrameworkElement button)
    {
        foreach (var row in new[] { _top, _bottom })
            if (row.Dropped.Any(id => ReferenceEquals(_builtIn.GetValueOrDefault(id), button))) return row.More;
        return button;
    }

    // ---- style ----

    private void ApplyStyle(ButtonBase button, ButtonStyle style)
    {
        var size = ToolbarPlan.Metrics(style);
        var key = (button is ToggleButton ? "ToolbarToggle" : "ToolbarButton") + (style.Outline == true ? "Outline" : "") + (style.Filled == true ? "Filled" : "");
        if (_popup.Root.Resources[key] is Style look && button.Style != look) button.Style = look;
        if (button.Width != size.Width) button.Width = size.Width;
        if (button.Height != size.Height) button.Height = size.Height;
        if (button.FontSize != size.FontSize) button.FontSize = size.FontSize;
    }

    // ---- the "⋯" menu ----

    private MenuFlyout OverflowMenu(Row row)
    {
        var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.TopEdgeAlignedLeft };
        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var id in row.Dropped) menu.Items.Add(Item(id, row));
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
            item.Click += (_, _) => _custom(custom.Definition);
            return item;
        }
        var button = _builtIn[id];
        var text = L.Get("Bar_" + id);
        var icon = new FontIcon { Glyph = button.Content as string ?? "" };
        if (button is ToggleButton toggle)
        {
            var item = new ToggleMenuFlyoutItem { Text = text, Icon = icon, IsChecked = toggle.IsChecked == true };
            item.Click += (_, _) => toggle.IsChecked = toggle.IsChecked != true; // its Checked/Unchecked handlers do the rest
            return item;
        }
        var entry = new MenuFlyoutItem { Text = text, Icon = icon };
        // After this menu has closed, so a menu the button opens isn't closed with it.
        entry.Click += (_, _) => _popup.DispatcherQueue.TryEnqueue(() =>
        {
            // An exception escaping a DispatcherQueue callback ends the process.
            try { Invoke((Button)button, row); }
            catch (Exception ex) { _log.Error($"bar item {id} from the overflow menu failed", ex); }
        });
        return entry;
    }

    private void Invoke(Button button, Row row)
    {
        if (button.Flyout is { } flyout)
        {
            // Its own menu (the prompt list), opened at "⋯": the button itself isn't on screen.
            var hold = _popup.HoldResize();
            void Closed(object? s, object e) { flyout.Closed -= Closed; hold.Dispose(); }
            flyout.Closed += Closed;
            flyout.ShowAt(row.More);
            return;
        }
        new ButtonAutomationPeer(button).Invoke(); // raises its Click: the same handlers as the button
    }
}
