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
/// they picked another in Settings › System › For developers).
/// </summary>
internal sealed class ContinueIn(ChatController chat, HotlineSettings settings, string handoffDirectory, Func<string, ValueTask> stopBackend,
    FileLog log)
{
    public sealed record Target(string Label, Func<Task> Run);

    public IReadOnlyList<Target> Targets()
    {
        var targets = new List<Target>();
        BackendType? own = null;
        if (chat.CurrentSession is { } current && Profile(current.BackendId) is { } profile && CliHandoff.CanResume(profile.Type)
            && Locate(profile) is { } exe)
        {
            own = profile.Type;
            var command = CliHandoff.Resume(profile.Type, exe, current.Session);
            targets.Add(new(profile.Name, async () =>
            {
                await stopBackend(profile.Id); // one process per conversation: Hotline lets go before the terminal takes it
                Launch(command);
                chat.NewChat(); // the conversation now lives in the terminal; Hotline must not write into it as well
            }));
        }
        // Another CLI reads the chat from a file: only when the panel has all of it (not a chat reopened from a record).
        if (chat.Messages.Count == 0 || !chat.HasWholeTranscript) return targets;
        foreach (var type in new[] { BackendType.ClaudeCode, BackendType.Antigravity })
        {
            if (type == own) continue;
            var other = settings.Chat.Backends.FirstOrDefault(b => b.Type == type) ?? new BackendProfile { Id = "", Type = type, Name = ConnectionTypes.Of(type).DisplayName };
            if (Locate(other) is not { } otherExe) continue;
            targets.Add(new(other.Name, () =>
            {
                var file = WriteHandoff();
                Launch(CliHandoff.Handoff(type, otherExe, file, Path.GetDirectoryName(file)!));
                return Task.CompletedTask;
            }));
        }
        return targets;
    }

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
                _ => null,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    private string WriteHandoff()
    {
        Directory.CreateDirectory(handoffDirectory);
        foreach (var old in Directory.EnumerateFiles(handoffDirectory, "*.md"))
            if (File.GetLastWriteTimeUtc(old) < DateTime.UtcNow.AddDays(-1))
                try { File.Delete(old); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        var title = HistoryStore.TitleOf(chat.Messages.FirstOrDefault(m => m.Role == ChatRole.User)?.Text ?? "");
        var file = Path.Combine(handoffDirectory, $"hotline-chat-{chat.ConversationId}.md");
        File.WriteAllText(file, CliHandoff.Markdown(title, chat.Messages));
        return file;
    }

    /// <summary>A console program without a console: Windows gives it a window in the user's default terminal.</summary>
    private void Launch(CliCommand command)
    {
        var psi = new ProcessStartInfo(command.Exe)
        {
            UseShellExecute = false, CreateNoWindow = false,
            WorkingDirectory = Directory.Exists(command.WorkingDirectory) ? command.WorkingDirectory : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        foreach (var a in command.Args) psi.ArgumentList.Add(a);
        using var _ = Process.Start(psi);
        log.Info($"continued in {Path.GetFileName(command.Exe)} ({command.Args.FirstOrDefault()})");
    }
}
