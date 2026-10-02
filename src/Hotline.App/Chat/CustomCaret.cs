using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Hotline.App.Chat;

/// <summary>
/// Draws Hotline's own text cursor over the message box. Windows draws its caret by inverting the pixels behind it,
/// which is invisible over a see-through (acrylic) box; this one is a plain shape with a configurable style, color,
/// width and blink. With style System the Windows caret is used instead.
/// </summary>
internal sealed class CustomCaret
{
    private readonly TextBox _box;
    private readonly Canvas _layer;
    private readonly Rectangle _shape = new() { IsHitTestVisible = false, RadiusX = 1, RadiusY = 1 };
    private readonly DispatcherTimer _blink = new();
    private CaretSettings _settings = new();
    private Brush _accent = new SolidColorBrush(Microsoft.UI.Colors.MediumPurple);
    private ScrollViewer? _inner;
    private double _onOpacity = 1;

    public CustomCaret(TextBox box, Canvas layer)
    {
        _box = box;
        _layer = layer;
        _layer.Children.Add(_shape);
        _shape.Visibility = Visibility.Collapsed;
        _blink.Tick += (_, _) => _shape.Opacity = _shape.Opacity > 0 ? 0 : _onOpacity;

        _box.SelectionChanged += (_, _) => Update();
        _box.TextChanged += (_, _) => Update();
        _box.SizeChanged += (_, _) => Update();
        _box.GotFocus += (_, _) => Update();
        _box.LostFocus += (_, _) => Update();
        _box.Loaded += (_, _) => HookInnerScroll();
    }

    public void Apply(CaretSettings settings, Brush accent)
    {
        _settings = settings;
        _accent = accent;
        _shape.Fill = settings.Color is { } hex && Hotline.Core.Theming.ThemeColor.TryParse(hex, out var c)
            ? new SolidColorBrush(Windows.UI.Color.FromArgb(c.A, c.R, c.G, c.B))
            : _accent;
        _blink.Interval = TimeSpan.FromMilliseconds(settings.BlinkMs);
        Update();
    }

    private void HookInnerScroll()
    {
        if (_inner is not null) return;
        _inner = FindChild<ScrollViewer>(_box);
        if (_inner is not null) _inner.ViewChanged += (_, _) => Update();
    }

    private void Update()
    {
        var focused = _box.FocusState != FocusState.Unfocused;
        if (_settings.Style == CaretStyle.System || !focused || _box.SelectionLength > 0)
        {
            Hide();
            return;
        }
        HookInnerScroll();
        var r = CaretRect();
        if (r.IsEmpty || !double.IsFinite(r.X) || !double.IsFinite(r.Y)) { Hide(); return; } // not laid out yet
        var origin = _box.TransformToVisual(_layer).TransformPoint(new Point(r.X, r.Y));
        if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y)) { Hide(); return; }
        var lineHeight = Math.Max(r.Height, _box.FontSize * 1.3);
        var charWidth = Math.Max(_box.FontSize * 0.55, 4);
        switch (_settings.Style)
        {
            case CaretStyle.Block:
                _shape.Width = charWidth; _shape.Height = lineHeight;
                Canvas.SetLeft(_shape, origin.X); Canvas.SetTop(_shape, origin.Y);
                _onOpacity = 0.55;
                break;
            case CaretStyle.Underline:
                _shape.Width = charWidth; _shape.Height = _settings.Width;
                Canvas.SetLeft(_shape, origin.X); Canvas.SetTop(_shape, origin.Y + lineHeight - _settings.Width);
                _onOpacity = 1;
                break;
            default:
                _shape.Width = _settings.Width; _shape.Height = lineHeight;
                Canvas.SetLeft(_shape, origin.X - _settings.Width / 2); Canvas.SetTop(_shape, origin.Y);
                _onOpacity = 1;
                break;
        }
        _shape.Opacity = _onOpacity;
        // Keep it inside the box's visible text area (it scrolls).
        var inside = origin.Y >= -1 && origin.Y + lineHeight <= _layer.ActualHeight + 1;
        _shape.Visibility = inside ? Visibility.Visible : Visibility.Collapsed;
        _blink.Stop();
        if (_settings.Blink && inside) _blink.Start(); // restart: the cursor stays solid while typing/moving
    }

    private void Hide()
    {
        _blink.Stop();
        _shape.Visibility = Visibility.Collapsed;
    }

    /// <summary>Caret position in TextBox coordinates (start of the line when the text is empty or ends in a newline).</summary>
    private Rect CaretRect()
    {
        var text = _box.Text ?? "";
        var index = Math.Clamp(_box.SelectionStart, 0, text.Length);
        var pad = _box.Padding;
        var border = _box.BorderThickness;
        var empty = new Rect(pad.Left + border.Left, pad.Top + border.Top, 0, _box.FontSize * 1.3);
        try
        {
            if (text.Length == 0) return empty;
            if (index < text.Length) return _box.GetRectFromCharacterIndex(index, trailingEdge: false);
            var last = _box.GetRectFromCharacterIndex(text.Length - 1, trailingEdge: true);
            return text[^1] is '\r' or '\n' ? new Rect(empty.X, last.Y + last.Height, 0, last.Height) : last;
        }
        catch (ArgumentException) { return empty; }
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
