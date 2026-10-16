using System.Text.Json;
using Hotline.Core.Processes;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Codex;

/// <summary>Codex's model list: parsers for `app-server`'s model/list and the cache file, and the curated fallback.</summary>
public static class CodexModels
{
    public static IReadOnlyList<ModelInfo> Curated { get; } =
    [
        new("gpt-6.1-sol", "GPT-6.1 Sol"), new("gpt-6-astra", "GPT-6 Astra"), new("gpt-6-sol", "GPT-6 Sol"),
        new("gpt-6-luna", "GPT-6 Luna"), new("gpt-5.6-sol", "GPT-5.6 Sol"), new("gpt-5.6-terra", "GPT-5.6 Terra"),
        new("gpt-5.6-luna", "GPT-5.6 Luna"), new("gpt-5.5", "GPT-5.5"),
    ];

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s ? s : null;

    private static IReadOnlyList<string>? Efforts(JsonElement e, params string[] listNames)
    {
        foreach (var listName in listNames)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(listName, out var arr) || arr.ValueKind != JsonValueKind.Array) continue;
            var levels = arr.EnumerateArray()
                .Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : Str(x, "reasoningEffort") ?? Str(x, "effort") ?? Str(x, "reasoning_effort"))
                .Where(s => s is not null).Select(s => s!).ToList();
            if (levels.Count > 0) return levels;
        }
        return null;
    }

    /// <summary>One page of model/list: its models (hidden ones skipped) and the next cursor, if any.</summary>
    public static (List<ModelInfo> Models, string? Next) ParsePage(JsonElement result)
    {
        var models = new List<ModelInfo>();
        if (result.ValueKind == JsonValueKind.Object && result.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var m in data.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object) continue;
                if (m.TryGetProperty("hidden", out var h) && h.ValueKind == JsonValueKind.True) continue;
                var id = Str(m, "model") ?? Str(m, "id");
                if (id is null) continue;
                models.Add(new ModelInfo(id, Str(m, "displayName") ?? id, Efforts(m, "supportedReasoningEfforts")));
            }
        return (models, Str(result, "nextCursor"));
    }

    /// <summary>`~/.codex/models_cache.json` (internal schema, read tolerantly); null when there is no usable list.</summary>
    public static IReadOnlyList<ModelInfo>? ParseCache(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var array = FindModels(root);
            if (array is null && root.ValueKind == JsonValueKind.Object)
                foreach (var prop in root.EnumerateObject())
                    if ((array = FindModels(prop.Value)) is not null) break;
            if (array is not { } list) return null;
            var found = new List<(double? Priority, int Index, ModelInfo Info)>();
            var i = 0;
            foreach (var m in list.EnumerateArray())
            {
                i++;
                if (Str(m, "slug") is not { } slug) continue;
                if (Str(m, "visibility") is { } v && v != "list") continue;
                double? priority = m.TryGetProperty("priority", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
                found.Add((priority, i, new ModelInfo(slug, Str(m, "display_name") ?? slug, Efforts(m, "supported_reasoning_levels", "supported_reasoning_efforts"))));
            }
            if (found.Count == 0) return null;
            return found.OrderBy(f => f.Priority ?? double.MaxValue).ThenBy(f => f.Index).Select(f => f.Info).ToList();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return null; }
    }

    private static JsonElement? FindModels(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty("models", out var m) && m.ValueKind == JsonValueKind.Array && m.GetArrayLength() > 0 ? m : null;
}

/// <summary>Lists Codex models: `codex app-server` first, then the cache file, then <see cref="CodexModels.Curated"/>.</summary>
public sealed class CodexModelLister(
    ILineProcessFactory processes, Func<BackendProfile, string?> locate, Func<string?> readCache, string appVersion, TimeSpan? timeout = null)
{
    public async Task<IReadOnlyList<ModelInfo>> ListAsync(BackendProfile p, CancellationToken ct)
    {
        if (locate(p) is { } exe)
        {
            try
            {
                var models = await FromAppServerAsync(exe, ct);
                if (models.Count > 0) return models;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { /* timeout, crash, bad output: next layer */ }
        }
        try
        {
            if (readCache() is { Length: > 0 } json && CodexModels.ParseCache(json) is { } cached) return cached;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* unreadable: next layer */ }
        return CodexModels.Curated;
    }

    private async Task<List<ModelInfo>> FromAppServerAsync(string exe, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(10));
        var token = cts.Token;
        await using var proc = processes.Start(exe, ["app-server"], Path.GetTempPath());
        var init = JsonSerializer.Serialize(new { method = "initialize", id = 0, @params = new { clientInfo = new { name = "hotline", title = "Hotline", version = appVersion } } });
        await proc.WriteLineAsync(init, token);
        await ReadResultAsync(proc, 0, token);
        await proc.WriteLineAsync("""{"method":"initialized","params":{}}""", token);
        var all = new List<ModelInfo>();
        string? cursor = null;
        for (var id = 1; id < 50; id++) // bounded: a server that never ends its pages
        {
            var args = new Dictionary<string, object?> { ["limit"] = 100, ["includeHidden"] = false };
            if (cursor is not null) args["cursor"] = cursor;
            await proc.WriteLineAsync(JsonSerializer.Serialize(new { method = "model/list", id, @params = args }), token);
            var (page, next) = CodexModels.ParsePage(await ReadResultAsync(proc, id, token));
            all.AddRange(page);
            if (next is null) break;
            cursor = next;
        }
        return all;
    }

    /// <summary>The "result" of the response with this id; other lines (non-JSON, notifications) are skipped.</summary>
    private static async Task<JsonElement> ReadResultAsync(ILineProcess proc, int id, CancellationToken ct)
    {
        while (true)
        {
            var line = await proc.ReadLineAsync(ct) ?? throw new InvalidOperationException("codex app-server ended");
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var rid) || !rid.TryGetInt32(out var n) || n != id) continue;
                if (root.TryGetProperty("error", out _)) throw new InvalidOperationException("codex app-server error");
                if (root.TryGetProperty("result", out var result)) return result.Clone();
            }
            catch (JsonException) { /* not JSON */ }
        }
    }
}
