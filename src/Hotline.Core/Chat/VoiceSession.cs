namespace Hotline.Core.Chat;

public enum VoiceState { Idle, Starting, Listening, Stopping }

/// <summary>
/// The platform speech recognizer, as push-to-talk needs it. <see cref="StopAsync"/> completes only after every
/// result recognized before the stop has been delivered to the session.
/// </summary>
public interface ISpeechSession
{
    Task StartAsync();
    Task StopAsync();
    Task CancelAsync();
}

/// <summary>How a dictation ended: the composer text, whether to send it, and what was dictated.</summary>
public sealed record VoiceOutcome(string Text, bool Send, bool Cancelled, string Dictated);

/// <summary>
/// Push-to-talk as a state machine (idle → starting → listening → stopping): one owner of the draft and the dictated
/// text, so overlapping start/stop/cancel commands, late results and sessions that end by themselves can't interleave.
/// Call everything from one thread (the UI thread); <see cref="ISpeechSession"/> events are marshalled there first.
/// </summary>
public sealed class VoiceSession(Func<Task<ISpeechSession?>> createSpeech)
{
    private ISpeechSession? _speech;
    private Task _starting = Task.CompletedTask;
    private string _draft = "";
    private string _committed = "";
    private bool _discard;
    private bool _send;

    public VoiceState State { get; private set; }

    /// <summary>The composer text while dictating (draft + what was heard so far).</summary>
    public event Action<string>? TextChanged;
    public event Action<VoiceOutcome>? Finished;
    /// <summary>Starting or stopping failed (microphone denied, privacy setting off, device gone…).</summary>
    public event Action<Exception>? Failed;

    public Task StartAsync(string draft)
    {
        if (State != VoiceState.Idle) return Task.CompletedTask;
        (State, _draft, _committed, _discard, _send) = (VoiceState.Starting, draft, "", false, false);
        return _starting = StartCoreAsync();
    }

    private async Task StartCoreAsync()
    {
        try
        {
            _speech = await createSpeech();
            if (_speech is null) { State = VoiceState.Idle; return; } // unavailable; the platform side has said why
            await _speech.StartAsync();
            if (State == VoiceState.Starting) State = VoiceState.Listening;
        }
        catch (Exception ex)
        {
            State = VoiceState.Idle;
            _speech = null;
            Failed?.Invoke(ex);
        }
    }

    /// <summary>Ends the dictation keeping what was said; <paramref name="send"/> sends it when anything was dictated.</summary>
    public async Task StopAsync(bool send)
    {
        if (State == VoiceState.Starting) await _starting;
        if (State != VoiceState.Listening) return;
        (State, _send) = (VoiceState.Stopping, send);
        Exception? failure = null;
        try { await _speech!.StopAsync(); }
        catch (Exception ex) { failure = ex; _send = false; } // keep what was heard, but don't send after a failure
        Finish();
        if (failure is not null) Failed?.Invoke(failure); // after finishing, so its warning isn't cleared
    }

    /// <summary>Ends the dictation and puts the draft back (also during a stop that's still finishing).</summary>
    public async Task CancelAsync()
    {
        if (State == VoiceState.Starting) await _starting;
        if (State == VoiceState.Stopping) { _discard = true; return; }
        if (State != VoiceState.Listening) return;
        (State, _discard) = (VoiceState.Stopping, true);
        Exception? failure = null;
        try { await _speech!.CancelAsync(); }
        catch (Exception ex) { failure = ex; }
        Finish();
        if (failure is not null) Failed?.Invoke(failure);
    }

    public void OnHypothesis(string text)
    {
        if (State == VoiceState.Listening) TextChanged?.Invoke(Compose(text));
    }

    /// <summary>A recognized phrase; results that arrive while stopping still count (the last phrase often does).</summary>
    public void OnResult(string text, bool rejected)
    {
        if (State is not (VoiceState.Listening or VoiceState.Stopping)) return;
        if (!rejected) _committed = VoiceText.Append(_committed, text);
        TextChanged?.Invoke(Compose(""));
    }

    /// <summary>The recognizer stopped on its own (silence, device change): keep the text, don't send.</summary>
    public void OnEndedByItself()
    {
        if (State != VoiceState.Listening) return;
        _send = false;
        Finish();
    }

    private string Compose(string hypothesis) => VoiceText.Append(_draft, VoiceText.Append(_committed, hypothesis));

    private void Finish()
    {
        if (State == VoiceState.Idle) return;
        State = VoiceState.Idle;
        var outcome = _discard
            ? new VoiceOutcome(_draft, Send: false, Cancelled: true, Dictated: "")
            : new VoiceOutcome(Compose(""), Send: _send && _committed.Length > 0, Cancelled: false, Dictated: _committed);
        Finished?.Invoke(outcome);
    }
}
