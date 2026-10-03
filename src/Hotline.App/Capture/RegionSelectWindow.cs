using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;

namespace Hotline.App.Capture;

/// <summary>
/// Snipping-tool style region picker: a borderless, always-on-top window over one monitor showing a frozen, dimmed
/// screenshot. Drag a rectangle to capture it (the selection is shown undimmed); Esc, right-click or a plain click
/// cancels. Result: the selection in the screenshot's pixels, or null.
/// </summary>
internal sealed class RegionSelectWindow : Window
{
    private static readonly SolidColorBrush Dim = new(Windows.UI.Color.FromArgb(0x88, 0, 0, 0));
    private readonly TaskCompletionSource<RectI?> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Grid _root = new() { IsTabStop = true };
    private readonly Canvas _overlay = new();
    private readonly Rectangle _top = new() { Fill = Dim }, _bottom = new() { Fill = Dim }, _left = new() { Fill = Dim }, _right = new() { Fill = Dim };
    private readonly Rectangle _border = new() { StrokeThickness = 2, Stroke = new SolidColorBrush(Colors.White), Visibility = Visibility.Collapsed };
    private readonly int _pixelWidth, _pixelHeight;
    private Point? _start;

    public RegionSelectWindow(byte[] bgra, RectI monitor)
    {
        (_pixelWidth, _pixelHeight) = (monitor.Width, monitor.Height);
        Title = "Hotline region capture";
        var presenter = OverlappedPresenter.Create();
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;

        // GDI grabs leave alpha at 0: make the frozen frame opaque.
        for (var i = 3; i < bgra.Length; i += 4) bgra[i] = 255;
        var bitmap = new WriteableBitmap(monitor.Width, monitor.Height);
        using (var pixels = bitmap.PixelBuffer.AsStream()) pixels.Write(bgra, 0, bgra.Length);

        _root.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Fill });
        _overlay.Children.Add(_top);
        _overlay.Children.Add(_bottom);
        _overlay.Children.Add(_left);
        _overlay.Children.Add(_right);
        _overlay.Children.Add(_border);
        _root.Children.Add(_overlay);
        _root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xCC, 0x20, 0x20, 0x20)), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 24, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Child = new TextBlock { Text = "Drag to capture a region  ·  Esc to cancel", Foreground = new SolidColorBrush(Colors.White) },
        });
        Content = _root;
        _root.Loaded += (_, _) => { Layout(null); _root.Focus(FocusState.Programmatic); };
        _root.SizeChanged += (_, _) => Layout(_current);
        _root.PointerPressed += OnPressed;
        _root.PointerMoved += OnMoved;
        _root.PointerReleased += OnReleased;
        _root.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) Finish(null); };
        Closed += (_, _) => _result.TrySetResult(null);
        AppWindow.MoveAndResize(new RectInt32(monitor.X, monitor.Y, monitor.Width, monitor.Height));
    }

    /// <summary>Shows the picker and waits for a selection (null = cancelled).</summary>
    public Task<RectI?> SelectAsync()
    {
        Activate();
        Interop.Native.ForceForeground(WinRT.Interop.WindowNative.GetWindowHandle(this));
        return _result.Task;
    }

    private Rect? _current;

    private void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_root);
        if (point.Properties.IsRightButtonPressed) { Finish(null); return; }
        _start = point.Position;
        _root.CapturePointer(e.Pointer);
        Layout(new Rect(point.Position, point.Position));
    }

    private void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_start is not { } start) return;
        Layout(new Rect(start, e.GetCurrentPoint(_root).Position));
    }

    private void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_start is not { } start) return;
        var end = e.GetCurrentPoint(_root).Position;
        _start = null;
        _root.ReleasePointerCapture(e.Pointer);
        // Map DIPs to the screenshot's pixels using the actual on-screen size (works at any scale factor).
        var scale = _root.ActualWidth > 0 ? _pixelWidth / _root.ActualWidth : 1;
        var selection = RegionMath.Selection(start.X, start.Y, end.X, end.Y, scale, _pixelWidth, _pixelHeight);
        Finish(RegionMath.IsUsable(selection) ? selection : null);
    }

    /// <summary>Dims everything except the selection and draws its border.</summary>
    private void Layout(Rect? selection)
    {
        _current = selection;
        double w = _root.ActualWidth, h = _root.ActualHeight;
        var r = selection ?? new Rect(0, 0, 0, 0);
        void Place(Rectangle rect, double x, double y, double width, double height)
        {
            Canvas.SetLeft(rect, x);
            Canvas.SetTop(rect, y);
            rect.Width = Math.Max(0, width);
            rect.Height = Math.Max(0, height);
        }
        Place(_top, 0, 0, w, r.Top);
        Place(_bottom, 0, r.Bottom, w, h - r.Bottom);
        Place(_left, 0, r.Top, r.Left, r.Height);
        Place(_right, r.Right, r.Top, w - r.Right, r.Height);
        _border.Visibility = selection is null || r.Width < 1 ? Visibility.Collapsed : Visibility.Visible;
        Place(_border, r.Left - 1, r.Top - 1, r.Width + 2, r.Height + 2);
    }

    private void Finish(RectI? selection)
    {
        if (!_result.TrySetResult(selection)) return;
        Close();
    }
}
