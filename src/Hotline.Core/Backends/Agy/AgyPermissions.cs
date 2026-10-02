using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// agy's permission rules live in ~/.gemini/antigravity-cli/settings.json under "permissions": { "allow", "deny", "ask" }
/// (rules like "command(git status)", "read_file(C:/path)", "mcp(server/tool)"; deny beats ask beats allow). In the
/// background (Hotline) anything that would ask is denied, so allow rules are how shell commands become usable.
/// Hotline only edits this file when the user presses the button, and keeps every other setting untouched.
/// </summary>
public static class AgyPermissions
{
    public static string SettingsPath(string userProfile) => Path.Combine(userProfile, ".gemini", "antigravity-cli", "settings.json");

    /// <summary>Commands that only read (PowerShell and common Unix-style names agy may use on Windows).</summary>
    public static IReadOnlyList<string> ReadOnlyCommandRules { get; } =
    [
        "command(Get-ChildItem)", "command(Get-Content)", "command(Get-Item)", "command(Get-Location)", "command(Get-Process)",
        "command(Get-Date)", "command(Get-ComputerInfo)", "command(Select-String)", "command(Test-Path)",
        "command(Resolve-Path)", "command(dir)", "command(ls)", "command(cat)", "command(type)", "command(pwd)",
        "command(whoami)", "command(hostname)", "command(where)", "command(findstr)", "command(tree)", "command(git status)",
        // Not included on purpose: Select/Where/Sort-Object and Format-* run script blocks; git log/diff/show/branch
        // have options that write files or delete branches.
    ];

    /// <summary>Destructive commands that stay denied even if something broader is allowed.</summary>
    public static IReadOnlyList<string> DenyRules { get; } =
    [
        "command(Remove-Item)", "command(rm)", "command(del)", "command(rmdir)", "command(format)", "command(Format-Volume)",
        "command(Stop-Computer)", "command(Restart-Computer)", "command(git push)", "command(git reset --hard)",
        "command(Set-Content)", "command(Out-File)", "command(Move-Item)", "command(Invoke-Expression)", "command(iex)",
    ];

    /// <summary>Returns the settings JSON with the rules merged in (no duplicates; other content unchanged).</summary>
    public static string AddRules(string? json, IEnumerable<string> allow, IEnumerable<string> deny)
    {
        var parsed = string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
        });
        var root = (parsed ?? new JsonObject()) as JsonObject ?? throw new InvalidDataException("agy's settings.json isn't a JSON object; not changing it.");
        if (root["permissions"] is null) root["permissions"] = new JsonObject();
        if (root["permissions"] is not JsonObject permissions) throw new InvalidDataException("\"permissions\" in agy's settings.json isn't an object; not changing it.");
        Merge(permissions, "allow", allow);
        Merge(permissions, "deny", deny);
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The rules currently in a list ("allow", "deny" or "ask").</summary>
    public static IReadOnlyList<string> Rules(string? json, string list)
    {
        try
        {
            return JsonNode.Parse(json ?? "{}", documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
                ?["permissions"]?[list] is JsonArray a ? a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var t) ? t : null).OfType<string>().ToList() : [];
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return []; }
    }

    private static void Merge(JsonObject permissions, string name, IEnumerable<string> rules)
    {
        if (permissions[name] is null) permissions[name] = new JsonArray();
        if (permissions[name] is not JsonArray list) throw new InvalidDataException($"\"permissions.{name}\" in agy's settings.json isn't a list; not changing it.");
        var existing = list.Select(n => n is JsonValue v && v.TryGetValue<string>(out var t) ? t : null).OfType<string>().ToHashSet(StringComparer.Ordinal);
        foreach (var rule in rules)
            if (existing.Add(rule)) list.Add(rule);
    }
}
