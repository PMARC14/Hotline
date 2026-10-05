using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.SpeechRecognition;
using Windows.System;

namespace Hotline.App.Chat;

/// <summary>
/// Push-to-talk in the panel: hold the key to dictate into the message box, let go to stop, Esc cancels. The state
/// machine is <see cref="VoiceSession"/> (Core); this connects it to Windows speech, the message box and the
/// "Listening…" bar, and stops listening whenever the panel hides (the text is kept, not sent).
/// </summary>
internal sealed class VoiceInput
{
    private const string Tag = "voice";
    private readonly PopupWindow _popup;
    private readonly Composer _composer;
    private readonly NoticeArea _notices;
    private readonly HotlineSettings _settings;
    private readonly UiTasks _tasks;
    private readonly FileLog _log;
    private readonly VoiceSession _session;
    private WindowsSpeech? _speech;
    private bool _holding;

    /// <summary>Dictation finished with "send" (chat.voiceAutoSend) and something was said.</summary>
    public event Action? SendRequested;

    public VoiceInput(PopupWindow popup, Composer composer, NoticeArea notices, HotlineSettings settings, UiTasks tasks, FileLog log)
    {
        (_popup, _composer, _notices, _settings, _tasks, _log) = (popup, composer, notices, settings, tasks, log);
        _session = new VoiceSession(CreateSpeechAsync);
        _session.TextChanged += text => _composer.Text = text;
        _session.Finished += OnFinished;
        _session.Failed += OnFailed;
    }

    public bool Active => _session.State != VoiceState.Idle;

    public void Initialize()
    {
        _popup.Hidden += () => { if (Active) _tasks.Run("voice stop (hidden)", () => _session.StopAsync(send: false)); };
        var previous = _composer.KeyFilter;
        _composer.KeyFilter = key => HandleKey(key) || previous?.Invoke(key) == true;
    }

    public void Handle(VoiceCommand command)
    {
        switch (command)
        {
            case VoiceCommand.Start:
                _holding = true;
                _tasks.Run("voice start", StartAsync);
                break;
            case VoiceCommand.Stop:
                _tasks.Run("voice stop", () => _session.StopAsync(send: _settings.Chat.VoiceAutoSend));
                break;
            default:
                _holding = false;
                _tasks.Run("voice toggle", () => Active ? _session.StopAsync(send: _settings.Chat.VoiceAutoSend) : StartAsync());
                break;
        }
    }

    /// <summary>Send while dictating: finish first, then send what was said.</summary>
    public Task FinishAndSendAsync() => _session.StopAsync(send: true);

    private async Task StartAsync()
    {
        if (Active) return;
        _composer.IsReadOnly = true; // typing now would be overwritten by the dictation
        _notices.ShowTagged(Tag, "🎙 Listening…", _holding ? "Let go of the key to stop. Esc cancels." : "Press the key again to stop. Esc cancels.",
            InfoBarSeverity.Informational);
        await _session.StartAsync(_composer.Text);
        if (_session.State == VoiceState.Listening) _log.Info("voice: listening");
    }

    private async Task<ISpeechSession?> CreateSpeechAsync()
    {
        if (_speech is not null) return _speech;
        var (speech, problem) = await WindowsSpeech.CreateAsync(_popup.DispatcherQueue);
        if (speech is null)
        {
            _log.Error($"voice: {problem}");
            Reset();
            _notices.ShowTagged(Tag, null, problem!, InfoBarSeverity.Warning, closable: true);
            return null;
        }
        speech.Hypothesis += _session.OnHypothesis;
        speech.Result += _session.OnResult;
        speech.Ended += status =>
        {
            if (_session.State != VoiceState.Listening) return; // our own stop or cancel
            _log.Info($"voice: session ended by itself ({status})");
            if (status is not (SpeechRecognitionResultStatus.Success or SpeechRecognitionResultStatus.TimeoutExceeded or SpeechRecognitionResultStatus.UserCanceled))
            {
                DropRecognizer(); // a fresh one next time (e.g. after a device change)
                _session.OnEndedByItself();
                _notices.ShowTagged(Tag, null, status == SpeechRecognitionResultStatus.MicrophoneUnavailable ? "No microphone is available."
                    : $"Voice input stopped ({status}).", InfoBarSeverity.Warning, closable: true);
                return;
            }
            _session.OnEndedByItself();
        };
        return _speech = speech;
    }

    private void OnFinished(VoiceOutcome outcome)
    {
        Reset();
        _composer.Text = outcome.Text;
        _log.Info(outcome.Cancelled ? "voice: cancelled" : $"voice: stopped ({outcome.Dictated.Length} chars)");
        if (outcome.Send) SendRequested?.Invoke();
    }

    private void OnFailed(Exception ex)
    {
        _log.Error("voice failed", ex);
        Reset();
        DropRecognizer();
        if (ex.HResult == WindowsSpeech.PrivacyDeclined)
            _notices.ShowTagged(Tag, null, "Voice input needs Windows' online speech recognition.", InfoBarSeverity.Warning, closable: true,
                ("Turn it on", () => _ = Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-speech"))));
        else if (ex is UnauthorizedAccessException)
            _notices.ShowTagged(Tag, null, "Hotline isn't allowed to use the microphone.", InfoBarSeverity.Warning, closable: true,
                ("Microphone settings", () => _ = Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-microphone"))));
        else
            _notices.ShowTagged(Tag, null, $"Voice input couldn't start: {ex.Message}", InfoBarSeverity.Warning, closable: true);
    }

    /// <summary>Back to typing: the box is editable and the voice bar is gone (warnings are shown after this).</summary>
    private void Reset()
    {
        _composer.IsReadOnly = false;
        _notices.Remove(Tag);
    }

    private void DropRecognizer()
    {
        try { _speech?.Dispose(); } catch (Exception ex) { _log.Error("voice: dispose failed", ex); }
        _speech = null;
    }

    /// <summary>Esc while listening cancels the dictation (the panel stays open). True when handled.</summary>
    private bool HandleKey(VirtualKey key)
    {
        if (!Active || key != VirtualKey.Escape) return false;
        _tasks.Run("voice cancel", _session.CancelAsync);
        return true;
    }
}
