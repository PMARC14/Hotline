using System.Diagnostics;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Text;
using Hotline.Core.Theming;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Hotline.App.Chat;

/// <summary>Connects the native chat panel to the ChatController. All members run on the UI thread.</summary>
internal sealed partial class ChatPresenter(
    PopupWindow popup, ChatController chat, AttachmentTray tray, HotlineSettings settings, SettingsStore store, FileLog log,
    Func<IReadOnlyList<BackendProfile>> profiles, string dataDirectory)
{
    private sealed class AssistantView
    {
        public required StackPanel Body { get; init; }
        public required StackPanel Container { get; init; }
        public required TextBlock Caret { get; init; }
        public IReadOnlyList<MdBlock> Blocks { get; set; } = [];
        public string Text { get; set; } = "";
        public bool Streaming { get; set; } = true;
        public bool Dirty { get; set; }
    }

    private readonly Dictionary<string, AssistantView> _assistants = [];
    private bool _stickToBottom = true;
    private bool _autoScrolling;
    private DispatcherQueueTimerWrapper? _renderTimer;
    private RenderStyle _style = null!;
    private ThemeTokens _tokens = ThemeTokens.Dark;

    public void Initialize()
    {
        var dark = settings.Window.Theme switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
        _tokens = ThemeTokens.For(dark, settings.Window);
        _style = new RenderStyle(_tokens.FontSizePx, new FontFamily(_tokens.Font), Brush(_tokens.Muted), Brush(_tokens.CodeBackground), Brush(_tokens.Accent), _tokens.RadiusPx, OpenLink);
        if (settings.Window.Backdrop == BackdropKind.Solid)
            popup.Root.Background = Brush(_tokens.SolidBackground); // follows the chosen theme, not the OS theme
        popup.Input.FontSize = _tokens.FontSizePx;
        popup.Input.FontFamily = _style.Font;

        chat.Event += e => Guard("chat event", () => OnChatEvent(e));
        // Focus after the window is laid out and active, or the caret may not appear.
        popup.Shown += () => popup.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => { if (!popup.Input.Focus(FocusState.Keyboard)) log.Debug("input focus refused"); });
        popup.NewChatRequested += NewChat;
        popup.CaptureRequested += window => Run("capture", () => CaptureAsync(window));

        popup.Input.PreviewKeyDown += Input_PreviewKeyDown;
        var restingBorder = popup.Composer.BorderBrush;
        popup.Input.GotFocus += (_, _) => { popup.Composer.BorderBrush = _style.Accent; popup.Composer.BorderThickness = new Thickness(1.5); };
        popup.Input.LostFocus += (_, _) => { popup.Composer.BorderBrush = restingBorder; popup.Composer.BorderThickness = new Thickness(1); };
        popup.Input.Paste += Input_Paste;
        popup.SendButton.Click += (_, _) => Run("send", SendAsync);
        popup.AttachFilesItem.Click += (_, _) => Run("pick files", PickFilesAsync);
        popup.CaptureWindowItem.Click += (_, _) => Run("capture window", () => CaptureAsync(window: true));
        popup.CaptureScreenItem.Click += (_, _) => Run("capture screen", () => CaptureAsync(window: false));
        popup.CaptureWindowButton.Click += (_, _) => Run("capture window", () => CaptureAsync(window: true));
        popup.CaptureScreenButton.Click += (_, _) => Run("capture screen", () => CaptureAsync(window: false));
        popup.PinButton.Checked += (_, _) => { popup.Pinned = true; popup.PinButton.Content = "\uE840"; };
        popup.PinButton.Unchecked += (_, _) => { popup.Pinned = false; popup.PinButton.Content = "\uE718"; };
        popup.NewChatButton.Click += (_, _) => NewChat();
        popup.SettingsButton.Click += (_, _) => OpenSettings();
        popup.Root.DragOver += Root_DragOver;
        popup.Root.Drop += Root_Drop;

        popup.MessagesPanel.SizeChanged += (_, _) => { ReportHeight(); if (_stickToBottom) ScrollToBottomNow(); };
        popup.MessagesScroll.ViewChanged += OnMessagesViewChanged;
        popup.NoticesPanel.SizeChanged += (_, _) => ReportHeight();
        popup.Composer.SizeChanged += (_, _) => ReportHeight();

        _renderTimer = new DispatcherQueueTimerWrapper(popup.DispatcherQueue, TimeSpan.FromMilliseconds(50), RenderDirty);
        UpdateBackendLabel();
        log.Info("chat view ready");
    }

    public void NewChat()
    {
        chat.NewChat();
        tray.TakeAll();
        RefreshChips();
    }

    // ---- composer ---------------------------------------------------------------------------

    private void Input_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (shift) return; // newline
        e.Handled = true;
        Run("send", SendAsync);
    }

    private async Task SendAsync()
    {
        if (chat.IsBusy) { chat.Cancel(); return; }
        var text = popup.Input.Text.Trim();
        if (text.Length == 0 && tray.Items.Count == 0) return;
        if (!chat.CanAccept(tray.Items, out var reason))
        {
            Notice(reason!, InfoBarSeverity.Warning); // draft stays in the box
            return;
        }
        var attachments = tray.TakeAll();
        RefreshChips();
        popup.Input.Text = "";
        log.Info($"chat send via {chat.BackendId}: {text.Length} chars, {attachments.Count} attachment(s)");
        await chat.SendAsync(text, attachments);
    }

    private void SetBusy(bool busy)
    {
        popup.SendButton.Content = busy ? "\uE71A" : "\uE724";
        ToolTipService.SetToolTip(popup.SendButton, busy ? "Stop" : "Send (Enter)");
    }

    // ---- conversation -----------------------------------------------------------------------

    private void OnChatEvent(ChatEvent e)
    {
        switch (e)
        {
            case UserMessageAdded u:
                AddUser(u.Message);
                break;
            case AssistantStarted s:
                StartAssistant(s.Id, s.BackendName);
                SetBusy(true);
                break;
            case AssistantDelta d when _assistants.TryGetValue(d.Id, out var view):
                view.Text = d.Replace ? d.Text : view.Text + d.Text;
                view.Dirty = true;
                _renderTimer?.Start();
                break;
            case AssistantCompleted c:
                Finish(c.Id);
                log.Info("chat answer completed");
                break;
            case AssistantCancelled c:
                Finish(c.Id);
                break;
            case AssistantFailed f:
                log.Error($"chat answer failed: {f.Kind}: {f.Message}");
                if (!_assistants.ContainsKey(f.Id)) StartAssistant(f.Id, "");
                Finish(f.Id);
                ShowError(f.Id, f.Message);
                break;
            case ConversationReset:
                _assistants.Clear();
                popup.MessagesPanel.Children.Clear();
                popup.NoticesPanel.Children.Clear();
                SetBusy(false);
                break;
        }
    }

    private void AddUser(ChatMessage message)
    {
        var text = new TextBlock { Text = message.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = _tokens.FontSizePx, FontFamily = _style.Font };
        var stack = new StackPanel { Spacing = 4 };
        if (message.Text.Length > 0) stack.Children.Add(text);
        if (message.Attachments.Count > 0)
            stack.Children.Add(AttachmentStrip(message.Attachments, thumbSize: 72));
        popup.MessagesPanel.Children.Add(new Border
        {
            Child = stack, Background = Brush(_tokens.UserBubble), CornerRadius = new CornerRadius(_tokens.RadiusPx + 2),
            Padding = new Thickness(12, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 720,
        });
        ScrollToEnd(force: true);
    }

    private void StartAssistant(string id, string backendName)
    {
        var container = new StackPanel { Spacing = 2 };
        if (backendName.Length > 0)
            container.Children.Add(new TextBlock { Text = backendName, FontSize = 11, Foreground = _style.Muted });
        var body = new StackPanel { Spacing = 8 };
        var caret = new TextBlock { Text = "▍", Foreground = _style.Accent, FontSize = _tokens.FontSizePx };
        container.Children.Add(body);
        container.Children.Add(caret);
        popup.MessagesPanel.Children.Add(container);
        _assistants[id] = new AssistantView { Body = body, Container = container, Caret = caret, Dirty = true };
        _renderTimer?.Start();
    }

    private void Finish(string id)
    {
        if (_assistants.TryGetValue(id, out var view))
        {
            view.Streaming = false;
            view.Dirty = true;
            RenderDirty();
        }
        SetBusy(false);
    }

    private void ShowError(string id, string message)
    {
        if (!_assistants.TryGetValue(id, out var view)) return;
        var bar = new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error, Message = message };
        var retry = new Button { Content = "Retry" };
        retry.Click += (_, _) => { bar.IsOpen = false; Run("retry", chat.RetryAsync); };
        bar.ActionButton = retry;
        view.Container.Children.Add(bar);
        ScrollToEnd(force: false);
    }

    /// <summary>
    /// Re-renders changed answers. Only blocks from the first changed one onward are rebuilt (completed paragraphs,
    /// code blocks and tables stay put, keeping selection), and the interval adapts to how long rendering takes.
    /// </summary>
    private void RenderDirty()
    {
        var watch = Stopwatch.StartNew();
        var any = false;
        foreach (var view in _assistants.Values.Where(v => v.Dirty))
        {
            view.Dirty = false;
            any = true;
            var blocks = MarkdownModel.Parse(view.Text);
            var from = MarkdownModel.FirstChangedIndex(view.Blocks, blocks);
            while (view.Body.Children.Count > from) view.Body.Children.RemoveAt(view.Body.Children.Count - 1);
            for (var b = from; b < blocks.Count; b++) view.Body.Children.Add(MarkdownRenderer.RenderBlock(blocks[b], _style));
            view.Blocks = blocks;
            view.Caret.Visibility = view.Streaming ? Visibility.Visible : Visibility.Collapsed;
        }
        if (!any) { _renderTimer?.Stop(); return; }
        _renderTimer?.SetInterval(RenderThrottle.NextInterval(watch.Elapsed));
        ScrollToEnd(force: false);
    }

    // ---- notices, height, scrolling ---------------------------------------------------------

    private void Notice(string message, InfoBarSeverity severity)
    {
        var bar = new InfoBar { IsOpen = true, IsClosable = true, Severity = severity, Message = message };
        bar.Closed += (_, _) => popup.NoticesPanel.Children.Remove(bar);
        popup.NoticesPanel.Children.Add(bar);
        while (popup.NoticesPanel.Children.Count > 3) popup.NoticesPanel.Children.RemoveAt(0);
        var timer = popup.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(10);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => popup.NoticesPanel.Children.Remove(bar);
        timer.Start();
    }

    private void ReportHeight()
    {
        // Messages area (natural height) + notices + composer + toolbar + spacing (3×8) + root padding (20).
        var messages = popup.MessagesPanel.Children.Count == 0 ? 0 : popup.MessagesPanel.ActualHeight;
        var dip = messages + popup.NoticesPanel.ActualHeight + popup.Composer.ActualHeight + popup.Toolbar.ActualHeight + 24 + 20;
        popup.SetContentHeight(dip);
    }

    /// <summary>
    /// Follow the conversation while the user is at the bottom; stop following once they scroll up to read
    /// (resumes when they scroll back down or send a message). Scrolling happens after layout (SizeChanged),
    /// so the target is the new bottom, not the old one.
    /// </summary>
    private void ScrollToEnd(bool force)
    {
        if (force) _stickToBottom = true;
        if (_stickToBottom) ScrollToBottomNow();
    }

    private void ScrollToBottomNow()
    {
        var sv = popup.MessagesScroll;
        _autoScrolling = true;
        sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true);
    }

    private void OnMessagesViewChanged(object? sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        var sv = popup.MessagesScroll;
        var atBottom = sv.ScrollableHeight - sv.VerticalOffset < 24;
        if (_autoScrolling) { _autoScrolling = false; if (atBottom) return; }
        _stickToBottom = atBottom; // user scrolled: follow only if they are back at the bottom
    }

    // ---- toolbar ----------------------------------------------------------------------------

    private void UpdateBackendLabel()
    {
        var profile = profiles().FirstOrDefault(p => p.Id == chat.BackendId);
        popup.BackendLabel.Text = profile is null ? "" :
            profile.Name + (string.IsNullOrWhiteSpace(profile.Model) ? "" : $" · {profile.Model}") + (string.IsNullOrWhiteSpace(profile.Effort) ? "" : $" · {profile.Effort}");
    }

    /// <summary>Until the settings window (Plan 3b) exists: open ~/.hotline so settings.json is one click away.</summary>
    private void OpenSettings()
    {
        try
        {
            Directory.CreateDirectory(dataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{store.FilePath}\"") { UseShellExecute = true });
            popup.HidePopup();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Notice($"Could not open the settings folder ({dataDirectory}): {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme is "https" or "http" or "mailto"))
            _ = Launcher.LaunchUriAsync(uri);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static SolidColorBrush Brush(string hex)
    {
        var (a, r, g, b) = ThemeColor.Parse(hex);
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    private void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { log.Error($"{what} failed", ex); }
    }

    private void Run(string what, Func<Task> work) => _ = RunAsync(what, work);

    private async Task RunAsync(string what, Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex)
        {
            log.Error($"{what} failed", ex);
            Notice($"Couldn't {what}: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>Re-arms a DispatcherQueueTimer only when stopped (cheap Start() calls while streaming).</summary>
    private sealed class DispatcherQueueTimerWrapper
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
        public DispatcherQueueTimerWrapper(Microsoft.UI.Dispatching.DispatcherQueue queue, TimeSpan interval, Action tick)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = interval;
            _timer.IsRepeating = true;
            _timer.Tick += (_, _) => tick();
        }
        public void Start() { if (!_timer.IsRunning) _timer.Start(); }
        public void SetInterval(TimeSpan interval) { if (_timer.Interval != interval) _timer.Interval = interval; }
        public void Stop() => _timer.Stop();
    }

    // Attachments, paste, drag-drop and capture: ChatPresenter.Attachments.cs (Task 5).
    private partial Task PickFilesAsync();
    private partial Task CaptureAsync(bool window);
    private partial void Input_Paste(object sender, TextControlPasteEventArgs e);
    private partial void Root_DragOver(object sender, DragEventArgs e);
    private partial void Root_Drop(object sender, DragEventArgs e);
    private partial void RefreshChips();
}
