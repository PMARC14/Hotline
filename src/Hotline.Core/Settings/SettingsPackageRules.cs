using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Tools;
using Hotline.Core.Windowing;

namespace Hotline.Core.Settings;

/// <summary>
/// What a backup package may hold and what each file must look like before it is written. A package comes from outside
/// (a download, a stick, another PC), so every name is checked against the exact shapes below and every file's content is
/// parsed before it can replace one of the user's files.
/// </summary>
public static partial class SettingsPackage
{
    /// <summary>Windows device names: a file called this (with any extension) opens the device, not a file.</summary>
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// The category and normalized relative path of an allowed entry name, or null for anything else. Only these shapes
    /// pass: <c>settings.json</c>, <c>toolbar.json</c>, <c>mcp.json</c>, <c>icons/&lt;custom id&gt;.png</c>, <c>sounds/&lt;name&gt;.wav</c>,
    /// <c>connections/&lt;id&gt;.json</c>, <c>actions/&lt;name&gt;.md</c> and <c>prompts/&lt;name&gt;.md</c>.
    /// </summary>
    internal static (SettingsCategory Category, string Path)? Classify(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 160) return null;
        // No backslash, drive colon, stream colon, wildcard, quote or control character in any spelling.
        foreach (var c in name)
            if (c < ' ' || c is '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|') return null;

        var parts = name.Split('/');
        if (parts.Any(p => p.Length == 0 || p is "." or "..")) return null;
        if (parts.Length == 1)
            return name switch
            {
                "settings.json" => (SettingsCategory.Settings, name),
                "toolbar.json" => (SettingsCategory.Toolbar, name),
                "mcp.json" => (SettingsCategory.Mcp, name),
                _ => null,
            };
        if (parts.Length != 2) return null;

        var (folder, file) = (parts[0], parts[1]);
        var category = folder switch
        {
            "icons" => SettingsCategory.Toolbar,
            "sounds" => SettingsCategory.Sounds,
            "connections" => SettingsCategory.Connections,
            "actions" => SettingsCategory.Actions,
            "prompts" => SettingsCategory.Prompts,
            _ => (SettingsCategory?)null,
        };
        if (category is not { } cat) return null;

        var extension = cat switch
        {
            SettingsCategory.Toolbar => ".png",
            SettingsCategory.Sounds => ".wav",
            SettingsCategory.Connections => ".json",
            _ => ".md",
        };
        // Sound and custom icon extensions are matched without case.
        var comparison = cat is SettingsCategory.Sounds or SettingsCategory.Toolbar ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (file.Length <= extension.Length || !file.EndsWith(extension, comparison)) return null;
        var stem = file[..^extension.Length];
        if (Reserved.Contains(stem.Split('.')[0].TrimEnd())) return null;

        var ok = cat switch
        {
            SettingsCategory.Toolbar => CustomIcons.IsFileName(file, stem),
            SettingsCategory.Sounds => IsSoundStem(stem),
            SettingsCategory.Connections => IsConnectionStem(stem),
            SettingsCategory.Actions => ToolbarModel.IsActionName(stem),
            _ => stem.Length <= 64 && stem == stem.Trim() && PromptLibrary.IsValidName(stem),
        };
        return ok ? (cat, name) : null;
    }

    // Names that Windows would shorten or reinterpret (trailing space or dot, "..") are not names.
    private static bool IsPlainStem(string stem, int max) =>
        stem.Length is > 0 && stem.Length <= max && stem[0] is not (' ' or '.') && stem[^1] is not (' ' or '.') && !stem.Contains("..");

    private static bool IsSoundStem(string stem) =>
        IsPlainStem(stem, 64) && stem.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '(' or ')' or '.');

    private static bool IsConnectionStem(string stem) =>
        SettingsStore.ValidConnectionId(stem);

    /// <summary>Why a file's content can't be written, or null when it is fine. The path has already passed <see cref="Classify"/>.</summary>
    internal static string? Validate(string dataDirectory, SettingsCategory category, string path, byte[] bytes)
    {
        if (bytes.Length > MaxEntryBytes) return "larger than 8 MB";
        switch (category)
        {
            case SettingsCategory.Sounds:
                return bytes.Length >= 12 && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8) && bytes.AsSpan(8, 4).SequenceEqual("WAVE"u8)
                    ? null : "not a .wav sound";
            case SettingsCategory.Toolbar when path.StartsWith("icons/", StringComparison.Ordinal):
                return CustomIcons.Validate(bytes) == CustomIconProblem.None ? null : "not a usable picture (PNG, 16-256 px)";
        }

        if (bytes.Contains((byte)0)) return "not a text file";
        var text = Encoding.UTF8.GetString(bytes);
        if (text.StartsWith('﻿')) text = text[1..];
        try
        {
            switch (category)
            {
                case SettingsCategory.Settings:
                    // The very reading loading uses: a wrong value or a newer enum word only costs that field (the app warns and goes on).
                    // A 0.6 file would replace a current one and then be reset by the upgrade: refuse it instead.
                    var parsed = SettingsStore.ParseSettings(text, new ReadProblems());
                    return parsed.Settings is null ? "not readable as settings" : parsed.Failure is not null ? "from Hotline 0.6 or older" : null;
                case SettingsCategory.Toolbar:
                    if (ToolbarModel.Parse(text, []) is not JsonObject toolbar) return "not a JSON object"; // field-level problems are the reader's warnings
                    return Files.JsonFile.VersionValue(toolbar) is { } version && version >= ToolbarModel.CurrentVersion ? null : "from Hotline 0.6 or older";
                case SettingsCategory.Mcp:
                    McpConfig.Parse(text);
                    return null;
                case SettingsCategory.Connections:
                    return ValidateConnection(path, text);
                case SettingsCategory.Actions:
                    return bytes.Length > QuickActions.MaxFileBytes ? "larger than a quick action can be (64 KB)" : null;
                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or FormatException or ArgumentException)
        {
            return "not readable";
        }
    }

    /// <summary>
    /// The very reading loading uses (<see cref="SettingsStore.ParseConnection"/>): refused only when it isn't a JSON object or its
    /// type is unknown. An id that differs from the file name is fine: the app saves the connection back to the file it came from.
    /// </summary>
    private static string? ValidateConnection(string path, string text) =>
        SettingsStore.ParseConnection(text, path, Path.GetFileNameWithoutExtension(path), new ReadProblems(), out _) is null
            ? "not readable as a connection" : null;

    private static string DecodeUtf8(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        return text.StartsWith('\uFEFF') ? text[1..] : text;
    }

    internal static string BuildExecutionReview(string rootDirectory, IReadOnlyList<(string Path, string Full, byte[] Data)> plan)
    {
        if (plan.Count == 0) return "";
        var planned = plan.ToDictionary(p => p.Path, p => p.Data, StringComparer.OrdinalIgnoreCase);
        var hasSettings = planned.ContainsKey("settings.json");
        var hasMcp = planned.ContainsKey("mcp.json");
        var hasConnections = plan.Any(p => p.Path.StartsWith("connections/", StringComparison.OrdinalIgnoreCase));
        if (!hasSettings && !hasMcp && !hasConnections) return "";

        var blocks = new List<string>();
        var anyInherit = false;
        if (!hasSettings)
        {
            var existing = Path.Combine(rootDirectory, SettingsStore.FileName);
            try
            {
                if (File.Exists(existing) && SettingsStore.ParseSettings(File.ReadAllText(existing), new ReadProblems()).Settings is { } current)
                    AppendRules(blocks, current.Tools.Approvals);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }

        if (hasSettings && planned.TryGetValue("settings.json", out var settingsBytes))
        {
            var (settings, legacy, _) = SettingsStore.ParseSettings(DecodeUtf8(settingsBytes), new ReadProblems());
            if (settings is not null) AppendRules(blocks, settings.Tools.Approvals);
            if (settings?.Chat?.ContinueIn is { } shared)
                AppendContinueTargets(blocks, "settings.json › chat.continueIn", shared);
            if (legacy is not null)
            {
                for (var i = 0; i < legacy.Count; i++)
                {
                    if (legacy[i] is not { } p) continue;
                    if (p.Tools == ToolMode.Inherit) anyInherit = true;
                    if (IsExecutableConnection(p)) AppendConnection(blocks, $"settings.json › chat.backends[{i}]", p);
                }
            }
        }

        foreach (var (path, _, data) in plan.Where(p => p.Path.StartsWith("connections/", StringComparison.OrdinalIgnoreCase)))
        {
            var profile = SettingsStore.ParseConnection(DecodeUtf8(data), path, Path.GetFileNameWithoutExtension(path), new ReadProblems(), out _);
            if (profile is null) continue;
            if (profile.Tools == ToolMode.Inherit) anyInherit = true;
            if (IsExecutableConnection(profile)) AppendConnection(blocks, path, profile);
            AppendEndpointChange(blocks, rootDirectory, path, profile);
        }

        if (hasSettings || hasMcp)
        {
            var connectionsDir = Path.Combine(rootDirectory, SettingsStore.ConnectionsFolder);
            if (Directory.Exists(connectionsDir))
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(connectionsDir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
                    {
                        var rel = $"{SettingsStore.ConnectionsFolder}/{Path.GetFileName(file)}";
                        if (planned.ContainsKey(rel)) continue;
                        var profile = SettingsStore.ParseConnection(File.ReadAllText(file), rel, Path.GetFileNameWithoutExtension(file), new ReadProblems(), out _);
                        if (profile is null) continue;
                        if (profile.Tools == ToolMode.Inherit) anyInherit = true;
                        if (hasSettings && IsExecutableConnection(profile)) AppendConnection(blocks, rel, profile);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }

        if (hasMcp && planned.TryGetValue("mcp.json", out var mcpBytes))
        {
            try { AppendMcp(blocks, "mcp.json", McpConfig.Parse(DecodeUtf8(mcpBytes))); }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or ArgumentException) { }
        }
        else if (anyInherit || hasSettings)
        {
            var existingMcp = Path.Combine(rootDirectory, "mcp.json");
            if (File.Exists(existingMcp))
            {
                try { AppendMcp(blocks, "mcp.json", McpConfig.Parse(File.ReadAllText(existingMcp))); }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or ArgumentException) { }
            }
        }

        return string.Join("\n\n", blocks);
    }

    private static bool IsExecutableContinueTarget(ContinueTarget? t) =>
        t is not null && (t.Kind == ContinueKind.App || !string.IsNullOrWhiteSpace(t.Command));

    private static bool IsExecutableConnection(ConnectionProfile p) =>
        ConnectionTypes.IsCli(p.Type)
        || p.Tools == ToolMode.Inherit
        || p.ApproveAllTools
        || !string.IsNullOrWhiteSpace(p.CliPath)
        || p.ExtraArgs is { Count: > 0 }
        || p.Args is { Count: > 0 }
        || !string.IsNullOrWhiteSpace(p.WorkingDirectory)
        || (p.ContinueIn is { } list && list.Any(IsExecutableContinueTarget));

    private static void AppendContinueTargets(List<string> blocks, string prefix, IReadOnlyList<ContinueTarget> targets)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (!IsExecutableContinueTarget(t)) continue;
            blocks.Add($"{prefix}[{i}] ({t.Name})\n  command: {t.Command ?? ""}");
        }
    }

    /// <summary>
    /// A replaced API connection that points somewhere else: its saved API key (kept in Credential Locker under the id) would
    /// go to the new address, so the review shows the change and asks.
    /// </summary>
    private static void AppendEndpointChange(List<string> blocks, string rootDirectory, string path, ConnectionProfile incoming)
    {
        if (ConnectionTypes.IsCli(incoming.Type)) return;
        var existing = Path.Combine(rootDirectory, path.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            if (!File.Exists(existing)) return;
            var current = SettingsStore.ParseConnection(File.ReadAllText(existing), path, Path.GetFileNameWithoutExtension(path), new ReadProblems(), out _);
            if (current is null) return;
            static string Where(ConnectionProfile p) => $"{p.Type} {(p.Endpoint ?? "").Trim().TrimEnd('/')}".Trim();
            if (string.Equals(Where(current), Where(incoming), StringComparison.OrdinalIgnoreCase)) return;
            blocks.Add($"{path} ({incoming.Id})\n  endpoint: {Where(current)} -> {Where(incoming)}\n  the API key saved for this connection is sent to the new address");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static void AppendConnection(List<string> blocks, string location, ConnectionProfile p)
    {
        var sb = new StringBuilder();
        var idSuffix = string.IsNullOrWhiteSpace(p.Id) ? "" : $" ({p.Id})";
        sb.Append($"{location}{idSuffix}");
        sb.Append($"\n  type: {JsonNamingPolicy.CamelCase.ConvertName(p.Type.ToString())}");
        sb.Append($"\n  tools: {JsonNamingPolicy.CamelCase.ConvertName(p.Tools.ToString())}");
        sb.Append($"\n  approveAllTools: {(p.ApproveAllTools ? "true" : "false")}");
        if (!string.IsNullOrWhiteSpace(p.CliPath)) sb.Append($"\n  cliPath: {p.CliPath}");
        if (p.Type == BackendType.Antigravity && !string.IsNullOrWhiteSpace(p.Agent)) sb.Append($"\n  agent: {p.Agent}");
        if (!string.IsNullOrWhiteSpace(p.WorkingDirectory)) sb.Append($"\n  workingDirectory: {p.WorkingDirectory}");
        if (p.ExtraArgs is { Count: > 0 }) sb.Append($"\n  extraArgs: {CliArguments.Join(p.ExtraArgs)}");
        if (p.Args is { Count: > 0 }) sb.Append($"\n  args: {string.Join(" ", p.Args)}");
        blocks.Add(sb.ToString());
        if (p.ContinueIn is { } list)
            AppendContinueTargets(blocks, $"{location} › continueIn", list);
    }

    private static void AppendMcp(List<string> blocks, string location, McpConfig mcp)
    {
        foreach (var s in mcp.Servers)
        {
            var sb = new StringBuilder();
            sb.Append($"{location} › mcpServers.{s.Name}");
            sb.Append($"\n  enabled: {(!s.Disabled ? "true" : "false")}");
            sb.Append($"\n  command: {s.Command}");
            if (s.Args.Count > 0) sb.Append($"\n  args: {string.Join(" ", s.Args)}");
            if (!string.IsNullOrWhiteSpace(s.WorkingDirectory)) sb.Append($"\n  cwd: {s.WorkingDirectory}");
            if (s.Env.Count > 0)
                sb.Append($"\n  env: {string.Join(", ", s.Env.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => $"{kv.Key}=***"))}");
            blocks.Add(sb.ToString());
        }
    }

    private static void AppendRules(List<string> blocks, IReadOnlyDictionary<string, string> rules)
    {
        if (rules.Count == 0) return;
        blocks.Add("settings.json › tools.approvals\n" + string.Join("\n", rules
            .OrderBy(p => string.Equals(p.Value, "allow", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"  {p.Key}: {p.Value}")));
    }
}
