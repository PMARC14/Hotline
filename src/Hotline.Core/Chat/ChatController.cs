using System.Text;
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Chat;

/// <summary>
/// Owns the current conversation and streams replies from the selected backend. Single-threaded by
/// design: call from the UI thread; events are raised on the caller's synchronization context.
/// </summary>
public sealed class ChatController(Func<string, IChatBackend?> resolveBackend, HistoryStore? history, TimeProvider clock, FileLog log)
{
    private readonly List<ChatMessage> _messages = [];
    private CancellationTokenSource? _cts;
    private (string Text, IReadOnlyList<Attachment> Attachments)? _lastFailed;

    public event Action<ChatEvent>? Event;
    public string ConversationId { get; private set; } = Ids.New();
    public string BackendId { get; set; } = "";
    public IReadOnlyList<ChatMessage> Messages => _messages;
    public bool IsBusy => _cts is not null;

    public bool CanAccept(IReadOnlyList<Attachment> attachments, out string? reason)
    {
        reason = null;
        var backend = resolveBackend(BackendId);
        if (backend is null) return true; // SendAsync reports the missing backend
        if (!backend.Capabilities.Images && attachments.Any(a => a.Kind == AttachmentKind.Image))
            reason = $"{backend.DisplayName} can't read images.";
        else if (!backend.Capabilities.TextFiles && attachments.Any(a => a.Kind == AttachmentKind.Text))
            reason = $"{backend.DisplayName} can't read files.";
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
        ConversationId = Ids.New();
        Emit(new ConversationReset());
    }

    /// <summary>
    /// Continues a saved conversation: its messages come back (attachments as "[attached: name]" — their bytes are
    /// never saved), new turns are appended to the same history file, and the backend replays it as context.
    /// </summary>
    public bool Resume(string conversationId)
    {
        if (IsBusy || conversationId == ConversationId) return false; // never while answering, never into itself
        IReadOnlyList<HistoryStore.Entry> entries;
        try { entries = history?.Load(conversationId) ?? []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            log.Error($"could not read conversation {conversationId}", ex);
            return false;
        }
        if (entries.Count == 0) return false;
        Cancel();
        _cts = null;
        _messages.Clear();
        _lastFailed = null;
        ConversationId = conversationId;
        Emit(new ConversationReset());
        foreach (var e in entries)
        {
            var text = e.Attachments.Count == 0 ? e.Text
                : (e.Text + "\n" + string.Join("\n", e.Attachments.Select(a => $"[attached: {a.Name}]"))).Trim();
            var message = new ChatMessage(e.Id, e.Role, text, [], e.At, e.BackendId);
            _messages.Add(message);
            Emit(new MessageRestored(message));
        }
        log.Info($"resumed conversation {conversationId} ({entries.Count} messages)");
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
            Emit(new AssistantFailed(assistantId, BackendErrorKind.NotConfigured, $"The '{BackendId}' backend isn't available yet."));
            return;
        }

        _messages.Add(user);
        _lastFailed = null;
        Emit(new AssistantStarted(assistantId, backend.DisplayName));
        var cts = _cts = new CancellationTokenSource();
        var reply = new StringBuilder();
        try
        {
            await foreach (var d in backend.StreamAsync(_messages.ToList(), cts.Token).WithCancellation(cts.Token))
            {
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
        TryHistory(conversationId, user);
        if (!keepEmptyReply) return; // cancelled before any text: keep the question only
        var assistant = new ChatMessage(assistantId, ChatRole.Assistant, text, [], clock.GetUtcNow(), backend.Id);
        _messages.Add(assistant);
        TryHistory(conversationId, assistant);
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
