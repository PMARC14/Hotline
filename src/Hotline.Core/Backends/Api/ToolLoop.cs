using System.Text.Json;
using Hotline.Core.Settings;
using Hotline.Core.Tools;

namespace Hotline.Core.Backends.Api;

/// <summary>Shared rules for tool calling in the API backends.</summary>
public static class ToolLoop
{
    /// <summary>Model ↔ tools rounds per answer before Hotline stops (protects against loops).</summary>
    public const int MaxRounds = 20;

    /// <summary>Tools for this connection: only when Tool use is "use Hotline's tools" and a host exists.</summary>
    public static async Task<IReadOnlyList<ToolSpec>> ToolsFor(BackendProfile profile, IToolHost? host, CancellationToken ct) =>
        host is not null && profile.Tools == ToolMode.Inherit ? await host.GetToolsAsync(ct) : [];

    /// <summary>The visible line in the answer when a tool runs.</summary>
    public static string Note(IReadOnlyList<ToolSpec> tools, string apiName)
    {
        var spec = tools.FirstOrDefault(t => t.ApiName == apiName);
        var label = spec is null ? apiName : $"{spec.Server} › {spec.Tool}";
        return $"\n\n> 🔧 {label}\n\n";
    }

    public static string Failed(ToolResult result) => result.IsError ? $"> ⚠ {FirstLine(result.Text)}\n\n" : "";

    public const string RoundLimitNote = "\n\n> ⏹ Stopped after too many tool calls in one answer.\n";

    /// <summary>The text sent back to the model for a tool result (never empty: some APIs reject empty results).</summary>
    public static string ResultText(ToolResult result) => result.Text.Length > 0 ? result.Text : "(no output)";

    /// <summary>
    /// Runs one requested call: arguments that aren't a JSON object go back to the model as an error instead of
    /// silently running the tool with no arguments.
    /// </summary>
    public static async Task<ToolResult> CallAsync(IToolHost host, IReadOnlyList<ToolSpec> offered, string apiName, string? argumentsJson, CancellationToken ct)
    {
        // Only tools this answer offered (a prompt-injected name for anything else goes nowhere).
        if (offered.FirstOrDefault(t => t.ApiName == apiName) is not { } tool) return new ToolResult($"There is no tool named {apiName}.", true);
        JsonElement args;
        try
        {
            args = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson).RootElement.Clone();
        }
        catch (JsonException ex) { return new ToolResult($"The arguments weren't valid JSON ({ex.Message}); call the tool again with a JSON object.", true); }
        if (args.ValueKind != JsonValueKind.Object) return new ToolResult("The arguments must be a JSON object; call the tool again.", true);
        return await host.CallAsync(tool, args, ct);
    }

    /// <summary>
    /// Earlier answers are replayed as plain text, without the tool calls behind them (those live only inside one
    /// answer). Hotline's own note lines are removed so the model doesn't imitate them instead of calling tools.
    /// </summary>
    public static string StripNotes(string text)
    {
        if (!text.Contains("> 🔧 ") && !text.Contains("> ⚠ ") && !text.Contains("> ⏹ ")) return text;
        var lines = text.Split('\n').Where(l => !(l.StartsWith("> 🔧 ") || l.StartsWith("> ⚠ ") || l.StartsWith("> ⏹ ")));
        return System.Text.RegularExpressions.Regex.Replace(string.Join('\n', lines), "\n{3,}", "\n\n").Trim();
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length > 160 ? line[..160] + "…" : line;
    }
}
