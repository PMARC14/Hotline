using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace Hotline.App.Settings;

/// <summary>
/// Settings › Window › Button rows: toolbar.json's four groups as a 2×2 grid (top row above the message box, bottom row
/// below it), a "Not shown" strip, the bar's look and the custom buttons. Items are dragged between and within the groups,
/// or moved with the buttons under each item (Ctrl+Left/Right and Alt+arrows on a focused item). Every edit goes through
/// <see cref="ToolbarEditor"/> (so only valid files are written), is saved at once and applied by App's file watcher;
/// only this control is rebuilt, never the page, and not while one of its flyouts is open (a rebuild would close it).
/// UI Automation ids: quadrant-&lt;topleft|…&gt;, bar-notshown, bar-item-&lt;id&gt; (the picker block, its arrows too: pickerBlock),
/// bar-move-&lt;left|right&gt;-&lt;id&gt;, bar-shift-&lt;up|down|left|right&gt;-&lt;id&gt;, bar-hide-&lt;id&gt;, bar-style-&lt;id&gt; (its flyout: bar-look-&lt;size|width|height|outline|filled|reset&gt;),
/// bar-span-&lt;picker&gt; (a picker's width, 1x-8x), bar-show-&lt;id&gt;, bar-size, bar-outline, bar-filled, bar-custom-add, bar-custom-edit-&lt;id&gt;, bar-custom-delete-&lt;id&gt;.
/// </summary>
internal sealed partial class ToolbarQuadrantEditor : StackPanel
{
    /// <summary>The built-in buttons' glyphs, as in PopupWindow.xaml.</summary>
    private static readonly Dictionary<string, string> BuiltInGlyphs = new()
    {
        ["pin"] = "\uE718", ["captureWindow"] = "\uE7C4", ["captureScreen"] = "\uE7F4", ["captureRegion"] = "\uF407", ["prompt"] = "\uE8A5",
        ["recent"] = "\uE81C", ["continueIn"] = "\uE756", ["newChat"] = "\uE8BD", ["settings"] = "\uE713",
    };

    private readonly Action<ToolbarModel> _save;
    private readonly Func<IReadOnlyList<string>> _quickActions;
    private readonly Func<ContentDialog, Task<ContentDialogResult>> _showDialog;
    private readonly Style _cardStyle;
    private readonly FileLog _log;
    /// <summary>Style overrides of items hidden in this session (Hide drops them), put back when the item is shown again.</summary>
    private readonly Dictionary<string, ButtonStyle> _stash = new(StringComparer.OrdinalIgnoreCase);
    private readonly InfoBar _notice = new() { IsOpen = false, IsClosable = true };
    private readonly StackPanel _layout = new() { Spacing = 8 };
    private readonly Dictionary<ListViewItem, (Rectangle Before, Rectangle After)> _markers = [];
    private readonly List<Shape> _litMarkers = [];
    private Border? _litCard;
    private ToolbarModel _model;
    private string? _dragId;
    private bool _rebuildQueued, _rebuildPending;
    private int _flyoutsOpen;
    /// <summary>What gets keyboard focus after the next rebuild: the element with the first id, else the second.</summary>
    private (string Preferred, string Fallback)? _focus;

    public ToolbarQuadrantEditor(ToolbarModel model, Action<ToolbarModel> save, Func<IReadOnlyList<string>> quickActions,
        Func<ContentDialog, Task<ContentDialogResult>> showDialog, Style cardStyle, FileLog log)
    {
        (_model, _save, _quickActions, _showDialog, _cardStyle, _log) = (model, save, quickActions, showDialog, cardStyle, log);
        Spacing = 8;
        Children.Add(_notice);
        Children.Add(new TextBlock { Text = Strings.Bar_EditorIntro, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        Children.Add(_layout);
        Children.Add(BarStyleCard());
        Children.Add(new TextBlock { Text = Strings.Bar_Custom, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 8, 0, 0) });
        Children.Add(_custom);
        Rebuild();
    }

    // ---- editing ----

    /// <summary>Runs one edit; when it changes the model, saves it (keeping the last good model if that fails), moves a style
    /// override between the item and the stash when it is hidden or shown, and rebuilds this control, then focuses
    /// <paramref name="focus"/> (see <see cref="_focus"/>).</summary>
    private void Edit(Func<ToolbarModel, ToolbarModel> edit, string? id, (string Preferred, string Fallback)? focus = null)
    {
        var before = _model;
        ToolbarModel next;
        try { next = edit(before); }
        catch (Exception ex) { _log.Error("button rows: edit failed", ex); return; } // ToolbarEditor never throws; a drop must never fail anyway
        if (ReferenceEquals(next, before)) return;
        var (wasHidden, isHidden) = id is null ? (false, false) : (IsHidden(before, id), IsHidden(next, id));
        ButtonStyle? restored = null;
        if (wasHidden && !isHidden && _stash.TryGetValue(id!, out var stashed))
        {
            next = ToolbarEditor.SetStyle(next, id, stashed);
            restored = stashed;
        }
        try { _save(next); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("saving toolbar.json failed", ex);
            Notice(L.F(Strings.Bar_SaveFailed, ex.Message), InfoBarSeverity.Error);
            _focus = focus ?? _focus;
            QueueRebuild(); // back to what the file holds
            return;
        }
        _model = next;
        if (_notice.Severity == InfoBarSeverity.Error) _notice.IsOpen = false;
        if (restored is not null) _stash.Remove(id!);
        if (!wasHidden && isHidden && before.Overrides.TryGetValue(id!, out var dropped)) _stash[id!] = dropped;
        _focus = focus ?? _focus;
        QueueRebuild();
    }

    private static bool IsHidden(ToolbarModel m, string id) => ToolbarEditor.Hidden(m).Contains(id, StringComparer.OrdinalIgnoreCase);

    private void Notice(string message, InfoBarSeverity severity) =>
        (_notice.Message, _notice.Severity, _notice.IsOpen) = (message, severity, true);

    /// <summary>Rebuilds after the current event (never inside a drop) and not while a flyout is open.</summary>
    private void QueueRebuild()
    {
        if (_flyoutsOpen > 0) { _rebuildPending = true; return; }
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildQueued = false;
            try { Rebuild(); }
            catch (Exception ex) { _log.Error("button rows editor: rebuild failed", ex); } // an exception escaping here ends the process
        });
    }

    private void FlyoutOpened() => _flyoutsOpen++;

    private void FlyoutClosed()
    {
        _flyoutsOpen = Math.Max(0, _flyoutsOpen - 1);
        if (_flyoutsOpen == 0 && _rebuildPending) { _rebuildPending = false; QueueRebuild(); }
    }

    // ---- layout ----

    private void Rebuild()
    {
        _layout.Children.Clear();
        _markers.Clear();
        _litMarkers.Clear();
        _litCard = null;
        _layout.Children.Add(Heading(Strings.Bar_TopRow));
        _layout.Children.Add(Trio(Quadrant.TopLeft, Quadrant.TopCenter, Quadrant.TopRight));
        _layout.Children.Add(Heading(Strings.Bar_BottomRow));
        _layout.Children.Add(Trio(Quadrant.BottomLeft, Quadrant.BottomCenter, Quadrant.BottomRight));
        _layout.Children.Add(Heading(Strings.Bar_NotShown));
        _layout.Children.Add(NotShownCard());
        SyncBarStyle();
        BuildCustomList();
        if (_focus is { } focus)
        {
            _focus = null;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => // after layout: the new items exist then
            {
                // An exception escaping a DispatcherQueue callback ends the process.
                try
                {
                    if (Find(this, focus.Preferred) is not Control { IsEnabled: true } c || !c.Focus(FocusState.Keyboard))
                        (Find(this, focus.Fallback) as Control)?.Focus(FocusState.Keyboard);
                }
                catch (Exception ex) { _log.Error("button rows: restoring focus failed", ex); }
            });
        }
    }

    private static TextBlock Heading(string text) => new() { Text = text, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0) };

    /// <summary>One row's three groups side by side: left, centre, right.</summary>
    private Grid Trio(Quadrant left, Quadrant center, Quadrant right)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        var column = 0;
        foreach (var q in new[] { left, center, right })
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var card = QuadrantCard(q);
            Grid.SetColumn(card, column++);
            grid.Children.Add(card);
        }
        return grid;
    }

    private static string Key(Quadrant q) => q.ToString().ToLowerInvariant();

    private static string QuadrantName(Quadrant q) => q switch
    {
        Quadrant.TopLeft => Strings.Quad_TopLeft,
        Quadrant.TopCenter => Strings.Quad_TopCenter,
        Quadrant.TopRight => Strings.Quad_TopRight,
        Quadrant.BottomLeft => Strings.Quad_BottomLeft,
        Quadrant.BottomCenter => Strings.Quad_BottomCenter,
        _ => Strings.Quad_BottomRight,
    };

    private Border QuadrantCard(Quadrant q)
    {
        var units = ToolbarEditorView.Units(_model, q);
        var list = Strip("quadrant-" + Key(q), QuadrantName(q));
        foreach (var unit in units) list.Items.Add(UnitItem(q, unit));
        var card = Card(QuadrantName(q), list, units.Count == 0);
        list.AddHandler(DragOverEvent, new DragEventHandler((_, e) =>
        {
            if (!Accept(e)) return;
            var gap = Gap(list, e);
            Highlight(card, list, gap);
        }), handledEventsToo: true);
        list.AddHandler(DropEvent, new DragEventHandler((_, e) =>
        {
            var id = _dragId;
            var gap = Gap(list, e);
            ClearHighlight();
            if (id is null) return;
            e.Handled = true;
            Edit(m => ToolbarEditor.Drop(m, id, q, ToolbarEditorView.ItemIndex(m, q, gap)), id);
        }), handledEventsToo: true);
        return card;
    }

    private Border NotShownCard()
    {
        var hidden = ToolbarEditor.Hidden(_model);
        var list = Strip("bar-notshown", Strings.Bar_NotShown);
        foreach (var id in hidden) list.Items.Add(HiddenItem(id));
        var card = Card(null, list, hidden.Count == 0);
        list.AddHandler(DragOverEvent, new DragEventHandler((_, e) => { if (Accept(e)) Highlight(card, list, null); }), handledEventsToo: true);
        list.AddHandler(DropEvent, new DragEventHandler((_, e) =>
        {
            var id = _dragId;
            ClearHighlight();
            if (id is null) return;
            e.Handled = true;
            Edit(m => HideUnit(m, id), id);
        }), handledEventsToo: true);
        return card;
    }

    /// <summary>Hides an item; a picker dragged out of the bar takes the whole block with it.</summary>
    private static ToolbarModel HideUnit(ToolbarModel m, string id)
    {
        if (!ToolbarPlan.IsPicker(id)) return ToolbarEditor.Hide(m, id);
        foreach (var picker in ToolbarPlan.PickerOrder(m)) m = ToolbarEditor.Hide(m, picker);
        return m;
    }

    /// <summary>A card around a strip, with "Empty" behind it when there is nothing in it (the strip still takes drops).</summary>
    private Border Card(string? header, ListView list, bool empty)
    {
        var area = new Grid();
        if (empty) area.Children.Add(new TextBlock { Text = Strings.Bar_RowEmpty, Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        area.Children.Add(list);
        var content = new StackPanel { Spacing = 4 };
        if (header is not null) content.Children.Add(new TextBlock { Text = header, FontSize = 12, Opacity = 0.7 });
        content.Children.Add(area);
        return new Border { Style = _cardStyle, Padding = new Thickness(8), Child = content };
    }

    private const double MaxStripHeight = 600;

    /// <summary>A vertical strip of items: draggable, a drop target, growing with its items before it scrolls.</summary>
    private ListView Strip(string automationId, string name)
    {
        var list = new ListView
        {
            CanDragItems = true, AllowDrop = true, CanReorderItems = false, SelectionMode = ListViewSelectionMode.None,
            MinHeight = 84, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), // a background takes drops on the empty part
            TabNavigation = KeyboardNavigationMode.Local, // Tab reaches every item's buttons
            ItemsPanel = (ItemsPanelTemplate)XamlReader.Load(
                """<ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><StackPanel Orientation="Vertical" Spacing="2" /></ItemsPanelTemplate>"""),
        };
        // The box grows with its items up to MaxStripHeight (about ten items), so a whole group is in view; only a longer
        // group scrolls, and only up and down.
        list.MaxHeight = MaxStripHeight;
        ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        AutomationProperties.SetAutomationId(list, automationId);
        AutomationProperties.SetName(list, name);
        list.DragItemsStarting += (_, e) =>
        {
            // Items added as ListViewItems: the item (or, per HANDOFF, its content) carries the id in Tag.
            if (e.Items.FirstOrDefault() is FrameworkElement { Tag: string id })
            {
                _dragId = id;
                e.Data.SetText(id);
                e.Data.RequestedOperation = DataPackageOperation.Move;
            }
            else e.Cancel = true;
        };
        list.DragItemsCompleted += (_, _) => { _dragId = null; ClearHighlight(); };
        list.DragLeave += (_, _) => ClearHighlight();
        return list;
    }

    /// <summary>Only drags started in this editor are taken.</summary>
    private bool Accept(DragEventArgs e)
    {
        e.AcceptedOperation = _dragId is null ? DataPackageOperation.None : DataPackageOperation.Move;
        e.Handled = true;
        return _dragId is not null;
    }

    /// <summary>The gap between items under the pointer: before the first item whose middle is below it, else the end.</summary>
    private static int Gap(ListView list, DragEventArgs e)
    {
        var y = e.GetPosition(list).Y;
        for (var i = 0; i < list.Items.Count; i++)
            if (list.Items[i] is ListViewItem item && item.ActualHeight > 0
                && y < item.TransformToVisual(list).TransformPoint(default).Y + item.ActualHeight / 2)
                return i;
        return list.Items.Count;
    }

    /// <summary>The drop marker: the card's border and a bar at the gap, in the accent colour (brushes only: no layout change).</summary>
    private void Highlight(Border card, ListView list, int? gap)
    {
        ClearHighlight();
        var accent = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]);
        card.BorderBrush = accent;
        _litCard = card;
        if (gap is not { } g || list.Items.Count == 0) return;
        var item = (ListViewItem)list.Items[Math.Min(g, list.Items.Count - 1)];
        if (!_markers.TryGetValue(item, out var marker)) return;
        var bar = g < list.Items.Count ? marker.Before : marker.After;
        bar.Fill = accent;
        _litMarkers.Add(bar);
    }

    private void ClearHighlight()
    {
        _litCard?.ClearValue(Border.BorderBrushProperty); // back to the card style's brush
        _litCard = null;
        foreach (var bar in _litMarkers) bar.Fill = null;
        _litMarkers.Clear();
    }

    // ---- items ----

    private static string ItemName(ToolbarModel m, string id) =>
        m.Buttons.FirstOrDefault(b => b.Id == id) is { } custom ? custom.Label : L.Get("Bar_" + id);

    private static string? Glyph(ToolbarModel m, string id) =>
        BuiltInGlyphs.TryGetValue(id, out var glyph) ? glyph
        : m.Buttons.FirstOrDefault(b => b.Id == id) is { } custom ? IconGlyph(custom.Icon)
        : null;

    /// <summary>A validated icon code (see ToolbarModel) as its Segoe Fluent Icons character.</summary>
    internal static string IconGlyph(string hex) =>
        char.ConvertFromUtf32(int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var code) && code is >= 0xE000 and <= 0xF8FF
            ? code : Convert.ToInt32(ToolbarModel.DefaultIcon, 16));

    private UIElement Title(string id)
    {
        var name = new TextBlock { Text = ItemName(_model, id), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis };
        if (Glyph(_model, id) is not { } glyph) return Chip(name);
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = glyph, FontSize = 14 }, name } };
    }

    private Border Chip(UIElement content) => new() { Style = _cardStyle, Padding = new Thickness(6, 2, 6, 2), Child = content };

    /// <summary>One place of a quadrant: the item (or the picker block, its chips in file order) and its buttons.</summary>
    private ListViewItem UnitItem(Quadrant q, EditorUnit unit)
    {
        var key = unit.IsPickers ? "pickerBlock" : unit.Id; // upper case: never a custom id
        var name = unit.IsPickers ? Strings.Bar_Pickers : ItemName(_model, unit.Id);
        var top = q is Quadrant.TopLeft or Quadrant.TopCenter or Quadrant.TopRight;
        UIElement title = Title(unit.Id);
        if (unit.IsPickers)
        {
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            foreach (var picker in unit.Ids)
            {
                var chipName = L.Get("Bar_Chip_" + picker);
                chips.Children.Add(Chip(new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = chipName, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
                        SpanBox(picker, chipName),
                        SmallButton("\uE711", Strings.Bar_Remove, chipName, "bar-hide-" + picker, true, m => ToolbarEditor.Hide(m, picker), picker, "bar-item-pickerBlock"),
                    },
                }));
            }
            title = chips;
        }
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var item = "bar-item-" + key;
        buttons.Children.Add(SmallButton("\uE76B", Strings.Bar_MoveLeft, name, "bar-move-left-" + key, unit.CanMoveLeft, m => ToolbarEditor.Move(m, unit.Id, -1), unit.Id, item));
        buttons.Children.Add(SmallButton("\uE76C", Strings.Bar_MoveRight, name, "bar-move-right-" + key, unit.CanMoveRight, m => ToolbarEditor.Move(m, unit.Id, 1), unit.Id, item));
        buttons.Children.Add(top
            ? SmallButton("\uE74B", Strings.Bar_ToBottom, name, "bar-shift-down-" + key, unit.CanShiftDown, m => ToolbarEditor.ShiftTo(m, unit.Id, Shift.Down), unit.Id, item)
            : SmallButton("\uE74A", Strings.Bar_ToTop, name, "bar-shift-up-" + key, unit.CanShiftUp, m => ToolbarEditor.ShiftTo(m, unit.Id, Shift.Up), unit.Id, item));
        buttons.Children.Add(SmallButton("\uE72B", Strings.Bar_ToLeft, name, "bar-shift-left-" + key, unit.CanShiftLeft, m => ToolbarEditor.ShiftTo(m, unit.Id, Shift.Left), unit.Id, item));
        buttons.Children.Add(SmallButton("\uE72A", Strings.Bar_ToRight, name, "bar-shift-right-" + key, unit.CanShiftRight, m => ToolbarEditor.ShiftTo(m, unit.Id, Shift.Right), unit.Id, item));
        if (!unit.IsPickers)
        {
            buttons.Children.Add(StyleButton(unit.Id, name));
            buttons.Children.Add(SmallButton("\uE711", Strings.Bar_Remove, name, "bar-hide-" + key, true, m => ToolbarEditor.Hide(m, unit.Id), unit.Id, "quadrant-" + Key(q)));
        }
        var listItem = Item(unit.Id, item, name, new StackPanel { Spacing = 4, Children = { title, buttons } });
        listItem.PreviewKeyDown += (_, e) => OnItemKey(unit.Id, item, e);
        return listItem;
    }

    /// <summary>A picker's width in button widths ("1x" to "8x"): picking one saves it. The list's own popup counts as a flyout, so
    /// the rebuild that follows waits until it has closed.</summary>
    private ComboBox SpanBox(string picker, string chipName)
    {
        var box = new ComboBox { FontSize = 12, MinWidth = 0, Width = 64, Padding = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        for (var span = ToolbarModel.MinSpan; span <= ToolbarModel.MaxSpan; span++) box.Items.Add($"{span}x");
        box.SelectedIndex = _model.EffectiveSpan(picker) - ToolbarModel.MinSpan;
        var automationId = "bar-span-" + picker;
        ToolTipService.SetToolTip(box, Strings.Bar_PickerWidth);
        AutomationProperties.SetName(box, $"{Strings.Bar_PickerWidth}: {chipName}");
        AutomationProperties.SetAutomationId(box, automationId);
        box.DropDownOpened += (_, _) => FlyoutOpened();
        box.DropDownClosed += (_, _) => FlyoutClosed();
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0) Edit(m => ToolbarEditor.SetPickerSpan(m, picker, box.SelectedIndex + ToolbarModel.MinSpan), picker, (automationId, "bar-item-pickerBlock"));
        };
        return box;
    }

    private ListViewItem HiddenItem(string id)
    {
        var name = ItemName(_model, id);
        var show = SmallButton("\uE710", Strings.Bar_Add, name, "bar-show-" + id, true, m => ToolbarEditorView.Show(m, id), id, "bar-notshown");
        return Item(id, "bar-item-" + id, name, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { Title(id), show } });
    }

    /// <summary>A strip item with its id in Tag (on the item and its content) and a drop-marker bar above and below it.</summary>
    private ListViewItem Item(string id, string automationId, string name, FrameworkElement tile)
    {
        var (before, after) = (new Rectangle { Height = 3 }, new Rectangle { Height = 3 });
        var content = new Grid { Tag = id, RowSpacing = 2 };
        foreach (var _ in Enumerable.Range(0, 3)) content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(tile, 1);
        Grid.SetRow(after, 2);
        content.Children.Add(before);
        content.Children.Add(tile);
        content.Children.Add(after);
        var item = new ListViewItem { Content = content, Tag = id, Padding = new Thickness(2, 4, 2, 4), MinWidth = 0 };
        AutomationProperties.SetAutomationId(item, automationId);
        AutomationProperties.SetName(item, name);
        _markers[item] = (before, after);
        return item;
    }

    /// <summary>A small icon button running one edit; afterwards the focus returns to it (or, when it is now disabled or gone,
    /// to <paramref name="fallbackFocus"/>).</summary>
    private Button SmallButton(string glyph, string tip, string itemName, string automationId, bool enabled,
        Func<ToolbarModel, ToolbarModel>? edit, string? id = null, string? fallbackFocus = null)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12 }, IsEnabled = enabled,
            Padding = new Thickness(6), MinWidth = 0, MinHeight = 0,
        };
        ToolTipService.SetToolTip(button, tip);
        AutomationProperties.SetName(button, $"{tip}: {itemName}");
        AutomationProperties.SetAutomationId(button, automationId);
        if (edit is not null)
            button.Click += (_, _) =>
            {
                Edit(edit, id, (automationId, fallbackFocus ?? automationId));
            };
        return button;
    }

    /// <summary>Ctrl+Left/Right moves the focused item within its group, Alt+arrow moves it to the neighbouring group.</summary>
    private void OnItemKey(string id, string itemAutomationId, KeyRoutedEventArgs e)
    {
        var ctrl = Down(VirtualKey.Control);
        var alt = Down(VirtualKey.Menu);
        if (ctrl == alt) return;
        var key = e.Key != VirtualKey.None ? e.Key : e.OriginalKey; // some Alt combinations report Key as None
        Func<ToolbarModel, ToolbarModel>? edit = (alt, key) switch
        {
            (true, VirtualKey.Left) => m => ToolbarEditor.ShiftTo(m, id, Shift.Left),
            (true, VirtualKey.Right) => m => ToolbarEditor.ShiftTo(m, id, Shift.Right),
            (true, VirtualKey.Up) => m => ToolbarEditor.ShiftTo(m, id, Shift.Up),
            (true, VirtualKey.Down) => m => ToolbarEditor.ShiftTo(m, id, Shift.Down),
            (false, VirtualKey.Left) => m => ToolbarEditor.Move(m, id, -1),
            (false, VirtualKey.Right) => m => ToolbarEditor.Move(m, id, 1),
            _ => null,
        };
        if (edit is null) return;
        e.Handled = true;
        Edit(edit, id, (itemAutomationId, itemAutomationId));

        static bool Down(VirtualKey k) =>
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
    }

    private static DependencyObject? Find(DependencyObject root, string automationId)
    {
        if (AutomationProperties.GetAutomationId(root) == automationId) return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (Find(VisualTreeHelper.GetChild(root, i), automationId) is { } found) return found;
        return null;
    }
}
