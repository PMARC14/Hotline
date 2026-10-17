using System.Runtime.CompilerServices;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends.Codex;

/// <summary>
/// Chat through the user's installed OpenAI Codex CLI (their own ChatGPT sign-in or API key). `codex exec` answers one
/// prompt and exits, so each turn is its own process. With keepCliSessions (default) each Hotline chat is one Codex
/// thread: the thread ID is reported to the chat (which records it) and later turns, after a restart too, resume it
/// (`exec resume`) in the folder it began in and send only the new message. Otherwise sessions are ephemeral and every
/// turn replays the conversation as text. Images go as temporary files, deleted after the turn.
/// </summary>
public sealed class CodexBackend(BackendProfile profile, Func<string?> locateExe, string workspace, ILineProcessFactory processes,
    FileLog log, Func<BackendProfile, string> systemPrompt, string homeDirectory) : IChatBackend, ICliSessionBackend
{
    private ILineProcess? _process;

    public CliSession? Resume { get; set; }

    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        if (conversation.Count == 0 || conversation[^1].Role != ChatRole.User)
            throw new ArgumentException("The conversation must end with a user message.", nameof(conversation));
        var exe = locateExe() ?? throw new BackendException(BackendErrorKind.NotInstalled,
            Strings.Codex_NotInstalled);

        var user = conversation[^1];
        var prior = conversation.Take(conversation.Count - 1).ToList();
        var prompt = systemPrompt(profile);
        var resume = profile.KeepCliSessions && Resume is { } r && CliSession.IsValidId(r.Id) ? r.Id : null;

        var inherit = profile.Tools == ToolMode.Inherit;
        var cwd = inherit ? WorkingDirectory() : homeDirectory;
        if (resume is not null && Resume!.WorkingDirectory is { Length: > 0 } was && Path.IsPathFullyQualified(was) && Directory.Exists(was)) cwd = was; // where it began (never recreate a deleted folder)
        Directory.CreateDirectory(cwd);
        string? reported = null;
        var images = new List<string>();
        try
        {
            foreach (var image in user.Attachments.Where(a => a.Kind == AttachmentKind.Image))
            {
                var dir = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(workspace)!, "images")).FullName;
                var path = Path.Combine(dir, $"{Guid.NewGuid():N}{Extension(image.MimeType)}");
                images.Add(path); // before writing: a cancelled write still gets cleaned up
                await File.WriteAllBytesAsync(path, image.Data, ct);
            }
            var texts = user.Attachments.Where(a => a.Kind == AttachmentKind.Text).Select(a => (a.Name, a.AsText())).ToList();
        attempt:
            var text = CodexProtocol.ComposeText(user.Text, texts, resume is null ? prior : []);
            var args = CodexProtocol.BuildArgs(profile, prompt, resume, images);

            await StopAsync();
            ILineProcess process;
            try { process = _process = processes.Start(exe, args, cwd); }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new BackendException(BackendErrorKind.NotInstalled, L.F(Strings.Codex_StartFailed, exe, ex.Message), ex);
            }
            log.Info($"codex started: {exe} ({(inherit ? "own tools" : "chat only")}, {(resume is null ? "new session" : "resumed")}, model {profile.Model ?? "default"}, effort {profile.Effort ?? "default"})");
            try
            {
                await process.WriteLineAsync(text, ct);
                process.CompleteInput();
            }
            catch (OperationCanceledException) { await StopAsync(); throw; }
            catch (IOException) { /* codex already exited (bad flag, not signed in): its stderr explains, below */ }

            var parser = new CodexTurnParser();
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
                    var reason = parser.LastError ?? (tail.Length > 0 ? tail : null);
                    // Signed out, rate-limited…: say so, resuming or not (not "couldn't resume", and no replay).
                    if (reason is not null && CodexErrors.Map(reason) is { Kind: not BackendErrorKind.Failed } mapped) throw mapped;
                    if (resume is not null && !parser.SawEvent)
                    {
                        // The saved session is gone (deleted, or codex can't read it): start over with the chat as text.
                        log.Info($"codex resume failed, starting a new session: {tail}");
                        if (prior.Count == 0) // Hotline kept only the record: nothing to replay
                            throw new BackendException(BackendErrorKind.Failed, L.F(Strings.Cli_ResumeFailed, profile.Name));
                        resume = null;
                        cwd = inherit ? WorkingDirectory() : homeDirectory;
                        Directory.CreateDirectory(cwd);
                        goto attempt;
                    }
                    throw new BackendException(BackendErrorKind.Failed, L.F(Strings.Cli_Stopped, "Codex") + (reason is null ? "" : " " + reason));
                }
                foreach (var delta in parser.Feed(output)) yield return delta;
                // The thread this chat lives in (a resumed run may not announce it again).
                if (profile.KeepCliSessions && (parser.ThreadId ?? resume) is { } thread && thread != reported && CliSession.IsValidId(thread))
                {
                    reported = thread;
                    yield return new ChatDelta("", Session: new CliSession(thread, cwd));
                }
            }
            await process.WaitForExitAsync(TimeSpan.FromSeconds(3)); // let codex finish saving its session
            await StopAsync();

            if (parser.Error is { } error)
            {
                log.Error($"codex turn failed: {error}");
                throw CodexErrors.Map(error);
            }
        }
        finally
        {
            await StopAsync(); // also when the caller stops reading early: no codex left running, images released
            foreach (var path in images)
                try { File.Delete(path); } catch (IOException) { /* still open; the temp folder is cleaned by Windows */ }
        }
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopAsync()
    {
        var p = Interlocked.Exchange(ref _process, null);
        if (p is not null) await p.DisposeAsync();
    }

    private static string Extension(string mime) => mime switch
    {
        "image/jpeg" => ".jpg",
        "image/gif" => ".gif",
        "image/webp" => ".webp",
        "image/bmp" => ".bmp",
        _ => ".png",
    };

    private string WorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(profile.WorkingDirectory)) return homeDirectory;
        var dir = Environment.ExpandEnvironmentVariables(profile.WorkingDirectory.Trim());
        return Directory.Exists(dir) ? dir : throw new BackendException(BackendErrorKind.NotConfigured,
            L.F(Strings.Cli_NoFolder, profile.Name, dir));
    }
}
