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

    public IEnumerable<ChatDelta> Feed(string line)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(line).RootElement; }
        catch (JsonException) { yield break; }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out var ev)) yield break;

        switch (ev.GetString())
        {
            case "step_update" when root.TryGetProperty("step_update", out var su):
                if (Str(su, "step_type") != "agent_response" || !su.TryGetProperty("text_delta", out var deltaEl)) yield break;
                var delta = deltaEl.GetString() ?? "";
                var step = su.TryGetProperty("step_index", out var si) ? si.GetInt32() : 0;
                var reset = _responseStep is { } current && current != step;
                _responseStep = step;
                if (reset) _shown.Clear();
                _shown.Append(delta);
                if (delta.Length > 0 || reset) yield return new ChatDelta(delta, reset);
                break;

            case "result" when root.TryGetProperty("result", out var r):
                Completed = true;
                var status = Str(r, "status");
                var response = Str(r, "response") ?? "";
                if (status != "SUCCESS")
                {
                    Error = Str(r, "error") ?? $"agy ended with status {status}";
                    yield break;
                }
                if (response.Length == 0 && r.TryGetProperty("denied_actions", out var denied) && denied.GetArrayLength() > 0)
                {
                    Error = "agy wasn't allowed to: " + string.Join(", ", denied.EnumerateArray().Select(d => Str(d, "action")));
                    yield break;
                }
                if (response.Length > 0 && response.TrimEnd() != _shown.ToString().TrimEnd())
                    yield return new ChatDelta(response, ResetBefore: _shown.Length > 0);
                break;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
