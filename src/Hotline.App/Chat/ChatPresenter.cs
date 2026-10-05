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
    string dataDirectory)
{
    private bool _stickToBottom = true;
    private bool _autoScrolling;
    private DispatcherQueueTimerWrapper? _renderTimer;
    private RenderStyle _style = null!;
    private ThemeTokens _tokens = ThemeTokens.Dark;

    /// <summary>Raised when an answer starts or finishes streaming (the bottom-bar pickers lock meanwhile).</summary>
    public event Action<bool>? BusyChanged;
    public event Action? SettingsRequested;
    public bool IsBusy { get; private set; }

    /// <summary>Recomputes tokens from the live settings and re-renders answers (font, colours, solid background).</summary>
    public void ApplyAppearance()
    {
        var dark = settings.Window.Theme switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
        _tokens = ThemeTokens.For(dark, settings.Window);
        _style = new RenderStyle(_tokens.FontSizePx, new FontFamily(_tokens.Font), Brush(_tokens.Muted), Brush(_tokens.CodeBackground),
            Brush(_tokens.Accent), _tokens.RadiusPx, OpenLink);
        if (settings.Window.Backdrop == BackdropKind.Solid)
            popup.Root.Background = Brush(_tokens.SolidBackground); // follows the chosen theme, not the OS theme
        popup.Input.FontSize = _tokens.FontSizePx;
        popup.Input.FontFamily = _style.Font;
        SizeComposer();
        var look = $"{_tokens.FontSizePx}|{_tokens.Font}|{dark}|{settings.Window.Backdrop}";
        if (_transcript is not null && look != _lastLook) RebuildTranscript();
        _lastLook = look;
    }

    private string? _lastLook;

    /// <summary>The message bar is sized from the text size (incl. Windows' text scaling), not fixed pixels.</summary>
    private void SizeComposer()
    {
        var line = _tokens.FontSizePx * new Windows.UI.ViewManagement.UISettings().TextScaleFactor;
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

    public void Initialize()
    {
        ApplyAppearance();
        CreateTranscript();

        chat.Event += e => Guard("chat event", () => OnChatEvent(e));
        // Focus after the window is laid out and active, or the caret may not appear.
        popup.Shown += () => popup.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => { if (!popup.Input.Focus(FocusState.Keyboard)) log.Debug("input focus refused"); });
        popup.NewChatRequested += NewChat;
        popup.CaptureRequested += window => Run("capture", () => CaptureAsync(window));
        popup.RegionCaptureRequested += () => Run("capture region", CaptureRegionAsync);

        popup.Input.PreviewKeyDown += Input_PreviewKeyDown;
        InitializeActions();
        var restingBorder = popup.Composer.BorderBrush;
        popup.Input.GotFocus += (_, _) => { popup.Composer.BorderBrush = _style.Accent; popup.Composer.BorderThickness = new Thickness(1.5); };
        popup.Input.LostFocus += (_, _) => { popup.Composer.BorderBrush = restingBorder; popup.Composer.BorderThickness = new Thickness(1); };
        popup.Input.Paste += Input_Paste;
        popup.SendButton.Click += (_, _) => Run("send", SendAsync);
        popup.AttachFilesItem.Click += (_, _) => Run("pick files", PickFilesAsync);
        popup.CaptureWindowItem.Click += (_, _) => Run("capture window", () => CaptureAsync(window: true));
        popup.CaptureScreenItem.Click += (_, _) => Run("capture screen", () => CaptureAsync(window: false));
        popup.CaptureRegionItem.Click += (_, _) => Run("capture region", CaptureRegionAsync);
        popup.CaptureRegionButton.Click += (_, _) => Run("capture region", CaptureRegionAsync);
        popup.CaptureWindowButton.Click += (_, _) => Run("capture window", () => CaptureAsync(window: true));
        popup.CaptureScreenButton.Click += (_, _) => Run("capture screen", () => CaptureAsync(window: false));
        popup.PinButton.Checked += (_, _) => { popup.Pinned = true; popup.PinButton.Content = "\uE840"; };
        popup.PinButton.Unchecked += (_, _) => { popup.Pinned = false; popup.PinButton.Content = "\uE718"; };
        popup.NewChatButton.Click += (_, _) => NewChat();
        popup.RecentMenu.Opening += (_, _) => BuildRecentMenu();
        popup.SettingsButton.Click += (_, _) => { if (SettingsRequested is null) OpenSettings(); else SettingsRequested(); };
        popup.Root.DragOver += Root_DragOver;
        popup.Root.Drop += Root_Drop;

        popup.MessagesPanel.SizeChanged += (_, _) => { ReportHeight(); if (_stickToBottom) ScrollToBottomNow(); };
        popup.MessagesScroll.ViewChanged += OnMessagesViewChanged;
        popup.JumpToLatestButton.Click += (_, _) => { _stickToBottom = true; UpdateJumpButton(); ScrollToBottomNow(); };
        popup.NoticesPanel.SizeChanged += (_, _) => ReportHeight();
        popup.Composer.SizeChanged += (_, _) => ReportHeight();

        _renderTimer = new DispatcherQueueTimerWrapper(popup.DispatcherQueue, TimeSpan.FromMilliseconds(50), RenderDirty);
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
        if (SuggestionsOpen && HandleSuggestionKey(e.Key))
        {
            e.Handled = true;
            return;
        }
        var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
        if (e.Key == VirtualKey.Up && ctrl && popup.Input.Text.Length == 0 && !chat.IsBusy)
        {
            // Ctrl+Up in an empty box: back to the previous conversation.
            if (RecentChats?.Invoke(5).FirstOrDefault(c => c.Id != chat.ConversationId) is { } last) ResumeChat(last.Id);
            e.Handled = true;
            return;
        }
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
        if (ApplyQuickAction(text) is not { } toSend) return; // draft stays in the box
        if (!await ApplyOcrAsync()) return;
        if (!chat.CanAccept(tray.Items, out var reason))
        {
            Notice(reason!, InfoBarSeverity.Warning); // draft stays in the box
            return;
        }
        var attachments = tray.TakeAll();
        RefreshChips();
        popup.Input.Text = "";
        log.Info($"chat send via {chat.BackendId}: {toSend.Length} chars, {attachments.Count} attachment(s)");
        await chat.SendAsync(toSend, attachments);
    }

    private void SetBusy(bool busy)
    {
        popup.SendButton.Content = busy ? "\uE71A" : "\uE724";
        ToolTipService.SetToolTip(popup.SendButton, busy ? "Stop" : "Send (Enter)");
        if (IsBusy == busy) return;
        IsBusy = busy;
        BusyChanged?.Invoke(busy);
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
            case AssistantStatus s:
                log.Info($"chat status: {s.Message}");
                Notice(s.Message, InfoBarSeverity.Informational);
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
            case MessageRestored r when r.Message.Role == ChatRole.User:
                AddUser(r.Message);
                break;
            case MessageRestored r:
                RestoreAnswer(r.Message);
                break;
            case ConversationReset:
                ClearTranscript();
                popup.NoticesPanel.Children.Clear();
                SetBusy(false);
                break;
        }
    }

    // ---- notices, height, scrolling ---------------------------------------------------------

    /// <summary>
    /// Asks whether a tool may run: an InfoBar in the panel with Allow once / Always / Deny. No answer within five
    /// minutes (or the answer being stopped) counts as Deny. Brings the panel up if it was hidden.
    /// </summary>
    internal Task<Hotline.Core.Tools.ToolDecision> AskToolApprovalAsync(Hotline.Core.Tools.ToolCallRequest request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<Hotline.Core.Tools.ToolDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        // Anything that goes wrong (shutting down, UI failure, cancellation) means Deny — never an endless wait.
        var registration = ct.Register(() => tcs.TrySetResult(Hotline.Core.Tools.ToolDecision.Deny));
        _ = tcs.Task.ContinueWith(_ => registration.Dispose(), TaskScheduler.Default);
        if (!popup.DispatcherQueue.TryEnqueue(() =>
        {
            try { ShowApproval(request, tcs); }
            catch (Exception ex)
            {
                log.Error("showing the tool approval failed", ex);
                tcs.TrySetResult(Hotline.Core.Tools.ToolDecision.Deny);
            }
        }))
            tcs.TrySetResult(Hotline.Core.Tools.ToolDecision.Deny);
        return tcs.Task;
    }

    /// <summary>The approval bar: the full arguments (pretty-printed, scrollable, selectable) — nothing is hidden.</summary>
    private void ShowApproval(Hotline.Core.Tools.ToolCallRequest request, TaskCompletionSource<Hotline.Core.Tools.ToolDecision> tcs)
    {
        var args = request.Arguments.ValueKind == System.Text.Json.JsonValueKind.Undefined ? "{}"
            : System.Text.Json.JsonSerializer.Serialize(request.Arguments, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var description = request.Tool.Description.Trim();
        if (description.Length > 200) description = description[..200] + "…";
        var details = new StackPanel { Spacing = 4 };
        if (description.Length > 0) details.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 });
        details.Children.Add(new ScrollViewer
        {
            MaxHeight = 160, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock { Text = args, IsTextSelectionEnabled = true, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Cascadia Mono, Consolas") },
        });
        var bar = new InfoBar
        {
            IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Warning, Tag = ApprovalTag,
            Title = $"Allow {request.Tool.Server} › {request.Tool.Tool}?",
        };
        var timer = popup.DispatcherQueue.CreateTimer();
        void Answer(Hotline.Core.Tools.ToolDecision d)
        {
            timer.Stop();
            popup.NoticesPanel.Children.Remove(bar);
            if (tcs.TrySetResult(d)) log.Info($"tool {request.Tool.Server}/{request.Tool.Tool}: {d}");
        }
        Button B(string text, Hotline.Core.Tools.ToolDecision d, bool accent = false)
        {
            var b = new Button { Content = text };
            if (accent && Application.Current.Resources.TryGetValue("AccentButtonStyle", out var style) && style is Style s) b.Style = s;
            b.Click += (_, _) => Answer(d);
            return b;
        }
        details.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 8),
            Children = { B("Allow once", Hotline.Core.Tools.ToolDecision.AllowOnce, accent: true), B("Always allow", Hotline.Core.Tools.ToolDecision.AllowAlways), B("Deny", Hotline.Core.Tools.ToolDecision.Deny) },
        });
        bar.Content = details;
        popup.NoticesPanel.Children.Add(bar);
        if (!popup.IsShown) popup.ShowPopup();
        timer.Interval = TimeSpan.FromMinutes(5);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Answer(Hotline.Core.Tools.ToolDecision.Deny);
        timer.Start();
        // Cancelled or timed out elsewhere: take the bar down.
        _ = tcs.Task.ContinueWith(_ => popup.DispatcherQueue.TryEnqueue(() => { timer.Stop(); popup.NoticesPanel.Children.Remove(bar); }), TaskScheduler.Default);
    }

    private const string ApprovalTag = "approval";

    internal void Notice(string message, InfoBarSeverity severity)
    {
        var bar = new InfoBar { IsOpen = true, IsClosable = true, Severity = severity, Message = message };
        bar.Closed += (_, _) => popup.NoticesPanel.Children.Remove(bar);
        popup.NoticesPanel.Children.Add(bar);
        // Keep at most 3 plain notices; pending tool approvals are never pushed out.
        while (popup.NoticesPanel.Children.OfType<InfoBar>().Count(b => !ApprovalTag.Equals(b.Tag)) > 3
               && popup.NoticesPanel.Children.OfType<InfoBar>().FirstOrDefault(b => !ApprovalTag.Equals(b.Tag)) is { } oldest)
            popup.NoticesPanel.Children.Remove(oldest);
        var timer = popup.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(10);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => popup.NoticesPanel.Children.Remove(bar);
        timer.Start();
    }

    private void ReportHeight()
    {
        // Messages area (natural height) + notices + composer + toolbar + spacing (3×8) + root padding (20).
        var messages = _transcript is null || _transcript.Blocks.Count == 0 ? 0 : popup.MessagesPanel.ActualHeight;
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

    private void UpdateJumpButton()
        => popup.JumpToLatestButton.Visibility = !_stickToBottom && popup.MessagesScroll.ScrollableHeight > 0 ? Visibility.Visible : Visibility.Collapsed;

    private void ScrollToBottomNow()
    {
        var sv = popup.MessagesScroll;
        _autoScrolling = true;
        sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: false); // animated: no jolts while streaming
    }

    private void OnMessagesViewChanged(object? sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs e)
    {
        if (e.IsIntermediate) return;
        var sv = popup.MessagesScroll;
        var atBottom = sv.ScrollableHeight - sv.VerticalOffset < 24;
        if (_autoScrolling) { _autoScrolling = false; if (atBottom) { UpdateJumpButton(); return; } }
        _stickToBottom = atBottom; // user scrolled: follow only if they are back at the bottom
        UpdateJumpButton();
    }

    // ---- toolbar ----------------------------------------------------------------------------

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

    private Button CopyButton(Func<string> text, string tooltip)
    {
        var button = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { new FontIcon { Glyph = "\uE8C8", FontSize = 12 }, new TextBlock { Text = "Copy", FontSize = 12 } } },
            Padding = new Thickness(8, 3, 8, 3), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0),
            Opacity = 0.75,
        };
        ToolTipService.SetToolTip(button, tooltip);
        button.Click += (_, _) => { CopyText(text()); Notice("Copied.", InfoBarSeverity.Success); };
        return button;
    }

    private void CopyText(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        }
        catch (Exception ex) { log.Error("copy failed", ex); Notice("The clipboard is busy; try again.", InfoBarSeverity.Warning); }
    }

    // Attachments, paste, drag-drop and capture: ChatPresenter.Attachments.cs (Task 5).
    private partial Task PickFilesAsync();
    private partial Task CaptureAsync(bool window);
    private partial void Input_Paste(object sender, TextControlPasteEventArgs e);
    private partial void Root_DragOver(object sender, DragEventArgs e);
    private partial void Root_Drop(object sender, DragEventArgs e);
    private partial void RefreshChips();
}
