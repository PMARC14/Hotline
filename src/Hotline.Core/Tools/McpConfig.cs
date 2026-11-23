using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Tools;

public enum ToolApproval { Ask, Allow, Deny }

/// <summary>One MCP server to launch (stdio). Same fields as the common "mcpServers" format.</summary>
public sealed record McpServerConfig(string Name, string Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Env,
    bool Disabled = false, string? WorkingDirectory = null, string? DisplayName = null);

/// <summary>
/// ~/.hotline/mcp.json: { "version": 1, "mcpServers": { name: { command, args, env, cwd, disabled } } }.
/// The server part is the format other MCP apps use, so configs can be pasted in. Comments and trailing commas are allowed.
/// </summary>
public sealed class McpConfig
{
    public const int CurrentVersion = 1;

    public IReadOnlyList<McpServerConfig> Servers { get; init; } = [];
    /// <summary>Set when the file couldn't be read; servers are then empty and the file is left untouched.</summary>
    public string? Error { get; init; }
    /// <summary>Problems in an otherwise readable file (e.g. ignored "approvals" key or skipped non-string env value).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    // A key twice in one object is an error anywhere in the file (not only where it's read, and never a crash).
    internal static readonly JsonDocumentOptions ReadOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        AllowDuplicateProperties = false,
    };

    public static McpConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new McpConfig();
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
        var root = JsonNode.Parse(json, documentOptions: ReadOptions) as JsonObject
            ?? throw new JsonException("mcp.json must be a JSON object");
        var servers = new List<McpServerConfig>();
        var warnings = new List<string>();
        if (root.ContainsKey("approvals"))
            warnings.Add(Strings.Mcp_ApprovalsIgnored);

        if (root["mcpServers"] is JsonObject list)
            foreach (var (name, node) in list)
            {
                if (node is not JsonObject s || Text(s["command"]) is not { Length: > 0 } command) continue;
                var args = s["args"] is JsonArray a ? a.Select(x => x?.ToString() ?? "").ToList() : [];
                // Windows variable names ignore case: "PATH" and "Path" are one variable, the last one wins.
                var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (s["env"] is JsonObject e)
                    foreach (var (k, v) in e)
                    {
                        if (Text(v) is { } sv) env[k] = sv;
                        else warnings.Add(L.F(Strings.Mcp_BadEnvValue, name, k));
                    }
                var disabled = s["disabled"] is JsonValue d && d.TryGetValue<bool>(out var off) && off;
                servers.Add(new McpServerConfig(name, command, args, env, disabled, Text(s["cwd"]), Text(s["displayName"])));
            }
        return new McpConfig { Servers = servers, Warnings = warnings };
    }

    /// <summary>A string value, or null when the node is missing or not a string (a hand-edit slip must not break the whole file).</summary>
    private static string? Text(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
