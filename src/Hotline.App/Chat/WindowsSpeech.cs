using Hotline.Core.Chat;
using Microsoft.UI.Dispatching;
using Windows.Media.SpeechRecognition;

namespace Hotline.App.Chat;

/// <summary>
/// Windows speech recognition (continuous dictation) as an <see cref="ISpeechSession"/>. Its events are raised on
/// the UI thread; <see cref="StopAsync"/> returns only after results recognized before the stop were raised.
/// </summary>
internal sealed class WindowsSpeech : ISpeechSession, IDisposable
{
    /// <summary>SPERR_SPEECH_PRIVACY_POLICY_NOT_ACCEPTED: Windows' "Online speech recognition" is off.</summary>
    public const int PrivacyDeclined = unchecked((int)0x80045509);

    private readonly SpeechRecognizer _recognizer;
    private readonly DispatcherQueue _ui;

    public event Action<string>? Hypothesis;
    public event Action<string, bool>? Result;
    /// <summary>The session ended without being asked to (silence timeout, device change, error); the status says which.</summary>
    public event Action<SpeechRecognitionResultStatus>? Ended;

    private WindowsSpeech(SpeechRecognizer recognizer, DispatcherQueue ui)
    {
        (_recognizer, _ui) = (recognizer, ui);
        // Long pauses are normal while thinking; the key (or Esc) ends the session.
        recognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(5);
        recognizer.HypothesisGenerated += (_, e) => _ui.TryEnqueue(() => Hypothesis?.Invoke(e.Hypothesis.Text));
        recognizer.ContinuousRecognitionSession.ResultGenerated += (_, e) =>
            _ui.TryEnqueue(() => Result?.Invoke(e.Result.Text, e.Result.Confidence == SpeechRecognitionConfidence.Rejected));
        recognizer.ContinuousRecognitionSession.Completed += (_, e) => _ui.TryEnqueue(() => Ended?.Invoke(e.Status));
    }

    /// <summary>A dictation recognizer, or null with the reason when Windows can't provide one.</summary>
    public static async Task<(WindowsSpeech? Speech, string? Problem)> CreateAsync(DispatcherQueue ui)
    {
        var recognizer = new SpeechRecognizer();
        try
        {
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            var compiled = await recognizer.CompileConstraintsAsync();
            if (compiled.Status == SpeechRecognitionResultStatus.Success) return (new WindowsSpeech(recognizer, ui), null);
            recognizer.Dispose();
            return (null, compiled.Status == SpeechRecognitionResultStatus.TopicLanguageNotSupported
                ? "Speech recognition doesn't support your Windows display language for dictation."
                : $"Speech recognition isn't available ({compiled.Status}).");
        }
        catch
        {
            recognizer.Dispose();
            throw;
        }
    }

    public async Task StartAsync() => await _recognizer.ContinuousRecognitionSession.StartAsync();

    public async Task StopAsync()
    {
        await _recognizer.ContinuousRecognitionSession.StopAsync();
        // Results raised during the stop are queued on the UI thread; let them run before the caller finishes.
        var drained = new TaskCompletionSource();
        if (!_ui.TryEnqueue(DispatcherQueuePriority.Low, () => drained.TrySetResult())) drained.TrySetResult();
        await drained.Task;
    }

    public async Task CancelAsync() => await _recognizer.ContinuousRecognitionSession.CancelAsync();

    public void Dispose() => _recognizer.Dispose();

    /// <summary>Can dictation be set up? Never opens the microphone (self-test).</summary>
    public static async Task<string> CheckAsync()
    {
        try
        {
            using var recognizer = new SpeechRecognizer();
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            return (await recognizer.CompileConstraintsAsync()).Status.ToString();
        }
        catch (Exception ex) { return ex.HResult == PrivacyDeclined ? "online speech recognition is off" : ex.Message; }
    }
}
