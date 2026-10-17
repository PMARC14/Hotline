using Hotline.App.Interop;
using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Hotline.App.Chat;

/// <summary>
/// Hold Alt over the panel (or while it has focus): a grip appears on the bar's outer edge to move it, and handles on
/// the edges and corners to resize it (spec: docs/superpowers/specs/2026-10-16-panel-placement-design.md). They are
/// drawn over the content in their own layer, so showing them never changes the layout. A drag moves the real window
/// (physical pixels from the cursor); the panel works out its new spot when the drag ends.
/// </summary>
internal sealed class PanelGrips(PopupWindow popup, FileLog log)
{
    private const int VK_MENU = 0x12;
    private readonly List<(FrameworkElement Element, ResizeEdge Up, ResizeEdge Down)> _handles = [];
    private FrameworkElement _grip = null!;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _timer;
    private bool _dragging, _menuOpen, _resized;
    private ResizeEdge _edges;
    private Native.POINT _start;
    private RectI _startRect;

    /// <summary>Saves where the panel is now as its default spot and size (the panel's owner writes the settings).</summary>
    public Action? SaveAsDefault { get; set; }

    public void Initialize()
    {
        var layer = popup.GripLayer;
        var accent = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        Handle Edge(ResizeEdge up, ResizeEdge down, double w, double h, HorizontalAlignment ha, VerticalAlignment va, InputSystemCursorShape cursor)
        {
            var handle = new Handle(cursor)
            {
                Width = w, Height = h, HorizontalAlignment = ha, VerticalAlignment = va,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Children = { new Border { Background = accent, Opacity = 0.55, CornerRadius = new CornerRadius(2), Margin = new Thickness(2) } },
            };
            Wire(handle, () => popup.GrowsUp ? up : down);
            _handles.Add((handle, up, down));
            layer.Children.Add(handle);
            return handle;
        }
        const double thick = 8, corner = 18;
        var stretch = double.NaN;
        // Edges: left/right change the width; the chat-side edge (top when growing up) the chat height; the bar side
        // carries the grip and doesn't resize.
        Edge(ResizeEdge.Left, ResizeEdge.Left, thick, stretch, HorizontalAlignment.Left, VerticalAlignment.Stretch, InputSystemCursorShape.SizeWestEast);
        Edge(ResizeEdge.Right, ResizeEdge.Right, thick, stretch, HorizontalAlignment.Right, VerticalAlignment.Stretch, InputSystemCursorShape.SizeWestEast);
        Edge(ResizeEdge.Top, ResizeEdge.None, stretch, thick, HorizontalAlignment.Stretch, VerticalAlignment.Top, InputSystemCursorShape.SizeNorthSouth);
        Edge(ResizeEdge.None, ResizeEdge.Bottom, stretch, thick, HorizontalAlignment.Stretch, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNorthSouth);
        // Corners: chat-side corners change both; bar-side corners only the width.
        Edge(ResizeEdge.Top | ResizeEdge.Left, ResizeEdge.Left, corner, corner, HorizontalAlignment.Left, VerticalAlignment.Top, InputSystemCursorShape.SizeNorthwestSoutheast);
        Edge(ResizeEdge.Top | ResizeEdge.Right, ResizeEdge.Right, corner, corner, HorizontalAlignment.Right, VerticalAlignment.Top, InputSystemCursorShape.SizeNortheastSouthwest);
        Edge(ResizeEdge.Left, ResizeEdge.Bottom | ResizeEdge.Left, corner, corner, HorizontalAlignment.Left, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNortheastSouthwest);
        Edge(ResizeEdge.Right, ResizeEdge.Bottom | ResizeEdge.Right, corner, corner, HorizontalAlignment.Right, VerticalAlignment.Bottom, InputSystemCursorShape.SizeNorthwestSoutheast);

        var grip = new Handle(InputSystemCursorShape.SizeAll)
        {
            Width = 160, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, // within the panel's 10-px edge, clear of the buttons
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Children = { new Border { Width = 56, Height = 4, CornerRadius = new CornerRadius(2), Background = accent, VerticalAlignment = VerticalAlignment.Center } },
        };
        ToolTipService.SetToolTip(grip, Strings.Grip_Tip);
        Wire(grip, () => ResizeEdge.None);
        var menu = new MenuFlyout();
        var save = new MenuFlyoutItem { Text = Strings.Grip_SaveDefault };
        save.Click += (_, _) => SaveAsDefault?.Invoke();
        var home = new MenuFlyoutItem { Text = Strings.Grip_BackHome };
        home.Click += (_, _) => popup.BackToDefaultSpot();
        menu.Items.Add(save);
        menu.Items.Add(home);
        IDisposable? hold = null;
        menu.Opened += (_, _) => { _menuOpen = true; hold = popup.HoldResize(); }; // a resize would close the menu
        menu.Closed += (_, _) => { _menuOpen = false; hold?.Dispose(); hold = null; };
        grip.ContextFlyout = menu;
        _grip = grip;
        layer.Children.Add(grip);

        _timer = popup.DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(80);
        _timer.Tick += (_, _) => Update();
        popup.Shown += () => _timer.Start();
        popup.DirectionChanged += _ => { if (popup.GripLayer.Visibility == Visibility.Visible) SyncDirection(); };
        popup.Hidden += () => { _timer.Stop(); SetVisible(false); };
    }

    /// <summary>Alt held while the panel has focus or the pointer is over it (or a drag/menu in progress).</summary>
    private void Update()
    {
        if (!popup.IsShown) { _timer?.Stop(); return; }
        var alt = (Native.GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
        var show = _dragging || _menuOpen || alt && !popup.IsResizeHeld && (Native.GetForegroundWindow() == popup.Hwnd || PointerOver());
        SetVisible(show);
    }

    private bool PointerOver()
    {
        Native.GetCursorPos(out var pt);
        var p = popup.AppWindow.Position;
        var s = popup.AppWindow.Size;
        return pt.X >= p.X && pt.X < p.X + s.Width && pt.Y >= p.Y && pt.Y < p.Y + s.Height;
    }

    public void SetVisible(bool visible)
    {
        var layer = popup.GripLayer;
        var wanted = visible ? Visibility.Visible : Visibility.Collapsed;
        if (visible) SyncDirection();
        if (layer.Visibility != wanted) layer.Visibility = wanted;
    }

    /// <summary>The grip on the bar's outer edge; the edges that resize for this direction (also after a flip while shown).</summary>
    private void SyncDirection()
    {
        _grip.VerticalAlignment = popup.GrowsUp ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        foreach (var (element, up, down) in _handles)
            element.Visibility = (popup.GrowsUp ? up : down) == ResizeEdge.None ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Wire(FrameworkElement element, Func<ResizeEdge> edges)
    {
        element.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(element).Properties.IsLeftButtonPressed || popup.IsResizeHeld) return;
            if (!element.CapturePointer(e.Pointer)) return;
            _edges = edges();
            _dragging = true;
            _resized = false;
            Native.GetCursorPos(out _start);
            _startRect = popup.WindowRect;
            popup.BeginUserDrag();
            e.Handled = true;
        };
        element.PointerMoved += (_, e) =>
        {
            if (!_dragging) return;
            Native.GetCursorPos(out var now);
            int dx = now.X - _start.X, dy = now.Y - _start.Y;
            if (dx == 0 && dy == 0) return; // a click without a move changes nothing
            if (_edges == ResizeEdge.None)
            {
                // Moving: by the pointer's travel (never re-anchored to a size Windows changes on a monitor with another
                // scale, which would bounce it across the boundary); growing up, the bottom edge (where the grip is) follows.
                var size = popup.AppWindow.Size;
                var y = popup.GrowsUp ? _startRect.Y + _startRect.Height + dy - size.Height : _startRect.Y + dy;
                popup.DragMoveTo(_startRect.X + dx, y);
            }
            else
            {
                if ((_edges & (ResizeEdge.Top | ResizeEdge.Bottom)) != 0 && dy != 0) _resized = true; // a height chosen by hand
                popup.DragResizeTo(PanelPlacement.Resize(_startRect, _edges, dx, dy, popup.MinDragWidthPx, popup.MinDragHeightPx, popup.WorkArea));
            }
            e.Handled = true;
        };
        void End()
        {
            if (!_dragging) return;
            _dragging = false;
            popup.EndUserDrag(_resized);
            log.Info($"panel {(_resized ? "resized" : "moved")} to {popup.WindowRect}");
        }
        element.PointerReleased += (_, e) => { element.ReleasePointerCapture(e.Pointer); End(); e.Handled = true; };
        element.PointerCaptureLost += (_, _) => End();
    }

    /// <summary>A grip with a resize/move pointer shape (ProtectedCursor is only settable from a subclass).</summary>
    private sealed class Handle : Grid
    {
        public Handle(InputSystemCursorShape cursor) => ProtectedCursor = InputSystemCursor.Create(cursor);
    }
}
