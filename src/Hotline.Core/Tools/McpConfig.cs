using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Text.Localization;

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

    // A key twice in one object is an error anywhere in the file (not only where it's read, and never a crash).
    private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, AllowDuplicateProperties = false };

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
        // ArgumentException: a key twice in one object (JsonObject refuses it when it's read).
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or ArgumentException)
        {
            return new McpConfig { Error = L.F(Strings.Mcp_Unreadable, ex.Message) };
        }
    }

    public static McpConfig Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject ?? throw new JsonException("mcp.json must be a JSON object");
        var servers = new List<McpServerConfig>();
        if (root["mcpServers"] is JsonObject list)
            foreach (var (name, node) in list)
            {
                if (node is not JsonObject s || Text(s["command"]) is not { Length: > 0 } command) continue;
                var args = s["args"] is JsonArray a ? a.Select(x => x?.ToString() ?? "").ToList() : [];
                // Windows variable names ignore case: "PATH" and "Path" are one variable, the last one wins.
                var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (s["env"] is JsonObject e)
                    foreach (var (k, v) in e) env[k] = v?.ToString() ?? "";
                var disabled = s["disabled"] is JsonValue d && d.TryGetValue<bool>(out var off) && off;
                servers.Add(new McpServerConfig(name, command, args, env, disabled, Text(s["cwd"]), Text(s["displayName"])));
            }
        var approvals = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        if (root["approvals"] is JsonObject rules)
            foreach (var (key, value) in rules)
            {
                // Only the exact words count; anything else (a typo, "3", "allow, deny") is treated as "deny".
                var text = value is JsonValue v && v.TryGetValue<string>(out var t) ? t.Trim().ToLowerInvariant() : null;
                approvals[key] = text switch { "allow" => ToolApproval.Allow, "ask" => ToolApproval.Ask, "deny" => ToolApproval.Deny, _ => ToolApproval.Deny };
                if (text is not ("allow" or "ask" or "deny")) warnings.Add(L.F(Strings.Mcp_BadApproval, key));
            }
        return new McpConfig { Servers = servers, Approvals = approvals, Warnings = warnings };
    }

    /// <summary>A string value, or null when the node is missing or not a string (a hand-edit slip must not break the whole file).</summary>
    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>
    /// Writes one approval rule, keeping everything else in the file. A rewrite loses comments and formatting, so the
    /// previous file is first copied to mcp.json.bak-&lt;timestamp&gt; (never overwrite a user's file without a backup).
    /// </summary>
    public static void SaveApproval(string path, string key, ToolApproval approval)
    {
        lock (SaveLock) // two "Always" clicks at once must not lose a rule
        {
            var original = File.Exists(path) ? File.ReadAllText(path) : null;
            JsonObject root;
            try
            {
                var parsed = original is null ? null : JsonNode.Parse(original, documentOptions: ReadOptions);
                if (original is not null && parsed is not JsonObject)
                    throw new JsonException("mcp.json isn't a JSON object, so the approval wasn't saved (the file was left as it is).");
                root = parsed as JsonObject ?? new JsonObject();
                if (root["approvals"] is not JsonObject rules) root["approvals"] = rules = new JsonObject();
                rules[key] = approval.ToString().ToLowerInvariant();
            }
            catch (ArgumentException ex) // a key twice in one object
            {
                throw new JsonException("mcp.json has a duplicate key, so the approval wasn't saved (the file was left as it is).", ex);
            }
            var updated = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            if (updated == original) return; // the rule was already there
            if (original is not null) WriteBackup(path, original); // never replacing an earlier backup
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var tmp = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tmp, updated);
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
                writer.Flush();
                stream.Flush();
                Settings.BackupFiles.Prune(path); // every approval saved is one more copy: keep the newest ten
                return;
            }
            catch (IOException) when (File.Exists(backup) && i < 100) { /* taken: try the next name */ }
        }
    }
}
