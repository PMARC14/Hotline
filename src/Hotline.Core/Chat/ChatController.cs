using System.Text;
using Hotline.Core.Diagnostics;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Chat;

/// <summary>
/// Owns the current conversation and streams replies from the selected backend. Single-threaded by
/// design: call from the UI thread; events are raised on the caller's synchronization context.
/// A CLI connection reports the conversation it keeps in its own history (<see cref="CliSession"/>): the controller
/// remembers it per chat (history record), hands it back so the chat resumes there, and drops it on New chat.
/// <paramref name="keepTranscript"/> decides per connection whether Hotline also keeps the messages (default: yes).
/// </summary>
public sealed class ChatController(Func<string, IChatBackend?> resolveBackend, HistoryStore? history, TimeProvider clock, FileLog log,
    Func<string, bool>? keepTranscript = null)
{
    private readonly List<ChatMessage> _messages = [];
    private CliSession? _session;
    private string? _sessionBackendId;
    private bool _partial; // reopened from a record only: the earlier messages are in the CLI, not here
    private CancellationTokenSource? _cts;
    private (string Text, IReadOnlyList<Attachment> Attachments)? _lastFailed;

    public event Action<ChatEvent>? Event;
    public string ConversationId { get; private set; } = Ids.New();
    public string BackendId { get; set; } = "";
    public IReadOnlyList<ChatMessage> Messages => _messages;
    public bool IsBusy => _cts is not null;
    /// <summary>The CLI conversation this chat lives in, and its connection (null: none yet, or not a CLI chat).</summary>
    public (CliSession Session, string BackendId)? CurrentSession => _session is { } s && _sessionBackendId is { } b ? (s, b) : null;
    /// <summary>False when the chat was reopened from a record only (its earlier messages are in the CLI's history).</summary>
    public bool HasWholeTranscript => !_partial;
    /// <summary>What the selected connection accepts (null when it isn't available).</summary>
    public BackendCapabilities? CurrentCapabilities => resolveBackend(BackendId)?.Capabilities;

    public bool CanAccept(IReadOnlyList<Attachment> attachments, out string? reason)
    {
        reason = null;
        var backend = resolveBackend(BackendId);
        if (backend is null) return true; // SendAsync reports the missing backend
        if (!backend.Capabilities.Images && attachments.Any(a => a.Kind == AttachmentKind.Image))
            reason = L.F(Strings.Chat_NoImages, backend.DisplayName);
        else if (!backend.Capabilities.TextFiles && attachments.Any(a => a.Kind == AttachmentKind.Text))
            reason = L.F(Strings.Chat_NoFiles, backend.DisplayName);
        return reason is null;
    }

    public Task SendAsync(string text, IReadOnlyList<Attachment> attachments) => SendCoreAsync(text.Trim(), attachments, announce: true);

    public Task RetryAsync() => _lastFailed is { } f ? SendCoreAsync(f.Text, f.Attachments, announce: false) : Task.CompletedTask;

    public void Cancel() => _cts?.Cancel();

    public void NewChat()
    {
        Cancel();
        _cts = null; // supersede the old turn now: a send right after must not be swallowed while it unwinds
        _messages.Clear();
        _lastFailed = null;
        _session = null; // a new chat is a new conversation for every connection, CLIs included
        _sessionBackendId = null;
        _partial = false;
        ConversationId = Ids.New();
        Emit(new ConversationReset());
    }

    /// <summary>
    /// Continues a saved conversation: its messages come back (attachments as "[attached: name]" — their bytes are
    /// never saved) and new turns are appended to the same history. A chat kept in a CLI's history switches back to
    /// that connection and resumes its CLI conversation; others are replayed as context. A chat with only a record
    /// (no transcript) starts empty in the panel and continues in the CLI's conversation.
    /// </summary>
    public bool Resume(string conversationId)
    {
        if (IsBusy || conversationId == ConversationId) return false; // never while answering, never into itself
        IReadOnlyList<HistoryStore.Entry> entries;
        HistoryStore.Meta? meta;
        try
        {
            entries = history?.Load(conversationId) ?? [];
            meta = history?.LoadMeta(conversationId);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            log.Error($"could not read conversation {conversationId}", ex);
            return false;
        }
        var session = meta is { SessionId: { } sid, BackendId: { } owner } && resolveBackend(owner) is not null
            ? new CliSession(sid, meta.WorkingDirectory ?? "") : null;
        if (entries.Count == 0 && session is null) return false; // nothing to show and nothing to resume
        Cancel();
        _cts = null;
        _messages.Clear();
        _lastFailed = null;
        ConversationId = conversationId;
        _session = session;
        _sessionBackendId = session is null ? null : meta!.BackendId;
        _partial = entries.Count == 0;
        if (_sessionBackendId is not null) BackendId = _sessionBackendId; // the CLI that holds the conversation
        Emit(new ConversationReset());
        foreach (var e in entries)
        {
            var text = e.Attachments.Count == 0 ? e.Text
                : (e.Text + "\n" + string.Join("\n", e.Attachments.Select(a => $"[attached: {a.Name}]"))).Trim();
            var message = new ChatMessage(e.Id, e.Role, text, [], e.At, e.BackendId);
            _messages.Add(message);
            Emit(new MessageRestored(message));
        }
        var title = meta?.Title ?? HistoryStore.TitleOf(entries.FirstOrDefault(e => e.Role == ChatRole.User)?.Text ?? "");
        Emit(new ConversationResumed(conversationId, title, BackendId, entries.Count > 0));
        log.Info($"resumed conversation {conversationId} ({entries.Count} messages{(session is null ? "" : $", {_sessionBackendId} session")})");
        return true;
    }

    private async Task SendCoreAsync(string text, IReadOnlyList<Attachment> attachments, bool announce)
    {
        if (IsBusy || (text.Length == 0 && attachments.Count == 0)) return;

        var conversationId = ConversationId;
        var assistantId = Ids.New();
        var user = new ChatMessage(Ids.New(), ChatRole.User, text, attachments, clock.GetUtcNow());
        if (announce) Emit(new UserMessageAdded(user));

        var backend = resolveBackend(BackendId);
        if (backend is null)
        {
            _lastFailed = (text, attachments);
            Emit(new AssistantFailed(assistantId, BackendErrorKind.NotConfigured, L.F(Strings.Chat_NoBackend, BackendId)));
            return;
        }

        _messages.Add(user);
        _lastFailed = null;
        // Resume this chat's CLI conversation; null (new chat, other connection) starts a new one.
        if (backend is ICliSessionBackend cli) cli.Resume = _sessionBackendId == backend.Id ? _session : null;
        Emit(new AssistantStarted(assistantId, backend.DisplayName, BackendId));
        var cts = _cts = new CancellationTokenSource();
        var reply = new StringBuilder();
        try
        {
            await foreach (var d in backend.StreamAsync(_messages.ToList(), cts.Token).WithCancellation(cts.Token))
            {
                if (d.Status is { } status)
                {
                    Emit(new AssistantStatus(assistantId, status));
                    continue;
                }
                if (d.Session is { } session)
                {
                    if (conversationId == ConversationId && CliSession.IsValidId(session.Id))
                    {
                        _session = session;
                        _sessionBackendId = backend.Id;
                    }
                    continue;
                }
                if (d.ResetBefore) reply.Clear();
                reply.Append(d.Text);
                Emit(new AssistantDelta(assistantId, d.Text, d.ResetBefore));
            }
            Keep(conversationId, user, assistantId, backend, reply.ToString());
            Emit(new AssistantCompleted(assistantId));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Keep the question in context even with no reply, so a follow-up ("shorter please") still makes sense.
            Keep(conversationId, user, assistantId, backend, reply.ToString(), keepEmptyReply: reply.Length > 0);
            Emit(new AssistantCancelled(assistantId));
        }
        catch (Exception ex) when (conversationId != ConversationId)
        {
            log.Error("turn from a previous conversation failed after a new chat started (ignored)", ex);
        }
        catch (BackendException ex)
        {
            Fail(user, text, attachments);
            Emit(new AssistantFailed(assistantId, ex.Kind, ex.Message));
        }
        catch (Exception ex)
        {
            log.Error("chat send failed", ex);
            Fail(user, text, attachments);
            Emit(new AssistantFailed(assistantId, BackendErrorKind.Failed, ex.Message));
        }
        finally
        {
            if (ReferenceEquals(_cts, cts)) _cts = null;
            cts.Dispose();
        }
    }

    private void Keep(string conversationId, ChatMessage user, string assistantId, IChatBackend backend, string text, bool keepEmptyReply = true)
    {
        if (conversationId != ConversationId) return; // a new chat started meanwhile: drop the old turn
        var transcript = keepTranscript?.Invoke(backend.Id) ?? true;
        if (transcript) TryHistory(conversationId, user);
        if (keepEmptyReply)
        {
            var assistant = new ChatMessage(assistantId, ChatRole.Assistant, text, [], clock.GetUtcNow(), backend.Id);
            _messages.Add(assistant);
            if (transcript) TryHistory(conversationId, assistant);
        }
        if (_session is { } session && _sessionBackendId == backend.Id) TryMeta(conversationId, session, backend.Id, added: keepEmptyReply ? 2 : 1);
    }

    /// <summary>The chat's record: what Recent chats lists and what resuming its CLI conversation needs.</summary>
    private void TryMeta(string conversationId, CliSession session, string backendId, int added)
    {
        if (history is null) return;
        try
        {
            var previous = history.LoadMeta(conversationId);
            var first = _messages.FirstOrDefault(m => m.Role == ChatRole.User && m.Text.Trim().Length > 0)?.Text;
            var title = previous?.Title is { Length: > 0 } kept ? kept : HistoryStore.TitleOf(first ?? "");
            var count = Math.Max(_messages.Count, (previous?.MessageCount ?? 0) + added); // the panel may hold only this session's part
            history.SaveMeta(conversationId, new HistoryStore.Meta(title, clock.GetUtcNow(), backendId, session.Id, session.WorkingDirectory, count));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            log.Error("history record write failed", ex);
        }
    }

    private void Fail(ChatMessage user, string text, IReadOnlyList<Attachment> attachments)
    {
        _messages.Remove(user);
        _lastFailed = (text, attachments);
    }

    private void TryHistory(string conversationId, ChatMessage m)
    {
        try { history?.Append(conversationId, m); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("history write failed", ex); }
    }

    private void Emit(ChatEvent e)
    {
        try { Event?.Invoke(e); }
        catch (Exception ex) { log.Error($"chat event handler failed for {e.GetType().Name}", ex); }
    }
}
