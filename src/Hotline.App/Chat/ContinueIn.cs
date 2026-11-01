using System.Diagnostics;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Backends.ClaudeCode;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.App.Chat;

/// <summary>
/// "Continue this chat in ▸": the chat's own CLI resumes its conversation in a terminal window; another installed CLI
/// starts a new session that reads the chat from a handoff file (only when the panel has the messages). The program
/// is started as a normal console program, so Windows opens it in the user's default terminal (Windows Terminal unless
/// they picked another in Settings › System › For developers). After them come the user's own targets (web pages, Open
/// WebUI, apps): the chat's connection's list, or the shared one (ContinueTargets).
/// </summary>
internal sealed class ContinueIn(ChatController chat, HotlineSettings settings, string handoffDirectory, Func<string, ValueTask> stopBackend,
    FileLog log, ISecretStore secrets, HttpClient http, Action<string> notify)
{
    /// <summary>One menu entry; <paramref name="More"/>: listed under "More…".</summary>
    public sealed record Target(string Label, Func<Task> Run, bool More = false);

    public IReadOnlyList<Target> Targets()
    {
        // The chat's own CLI comes from its connection, with or without a conversation yet (CliHandoff.Choices). Its
        // CLI conversation counts only while the chat is still on that connection: after a switch, later turns aren't in
        // it, so the chat goes over as a handoff instead.
        var own = chat.CurrentSession is { } s && s.BackendId == chat.BackendId ? s : ((CliSession Session, string BackendId)?)null;
        var profile = Profile(chat.BackendId);
        var exes = new Dictionary<BackendType, (BackendProfile Profile, string Exe)>();
        foreach (var type in new[] { BackendType.ClaudeCode, BackendType.Antigravity, BackendType.Codex })
        {
            var p = profile?.Type == type ? profile
                : settings.Chat.Backends.FirstOrDefault(b => b.Type == type) ?? new BackendProfile { Id = "", Type = type, Name = ConnectionTypes.Of(type).DisplayName };
            if (Locate(p) is { } exe) exes[type] = (p, exe);
        }
        var targets = new List<Target>();
        var choices = CliHandoff.Choices(profile?.Type, own is not null,
            chat.Messages.Count > 0 && chat.HasWholeTranscript, exes.Keys);
        foreach (var choice in choices)
        {
            var (p, exe) = exes[choice.Type];
            if (choice.Way == CliHandoff.Way.Resume)
            {
                var current = own!.Value;
                var command = CliHandoff.Resume(choice.Type, exe, current.Session);
                targets.Add(new(p.Name, async () =>
                {
                    await stopBackend(p.Id); // one process per conversation: Hotline lets go before the terminal takes it
                    Launch(command);
                    chat.NewChat(); // the conversation now lives in the terminal; Hotline must not write into it as well
                }));
            }
            else
                targets.Add(new(p.Name, () =>
                {
                    Launch(CliHandoff.Handoff(choice.Type, exe, WriteHandoff(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
                    return Task.CompletedTask;
                }, choice.More));
        }
        foreach (var target in ContinueTargets.For(Profile(chat.BackendId), settings))
            targets.Add(new(target.Name, () => RunAsync(target)));
        return targets;
    }

    /// <summary>The Credential Locker key of an Open WebUI target's API key.</summary>
    public static string ApiKeyName(string url) => $"continue:{url.Trim().TrimEnd('/')}:apiKey";

    /// <summary>
    /// Runs one of the user's targets. Failures surface as InvalidOperationException with a message for the notice
    /// (the history list shows "Couldn't open {name}: {message}").
    /// </summary>
    private async Task RunAsync(ContinueTarget target)
    {
        var title = HistoryStore.TitleOf(chat.Messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Text ?? "");
        var model = Profile(chat.BackendId)?.Model;
        switch (target.Kind)
        {
            case ContinueKind.Web:
            {
                var file = target.Url?.Contains("{file}", StringComparison.Ordinal) == true ? WriteHandoff() : null;
                var filled = ContinueTargets.FillUrl(target.Url ?? "", ContinueTargets.Values(title, chat.Messages, file, model))
                    ?? throw new InvalidOperationException(Strings.Continue_NotAWebAddress);
                if (target.CopyChat || filled.Shortened) CopyChat(title);
                if (filled.Shortened) notify(Strings.Continue_Shortened);
                await Windows.System.Launcher.LaunchUriAsync(filled.Uri);
                break;
            }
            case ContinueKind.OpenWebUi:
            {
                var url = target.Url?.Trim() ?? "";
                if (!Uri.TryCreate(url, UriKind.Absolute, out var baseUrl) || baseUrl.Scheme is not ("http" or "https")) throw new InvalidOperationException(Strings.Continue_NotAWebAddress);
                var key = secrets.Get(ApiKeyName(url));
                if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(Strings.Continue_NoKey);
                if (target.CopyChat) CopyChat(title);
                var body = OpenWebUiChat.Body(title, target.Model ?? model, chat.Messages, Guid.NewGuid);
                Uri page;
                try { page = await OpenWebUiChat.CreateAsync(http, baseUrl, key, body, CancellationToken.None); }
                catch (OpenWebUiException ex)
                {
                    throw new InvalidOperationException(ex.Error switch
                    {
                        OpenWebUiError.KeyRefused => Strings.Continue_KeyRefused,
                        OpenWebUiError.Unreachable => L.F(Strings.Continue_Unreachable, url),
                        OpenWebUiError.InsecureKey => Strings.Continue_InsecureKey,
                        _ => ex.Message,
                    }, ex);
                }
                await Windows.System.Launcher.LaunchUriAsync(page);
                break;
            }
            case ContinueKind.App:
            {
                var file = target.Command?.Contains("{file}", StringComparison.Ordinal) == true ? WriteHandoff() : null;
                var (program, args) = ContinueTargets.FillCommand(target.Command ?? "", ContinueTargets.Values(title, chat.Messages, file, model));
                if (program.Length == 0) throw new InvalidOperationException(Strings.Continue_NoCommand);
                if (ContinueTargets.IsBatchFile(program)) throw new InvalidOperationException(Strings.Continue_NoBatch); // cmd.exe would read the chat as commands
                if (target.CopyChat) CopyChat(title);
                Launch(new CliCommand(program, args, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
                break;
            }
        }
        log.Info($"continued in {target.Kind} target");
    }

    private void CopyChat(string title) => MarkdownRenderer.SetClipboardText(CliHandoff.Markdown(title, chat.Messages));

    private BackendProfile? Profile(string id) => settings.Chat.Backends.FirstOrDefault(b => b.Id == id);

    private static string? Locate(BackendProfile p)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        try
        {
            return p.Type switch
            {
                BackendType.Antigravity => AgyLocator.Find(p.CliPath, File.Exists, Environment.GetEnvironmentVariable("LOCALAPPDATA"), path),
                BackendType.ClaudeCode => ClaudeLocator.Find(p.CliPath, File.Exists, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path),
                BackendType.Codex => Hotline.Core.Backends.Codex.CodexLocator.Find(p.CliPath, File.Exists, path,
                    System.Runtime.InteropServices.RuntimeInformation.OSArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "arm64" : "x64"),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private string WriteHandoff()
    {
        Directory.CreateDirectory(handoffDirectory);
        foreach (var old in Directory.EnumerateFiles(handoffDirectory, "hotline-chat-*.md"))
            if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1))
                try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var title = HistoryStore.TitleOf(chat.Messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Text ?? "");
        var id = HistoryIds.IsSafe(chat.ConversationId) ? chat.ConversationId : Ids.New(); // the id names a file
        var file = Path.Combine(handoffDirectory, $"hotline-chat-{id}.md");
        File.WriteAllText(file, CliHandoff.Markdown(title, chat.Messages));
        return file;
    }

    /// <summary>A console program without a console: Windows gives it a window in the user's default terminal.</summary>
    private void Launch(CliCommand command)
    {
        var psi = new ProcessStartInfo(command.Exe)
        {
            UseShellExecute = false, CreateNoWindow = false,
            WorkingDirectory = Hotline.Core.Platform.SafeDirectory.IsLocal(command.WorkingDirectory) && Directory.Exists(command.WorkingDirectory) ? command.WorkingDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        foreach (var a in command.Args) psi.ArgumentList.Add(a);
        using var _ = Process.Start(psi);
        log.Info($"continued in {Path.GetFileName(command.Exe)} ({command.Args.FirstOrDefault()})");
    }
}
