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
        // The gaps keep their size and move to the side facing the bar (ReportHeight sets their size).
        var noticesGap = popup.NoticesPanel.Margin.Top + popup.NoticesPanel.Margin.Bottom;
        var chatGap = popup.MessagesScroll.Margin.Top + popup.MessagesScroll.Margin.Bottom;
        popup.NoticesPanel.Margin = up ? new Thickness(0, 0, 0, noticesGap) : new Thickness(0, noticesGap, 0, 0);
        popup.MessagesScroll.Margin = up ? new Thickness(0, 0, 0, chatGap) : new Thickness(0, chatGap, 0, 0);
        if (input.SelectionStart != start || input.SelectionLength != length) input.Select(start, length);
    }

    private const double Gap = 8;

    /// <summary>
    /// The gaps between the rows: 8 between the bar and its button row always; between the bar and the notices, and the
    /// notices and the chat, only when there is something there. Margins, not Grid.RowSpacing: row spacing stays around
    /// empty rows, which left an empty band beside the bar of an empty panel.
    /// </summary>
    public static (double Notices, double Chat) Gaps(PopupWindow popup, bool hasChat) =>
        (popup.NoticesPanel.Children.Count > 0 ? Gap : 0, hasChat ? Gap : 0);

    /// <summary>Sets the gaps on the bar-facing side of the notices and the chat; unchanged values are left alone.</summary>
    public static void UpdateSpacing(PopupWindow popup, bool up, bool hasChat)
    {
        var (notices, chat) = Gaps(popup, hasChat);
        Set(popup.NoticesPanel, up ? new Thickness(0, 0, 0, notices) : new Thickness(0, notices, 0, 0));
        Set(popup.MessagesScroll, up ? new Thickness(0, 0, 0, chat) : new Thickness(0, chat, 0, 0));

        static void Set(FrameworkElement e, Thickness margin) { if (e.Margin != margin) e.Margin = margin; }
    }

    private static void SetRows(FrameworkElement[] elements, int row)
    {
        foreach (var e in elements) Grid.SetRow(e, row);
    }
}
