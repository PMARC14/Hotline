using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// Turns agy stream-json output lines for one turn into answer deltas. A new agent_response step
/// restarts the answer; the final result's response is authoritative.
/// </summary>
public sealed class AgyTurnParser
{
    private readonly StringBuilder _shown = new();
    private int? _responseStep;

    public bool Completed { get; private set; }
    public string? Error { get; private set; }
    /// <summary>The agy conversation this turn ran in (from the init event), when reported.</summary>
    public string? SessionId { get; private set; }

    /// <summary>The deltas in one output line. A line that isn't what agy is expected to print (any JSON shape) is skipped.</summary>
    public IEnumerable<ChatDelta> Feed(string line)
    {
        var deltas = new List<ChatDelta>();
        try
        {
            using var doc = JsonDocument.Parse(line); // disposed: its buffers come from a shared pool
            Read(doc.RootElement, deltas);
        }
        catch (JsonException) { /* not JSON: noise on stdout */ }
        return deltas;
    }

    private void Read(JsonElement root, List<ChatDelta> deltas)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out var ev) || ev.ValueKind != JsonValueKind.String) return;

        switch (ev.GetString())
        {
            case "init":
                SessionId = Str(root, "conversation_id");
                break;

            case "step_update" when Obj(root, "step_update") is { } su:
                if (Str(su, "step_type") != "agent_response" || Str(su, "text_delta") is not { } delta) return;
                var step = su.TryGetProperty("step_index", out var si) && si.ValueKind == JsonValueKind.Number && si.TryGetInt32(out var n) ? n : 0;
                var reset = _responseStep is { } current && current != step;
                _responseStep = step;
                if (reset) _shown.Clear();
                _shown.Append(delta);
                if (delta.Length > 0 || reset) deltas.Add(new ChatDelta(delta, reset));
                break;

            case "result" when Obj(root, "result") is { } r:
                Completed = true;
                var status = Str(r, "status");
                var response = Str(r, "response") ?? "";
                if (status != "SUCCESS")
                {
                    Error = Str(r, "error") ?? $"agy ended with status {status}";
                    return;
                }
                if (response.Length == 0 && r.TryGetProperty("denied_actions", out var denied) && denied.ValueKind == JsonValueKind.Array && denied.GetArrayLength() > 0)
                {
                    Error = "agy wasn't allowed to: " + string.Join(", ", denied.EnumerateArray().Select(d => Str(d, "action")));
                    return;
                }
                if (response.Length > 0 && response.TrimEnd() != _shown.ToString().TrimEnd())
                    deltas.Add(new ChatDelta(response, ResetBefore: _shown.Length > 0));
                break;
        }
    }

    private static JsonElement? Obj(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
