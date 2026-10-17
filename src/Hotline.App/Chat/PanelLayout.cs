using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Chat;

/// <summary>
/// The panel's rows for the chat's direction. Up (as always): chat, notices, the bar (top button row and message box),
/// bottom button row. Down: the bar and bottom row on top, then notices and the chat. Only rows change: no control is moved to another parent, so the
/// message box keeps its focus, caret and draft through a swap.
/// </summary>
internal static class PanelLayout
{
    public static void Apply(PopupWindow popup, bool up)
    {
        var rows = popup.Root.RowDefinitions;
        var input = popup.Input;
        var (start, length) = (input.SelectionStart, input.SelectionLength);
        // Row order, top to bottom.
        FrameworkElement[] chat = [popup.MessagesScroll, popup.JumpToLatestButton, popup.StatusText];
        if (up)
        {
            SetRows(chat, 0);
            Grid.SetRow(popup.NoticesPanel, 1);
            Grid.SetRow(popup.BarStack, 2);
            Grid.SetRow(popup.Toolbar, 3);
            rows[0].Height = new GridLength(1, GridUnitType.Star);
            rows[3].Height = GridLength.Auto;
        }
        else
        {
            Grid.SetRow(popup.BarStack, 0);
            Grid.SetRow(popup.Toolbar, 1);
            Grid.SetRow(popup.NoticesPanel, 2);
            SetRows(chat, 3);
            rows[0].Height = GridLength.Auto;
            rows[3].Height = new GridLength(1, GridUnitType.Star);
        }
        // Short chats sit next to the message box.
        popup.MessagesPanel.VerticalAlignment = up ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        if (input.SelectionStart != start || input.SelectionLength != length) input.Select(start, length);
    }

    private static void SetRows(FrameworkElement[] elements, int row)
    {
        foreach (var e in elements) Grid.SetRow(e, row);
    }
}
