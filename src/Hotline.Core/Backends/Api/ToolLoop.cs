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

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0].Trim();
        return line.Length > 160 ? line[..160] + "…" : line;
    }
}
