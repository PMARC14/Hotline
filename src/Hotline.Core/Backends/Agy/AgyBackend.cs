using System.Runtime.CompilerServices;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// Chat through the user's installed Antigravity CLI, kept running as one persistent stream-json
/// session per conversation (agy holds the context). If the session is lost (cancel, crash, new chat,
/// backend switch) the next turn starts a fresh process and replays the conversation as a transcript.
/// </summary>
public sealed class AgyBackend(BackendProfile profile, Func<string?> locateExe, AgyWorkspace workspace, ILineProcessFactory processes, FileLog log)
    : IChatBackend
{
    private ILineProcess? _process;
    private int _knownCount;
    private string? _knownFirstId;

    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        if (conversation.Count == 0 || conversation[^1].Role != ChatRole.User)
            throw new ArgumentException("The conversation must end with a user message.", nameof(conversation));
        var exe = locateExe() ?? throw new BackendException(BackendErrorKind.NotInstalled,
            "The Antigravity CLI (agy) isn't installed. Install it from antigravity.google/cli, then try again.");

        var user = conversation[^1];
        var prior = conversation.Take(conversation.Count - 1).ToList();
        var fresh = false;
        if (_process is null || _process.HasExited || prior.Count != _knownCount || (prior.Count > 0 && prior[0].Id != _knownFirstId))
        {
            await StopAsync();
            workspace.Ensure();
            var args = AgyProtocol.BuildArgs(profile);
            _process = processes.Start(exe, args, workspace.Root);
            fresh = true;
            log.Info($"agy started: {exe} {string.Join(' ', args)}");
        }

        var images = workspace.SaveImages(user.Id, user.Attachments.Where(a => a.Kind == AttachmentKind.Image).ToList());
        var texts = user.Attachments.Where(a => a.Kind == AttachmentKind.Text).Select(a => (a.Name, a.AsText())).ToList();
        var prompt = AgyProtocol.ComposePrompt(user.Text, images, texts, fresh ? prior : Array.Empty<ChatMessage>());

        var process = _process;
        // agy has no "cancel turn" message, so cancelling kills the process (the next turn replays context).
        // Stop explicitly where cancellation is observed: a ct.Register callback can be unregistered before it
        // runs when the pending read's own cancellation completes first (LIFO callbacks + inline continuations).
        try { await process.WriteLineAsync(AgyProtocol.UserLine(prompt), ct); }
        catch (OperationCanceledException) { await StopAsync(); throw; }

        var parser = new AgyTurnParser();
        while (!parser.Completed)
        {
            string? line;
            try { line = await process.ReadLineAsync(ct); }
            catch (OperationCanceledException) { await StopAsync(); throw; }
            if (line is null)
            {
                ct.ThrowIfCancellationRequested();
                var tail = process.StandardErrorTail;
                await StopAsync();
                throw new BackendException(BackendErrorKind.Failed, "agy stopped unexpectedly." + (tail.Length > 0 ? " " + tail : ""));
            }
            foreach (var delta in parser.Feed(line)) yield return delta;
        }

        if (parser.Error is { } error)
        {
            log.Error($"agy turn failed: {error}");
            throw AgyErrors.Map(error);
        }
        _knownCount = conversation.Count + 1; // + the assistant reply the controller is about to append
        _knownFirstId = conversation[0].Id;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopAsync()
    {
        var p = Interlocked.Exchange(ref _process, null);
        if (p is not null) await p.DisposeAsync();
    }
}
