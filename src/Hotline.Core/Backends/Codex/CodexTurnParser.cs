using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Backends.Codex;

/// <summary>
/// Turns `codex exec --json` lines for one turn into answer text. Agent messages (item type agent_message) are the
/// answer, joined with a blank line; reasoning, commands, file changes and tool calls are not shown. An item's text
/// may arrive in item.started/updated/completed: only the new part is emitted (a rewrite resets the answer).
/// turn.completed / turn.failed end the turn; a top-level "error" can be a retry notice ("Reconnecting... 2/5"), shown as a status note and remembered.
/// </summary>
public sealed class CodexTurnParser
{
    private readonly List<string> _order = [];
    private readonly Dictionary<string, string> _texts = [];
    private string _shown = "";

    public bool Completed { get; private set; }
    public string? Error { get; private set; }
    public string? LastError { get; private set; }
    public string? ThreadId { get; private set; }
    /// <summary>Whether any codex event arrived (a resume of a missing session fails before the first one).</summary>
    public bool SawEvent { get; private set; }

    public IEnumerable<ChatDelta> Feed(string line)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch (JsonException) { yield break; }
        using var _ = doc;
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Str(root, "type") is not { } type) yield break;
        SawEvent = true;

        switch (type)
        {
            case "thread.started":
                ThreadId = Str(root, "thread_id");
                break;
            case "item.started" or "item.updated" or "item.completed"
                when root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object
                     && Str(item, "type") == "agent_message" && Str(item, "text") is { } text:
                var id = Str(item, "id") ?? ""; // codex always sends one; without it, treat the updates as one message
                if (!_texts.ContainsKey(id)) _order.Add(id);
                _texts[id] = text;
                var full = string.Join("\n\n", _order.Select(i => _texts[i]).Where(t => t.Length > 0));
                if (full == _shown) break;
                var delta = full.StartsWith(_shown, StringComparison.Ordinal) ? new ChatDelta(full[_shown.Length..]) : new ChatDelta(full, ResetBefore: true);
                _shown = full;
                yield return delta;
                break;
            case "turn.completed":
                Completed = true;
                break;
            case "turn.failed":
                Completed = true;
                Error = root.TryGetProperty("error", out var error) && Str(error, "message") is { Length: > 0 } message
                    ? message : LastError ?? Strings.Codex_TurnFailed;
                break;
            case "error":
                LastError = Str(root, "message");
                if (LastError?.StartsWith("Reconnecting", StringComparison.OrdinalIgnoreCase) == true)
                    yield return ChatDelta.StatusNote(Strings.Codex_Reconnecting);
                break;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
