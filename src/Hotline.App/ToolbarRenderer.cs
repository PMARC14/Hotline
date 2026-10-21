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

namespace Hotline.App;

/// <summary>
/// Draws toolbar.json (a resolved <see cref="ToolbarModel"/>) into the panel's two button rows. Each row is
/// <c>[left group…] [*] [right group…]</c>; the pickers are one block (PickersPanel). Built-in buttons are the XAML ones,
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

    /// <summary>The model the rows show; set only once a whole apply has succeeded.</summary>
    public ToolbarModel Model { get; private set; } = ToolbarModel.Defaults;

    /// <summary>The last fit's room and pickers' minimum: an apply fits to them at once, so a changed bar isn't drawn too
    /// wide for a frame before the queued layout (which then fits to the current width).</summary>
    private (double Room, double PickersMin)? _lastFit;

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
                var size = ToolbarPlan.Metrics(model.EffectiveStyle(b.Id));
                button.Content = CustomButtonPicture.View(picture, Math.Round(Math.Min(size.Width, size.Height) * 0.6), () =>
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
        // The picker boxes in the order listed; the block sits where the first one is.
        var boxes = new Dictionary<string, ComboBox> { ["effort"] = _popup.EffortBox, ["model"] = _popup.ModelBox, ["provider"] = _popup.ProviderBox };
        _popup.PickersPanel.Children.Clear();
        foreach (var name in ToolbarPlan.PickerOrder(model)) _popup.PickersPanel.Children.Add(boxes[name]);

        foreach (var (row, top) in new[] { (_top, true), (_bottom, false) })
        {
            row.Plan = ToolbarPlan.Row(model, top);
            row.Arranged = null;
            Arrange(row, row.Plan.Droppable.Count(id => !ToolbarPlan.IsPicker(id)), overflow: false); // as Fit counts them
            var visibility = row.Plan.Left.Count + row.Plan.Center.Count + row.Plan.Right.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (row.Grid.Visibility != visibility) row.Grid.Visibility = visibility;
        }
        // The look, after placing; only values that differ are set. The pickers take none of it (their widths are ProviderBar's).
        foreach (var (id, button) in _builtIn) ApplyStyle(button, model.EffectiveStyle(id));
        foreach (var (id, (button, _)) in _customButtons) ApplyStyle(button, model.EffectiveStyle(id));
        ApplyStyle(_top.More, model.Style);
        ApplyStyle(_bottom.More, model.Style);
        Model = model;
        // Not inside a layout pass (App's file timer, the self-test): only moves buttons between a row and its menu.
        if (_lastFit is { } last) Fit(last.Room, last.PickersMin);
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

    /// <summary>Lays a row out: the left buttons kept (the pickers always), "⋯" when some moved into it, a flexible gap, the centre
    /// group (between two flexible gaps, so it sits in the free space), the right group.</summary>
    private void Arrange(Row row, int leftShown, bool overflow)
    {
        if (row.Arranged == (leftShown, overflow)) return;
        row.Arranged = (leftShown, overflow);
        var (shown, dropped) = ToolbarPlan.Split(row.Plan.Droppable, leftShown); // the left group, then the centre: the centre goes first
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
        void Gap() => grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        foreach (var id in shown.Where(row.Plan.Left.Contains)) Add(Element(id));
        if (overflow) Add(row.More);
        Gap();
        var centre = shown.Where(row.Plan.Center.Contains).ToList();
        if (centre.Count > 0)
        {
            foreach (var id in centre) Add(Element(id));
            Gap();
        }
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
        _lastFit = (room, pickersMin);
        var fixedWidth = 0.0;
        foreach (var row in new[] { _top, _bottom })
        {
            var (left, centre, right) = (row.Plan.Droppable, row.Plan.Center, row.Plan.Right);
            if (left.Count + right.Count == 0) continue;
            var hasPickers = left.Concat(right).Any(ToolbarPlan.IsPicker);
            var leftWidths = left.Where(id => !ToolbarPlan.IsPicker(id)).Select(Width).ToList();
            var rightWidths = right.Where(id => !ToolbarPlan.IsPicker(id)).Select(Width).ToList();
            var fit = ToolbarPlan.FitRow(room, row.Grid.ColumnSpacing, leftWidths, rightWidths, hasPickers, pickersMin, MoreWidth, hasCenter: centre.Count > 0);
            Arrange(row, fit.LeftShown, fit.Overflow);
            if (hasPickers) fixedWidth = fit.FixedWidth;
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
            try { flyout.ShowAt(row.More); }
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
            await bitmap.SetSourceAsync(stream);
            loaded(bitmap);
        }
        catch (Exception)
        {
            try { failed(); }
            catch (Exception) { } // the glyph fallback itself failed: the button stays as it is
        }
    }
}
