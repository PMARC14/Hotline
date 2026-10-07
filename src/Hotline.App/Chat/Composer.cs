using Hotline.Core.Chat;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;

namespace Hotline.App.Chat;

/// <summary>
/// The message box: its size and focus look, its keys (Enter sends, Shift+Enter is a new line, Ctrl+↑ in an empty
/// box reopens the last chat) and the "/" list of quick actions (↑/↓, Tab or Enter to pick, Esc to close).
/// </summary>
internal sealed class Composer(PopupWindow popup, PanelTheme theme, QuickActions actions, UiTasks tasks)
{
    /// <summary>Enter (send what's typed). Never a stop: see <see cref="SendButtonClicked"/>.</summary>
    public event Action? SendRequested;
    /// <summary>The Send button, which is the Stop button while an answer is being written.</summary>
    public event Action? SendButtonClicked;
    public event Action? PreviousChatRequested;

    /// <summary>Gets the first look at every key (e.g. Esc cancels dictation); true when it handled the key.</summary>
    public Func<VirtualKey, bool>? KeyFilter { get; set; }

    public string Text
    {
        get => popup.Input.Text;
        set
        {
            popup.Input.Text = value;
            popup.Input.SelectionStart = value.Length;
        }
    }

    public bool IsReadOnly
    {
        get => popup.Input.IsReadOnly;
        set => popup.Input.IsReadOnly = value;
    }

    public void Initialize()
    {
        popup.Input.PreviewKeyDown += OnPreviewKeyDown;
        popup.Input.TextChanged += (_, _) => tasks.Guard("quick action suggestions", UpdateSuggestions);
        popup.SuggestionsList.ItemClick += (_, e) => { if (e.ClickedItem is ListViewItem { Tag: QuickAction a }) Complete(a); };
        popup.SendButton.Click += (_, _) => SendButtonClicked?.Invoke();
        var restingBorder = popup.Composer.BorderBrush;
        popup.Input.GotFocus += (_, _) => { popup.Composer.BorderBrush = theme.Style.Accent; popup.Composer.BorderThickness = new Thickness(1.5); };
        popup.Input.LostFocus += (_, _) => { popup.Composer.BorderBrush = restingBorder; popup.Composer.BorderThickness = new Thickness(1); };
        // Focus after the window is laid out and active, or the caret may not appear.
        popup.Shown += () => popup.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => popup.Input.Focus(FocusState.Keyboard));
    }

    /// <summary>Text size, font and the button sizes that follow it (incl. Windows' text scaling).</summary>
    public void ApplyAppearance()
    {
        popup.Input.FontSize = theme.Tokens.FontSizePx;
        popup.Input.FontFamily = theme.Style.Font;
        var line = theme.Tokens.FontSizePx * new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
        var button = Math.Round(Math.Clamp(line * 2.1, 28, 64));
        foreach (var b in new[] { popup.PlusButton, popup.SendButton })
        {
            b.Width = b.Height = button;
            b.CornerRadius = new CornerRadius(Math.Round(button * 0.3));
        }
        popup.PlusButton.FontSize = Math.Round(button * 0.45);
        popup.SendButton.FontSize = Math.Round(button * 0.4);
        popup.Composer.CornerRadius = new CornerRadius(Math.Round(button * 0.3) + 4);
    }

    public void SetBusy(bool busy)
    {
        popup.SendButton.Content = busy ? Glyphs.Stop : Glyphs.Send;
        ToolTipService.SetToolTip(popup.SendButton, busy ? "Stop" : "Send (Enter)");
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (KeyFilter?.Invoke(e.Key) == true || (SuggestionsOpen && HandleSuggestionKey(e.Key)))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == VirtualKey.Up && IsDown(VirtualKey.Control) && popup.Input.Text.Length == 0)
        {
            PreviousChatRequested?.Invoke();
            e.Handled = true;
            return;
        }
        if (e.Key != VirtualKey.Enter || IsDown(VirtualKey.Shift)) return; // Shift+Enter: new line
        e.Handled = true;
        SendRequested?.Invoke();
    }

    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    // ---- "/" suggestions ------------------------------------------------------------------------

    public bool SuggestionsOpen => popup.SuggestionsList.Visibility == Visibility.Visible;

    public int SuggestionCount => popup.SuggestionsList.Items.Count;

    private void UpdateSuggestions()
    {
        var query = QuickActions.SuggestionQuery(popup.Input.Text);
        var matches = query is null ? [] : actions.Matching(query);
        popup.SuggestionsList.Items.Clear();
        foreach (var a in matches) popup.SuggestionsList.Items.Add(SuggestionItem(a));
        popup.SuggestionsList.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (matches.Count > 0) popup.SuggestionsList.SelectedIndex = 0;
    }

    private ListViewItem SuggestionItem(QuickAction a)
    {
        // A Grid (not a horizontal StackPanel) so the description gets a bounded width and trims with "…".
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new TextBlock { Text = "/" + a.Name, FontWeight = FontWeights.SemiBold, FontSize = theme.Tokens.FontSizePx });
        var description = new TextBlock
        {
            Text = a.Description, Foreground = theme.Style.Muted, FontSize = Math.Max(11, theme.Tokens.FontSizePx - 2),
            TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap, VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(description, 1);
        row.Children.Add(description);
        return new ListViewItem
        {
            Content = row, Tag = a, MinHeight = 0, Padding = new Thickness(8, 4, 8, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, AllowFocusOnInteraction = false,
        };
    }

    /// <summary>
    /// Up/Down move, Tab/Enter pick, Esc closes the list (the panel stays open). Enter on a fully typed name sends;
    /// keys with Ctrl/Shift/Alt keep their usual meaning. True when handled.
    /// </summary>
    private bool HandleSuggestionKey(VirtualKey key)
    {
        var list = popup.SuggestionsList;
        if (IsDown(VirtualKey.Control) || IsDown(VirtualKey.Shift) || IsDown(VirtualKey.Menu)) return false;
        if (list.SelectedItem is null && list.Items.Count > 0) list.SelectedIndex = 0;
        if (key == VirtualKey.Enter && list.SelectedItem is ListViewItem { Tag: QuickAction typed }
            && string.Equals(QuickActions.SuggestionQuery(popup.Input.Text), typed.Name, StringComparison.OrdinalIgnoreCase))
        {
            list.Visibility = Visibility.Collapsed;
            return false; // "/summarize" + Enter: send (applies to the attachments)
        }
        switch (key)
        {
            case VirtualKey.Down:
                list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1);
                list.ScrollIntoView(list.SelectedItem);
                return true;
            case VirtualKey.Up:
                list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1);
                list.ScrollIntoView(list.SelectedItem);
                return true;
            case VirtualKey.Tab or VirtualKey.Enter when list.SelectedItem is ListViewItem { Tag: QuickAction a }:
                Complete(a);
                return true;
            case VirtualKey.Escape:
                list.Visibility = Visibility.Collapsed;
                return true;
            default:
                return false;
        }
    }

    private void Complete(QuickAction a)
    {
        Text = $"/{a.Name} ";
        popup.SuggestionsList.Visibility = Visibility.Collapsed;
        popup.Input.Focus(FocusState.Keyboard);
    }
}
