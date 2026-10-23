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
        // A .wav may be spelled .WAV by whatever made it; the rest are written by Hotline in lower case.
        var comparison = cat == SettingsCategory.Sounds ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
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
        IsPlainStem(stem, 100) && stem.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');

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
                    return JsonSerializer.Deserialize<HotlineSettings>(text, SettingsStore.Options) is null ? "not readable as settings" : null;
                case SettingsCategory.Toolbar:
                    return ToolbarModel.Parse(text, []) is JsonObject ? null : "not a JSON object"; // field-level problems are the reader's warnings
                case SettingsCategory.Mcp:
                    McpConfig.Parse(text);
                    return null;
                case SettingsCategory.Connections:
                    return ValidateConnection(dataDirectory, path, text);
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

    private static string? ValidateConnection(string dataDirectory, string path, string text)
    {
        var profile = JsonSerializer.Deserialize<BackendProfile>(text, SettingsStore.ConnectionOptions);
        if (profile is null) return "not readable as a connection";
        // The store reads a file with no id as its file name, and names the file of an id with odd characters by a safe form.
        var stem = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(profile.Id)) return null;
        var safe = Path.GetFileNameWithoutExtension(new SettingsStore(dataDirectory).ConnectionPath(profile.Id));
        return profile.Id.Equals(stem, StringComparison.OrdinalIgnoreCase) || safe.Equals(stem, StringComparison.OrdinalIgnoreCase)
            ? null : "its id doesn't match the file name";
    }
}
