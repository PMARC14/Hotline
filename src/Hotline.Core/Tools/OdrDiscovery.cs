using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hotline.Core.Tools;

/// <summary>
/// The Windows on-device agent registry (odr.exe, build 26220.7262+): `odr.exe mcp list` prints the registered agent
/// connectors as JSON. Each becomes a server named windows-&lt;id&gt; launched through its manifest's mcp_config.
/// </summary>
public static class OdrDiscovery
{
    public static string? FindOdr(Func<string, bool> exists, string? windowsDir, string? localAppData)
    {
        foreach (var candidate in new[]
                 {
                     windowsDir is null ? null : Path.Combine(windowsDir, "System32", "odr.exe"),
                     localAppData is null ? null : Path.Combine(localAppData, "Microsoft", "WindowsApps", "odr.exe"),
                 })
            if (candidate is not null && exists(candidate)) return candidate;
        return null;
    }

    public static IReadOnlyList<McpServerConfig> Parse(string json)
    {
        var result = new List<McpServerConfig>();
        try
        {
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
            var items = root as JsonArray ?? (root?["servers"] as JsonArray) ?? [];
            foreach (var item in items.OfType<JsonObject>())
            {
                var config = item["manifest"]?["server"]?["mcp_config"] as JsonObject;
                if (config?["command"]?.GetValue<string>() is not { Length: > 0 } command) continue;
                var id = item["id"]?.GetValue<string>() ?? item["manifest"]?["name"]?.GetValue<string>() ?? command;
                var args = config["args"] is JsonArray a ? a.Select(x => x?.ToString() ?? "").ToList() : [];
                var display = item["manifest"]?["display_name"]?.GetValue<string>() ?? id;
                result.Add(new McpServerConfig($"windows-{id}", command, args, new Dictionary<string, string>(), DisplayName: display));
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        return result;
    }
}
