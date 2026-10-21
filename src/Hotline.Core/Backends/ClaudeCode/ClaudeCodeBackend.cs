using System.Runtime.CompilerServices;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends.ClaudeCode;

/// <summary>
/// Chat through the user's installed Claude Code CLI (their own login and plan), one persistent stream-json session
/// per conversation. With keepCliSessions the session is kept in Claude Code's history and a restarted one resumes it
/// (--resume, in the folder it began in). If the session is lost and can't be resumed (cancel, crash, new chat,
/// unknown ID, prompt change without kept sessions) the next turn starts a fresh process and replays the
/// conversation as text. Images go inline as base64 content blocks.
/// </summary>
public sealed class ClaudeCodeBackend(BackendProfile profile, Func<string?> locateExe, ILineProcessFactory processes,
    FileLog log, Func<BackendProfile, string> systemPrompt, string homeDirectory) : IChatBackend, ICliSessionBackend
{
    private ILineProcess? _process;
    private int _knownCount;
    private string? _knownFirstId;
    private string? _startedPrompt;
    private string? _sessionId; // the Claude Code session the running process is in
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
            Strings.Claude_NotInstalled);

        var user = conversation[^1];
        var prior = conversation.Take(conversation.Count - 1).ToList();
        var prompt = systemPrompt(profile);
        var resume = profile.KeepCliSessions && Resume is { } r && CliSession.IsValidId(r.Id) ? r : null;
        var fresh = false;
        var resuming = false;
        if (_process is null || _process.HasExited || prompt != _startedPrompt
            || (resume is not null ? resume.Id != _sessionId : prior.Count != _knownCount || (prior.Count > 0 && prior[0].Id != _knownFirstId)))
        {
            await StartAsync(exe, prompt, resume);
            resuming = resume is not null;
            fresh = !resuming;
        }

        _knownCount = -1; // until this turn succeeds, the session's context is suspect
        _knownFirstId = null;

        var images = user.Attachments.Where(a => a.Kind == AttachmentKind.Image).ToList();
        var texts = user.Attachments.Where(a => a.Kind == AttachmentKind.Text).Select(a => (a.Name, a.AsText())).ToList();

        for (var attempt = 0; ; attempt++)
        {
            var line = ClaudeCodeProtocol.UserLine(ClaudeCodeProtocol.ComposeText(user.Text, texts, fresh ? prior : []), images);
            var process = _process!;
            try { await process.WriteLineAsync(line, ct); }
            catch (OperationCanceledException) { await StopAsync(); throw; }

            var parser = new ClaudeTurnParser();
            var answered = false;
            var resumeFailed = false;
            while (!parser.Completed)
            {
                string? output;
                try { output = await process.ReadLineAsync(ct); }
                catch (OperationCanceledException) { await StopAsync(); throw; }
                if (output is null)
                {
                    ct.ThrowIfCancellationRequested();
                    await process.WaitForExitAsync(TimeSpan.FromSeconds(2));
                    var tail = process.StandardErrorTail;
                    await StopAsync();
                    if (tail.Length > 0 && ClaudeErrors.Map(tail) is { Kind: not BackendErrorKind.Failed } mapped) throw mapped;
                    if (resuming && !answered) { resumeFailed = true; break; } // "No conversation found with session ID"
                    throw new BackendException(BackendErrorKind.Failed, L.F(Strings.Cli_Stopped, "Claude Code") + (tail.Length > 0 ? " " + tail : ""));
                }
                foreach (var delta in parser.Feed(output))
                {
                    answered |= delta.Text.Length > 0;
                    yield return delta;
                }
                if (parser.SessionId is { } sid && sid != _sessionId && CliSession.IsValidId(sid))
                {
                    _sessionId = sid;
                    yield return new ChatDelta("", Session: new CliSession(sid, _cwd));
                }
            }
            if (!resumeFailed && resuming && parser.Error is { } resumeError && !answered
                && ClaudeErrors.Map(resumeError) is { Kind: BackendErrorKind.Failed }) resumeFailed = true;

            if (resumeFailed)
            {
                log.Info($"claude couldn't resume session {resume!.Id}; starting a new one");
                await StopAsync();
                if (attempt > 0 || prior.Count == 0) // nothing to replay (Hotline kept only the record)
                    throw new BackendException(BackendErrorKind.Failed, L.F(Strings.Cli_ResumeFailed, profile.Name));
                await StartAsync(exe, prompt, null);
                resuming = false;
                fresh = true;
                continue;
            }
            if (parser.Error is { } error)
            {
                log.Error($"claude turn failed: {error}");
                throw ClaudeErrors.Map(error);
            }
            break;
        }
        _knownCount = conversation.Count + 1;
        _knownFirstId = conversation[0].Id;
    }

    private async Task StartAsync(string exe, string prompt, CliSession? resume)
    {
        await StopAsync();
        var inherit = profile.Tools == ToolMode.Inherit;
        var cwd = inherit ? WorkingDirectory() : homeDirectory; // chat only has no tools; home: `claude --resume` there finds it
        if (resume is { WorkingDirectory: { Length: > 0 } was } && Hotline.Core.Platform.SafeDirectory.IsLocal(was)) cwd = was; // Claude Code finds sessions by folder (recreated below if gone)
        Directory.CreateDirectory(cwd);
        var args = ClaudeCodeProtocol.BuildArgs(profile, prompt).ToList();
        if (resume is not null) args.AddRange(["--resume", resume.Id]);
        _process = processes.Start(exe, args, cwd);
        _startedPrompt = prompt;
        _cwd = cwd;
        _sessionId = null;
        log.Info($"claude started: {exe} ({(inherit ? "own tools" : "chat only")}, model {profile.Model ?? "default"}, effort {profile.Effort ?? "default"}{(resume is null ? "" : ", resuming")})");
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopAsync()
    {
        var p = Interlocked.Exchange(ref _process, null);
        if (p is not null) await p.DisposeAsync();
    }

    private string WorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(profile.WorkingDirectory)) return homeDirectory;
        var dir = Environment.ExpandEnvironmentVariables(profile.WorkingDirectory.Trim());
        return Directory.Exists(dir) ? dir : throw new BackendException(BackendErrorKind.NotConfigured,
            L.F(Strings.Cli_NoFolder, profile.Name, dir));
    }
}
