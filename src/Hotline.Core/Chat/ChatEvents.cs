namespace Hotline.Core.Chat;

public abstract record ChatEvent;
public sealed record UserMessageAdded(ChatMessage Message) : ChatEvent;
public sealed record AssistantStarted(string Id, string BackendName) : ChatEvent;
/// <summary>Replace = the text replaces what was shown for this answer (backend restarted it).</summary>
public sealed record AssistantDelta(string Id, string Text, bool Replace) : ChatEvent;
public sealed record AssistantCompleted(string Id) : ChatEvent;
public sealed record AssistantCancelled(string Id) : ChatEvent;
public sealed record AssistantFailed(string Id, BackendErrorKind Kind, string Message) : ChatEvent;
public sealed record ConversationReset : ChatEvent;
