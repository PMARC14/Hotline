using Hotline.Core.Chat;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Media.SpeechRecognition;

namespace Hotline.App.Chat;

/// <summary>
/// Push-to-talk (activation "voice"): hold the key to dictate into the message, let go to stop; Esc cancels. Uses
/// Windows speech recognition (dictation), which needs the microphone permission and Windows' "Online speech
/// recognition" privacy setting. Listening always stops when the panel hides (the dictated text is kept, not sent).
/// One explicit state, so overlapping start/stop/cancel commands can't interleave.
/// </summary>
internal sealed partial class ChatPresenter
{
    private const int SpeechPrivacyDeclined = unchecked((int)0x80045509); // SPERR_SPEECH_PRIVACY_POLICY_NOT_ACCEPTED
    private const string VoiceTag = "voice";

    private enum VoiceState { Idle, Starting, Listening, Stopping }

    private VoiceState _voiceState;
    private SpeechRecognizer? _recognizer;
    private Task _voiceStart = Task.CompletedTask;
    private bool _voiceDiscard;
    private string _voiceDraft = "";
    private string _voiceCommitted = "";

    public void Voice(VoiceCommand command)
    {
        switch (command)
        {
            case VoiceCommand.Start:
                Run("voice start", () => StartVoiceAsync(holding: true));
                break;
            case VoiceCommand.Stop:
                Run("voice stop", () => StopVoiceAsync(send: settings.Chat.VoiceAutoSend));
                break;
            default:
                Run("voice toggle", () => _voiceState == VoiceState.Idle
                    ? StartVoiceAsync(holding: false)
                    : StopVoiceAsync(send: settings.Chat.VoiceAutoSend));
                break;
        }
    }

    private void InitializeVoice() =>
        popup.Hidden += () => { if (_voiceState is VoiceState.Starting or VoiceState.Listening) Run("voice stop (hidden)", () => StopVoiceAsync(send: false)); };

    private Task StartVoiceAsync(bool holding)
    {
        if (_voiceState != VoiceState.Idle) return Task.CompletedTask;
        _voiceState = VoiceState.Starting;
        return _voiceStart = StartVoiceCoreAsync(holding);
    }

    private async Task StartVoiceCoreAsync(bool holding)
    {
        try
        {
            _recognizer ??= await CreateRecognizerAsync();
            if (_recognizer is null) { _voiceState = VoiceState.Idle; return; }
            _voiceDraft = popup.Input.Text;
            _voiceCommitted = "";
            _voiceDiscard = false;
            popup.Input.IsReadOnly = true; // typing now would be overwritten by the dictation
            ShowListening(holding ? "Let go of the key to stop. Esc cancels." : "Press the key again to stop. Esc cancels.");
            await _recognizer.ContinuousRecognitionSession.StartAsync();
            _voiceState = VoiceState.Listening;
            log.Info("voice: listening");
        }
        catch (Exception ex)
        {
            _voiceState = VoiceState.Idle;
            popup.Input.IsReadOnly = false;
            ShowListening(null);
            ResetRecognizer(); // a fresh one next time (e.g. after a device change)
            VoiceFailed(ex);
        }
    }

    private async Task<SpeechRecognizer?> CreateRecognizerAsync()
    {
        var recognizer = new SpeechRecognizer();
        try
        {
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            var compiled = await recognizer.CompileConstraintsAsync();
            if (compiled.Status != SpeechRecognitionResultStatus.Success)
            {
                recognizer.Dispose();
                log.Error($"voice: speech recognition unavailable ({compiled.Status})");
                VoiceNotice(compiled.Status == SpeechRecognitionResultStatus.TopicLanguageNotSupported
                    ? "Speech recognition doesn't support your Windows display language for dictation."
                    : $"Speech recognition isn't available ({compiled.Status}).");
                return null;
            }
        }
        catch
        {
            recognizer.Dispose();
            throw;
        }
        // Long pauses are normal while thinking; the key (or Esc) ends the session.
        recognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(5);
        recognizer.HypothesisGenerated += (_, e) =>
            popup.DispatcherQueue.TryEnqueue(() => { if (_voiceState == VoiceState.Listening) ShowDictation(e.Hypothesis.Text); });
        recognizer.ContinuousRecognitionSession.ResultGenerated += (_, e) => popup.DispatcherQueue.TryEnqueue(() =>
        {
            // Results that arrive while stopping still count (the last phrase is often finalized then).
            if (_voiceState is not (VoiceState.Listening or VoiceState.Stopping)) return;
            if (e.Result.Confidence != SpeechRecognitionConfidence.Rejected) _voiceCommitted = VoiceText.Append(_voiceCommitted, e.Result.Text);
            ShowDictation("");
        });
        recognizer.ContinuousRecognitionSession.Completed += (_, e) => popup.DispatcherQueue.TryEnqueue(() =>
        {
            if (_voiceState != VoiceState.Listening) return; // our own stop/cancel
            // The session ended by itself (silence, device change, error): keep what was said, don't send.
            log.Info($"voice: session ended by itself ({e.Status})");
            FinishVoice(send: false);
            if (e.Status is not (SpeechRecognitionResultStatus.Success or SpeechRecognitionResultStatus.TimeoutExceeded or SpeechRecognitionResultStatus.UserCanceled))
            {
                ResetRecognizer();
                VoiceNotice(e.Status == SpeechRecognitionResultStatus.MicrophoneUnavailable ? "No microphone is available." : $"Voice input stopped ({e.Status}).");
            }
        });
        return recognizer;
    }

    private void ResetRecognizer()
    {
        try { _recognizer?.Dispose(); } catch (Exception ex) { log.Error("voice: dispose failed", ex); }
        _recognizer = null;
    }

    private void ShowDictation(string hypothesis)
    {
        popup.Input.Text = VoiceText.Append(_voiceDraft, VoiceText.Append(_voiceCommitted, hypothesis));
        popup.Input.SelectionStart = popup.Input.Text.Length;
    }

    private async Task StopVoiceAsync(bool send)
    {
        if (_voiceState == VoiceState.Starting) await _voiceStart;
        if (_voiceState != VoiceState.Listening || _recognizer is null) return;
        _voiceState = VoiceState.Stopping;
        try { await _recognizer.ContinuousRecognitionSession.StopAsync(); }
        catch (Exception ex) { log.Error("voice: stop failed", ex); }
        // Results raised during the stop are queued on the dispatcher; finish after them.
        popup.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => FinishVoice(send));
    }

    private async Task CancelVoiceAsync()
    {
        if (_voiceState == VoiceState.Starting) await _voiceStart;
        _voiceDiscard = true;
        if (_voiceState != VoiceState.Listening || _recognizer is null) return; // a stop in progress now discards
        _voiceState = VoiceState.Stopping;
        try { await _recognizer.ContinuousRecognitionSession.CancelAsync(); }
        catch (Exception ex) { log.Error("voice: cancel failed", ex); }
        FinishVoice(send: false);
    }

    private void FinishVoice(bool send)
    {
        if (_voiceState == VoiceState.Idle) return;
        _voiceState = VoiceState.Idle;
        popup.Input.IsReadOnly = false;
        ShowListening(null);
        popup.Input.Text = _voiceDiscard ? _voiceDraft : VoiceText.Append(_voiceDraft, _voiceCommitted);
        popup.Input.SelectionStart = popup.Input.Text.Length;
        log.Info(_voiceDiscard ? "voice: cancelled" : $"voice: stopped ({_voiceCommitted.Length} chars)");
        if (send && !_voiceDiscard && _voiceCommitted.Length > 0 && !chat.IsBusy) Run("send", SendAsync);
    }

    /// <summary>Shows the listening bar with <paramref name="hint"/>, or removes it (and old voice notices) for null.</summary>
    private void ShowListening(string? hint)
    {
        RemoveVoiceBars();
        if (hint is null) return;
        popup.NoticesPanel.Children.Add(new InfoBar
        {
            Tag = VoiceTag, IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational, Title = "🎙 Listening…", Message = hint,
        });
    }

    private void RemoveVoiceBars()
    {
        foreach (var old in popup.NoticesPanel.Children.OfType<InfoBar>().Where(b => VoiceTag.Equals(b.Tag)).ToList())
            popup.NoticesPanel.Children.Remove(old);
    }

    private void VoiceFailed(Exception ex)
    {
        log.Error("voice: start failed", ex);
        if (ex.HResult == SpeechPrivacyDeclined)
            VoiceNotice("Voice input needs Windows' online speech recognition.", ("Turn it on", "ms-settings:privacy-speech"));
        else if (ex is UnauthorizedAccessException)
            VoiceNotice("Hotline isn't allowed to use the microphone.", ("Microphone settings", "ms-settings:privacy-microphone"));
        else
            VoiceNotice($"Voice input couldn't start: {ex.Message}");
    }

    /// <summary>A voice warning (replacing any earlier one), optionally with a button that opens a Settings page.</summary>
    private void VoiceNotice(string message, (string Text, string Uri)? link = null)
    {
        RemoveVoiceBars();
        var bar = new InfoBar { Tag = VoiceTag, IsOpen = true, IsClosable = true, Severity = InfoBarSeverity.Warning, Message = message };
        if (link is { } l)
        {
            var button = new Button { Content = l.Text };
            button.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(new Uri(l.Uri));
            bar.ActionButton = button;
        }
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
        if (_voiceState == VoiceState.Idle || key != Windows.System.VirtualKey.Escape) return false;
        Run("voice cancel", CancelVoiceAsync);
        return true;
    }
}
