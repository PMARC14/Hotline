using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace Hotline.App.Chat;

/// <summary>
/// The history list: the 🕘 button, Ctrl+↑ in an empty message box and "/resume [words]" all open it, centered over
/// the message bar and as wide as it. Saved chats run oldest (top) to newest (bottom); it opens on the newest one other
/// than the open chat. Typing in its search box filters by title (every word); ↑/↓ move, Enter opens, Esc closes.
/// "Continue this chat in ▸" and "Open history folder" sit above the list.
/// </summary>
internal sealed class RecentChatsMenu(PopupWindow popup, ChatController chat, NoticeArea notices, string dataDirectory, FileLog log,
    Func<int> listSize)
{
    /// <summary>Saved conversations, newest first (set by the app; null when history is off).</summary>
    public Func<int, IReadOnlyList<HistoryStore.Summary>>? Source { get; set; }

    /// <summary>Where the current chat can continue in a terminal window (set by the app).</summary>
    public ContinueIn? ContinueIn { get; set; }


    private Flyout? _flyout;
    private TextBox _search = null!;
    private ListView _list = null!;
    private TextBlock _empty = null!;
    private StackPanel _actions = null!;
    private IReadOnlyList<HistoryStore.Summary> _all = [];
    private IDisposable? _modal, _resizeHold;

    public void Initialize()
    {
        popup.RecentButton.Click += (_, _) => Show("");
        popup.ContinueInButton.Click += (_, _) => ShowContinueMenu(popup.ToolbarAnchor(popup.ContinueInButton)); // or "⋯" when it's in there
    }

    /// <summary>The toolbar's Continue in button: the same menu as in the history list, or a note when there's nowhere to go.</summary>
    private void ShowContinueMenu(FrameworkElement at)
    {
        if (ContinueMenu() is not { } menu) { notices.Show(Strings.Recent_ContinueNothing, InfoBarSeverity.Informational); return; }
        var hold = popup.HoldResize(); // a resize would close the menu (see PopupWindow.HoldResize)
        menu.Closed += (_, _) => hold.Dispose();
        menu.ShowAt(at);
    }

    /// <summary>"Continue this chat in ▸": the chat's own CLI, other CLIs under More…; null when there's nowhere to go.</summary>
    private MenuFlyout? ContinueMenu()
    {
        var targets = ContinueIn?.Targets() ?? [];
        if (targets.Count == 0) return null;
        var menu = new MenuFlyout();
        var more = new MenuFlyoutSubItem { Text = Strings.Recent_ContinueMore }; // other CLIs, reading the chat from a file
        foreach (var t in targets)
        {
            var item = new MenuFlyoutItem { Text = t.Label };
            item.Click += async (_, _) =>
            {
                _flyout?.Hide();
                popup.HidePopup(); // the user is moving to the terminal
                try { await t.Run(); }
                catch (Exception ex) // async void: nothing may escape and end the process
                {
                    popup.ShowPopup(readSelection: false);
                    notices.Show(L.F(Strings.Recent_ContinueFailed, t.Label, ex.Message), InfoBarSeverity.Warning);
                }
            };
            (t.More ? more.Items : menu.Items).Add(item);
        }
        if (more.Items.Count > 0) menu.Items.Add(more);
        return menu;
    }

    /// <summary>Opens the list, filtered by <paramref name="query"/> (words that must all be in the title).</summary>
    /// <param name="fromTyping">Opened by typing "/resume ": keys typed in the message box meanwhile move to the search.</param>
    public async void Show(string query, bool fromTyping = false)
    {
        if (Source is not { } source) { notices.Show(Strings.Recent_HistoryOff, InfoBarSeverity.Informational); return; }
        if (_loading || _flyout?.IsOpen == true) return; // already opening or open (a second Ctrl+Up, a double click)
        _loading = true;
        try
        {
            // Open at once (the search takes the keyboard), then fill once the chats are read.
            var flyout = _flyout ??= Build();
            BuildActions();
            _all = [];
            _takeTyping = fromTyping;
            _search.Text = query;
            Fill(query);
            flyout.ShowAt(popup.Composer, new FlyoutShowOptions { Placement = FlyoutPlacementMode.Top });
            _all = await Task.Run(() => source(Math.Clamp(listSize(), 10, 5000))); // reads each chat's file: off the UI thread
            if (flyout.IsOpen) Fill(_search.Text); // what has been typed by now
        }
        catch (Exception ex) // async void: nothing may escape (an unreadable history folder, a WinUI refusal)
        {
            log.Error("opening the history list failed", ex);
            notices.Show(Strings.Recent_OpenFailed, InfoBarSeverity.Warning);
        }
        finally { _loading = false; }
    }

    private bool _loading, _takeTyping;

    private Flyout Build()
    {
        _search = new TextBox { PlaceholderText = Strings.History_Search };
        _search.TextChanged += (_, _) => Fill(_search.Text);
        _search.PreviewKeyDown += OnSearchKey;
        _list = new ListView { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = true, MaxHeight = 360 };
        // ListViewItems added directly: ItemClick reports their content (the row), which carries the chat id too.
        _list.ItemClick += (_, e) => { if (e.ClickedItem is FrameworkElement { Tag: string id }) Open(id); };
        _empty = new TextBlock { Opacity = 0.7, Margin = new Thickness(12, 8, 12, 8), TextWrapping = TextWrapping.Wrap };
        _actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        var grid = new Grid { RowSpacing = 6 };
        foreach (var _ in Enumerable.Range(0, 4)) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        AddRow(grid, _actions, 0);
        AddRow(grid, _list, 1);
        AddRow(grid, _empty, 2);
        AddRow(grid, _search, 3); // next to the message bar, where the eye already is

        var flyout = new Flyout { Content = grid, ShouldConstrainToRootBounds = false, AreOpenCloseAnimationsEnabled = true };
        var presenter = new Style(typeof(FlyoutPresenter)); // layered over the default style, which keeps the frame
        presenter.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, double.PositiveInfinity)); // the default caps it at 456
        flyout.FlyoutPresenterStyle = presenter;
        flyout.Opening += (_, _) =>
        {
            _modal ??= popup.Modal(); // the list is part of the panel: do not hide the panel as focus moves into it
            _resizeHold ??= popup.HoldResize(); // and keep its size: a window resize would close the list
            grid.Width = Math.Max(320, popup.Composer.ActualWidth - 24); // as wide as the message bar (less the frame)
        };
        flyout.Opened += (_, _) =>
        {
            if (_takeTyping && popup.Input.Text.Length > 0) // typed after "/resume " before the list appeared
            {
                _search.Text += popup.Input.Text;
                popup.Input.Text = "";
            }
            _takeTyping = false;
            _search.Focus(FocusState.Programmatic);
            _search.SelectionStart = _search.Text.Length;
            if (_list.SelectedItem is { } selected) _list.ScrollIntoView(selected, ScrollIntoViewAlignment.Default);
        };
        flyout.Closed += (_, _) => { _modal?.Dispose(); _modal = null; _resizeHold?.Dispose(); _resizeHold = null; popup.Input.Focus(FocusState.Programmatic); };
        return flyout;

        static void AddRow(Grid g, FrameworkElement e, int row) { Grid.SetRow(e, row); g.Children.Add(e); }
    }

    private void Fill(string query)
    {
        var view = HistoryList.Build(_all, chat.ConversationId, query);
        _list.Items.Clear();
        foreach (var c in view.Items)
        {
            var row = new Grid { ColumnSpacing = 12, Tag = c.Id };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = c.Title, TextTrimming = TextTrimming.CharacterEllipsis };
            if (c.Id == chat.ConversationId) title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; // the open chat
            var ago = new TextBlock { Text = Ago(c.LastAt), Opacity = 0.6 };
            Grid.SetColumn(ago, 1);
            row.Children.Add(title);
            row.Children.Add(ago);
            var item = new ListViewItem { Content = row, Tag = c.Id };
            ToolTipService.SetToolTip(item, L.F(Strings.Recent_Tip, c.MessageCount, c.LastAt.ToLocalTime()));
            _list.Items.Add(item);
        }
        _list.SelectedIndex = view.Selected;
        if (view.Selected >= 0) _list.ScrollIntoView(_list.Items[view.Selected]);
        _empty.Text = view.Items.Count > 0 ? "" : _all.Count == 0 ? Strings.Recent_None : L.F(Strings.Recent_NoMatch, query.Trim());
        _empty.Visibility = view.Items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        _list.Visibility = view.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The search box keeps the keyboard: ↑/↓ move through the list, Enter opens, Esc closes.</summary>
    private void OnSearchKey(object sender, KeyRoutedEventArgs e)
    {
        var count = _list.Items.Count;
        switch (e.Key)
        {
            case VirtualKey.Up when count > 0:
                _list.SelectedIndex = Math.Max(0, (_list.SelectedIndex < 0 ? count : _list.SelectedIndex) - 1);
                break;
            case VirtualKey.Down when count > 0:
                _list.SelectedIndex = Math.Min(count - 1, _list.SelectedIndex + 1);
                break;
            case VirtualKey.Enter when e.KeyStatus.WasKeyDown: // a repeat of the Enter that sent "/resume": swallow it
                break;
            case VirtualKey.Enter:
                if (_list.SelectedItem is ListViewItem { Tag: string id }) Open(id);
                break;
            case VirtualKey.Escape:
                _flyout?.Hide();
                break;
            default:
                return;
        }
        if (_list.SelectedItem is { } selected) _list.ScrollIntoView(selected);
        e.Handled = true;
    }

    private void Open(string id)
    {
        _flyout?.Hide();
        if (id == chat.ConversationId) return; // already open
        if (chat.IsBusy) { notices.Show(Strings.Recent_Busy, InfoBarSeverity.Informational); return; }
        if (!chat.Resume(id)) notices.Show(Strings.Recent_OpenFailed, InfoBarSeverity.Warning);
    }

    /// <summary>"Continue this chat in ▸" (when there is somewhere to go) and "Open history folder".</summary>
    private void BuildActions()
    {
        _actions.Children.Clear();
        if (ContinueMenu() is { } menu)
        {
            _actions.Children.Add(new DropDownButton
            {
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6,
                    Children = { new FontIcon { Glyph = "", FontSize = 14 }, new TextBlock { Text = Strings.Recent_ContinueIn } }, // CommandPrompt
                },
                Flyout = menu,
            });
        }
        var folder = new Button { Content = Strings.Recent_OpenFolder };
        folder.Click += (_, _) => { _flyout?.Hide(); popup.HidePopup(); CliRunner.OpenFolder(Path.Combine(dataDirectory, "history")); };
        _actions.Children.Add(folder);
    }

    private static string Ago(DateTimeOffset at)
    {
        var span = DateTimeOffset.Now - at;
        return span.TotalMinutes < 1 ? Strings.Recent_JustNow
            : span.TotalHours < 1 ? L.F(Strings.Recent_MinutesAgo, (int)span.TotalMinutes)
            : span.TotalDays < 1 ? L.F(Strings.Recent_HoursAgo, (int)span.TotalHours)
            : span.TotalDays < 7 ? L.F(Strings.Recent_DaysAgo, (int)span.TotalDays)
            : at.ToLocalTime().ToString(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.MonthDayPattern, System.Globalization.CultureInfo.CurrentCulture);
    }
}
