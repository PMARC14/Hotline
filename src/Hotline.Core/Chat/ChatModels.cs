using System.Text;

namespace Hotline.Core.Chat;

public enum ChatRole { User, Assistant }

public enum AttachmentKind { Image, Text }

public sealed record Attachment(string Id, string Name, AttachmentKind Kind, string MimeType, byte[] Data)
{
    public string AsText() => Encoding.UTF8.GetString(Data);
}

public sealed record ChatMessage(
    string Id, ChatRole Role, string Text, IReadOnlyList<Attachment> Attachments, DateTimeOffset At, string? BackendId = null);

/// <summary>
/// A streamed piece of an answer. <paramref name="ResetBefore"/>: discard what was shown so far (the backend restarted
/// its answer). <paramref name="Status"/>: a passing note for the panel (e.g. "retrying in 4 s"), not part of the answer.
/// <paramref name="Session"/>: the CLI conversation this chat now lives in (no text).
/// </summary>
public readonly record struct ChatDelta(string Text, bool ResetBefore = false, string? Status = null, CliSession? Session = null)
{
    public static ChatDelta StatusNote(string message) => new("", Status: message);
}

public enum BackendErrorKind { NotConfigured, NotInstalled, NotLoggedIn, Unauthorized, RateLimited, ServerDown, Unsupported, Failed }

public sealed class BackendException(BackendErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public BackendErrorKind Kind { get; } = kind;
}

public sealed record BackendCapabilities(bool Images, bool TextFiles);

public interface IChatBackend : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }
    BackendCapabilities Capabilities { get; }

    /// <summary>Streams the reply to the last message of <paramref name="conversation"/> (always a user message).</summary>
    IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, CancellationToken ct);
}

/// <summary>A conversation in a CLI's own history (its ID and the folder it runs in), so it can be resumed there.</summary>
public sealed record CliSession(string Id, string WorkingDirectory)
{
    /// <summary>IDs come from CLI output and go back on a command line: letters, digits and dashes only.</summary>
    public static bool IsValidId(string? id) =>
        id is { Length: >= 8 and <= 64 } && char.IsAsciiLetterOrDigit(id[0]) // never a flag
        && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>A backend whose chats live in a CLI's own history and can be resumed (Antigravity, Claude Code, Codex).</summary>
public interface ICliSessionBackend
{
    /// <summary>Set by the controller before each turn: the conversation to continue, or null to start a new one.</summary>
    CliSession? Resume { get; set; }
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
