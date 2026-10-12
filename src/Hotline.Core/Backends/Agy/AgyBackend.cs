using System.Runtime.CompilerServices;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// Chat through the user's installed Antigravity CLI, kept running as one persistent stream-json session per
/// conversation (agy holds the context). agy keeps every conversation in its own history and reports its ID; with
/// keepCliSessions a restarted session resumes it (--conversation) instead of replaying. If the session is lost and
/// can't be resumed (cancel, crash, new chat, unknown ID) the next turn starts a new conversation and replays the
/// transcript Hotline has.
/// </summary>
public sealed class AgyBackend(BackendProfile profile, Func<string?> locateExe, AgyWorkspace workspace, ILineProcessFactory processes, FileLog log,
    Func<BackendProfile, string> systemPrompt, string homeDirectory, AgyProjects? projects = null)
    : IChatBackend, ICliSessionBackend
{
    private ILineProcess? _process;
    private int _knownCount;
    private string? _knownFirstId;
    private string? _startedPrompt;
    private string? _sessionId; // the agy conversation the running process is in
    private string _cwd = "";

    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);
    public CliSession? Resume { get; set; }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        if (conversation.Count == 0 || conversation[^1].Role != ChatRole.User)
            throw new ArgumentException("The conversation must end with a user message.", nameof(conversation));
        var exe = locateExe() ?? throw new BackendException(BackendErrorKind.NotInstalled,
            Strings.Agy_NotInstalledBackend);

        var user = conversation[^1];
        var prior = conversation.Take(conversation.Count - 1).ToList();
        var currentPrompt = systemPrompt(profile);
        var resume = profile.KeepCliSessions && Resume is { } r && CliSession.IsValidId(r.Id) ? r : null;
        var fresh = false;
        var resuming = false;
        if (_process is null || _process.HasExited || currentPrompt != _startedPrompt // prompt file edited / switched: restart so it applies now
            || (resume is not null ? resume.Id != _sessionId : prior.Count != _knownCount || (prior.Count > 0 && prior[0].Id != _knownFirstId)))
        {
            await StartAsync(exe, currentPrompt, resume);
            resuming = resume is not null;
            fresh = !resuming;
        }

        // Until this turn succeeds, the process's context is suspect (a failed/cancelled prompt may be in it):
        // force the next turn to start fresh (or resume) rather than reuse it.
        _knownCount = -1;
        _knownFirstId = null;

        IReadOnlyList<string> images = workspace.SaveImages(user.Id, user.Attachments.Where(a => a.Kind == AttachmentKind.Image).ToList());
        var texts = user.Attachments.Where(a => a.Kind == AttachmentKind.Text).Select(a => (a.Name, a.AsText())).ToList();
        var inheritMode = profile.Tools == ToolMode.Inherit;
        if (inheritMode) images = images.Select(p => Path.Combine(workspace.Root, p.Replace('/', Path.DirectorySeparatorChar))).ToList();

        for (var attempt = 0; ; attempt++)
        {
            var prompt = AgyProtocol.ComposePrompt(user.Text, images, texts, fresh ? prior : Array.Empty<ChatMessage>(),
                fresh && inheritMode ? currentPrompt : null);
            var process = _process!;
            // agy has no "cancel turn" message, so cancelling kills the process (the next turn resumes or replays).
            // Stop explicitly where cancellation is observed: a ct.Register callback can be unregistered before it
            // runs when the pending read's own cancellation completes first (LIFO callbacks + inline continuations).
            try { await process.WriteLineAsync(AgyProtocol.UserLine(prompt), ct); }
            catch (OperationCanceledException) { await StopAsync(); throw; }

            var parser = new AgyTurnParser();
            var answered = false;
            var resumeFailed = false;
            while (!parser.Completed)
            {
                string? line;
                try { line = await process.ReadLineAsync(ct); }
                catch (OperationCanceledException) { await StopAsync(); throw; }
                if (line is null)
                {
                    ct.ThrowIfCancellationRequested();
                    await process.WaitForExitAsync(TimeSpan.FromSeconds(2)); // let the final stderr arrive
                    var tail = process.StandardErrorTail;
                    await StopAsync();
                    var mapped = tail.Length > 0 ? AgyErrors.Map(tail) : null;
                    if (mapped is { Kind: not BackendErrorKind.Failed }) throw mapped; // sign-in, quota…: say so, resuming or not
                    if (resuming && !answered) { resumeFailed = true; break; }
                    throw new BackendException(BackendErrorKind.Failed, L.F(Strings.Cli_Stopped, "agy") + (tail.Length > 0 ? " " + tail : ""));
                }
                foreach (var delta in parser.Feed(line))
                {
                    answered |= delta.Text.Length > 0;
                    yield return delta;
                }
                if (parser.SessionId is { } sid && sid != _sessionId)
                {
                    // An unknown ID makes agy start a new conversation (with a warning): no context, so don't use it.
                    if (resuming && sid != resume!.Id) { resumeFailed = true; break; }
                    _sessionId = sid;
                    if (CliSession.IsValidId(sid)) yield return new ChatDelta("", Session: new CliSession(sid, _cwd));
                }
            }
            if (!resumeFailed && resuming && parser.Error is not null && !answered) resumeFailed = true;

            if (resumeFailed)
            {
                log.Info($"agy couldn't resume conversation {resume!.Id}; starting a new one");
                await StopAsync();
                if (attempt > 0 || prior.Count == 0) // nothing to replay (Hotline kept only the record)
                    throw new BackendException(BackendErrorKind.Failed, L.F(Strings.Cli_ResumeFailed, profile.Name));
                await StartAsync(exe, currentPrompt, null);
                resuming = false;
                fresh = true;
                continue;
            }
            if (parser.Error is { } error)
            {
                log.Error($"agy turn failed: {error}");
                throw AgyErrors.Map(error);
            }
            break;
        }
        _knownCount = conversation.Count + 1; // + the assistant reply the controller is about to append
        _knownFirstId = conversation[0].Id;
    }

    private async Task StartAsync(string exe, string prompt, CliSession? resume)
    {
        await StopAsync();
        workspace.Ensure(prompt);
        _startedPrompt = prompt;
        var inherit = profile.Tools == ToolMode.Inherit;
        var cwd = inherit ? WorkingDirectory() : workspace.Root;
        if (resume is { WorkingDirectory: { Length: > 0 } was } && Directory.Exists(was)) cwd = was; // where it began
        var args = AgyProtocol.BuildArgs(profile, inherit ? workspace.Root : null).ToList();
        if (resume is not null) args.AddRange(["--conversation", resume.Id]);
        // Group new chat-only conversations in the "Hotline" project; leave the user's own folders (Inherit) alone.
        else if (!inherit && projects is not null) args.AddRange(projects.ArgsFor(cwd));
        _process = processes.Start(exe, args, cwd);
        _cwd = cwd;
        _sessionId = null;
        log.Info($"agy started: {exe} {string.Join(' ', args)}");
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopAsync()
    {
        var p = Interlocked.Exchange(ref _process, null);
        if (p is not null) await p.DisposeAsync();
    }

    /// <summary>Inherit mode's folder: the configured one (env vars expanded) or, when none is set, the user folder.</summary>
    private string WorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(profile.WorkingDirectory)) return homeDirectory;
        var dir = Environment.ExpandEnvironmentVariables(profile.WorkingDirectory.Trim());
        return Directory.Exists(dir) ? dir : throw new BackendException(BackendErrorKind.NotConfigured,
            L.F(Strings.Cli_NoFolder, profile.Name, dir));
    }
}
