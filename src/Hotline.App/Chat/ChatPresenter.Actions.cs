using Hotline.Core.Chat;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Hotline.App.Chat;

/// <summary>
/// Quick actions: "/" at the start of the composer lists ~/.hotline/actions/*.md; picking one fills "/name ", and on
/// send "/name text" becomes the action's instruction applied to the text (or to the attachments when empty).
/// </summary>
internal sealed partial class ChatPresenter
{
    private QuickActions? _actions;

    private QuickActions Actions
    {
        get
        {
            if (_actions is not null) return _actions;
            _actions = new QuickActions(Path.Combine(dataDirectory, "actions"));
            try { _actions.EnsureDefaults(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("quick actions setup failed", ex); }
            return _actions;
        }
    }

    private void InitializeActions()
    {
        _ = Actions; // first run writes the defaults
        popup.Input.TextChanged += (_, _) => Guard("quick action suggestions", UpdateSuggestions);
        popup.SuggestionsList.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is ListViewItem { Tag: QuickAction a }) Complete(a);
        };
    }

    private bool SuggestionsOpen => popup.SuggestionsList.Visibility == Visibility.Visible;

    private void UpdateSuggestions()
    {
        var query = QuickActions.SuggestionQuery(popup.Input.Text);
        var matches = query is null ? [] : Actions.Matching(query);
        popup.SuggestionsList.Items.Clear();
        foreach (var a in matches) popup.SuggestionsList.Items.Add(SuggestionItem(a));
        popup.SuggestionsList.Visibility = matches.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (matches.Count > 0) popup.SuggestionsList.SelectedIndex = 0;
    }

    private ListViewItem SuggestionItem(QuickAction a)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        row.Children.Add(new TextBlock { Text = "/" + a.Name, FontWeight = FontWeights.SemiBold, FontSize = _tokens.FontSizePx });
        row.Children.Add(new TextBlock
        {
            Text = a.Description, Foreground = _style.Muted, FontSize = Math.Max(11, _tokens.FontSizePx - 2),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        });
        return new ListViewItem { Content = row, Tag = a, MinHeight = 0, Padding = new Thickness(8, 4, 8, 4) };
    }

    /// <summary>Up/Down move, Tab/Enter pick, Esc closes the list (the panel stays open). True when handled.</summary>
    private bool HandleSuggestionKey(VirtualKey key)
    {
        var list = popup.SuggestionsList;
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
        popup.Input.Text = $"/{a.Name} ";
        popup.Input.SelectionStart = popup.Input.Text.Length;
        popup.SuggestionsList.Visibility = Visibility.Collapsed;
        popup.Input.Focus(FocusState.Keyboard);
    }

    /// <summary>The text to send: a quick action expanded, or null (with a notice) when it has nothing to apply to.</summary>
    private string? ApplyQuickAction(string text)
    {
        if (Actions.Expand(text, tray.Items.Count > 0) is not { } expanded) return text;
        if (expanded.Text is null)
        {
            Notice($"Type the text for /{expanded.Action.Name} after it, or attach something.", InfoBarSeverity.Warning);
            return null;
        }
        log.Info($"quick action /{expanded.Action.Name}");
        return expanded.Text;
    }
}
