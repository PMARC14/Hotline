using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Tools;

/// <summary>
/// The tools API connections see: Hotline's own (the remember tool, offered even in chat-only mode) plus the MCP
/// tools when a connection uses Hotline's tools. Saving a memory asks first unless chat.memoryTool says otherwise.
/// </summary>
public sealed class HotlineToolHost(IToolHost? mcp, MemoryStore memory, Func<MemoryToolMode> mode,
    Func<ToolCallRequest, CancellationToken, Task<ToolDecision>> approve, Action alwaysAllow) : IToolHost
{
    public const string RememberApiName = "hotline_remember";

    public static readonly ToolSpec Remember = new(RememberApiName, "hotline", "remember",
        "Save one short fact about the user to their memory file, which every future chat sees (name, preferences, " +
        "ongoing projects). Use it only when the user asks you to remember something or clearly states a lasting " +
        "preference; never for passing details or anything sensitive. One fact per call, in the user's words.",
        JsonDocument.Parse("""
            {"type":"object","properties":{"fact":{"type":"string","description":"The fact to remember, one sentence."}},"required":["fact"]}
            """).RootElement, ReadOnly: false);

    /// <summary>Hotline's own tools (offered to every API connection, also in chat-only mode).</summary>
    public IReadOnlyList<ToolSpec> BuiltIn => mode() == MemoryToolMode.Off ? [] : [Remember];

    public async Task<IReadOnlyList<ToolSpec>> GetToolsAsync(CancellationToken ct) =>
        [.. mcp is null ? [] : await mcp.GetToolsAsync(ct), .. BuiltIn];

    public async Task<ToolResult> CallAsync(ToolSpec tool, JsonElement arguments, CancellationToken ct)
    {
        if (tool.ApiName != RememberApiName || tool.Server != "hotline")
            return mcp is null ? new ToolResult($"There is no tool named {tool.ApiName}.", true) : await mcp.CallAsync(tool, arguments, ct);
        var current = mode();
        if (current == MemoryToolMode.Off) return new ToolResult("Saving memories is turned off.", true);
        var fact = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("fact", out var f) && f.ValueKind == JsonValueKind.String
            ? f.GetString() ?? "" : "";
        if (fact.Trim().Length == 0) return new ToolResult("Give the fact to remember in \"fact\".", true);
        if (current == MemoryToolMode.Ask)
        {
            var answer = await approve(new ToolCallRequest(tool, arguments), ct);
            if (answer == ToolDecision.Deny) return new ToolResult("The user declined saving that.", true);
            if (answer == ToolDecision.AllowAlways) alwaysAllow();
        }
        return memory.Append(fact)
            ? new ToolResult("Saved to the user's memory.", false)
            : new ToolResult($"Not saved: a fact must be one line of at most {MemoryStore.MaxFactChars} characters.", true);
    }
}
