namespace Hotline.Core.Chat;

/// <summary>What pressing Send does with the typed text.</summary>
public abstract record SendRoute
{
    /// <summary>Empty box and nothing attached.</summary>
    public sealed record Nothing : SendRoute;
    /// <summary>"/remember fact": save it locally, send nothing (an empty fact means "say what to remember").</summary>
    public sealed record Remember(string Fact) : SendRoute;
    /// <summary>"/resume [words]": reopen the last chat, or the newest whose title has the words; send nothing.</summary>
    public sealed record Resume(string Query) : SendRoute;
    /// <summary>A quick action with nothing to apply it to.</summary>
    public sealed record NeedsText(string Action) : SendRoute;
    /// <summary>Send this text (a quick action already expanded; <see cref="Action"/> names it).</summary>
    public sealed record Message(string Text, string? Action) : SendRoute;
}

/// <summary>Decides what a send means: built-in commands first, then quick actions, else the text as typed.</summary>
public sealed class SendRouter(QuickActions actions)
{
    public SendRoute Route(string typed, bool hasAttachments)
    {
        var text = typed.Trim();
        if (text.Length == 0 && !hasAttachments) return new SendRoute.Nothing();
        if (MemoryStore.TryParseCommand(text, out var fact)) return new SendRoute.Remember(fact);
        if (ResumeCommand.TryParse(text, out var query)) return new SendRoute.Resume(query);
        if (actions.Expand(text, hasAttachments) is { } expanded)
            return expanded.Text is { } message ? new SendRoute.Message(message, expanded.Action.Name) : new SendRoute.NeedsText(expanded.Action.Name);
        return new SendRoute.Message(text, null);
    }
}
