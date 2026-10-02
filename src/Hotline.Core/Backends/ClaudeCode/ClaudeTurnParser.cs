using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;

namespace Hotline.Core.Backends.ClaudeCode;

/// <summary>
/// Turns Claude Code stream-json lines for one turn into answer text. Text arrives as stream_event text_delta pieces
/// (thinking is not shown); a turn with tool use has several assistant messages, joined with a blank line. The
/// "result" line ends the turn. Without partial messages, the assistant message text is used instead.
/// </summary>
public sealed class ClaudeTurnParser
{
    private readonly StringBuilder _shown = new();
    private bool _messageHasText;
    private bool _sawDeltas;

    public bool Completed { get; private set; }
    public string? Error { get; private set; }

    public IEnumerable<ChatDelta> Feed(string line)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(line).RootElement; }
        catch (JsonException) { yield break; }
        if (root.ValueKind != JsonValueKind.Object || Str(root, "type") is not { } type) yield break;

        switch (type)
        {
            case "stream_event" when root.TryGetProperty("event", out var ev):
                switch (Str(ev, "type"))
                {
                    case "message_start":
                        _messageHasText = false;
                        break;
                    case "content_block_delta" when ev.TryGetProperty("delta", out var delta) && Str(delta, "type") == "text_delta":
                        var piece = Str(delta, "text") ?? "";
                        if (piece.Length == 0) break;
                        _sawDeltas = true;
                        if (!_messageHasText && _shown.Length > 0) { _shown.Append("\n\n"); yield return new ChatDelta("\n\n"); }
                        _messageHasText = true;
                        _shown.Append(piece);
                        yield return new ChatDelta(piece);
                        break;
                }
                break;

            case "assistant" when !_sawDeltas && root.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content):
                foreach (var block in content.EnumerateArray())
                {
                    if (Str(block, "type") != "text" || Str(block, "text") is not { Length: > 0 } text) continue;
                    var prefix = _shown.Length > 0 ? "\n\n" : "";
                    _shown.Append(prefix).Append(text);
                    yield return new ChatDelta(prefix + text);
                }
                break;

            case "result":
                Completed = true;
                var result = Str(root, "result") ?? "";
                var isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
                if (isError || Str(root, "subtype") is { } sub && sub != "success")
                {
                    Error = result.Length > 0 ? result
                        : Str(root, "error") is { Length: > 0 } err ? err
                        : root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                            ? string.Join("; ", errors.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : x.ToString()))
                        : $"Claude Code ended with {Str(root, "subtype") ?? "an error"}";
                    yield break;
                }
                if (_shown.Length == 0 && result.Length > 0) yield return new ChatDelta(result);
                break;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
