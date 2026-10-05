using System.Diagnostics;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Tools;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Hotline.App.Chat;

/// <summary>
/// Coordinates the chat panel: builds its parts (message box, attachments, transcript, notices, voice, recent chats),
/// routes chat events to them and runs the send flow. Each part does one job; this class only connects them.
/// All members run on the UI thread.
/// </summary>
internal sealed class ChatPresenter
{
    private readonly PopupWindow _popup;
    private readonly ChatController _chat;
    private readonly HotlineSettings _settings;
    private readonly SettingsStore _store;
    private readonly FileLog _log;
    private readonly string _dataDirectory;
    private readonly PanelTheme _theme;
    private readonly NoticeArea _notices;
    private readonly UiTasks _tasks;
    private readonly AttachmentPanel _attachments;
    private readonly TranscriptView _transcript;
    private readonly Composer _composer;
    private readonly VoiceInput _voice;
    private readonly RecentChatsMenu _recent;
    private readonly SendRouter _router;
    private readonly MemoryStore _memory;
    private readonly PanelSelfTest _selfTest;
    private string? _lastLook;
    private bool _preparingSend;

    /// <summary>Raised when an answer starts or finishes streaming (the bottom-bar pickers lock meanwhile).</summary>
    public event Action<bool>? BusyChanged;
    public event Action? SettingsRequested;
    public bool IsBusy { get; private set; }

    /// <summary>Recent conversations from the history (set by the app; null when history is off).</summary>
    public Func<int, IReadOnlyList<HistoryStore.Summary>>? RecentChats { get => _recent.Source; set => _recent.Source = value; }

    public ChatPresenter(PopupWindow popup, ChatController chat, AttachmentTray tray, HotlineSettings settings, SettingsStore store, FileLog log,
        string dataDirectory)
    {
        (_popup, _chat, _settings, _store, _log, _dataDirectory) = (popup, chat, settings, store, log, dataDirectory);
        _theme = new PanelTheme(settings, OpenLink);
        _notices = new NoticeArea(popup, log);
        _tasks = new UiTasks(log, _notices);
        var visuals = new AttachmentVisuals(_theme, _notices, log);
        _attachments = new AttachmentPanel(popup, tray, settings, _notices, visuals, _theme, _tasks, log);
        _transcript = new TranscriptView(popup, _theme, visuals, _notices, () => _tasks.Run("retry", chat.RetryAsync));
        var actions = new QuickActions(Path.Combine(dataDirectory, "actions"));
        try { actions.EnsureDefaults(); } // first run writes the defaults
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("quick actions setup failed", ex); }
        _router = new SendRouter(actions);
        _memory = new MemoryStore(Path.Combine(dataDirectory, "memory.md"));
        _composer = new Composer(popup, _theme, actions, _tasks);
        _voice = new VoiceInput(popup, _composer, _notices, settings, _tasks, log);
        _recent = new RecentChatsMenu(popup, chat, _notices, dataDirectory);
        _selfTest = new PanelSelfTest(popup, _transcript, _composer, settings, log, OnChatEvent, () => { _lastLook = null; ApplyAppearance(); }, NewChat);
    }

    public void Initialize()
    {
        ApplyAppearance();
        _transcript.Initialize();
        _composer.Initialize();
        _attachments.Initialize();
        _voice.Initialize();
        _recent.Initialize();

        _chat.Event += e => _tasks.Guard("chat event", () => OnChatEvent(e));
        _composer.SendRequested += () => _tasks.Run("send", SendAsync);
        _composer.PreviousChatRequested += () => { if (!_chat.IsBusy) _recent.ResumeLast(); };
        _voice.SendRequested += () => _tasks.Run("send", SendAsync);
        _popup.NewChatRequested += NewChat;
        _popup.PinButton.Checked += (_, _) => { _popup.Pinned = true; _popup.PinButton.Content = Glyphs.Pinned; };
        _popup.PinButton.Unchecked += (_, _) => { _popup.Pinned = false; _popup.PinButton.Content = Glyphs.Pin; };
        _popup.NewChatButton.Click += (_, _) => NewChat();
        _popup.SettingsButton.Click += (_, _) => { if (SettingsRequested is null) OpenSettingsFolder(); else SettingsRequested(); };
        _popup.MessagesPanel.SizeChanged += (_, _) => ReportHeight();
        _popup.NoticesPanel.SizeChanged += (_, _) => ReportHeight();
        _popup.Composer.SizeChanged += (_, _) => ReportHeight();
        _log.Info("chat view ready");
    }

    /// <summary>Recomputes the look from the live settings and re-renders what's shown when it changed.</summary>
    public void ApplyAppearance()
    {
        _theme.Update();
        if (_settings.Window.Backdrop == BackdropKind.Solid)
            _popup.Root.Background = PanelTheme.Brush(_theme.Tokens.SolidBackground); // follows the chosen theme, not the OS theme
        _composer.ApplyAppearance();
        if (_lastLook is not null && _theme.Look != _lastLook) _transcript.Rebuild();
        _lastLook = _theme.Look;
    }

    public void NewChat()
    {
        _chat.NewChat();
        _attachments.TakeAll();
    }

    // ---- what the rest of the app calls ---------------------------------------------------------

    internal void Notice(string message, InfoBarSeverity severity) => _notices.Show(message, severity);

    internal Task<ToolDecision> AskToolApprovalAsync(ToolCallRequest request, CancellationToken ct) => _notices.AskToolApprovalAsync(request, ct);

    public void AttachSelection(string text, string? app) => _attachments.AttachSelection(text, app);

    public void Voice(VoiceCommand command) => _voice.Handle(command);

    public Task<string> VoiceSelfTestAsync() => WindowsSpeech.CheckAsync();

    public void Demo() => _selfTest.Demo();

    public async void SelfTest(Uri? uri) => await _selfTest.RunAsync();

    // ---- sending ----------------------------------------------------------------------------------

    private async Task SendAsync()
    {
        if (_chat.IsBusy) { _chat.Cancel(); return; }
        if (_preparingSend) return; // a second Enter/click while OCR runs
        if (_voice.Active) { await _voice.FinishAndSendAsync(); return; } // it sends once the dictation is final
        var typed = _composer.Text;
        switch (_router.Route(typed, _attachments.Items.Count > 0))
        {
            case SendRoute.Nothing:
                return;
            case SendRoute.Remember { Fact: "" }:
                _notices.Show("Type what to remember after /remember, e.g. \"/remember I prefer metric units\".", InfoBarSeverity.Warning);
                return;
            case SendRoute.Remember r:
                if (_memory.Append(r.Fact))
                {
                    _composer.Text = "";
                    _notices.Show("Remembered. Every chat sees it from now on (edit in Settings › Chat and history › Memory).", InfoBarSeverity.Success);
                }
                else _notices.Show($"Not saved: keep it to one line of at most {MemoryStore.MaxFactChars} characters.", InfoBarSeverity.Warning);
                return;
            case SendRoute.NeedsText n:
                _notices.Show($"Type the text for /{n.Action} after it, or attach something.", InfoBarSeverity.Warning);
                return;
            case SendRoute.Message m:
                await SendMessageAsync(typed, m);
                return;
        }
    }

    private async Task SendMessageAsync(string typed, SendRoute.Message message)
    {
        _preparingSend = true;
        try
        {
            if (!await _attachments.PrepareForSendAsync(_chat.CurrentCapabilities?.Images ?? true)) return;
            if (!_chat.CanAccept(_attachments.Items, out var reason))
            {
                _notices.Show(reason!, InfoBarSeverity.Warning); // draft stays in the box
                return;
            }
        }
        finally { _preparingSend = false; }
        var attachments = _attachments.TakeAll();
        if (_composer.Text == typed) _composer.Text = ""; // keep anything typed while OCR ran
        if (message.Action is { } action) _log.Info($"quick action /{action}");
        _log.Info($"chat send via {_chat.BackendId}: {message.Text.Length} chars, {attachments.Count} attachment(s)");
        await _chat.SendAsync(message.Text, attachments);
    }

    // ---- chat events ------------------------------------------------------------------------------

    private void OnChatEvent(ChatEvent e)
    {
        switch (e)
        {
            case UserMessageAdded u:
                _transcript.AddUser(u.Message);
                break;
            case AssistantStarted s:
                _transcript.StartAnswer(s.Id, s.BackendName);
                SetBusy(true);
                break;
            case AssistantDelta d:
                _transcript.AppendToAnswer(d.Id, d.Text, d.Replace);
                break;
            case AssistantStatus s:
                _log.Info($"chat status: {s.Message}");
                _notices.Show(s.Message, InfoBarSeverity.Informational);
                break;
            case AssistantCompleted c:
                _transcript.FinishAnswer(c.Id);
                SetBusy(false);
                _log.Info("chat answer completed");
                break;
            case AssistantCancelled c:
                _transcript.FinishAnswer(c.Id);
                SetBusy(false);
                break;
            case AssistantFailed f:
                _log.Error($"chat answer failed: {f.Kind}: {f.Message}");
                if (!_transcript.HasAnswer(f.Id)) _transcript.StartAnswer(f.Id, "");
                _transcript.FinishAnswer(f.Id);
                _transcript.ShowError(f.Id, f.Message);
                SetBusy(false);
                break;
            case MessageRestored r when r.Message.Role == ChatRole.User:
                _transcript.AddUser(r.Message);
                break;
            case MessageRestored r:
                var name = _settings.Chat.Backends.FirstOrDefault(b => b.Id == r.Message.BackendId)?.Name ?? r.Message.BackendId ?? "";
                _transcript.RestoreAnswer(r.Message, name);
                break;
            case ConversationReset:
                _transcript.Clear();
                _notices.Clear();
                SetBusy(false);
                break;
        }
    }

    private void SetBusy(bool busy)
    {
        _composer.SetBusy(busy);
        if (IsBusy == busy) return;
        IsBusy = busy;
        BusyChanged?.Invoke(busy);
    }

    /// <summary>The panel's natural height: messages + notices + message box + toolbar + spacing (3×8) + padding (20).</summary>
    private void ReportHeight()
    {
        var messages = _transcript.IsEmpty ? 0 : _popup.MessagesPanel.ActualHeight;
        _popup.SetContentHeight(messages + _popup.NoticesPanel.ActualHeight + _popup.Composer.ActualHeight + _popup.Toolbar.ActualHeight + 24 + 20);
    }

    /// <summary>Without a settings window (tests): open ~/.hotline so settings.json is one click away.</summary>
    private void OpenSettingsFolder()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_store.FilePath}\"") { UseShellExecute = true });
            _popup.HidePopup();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _notices.Show($"Could not open the settings folder ({_dataDirectory}): {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private static void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme is "https" or "http" or "mailto"))
            _ = Launcher.LaunchUriAsync(uri);
    }
}
