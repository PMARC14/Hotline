using Hotline.Core.Chat;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.SpeechRecognition;

namespace Hotline.App.Chat;

/// <summary>
/// Push-to-talk (activation "voice"): hold the key to dictate into the message, let go to stop; Esc cancels. Uses
/// Windows speech recognition (dictation), which needs the microphone permission and Windows' "Online speech
/// recognition" privacy setting. Listening always stops when the panel hides.
/// </summary>
internal sealed partial class ChatPresenter
{
    private const int SpeechPrivacyDeclined = unchecked((int)0x80045509); // SPERR_SPEECH_PRIVACY_POLICY_NOT_ACCEPTED
    private const string VoiceTag = "voice";
    private SpeechRecognizer? _recognizer;
    private Task? _voiceStart;
    private bool _listening;
    private string _voiceDraft = "";
    private string _voiceCommitted = "";

    public void Voice(VoiceCommand command)
    {
        switch (command)
        {
            case VoiceCommand.Start:
                Run("voice start", StartVoiceAsync);
                break;
            case VoiceCommand.Stop:
                Run("voice stop", () => StopVoiceAsync(send: settings.Chat.VoiceAutoSend));
                break;
            default:
                Run("voice toggle", () => _listening ? StopVoiceAsync(send: settings.Chat.VoiceAutoSend) : StartVoiceAsync());
                break;
        }
    }

    private void InitializeVoice() => popup.Hidden += () => { if (_listening) Run("voice cancel", CancelVoiceAsync); };

    private Task StartVoiceAsync()
    {
        if (_listening) return Task.CompletedTask;
        _listening = true;
        return _voiceStart = StartVoiceCoreAsync();
    }

    private async Task StartVoiceCoreAsync()
    {
        try
        {
            var recognizer = _recognizer ?? await CreateRecognizerAsync();
            if (recognizer is null) { _listening = false; return; }
            _recognizer = recognizer;
            _voiceDraft = popup.Input.Text;
            _voiceCommitted = "";
            ShowListening(true);
            await recognizer.ContinuousRecognitionSession.StartAsync();
            log.Info("voice: listening");
        }
        catch (Exception ex)
        {
            _listening = false;
            ShowListening(false);
            VoiceFailed(ex);
        }
    }

    private async Task<SpeechRecognizer?> CreateRecognizerAsync()
    {
        var recognizer = new SpeechRecognizer();
        recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
        var compiled = await recognizer.CompileConstraintsAsync();
        if (compiled.Status != SpeechRecognitionResultStatus.Success)
        {
            recognizer.Dispose();
            log.Error($"voice: speech recognition unavailable ({compiled.Status})");
            Notice(compiled.Status == SpeechRecognitionResultStatus.TopicLanguageNotSupported
                ? "Speech recognition doesn't support your Windows display language for dictation."
                : $"Speech recognition isn't available ({compiled.Status}).", InfoBarSeverity.Warning);
            return null;
        }
        recognizer.HypothesisGenerated += (_, e) =>
            popup.DispatcherQueue.TryEnqueue(() => { if (_listening) ShowDictation(e.Hypothesis.Text); });
        recognizer.ContinuousRecognitionSession.ResultGenerated += (_, e) => popup.DispatcherQueue.TryEnqueue(() =>
        {
            if (!_listening || e.Result.Confidence == SpeechRecognitionConfidence.Rejected) return;
            _voiceCommitted = VoiceText.Append(_voiceCommitted, e.Result.Text);
            ShowDictation("");
        });
        recognizer.ContinuousRecognitionSession.Completed += (_, e) => popup.DispatcherQueue.TryEnqueue(() =>
        {
            if (e.Status is SpeechRecognitionResultStatus.Success or SpeechRecognitionResultStatus.UserCanceled) return;
            log.Error($"voice: session ended ({e.Status})");
            if (_listening)
            {
                _listening = false;
                ShowListening(false);
                Notice(e.Status == SpeechRecognitionResultStatus.MicrophoneUnavailable
                    ? "No microphone is available." : $"Voice input stopped ({e.Status}).", InfoBarSeverity.Warning);
            }
        });
        return recognizer;
    }

    private void ShowDictation(string hypothesis)
    {
        popup.Input.Text = VoiceText.Append(_voiceDraft, VoiceText.Append(_voiceCommitted, hypothesis));
        popup.Input.SelectionStart = popup.Input.Text.Length;
    }

    private async Task StopVoiceAsync(bool send)
    {
        if (_voiceStart is { } starting) await starting;
        if (!_listening || _recognizer is null) return;
        try { await _recognizer.ContinuousRecognitionSession.StopAsync(); } // final results arrive before this completes
        catch (Exception ex) { log.Error("voice: stop failed", ex); }
        _listening = false;
        ShowListening(false);
        popup.Input.Text = VoiceText.Append(_voiceDraft, _voiceCommitted);
        popup.Input.SelectionStart = popup.Input.Text.Length;
        log.Info($"voice: stopped ({_voiceCommitted.Length} chars)");
        if (send && _voiceCommitted.Length > 0 && !chat.IsBusy) await SendAsync();
    }

    private async Task CancelVoiceAsync()
    {
        if (_voiceStart is { } starting) await starting;
        if (!_listening || _recognizer is null) return;
        _listening = false;
        try { await _recognizer.ContinuousRecognitionSession.CancelAsync(); }
        catch (Exception ex) { log.Error("voice: cancel failed", ex); }
        ShowListening(false);
        popup.Input.Text = _voiceDraft;
        log.Info("voice: cancelled");
    }

    private void ShowListening(bool on)
    {
        foreach (var old in popup.NoticesPanel.Children.OfType<InfoBar>().Where(b => VoiceTag.Equals(b.Tag)).ToList())
            popup.NoticesPanel.Children.Remove(old);
        if (!on) return;
        popup.NoticesPanel.Children.Add(new InfoBar
        {
            Tag = VoiceTag, IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational,
            Title = "🎙 Listening…", Message = "Let go of the key to stop. Esc cancels.",
        });
    }

    private void VoiceFailed(Exception ex)
    {
        log.Error("voice: start failed", ex);
        if (ex.HResult == SpeechPrivacyDeclined)
            NoticeWithLink("Voice input needs Windows' online speech recognition.", "Turn it on", "ms-settings:privacy-speech");
        else if (ex is UnauthorizedAccessException)
            NoticeWithLink("Hotline isn't allowed to use the microphone.", "Microphone settings", "ms-settings:privacy-microphone");
        else
            Notice($"Voice input couldn't start: {ex.Message}", InfoBarSeverity.Warning);
    }

    private void NoticeWithLink(string message, string linkText, string uri)
    {
        var link = new HyperlinkButton { Content = linkText, NavigateUri = new Uri(uri) };
        var bar = new InfoBar { IsOpen = true, IsClosable = true, Severity = InfoBarSeverity.Warning, Message = message, ActionButton = link };
        bar.Closed += (_, _) => popup.NoticesPanel.Children.Remove(bar);
        popup.NoticesPanel.Children.Add(bar);
    }

    /// <summary>Self-test (hotline://selftest?voice): can dictation be set up? Never opens the microphone.</summary>
    public async Task<string> VoiceSelfTestAsync()
    {
        try
        {
            using var recognizer = new SpeechRecognizer();
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            return (await recognizer.CompileConstraintsAsync()).Status.ToString();
        }
        catch (Exception ex) { return ex.HResult == SpeechPrivacyDeclined ? "online speech recognition is off" : ex.Message; }
    }

    /// <summary>Esc while listening cancels the dictation (the panel stays open). True when handled.</summary>
    private bool HandleVoiceKey(Windows.System.VirtualKey key)
    {
        if (!_listening || key != Windows.System.VirtualKey.Escape) return false;
        Run("voice cancel", CancelVoiceAsync);
        return true;
    }
}
