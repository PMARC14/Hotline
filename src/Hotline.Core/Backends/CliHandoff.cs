using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

/// <summary>A program to start in a terminal window: what "Continue this chat in ▸" runs.</summary>
public sealed record CliCommand(string Exe, IReadOnlyList<string> Args, string WorkingDirectory);

/// <summary>
/// Moves a chat into a CLI's own window: its own conversation resumed (--conversation / --resume), or, for another
/// CLI, a new session told to read the chat from a Markdown file (the chat text never goes on the command line).
/// </summary>
public static class CliHandoff
{
    /// <summary>How a CLI entry continues the chat: its own conversation resumed, or a new session reading a file.</summary>
    public enum Way { Resume, Handoff }

    /// <summary>One "Continue this chat in ▸" CLI entry; <paramref name="More"/>: listed under "More…".</summary>
    public sealed record Choice(BackendType Type, Way Way, bool More);

    /// <summary>
    /// The CLI entries for a chat on a connection of type <paramref name="own"/>: that CLI first (resuming its
    /// conversation when there is one, else reading the chat from a file), and the other installed CLIs under "More…"
    /// (file handoffs, so only when the panel has the whole chat). A chat on an API connection has no own CLI: the
    /// handoffs are then listed directly.
    /// </summary>
    public static IReadOnlyList<Choice> Choices(BackendType? own, bool hasSession, bool wholeChat, IReadOnlyCollection<BackendType> installed)
    {
        var choices = new List<Choice>();
        var ownCli = own is { } o && CanResume(o) && installed.Contains(o) ? o : (BackendType?)null;
        if (ownCli is { } mine)
        {
            if (hasSession) choices.Add(new(mine, Way.Resume, false));
            else if (wholeChat) choices.Add(new(mine, Way.Handoff, false));
        }
        if (!wholeChat) return choices;
        foreach (var type in new[] { BackendType.ClaudeCode, BackendType.Antigravity, BackendType.Codex })
            if (type != ownCli && installed.Contains(type)) choices.Add(new(type, Way.Handoff, ownCli is not null));
        return choices;
    }

    public static bool CanResume(BackendType type) => type is BackendType.Antigravity or BackendType.ClaudeCode or BackendType.Codex;

    public static CliCommand Resume(BackendType type, string exe, CliSession session)
    {
        if (!CliSession.IsValidId(session.Id)) throw new ArgumentException("Not a session ID.", nameof(session));
        return type switch
        {
            BackendType.Antigravity => new(exe, ["--conversation", session.Id], session.WorkingDirectory),
            BackendType.ClaudeCode => new(exe, ["--resume", session.Id], session.WorkingDirectory),
            BackendType.Codex => new(exe, ["resume", session.Id], session.WorkingDirectory),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "No resume for this connection type."),
        };
    }

    /// <summary>A new interactive session that starts by reading <paramref name="file"/>.</summary>
    public static CliCommand Handoff(BackendType type, string exe, string file, string workingDirectory) => type switch
    {
        BackendType.Antigravity => new(exe, ["--prompt-interactive", Prompt(file)], workingDirectory),
        BackendType.ClaudeCode => new(exe, [Prompt(file)], workingDirectory),
        BackendType.Codex => new(exe, [Prompt(file)], workingDirectory),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Not a CLI connection type."),
    };

    public static string Prompt(string file) =>
        $"Continue the conversation saved in \"{file}\" (from Hotline). Read it first, then reply to its last message.";

    /// <summary>The chat as Markdown (attachments by name only).</summary>
    public static string Markdown(string title, IReadOnlyList<ChatMessage> messages)
    {
        var sb = new StringBuilder().Append("# ").AppendLine(title.Length > 0 ? title : "Hotline chat").AppendLine();
        foreach (var m in messages)
        {
            sb.Append("## ").AppendLine(m.Role == ChatRole.User ? "User" : "Assistant").AppendLine();
            sb.AppendLine(m.Text.TrimEnd());
            foreach (var a in m.Attachments) sb.Append("[attached: ").Append(a.Name).AppendLine("]");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
