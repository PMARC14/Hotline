using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

/// <summary>Push-to-talk state machine: overlapping start/stop/cancel, late results, sessions that end by themselves.</summary>
public sealed class VoiceSessionTests
{
    private sealed class FakeSpeech : ISpeechSession
    {
        public TaskCompletionSource StartGate { get; } = new();
        public TaskCompletionSource StopGate { get; } = new();
        public int Starts, Stops, Cancels;
        public bool AutoStart = true, AutoStop = true;
        public Task StartAsync() { Starts++; if (AutoStart) StartGate.TrySetResult(); return StartGate.Task; }
        public Task StopAsync() { Stops++; if (AutoStop) StopGate.TrySetResult(); return StopGate.Task; }
        public Task CancelAsync() { Cancels++; return Task.CompletedTask; }
    }

    private static (VoiceSession Voice, FakeSpeech Speech, List<string> Texts, List<VoiceOutcome> Outcomes) New(Func<FakeSpeech?>? make = null)
    {
        var speech = new FakeSpeech();
        var voice = new VoiceSession(() => Task.FromResult<ISpeechSession?>(make is null ? speech : make()));
        var texts = new List<string>();
        var outcomes = new List<VoiceOutcome>();
        voice.TextChanged += texts.Add;
        voice.Finished += outcomes.Add;
        return (voice, speech, texts, outcomes);
    }

    [Fact]
    public async Task Dictation_appends_to_the_draft_and_stop_hands_over_the_text()
    {
        var (voice, _, texts, outcomes) = New();
        await voice.StartAsync("Note:");
        Assert.Equal(VoiceState.Listening, voice.State);
        voice.OnHypothesis("hello wor");
        voice.OnResult("hello world", rejected: false);
        await voice.StopAsync(send: true);
        Assert.Equal(["Note: hello wor", "Note: hello world"], texts);
        Assert.Equal(new VoiceOutcome("Note: hello world", Send: true, Cancelled: false, Dictated: "hello world"), Assert.Single(outcomes));
        Assert.Equal(VoiceState.Idle, voice.State);
    }

    [Fact]
    public async Task Release_before_listening_started_still_stops_once_it_has()
    {
        var (voice, speech, _, outcomes) = New();
        speech.AutoStart = false;
        var starting = voice.StartAsync("");
        var stopping = voice.StopAsync(send: false);
        Assert.Equal(VoiceState.Starting, voice.State);
        speech.StartGate.SetResult();
        await Task.WhenAll(starting, stopping);
        Assert.Equal(1, speech.Stops);
        Assert.Single(outcomes);
    }

    [Fact]
    public async Task A_second_stop_does_nothing()
    {
        var (voice, speech, _, outcomes) = New();
        speech.AutoStop = false;
        await voice.StartAsync("");
        var first = voice.StopAsync(send: true);
        var second = voice.StopAsync(send: true);
        speech.StopGate.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, speech.Stops);
        Assert.Single(outcomes);
    }

    [Fact]
    public async Task Results_that_arrive_while_stopping_count_but_hypotheses_dont()
    {
        var (voice, speech, _, outcomes) = New();
        speech.AutoStop = false;
        await voice.StartAsync("");
        var stopping = voice.StopAsync(send: false);
        voice.OnHypothesis("ignored");
        voice.OnResult("last words", rejected: false);
        speech.StopGate.SetResult();
        await stopping;
        Assert.Equal("last words", Assert.Single(outcomes).Text);
    }

    [Fact]
    public async Task Cancel_during_a_stop_discards_and_never_sends()
    {
        var (voice, speech, _, outcomes) = New();
        speech.AutoStop = false;
        await voice.StartAsync("draft");
        voice.OnResult("spoken", rejected: false);
        var stopping = voice.StopAsync(send: true);
        await voice.CancelAsync();
        speech.StopGate.SetResult();
        await stopping;
        Assert.Equal(new VoiceOutcome("draft", Send: false, Cancelled: true, Dictated: ""), Assert.Single(outcomes));
    }

    [Fact]
    public async Task Cancel_restores_the_draft()
    {
        var (voice, speech, _, outcomes) = New();
        await voice.StartAsync("draft");
        voice.OnResult("spoken", rejected: false);
        await voice.CancelAsync();
        Assert.Equal(1, speech.Cancels);
        Assert.Equal("draft", Assert.Single(outcomes).Text);
        Assert.True(outcomes[0].Cancelled);
    }

    [Fact]
    public async Task A_session_that_ends_by_itself_keeps_the_text_without_sending()
    {
        var (voice, _, _, outcomes) = New();
        await voice.StartAsync("");
        voice.OnResult("before the silence", rejected: false);
        voice.OnEndedByItself();
        Assert.Equal(new VoiceOutcome("before the silence", Send: false, Cancelled: false, Dictated: "before the silence"), Assert.Single(outcomes));
        Assert.Equal(VoiceState.Idle, voice.State);
    }

    [Fact]
    public async Task Rejected_results_are_dropped()
    {
        var (voice, _, _, outcomes) = New();
        await voice.StartAsync("");
        voice.OnResult("mumble", rejected: true);
        await voice.StopAsync(send: true);
        Assert.Equal("", Assert.Single(outcomes).Text);
    }

    [Fact]
    public async Task Start_while_busy_is_ignored()
    {
        var (voice, speech, _, _) = New();
        await voice.StartAsync("");
        await voice.StartAsync("");
        Assert.Equal(1, speech.Starts);
    }

    [Fact]
    public async Task A_failed_start_returns_to_idle_and_reports_it()
    {
        var voice = new VoiceSession(() => Task.FromException<ISpeechSession?>(new UnauthorizedAccessException("mic")));
        Exception? failure = null;
        voice.Failed += ex => failure = ex;
        await voice.StartAsync("");
        Assert.Equal(VoiceState.Idle, voice.State);
        Assert.IsType<UnauthorizedAccessException>(failure);
        var unavailable = new VoiceSession(() => Task.FromResult<ISpeechSession?>(null));
        await unavailable.StartAsync("");
        Assert.Equal(VoiceState.Idle, unavailable.State);
    }

    [Fact]
    public async Task A_failed_stop_keeps_the_text_never_sends_and_reports_after_finishing()
    {
        var order = new List<string>();
        var speech = new FailingStop();
        var voice = new VoiceSession(() => Task.FromResult<ISpeechSession?>(speech));
        voice.Finished += o => order.Add($"finished send={o.Send} text={o.Text}");
        voice.Failed += _ => order.Add("failed");
        await voice.StartAsync("");
        voice.OnResult("spoken", rejected: false);
        await voice.StopAsync(send: true);
        Assert.Equal(["finished send=False text=spoken", "failed"], order); // the warning comes last, so it stays up
    }

    private sealed class FailingStop : ISpeechSession
    {
        public Task StartAsync() => Task.CompletedTask;
        public Task StopAsync() => Task.FromException(new InvalidOperationException("device gone"));
        public Task CancelAsync() => Task.CompletedTask;
    }

    [Fact]
    public async Task Punctuation_joins_without_a_space()
    {
        var (voice, _, _, outcomes) = New();
        await voice.StartAsync("");
        voice.OnResult("hello", rejected: false);
        voice.OnResult(".", rejected: false);
        await voice.StopAsync(send: false);
        Assert.Equal("hello.", Assert.Single(outcomes).Text);
    }
}
