using System.Runtime.CompilerServices;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.ClaudeCode;

/// <summary>
/// Chat through the user's installed Claude Code CLI (their own login and plan), one persistent stream-json session
/// per conversation. If the session is lost (cancel, crash, new chat, prompt change) the next turn starts a fresh
/// process and replays the conversation as text. Images go inline as base64 content blocks.
/// </summary>
public sealed class ClaudeCodeBackend(BackendProfile profile, Func<string?> locateExe, string workspace, ILineProcessFactory processes,
    FileLog log, Func<BackendProfile, string> systemPrompt, string homeDirectory) : IChatBackend
{
    private ILineProcess? _process;
    private int _knownCount;
    private string? _knownFirstId;
    private string? _startedPrompt;

    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        if (conversation.Count == 0 || conversation[^1].Role != ChatRole.User)
            throw new ArgumentException("The conversation must end with a user message.", nameof(conversation));
        var exe = locateExe() ?? throw new BackendException(BackendErrorKind.NotInstalled,
            "Claude Code isn't installed. Install it from claude.com/claude-code, run `claude` once to sign in, then try again.");

        var user = conversation[^1];
        var prior = conversation.Take(conversation.Count - 1).ToList();
        var prompt = systemPrompt(profile);
        var fresh = false;
        if (_process is null || _process.HasExited || prior.Count != _knownCount || (prior.Count > 0 && prior[0].Id != _knownFirstId)
            || prompt != _startedPrompt)
        {
            await StopAsync();
            var inherit = profile.Tools == ToolMode.Inherit;
            var cwd = inherit ? WorkingDirectory() : workspace;
            Directory.CreateDirectory(cwd);
            var args = ClaudeCodeProtocol.BuildArgs(profile, prompt);
            _process = processes.Start(exe, args, cwd);
            _startedPrompt = prompt;
            fresh = true;
            log.Info($"claude started: {exe} ({(inherit ? "own tools" : "chat only")}, model {profile.Model ?? "default"}, effort {profile.Effort ?? "default"})");
        }

        _knownCount = -1; // until this turn succeeds, the session's context is suspect
        _knownFirstId = null;

        var images = user.Attachments.Where(a => a.Kind == AttachmentKind.Image).ToList();
        var texts = user.Attachments.Where(a => a.Kind == AttachmentKind.Text).Select(a => (a.Name, a.AsText())).ToList();
        var line = ClaudeCodeProtocol.UserLine(ClaudeCodeProtocol.ComposeText(user.Text, texts, fresh ? prior : []), images);

        var process = _process;
        try { await process.WriteLineAsync(line, ct); }
        catch (OperationCanceledException) { await StopAsync(); throw; }

        var parser = new ClaudeTurnParser();
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
                throw new BackendException(BackendErrorKind.Failed, "Claude Code stopped unexpectedly." + (tail.Length > 0 ? " " + tail : ""));
            }
            foreach (var delta in parser.Feed(output)) yield return delta;
        }

        if (parser.Error is { } error)
        {
            log.Error($"claude turn failed: {error}");
            throw ClaudeErrors.Map(error);
        }
        _knownCount = conversation.Count + 1;
        _knownFirstId = conversation[0].Id;
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
            $"The working folder for {profile.Name} doesn't exist: {dir}. Pick another one in Settings › AI connections.");
    }
}
