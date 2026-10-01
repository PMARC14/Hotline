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

/// <summary>A streamed piece of an answer. <paramref name="ResetBefore"/>: discard what was shown so far (the backend restarted its answer).</summary>
public readonly record struct ChatDelta(string Text, bool ResetBefore = false);

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

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
