using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hotline.Core.Tools;

public enum ToolApproval { Ask, Allow, Deny }

/// <summary>One MCP server to launch (stdio). Same fields as the common "mcpServers" format.</summary>
public sealed record McpServerConfig(string Name, string Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env,
    bool Disabled = false, string? WorkingDirectory = null, string? DisplayName = null);

/// <summary>
/// ~/.hotline/mcp.json: { "mcpServers": { name: { command, args, env, cwd, disabled } }, "approvals": { "server/tool": "allow|ask|deny" } }.
/// The server part is the format other MCP apps use, so configs can be pasted in. Comments and trailing commas are allowed.
/// </summary>
public sealed class McpConfig
{
    public IReadOnlyList<McpServerConfig> Servers { get; init; } = [];
    public IReadOnlyDictionary<string, ToolApproval> Approvals { get; init; } = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase);
    /// <summary>Set when the file couldn't be read; servers/approvals are then empty and the file is left untouched.</summary>
    public string? Error { get; init; }
    /// <summary>Problems in an otherwise readable file (e.g. an unknown approval value, which counts as "deny").</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private const string Example = """
        {
          // MCP servers Hotline can give to API connections (Tool use: "Use Hotline's tools").
          // Same format as other MCP apps, e.g.:
          //   "files": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\you\\Documents"] }
          "mcpServers": {},
          // Per tool: "allow" (no prompt), "ask" (prompt in the panel), "deny". "server/*" covers a whole server.
          // Without a rule, read-only tools run and everything else asks.
          "approvals": {}
        }
        """;

    public static McpConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, Example);
                return new McpConfig();
            }
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException)
        {
            return new McpConfig { Error = $"mcp.json couldn't be read: {ex.Message}" };
        }
    }

    public static McpConfig Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject ?? throw new JsonException("mcp.json must be a JSON object");
        var servers = new List<McpServerConfig>();
        if (root["mcpServers"] is JsonObject list)
            foreach (var (name, node) in list)
            {
                if (node is not JsonObject s || s["command"]?.GetValue<string>() is not { Length: > 0 } command) continue;
                var args = s["args"] is JsonArray a ? a.Select(x => x?.ToString() ?? "").ToList() : [];
                var env = s["env"] is JsonObject e
                    ? e.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var disabled = s["disabled"] is JsonValue d && d.TryGetValue<bool>(out var off) && off;
                servers.Add(new McpServerConfig(name, command, args, env, disabled, s["cwd"]?.GetValue<string>(), s["displayName"]?.GetValue<string>()));
            }
        var approvals = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        if (root["approvals"] is JsonObject rules)
            foreach (var (key, value) in rules)
            {
                // Only the exact words count; anything else (a typo, "3", "allow, deny") is treated as "deny".
                var text = value is JsonValue v && v.TryGetValue<string>(out var t) ? t.Trim().ToLowerInvariant() : null;
                approvals[key] = text switch { "allow" => ToolApproval.Allow, "ask" => ToolApproval.Ask, "deny" => ToolApproval.Deny, _ => ToolApproval.Deny };
                if (text is not ("allow" or "ask" or "deny")) warnings.Add($"Approval \"{key}\" has an unknown value, so it's treated as \"deny\".");
            }
        return new McpConfig { Servers = servers, Approvals = approvals, Warnings = warnings };
    }

    /// <summary>
    /// Writes one approval rule, keeping everything else in the file. Comments can't be kept, so the previous file is
    /// first copied to mcp.json.bak-&lt;timestamp&gt; (never overwrite a user's file without a backup).
    /// </summary>
    public static void SaveApproval(string path, string key, ToolApproval approval)
    {
        lock (SaveLock) // two "Always" clicks at once must not lose a rule
        {
            var original = File.Exists(path) ? File.ReadAllText(path) : null;
            var root = (original is null ? null : JsonNode.Parse(original, documentOptions: ReadOptions)) as JsonObject ?? new JsonObject();
            if (root["approvals"] is not JsonObject rules) root["approvals"] = rules = new JsonObject();
            rules[key] = approval.ToString().ToLowerInvariant();
            // Comments are what a rewrite loses: keep a copy of a commented file (never replacing an earlier backup).
            if (original is not null && (original.Contains("//") || original.Contains("/*"))) WriteBackup(path, original);
            var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, path, overwrite: true);
        }
    }

    private static readonly Lock SaveLock = new();

    private static void WriteBackup(string path, string content)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        for (var i = 0; ; i++)
        {
            var backup = i == 0 ? $"{path}.bak-{stamp}" : $"{path}.bak-{stamp}-{i}";
            try
            {
                using var stream = new FileStream(backup, FileMode.CreateNew);
                using var writer = new StreamWriter(stream);
                writer.Write(content);
                return;
            }
            catch (IOException) when (File.Exists(backup) && i < 100) { /* taken: try the next name */ }
        }
    }
}

public static class ToolPolicy
{
    /// <summary>
    /// A deny (for the tool or for "server/*") always wins; otherwise the specific rule, then "server/*", then the
    /// default: read-only tools run, others ask.
    /// </summary>
    public static ToolApproval Decide(IReadOnlyDictionary<string, ToolApproval> approvals, string server, string tool, bool readOnly)
    {
        var hasSpecific = approvals.TryGetValue($"{server}/{tool}", out var specific);
        var hasWildcard = approvals.TryGetValue($"{server}/*", out var wildcard);
        if (hasSpecific && specific == ToolApproval.Deny || hasWildcard && wildcard == ToolApproval.Deny) return ToolApproval.Deny;
        if (hasSpecific) return specific;
        if (hasWildcard) return wildcard;
        return readOnly ? ToolApproval.Allow : ToolApproval.Ask;
    }
}

public static class ToolNames
{
    /// <summary>Names with '/' or '*' would make approval keys ("server/tool", "server/*") ambiguous.</summary>
    public static bool IsAmbiguous(string name) => name.Contains('/') || name.Contains('*');

    /// <summary>
    /// A function name every API accepts (^[A-Za-z_][A-Za-z0-9_-]{0,63}$ — Gemini needs a letter or "_" first), unique
    /// within <paramref name="used"/>.
    /// </summary>
    public static string ForApi(string server, string tool, HashSet<string> used)
    {
        static string Clean(string s) => new(s.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_').ToArray());
        var baseName = $"{Clean(server)}__{Clean(tool)}";
        if (!char.IsAsciiLetter(baseName[0]) && baseName[0] != '_') baseName = "_" + baseName;
        if (baseName.Length > 60) baseName = baseName[..60];
        var name = baseName;
        for (var i = 2; !used.Add(name); i++) name = $"{baseName[..Math.Min(baseName.Length, 60 - i.ToString().Length)]}_{i}";
        return name;
    }
}

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
