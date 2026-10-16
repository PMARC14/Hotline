using System.Text.Json;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// Groups Hotline's agy conversations in one agy project ("Hotline", created by earlier versions, which ran agy in a
/// folder of that name). agy keeps projects in ~/.gemini/config/projects/&lt;id&gt;.json. Hotline only reads those files.
/// Resumed conversations get no project flags, so one the user moved elsewhere stays there.
/// </summary>
public sealed class AgyProjects(string projectsDirectory)
{
    /// <summary>
    /// Flags for a new chat-only conversation: the project holding <paramref name="folder"/> (Hotline's workspace), or
    /// the one named "Hotline". None when neither exists: Hotline never creates one, since agy would name a new project
    /// after the folder agy runs in (the home folder).
    /// </summary>
    public IReadOnlyList<string> ArgsFor(string folder) =>
        (Find(folder) ?? FindByName("Hotline")) is { } id && Chat.CliSession.IsValidId(id) ? ["--project", id] : [];

    private string? FindByName(string name)
    {
        if (!Directory.Exists(projectsDirectory)) return null;
        foreach (var file in Directory.EnumerateFiles(projectsDirectory, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() == name
                    && root.TryGetProperty("id", out var idEl) && idEl.GetString() is { Length: > 0 } id) return id;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { }
        }
        return null;
    }

    /// <summary>The ID of the agy project that holds <paramref name="folder"/>, if any.</summary>
    public string? Find(string folder)
    {
        if (!Directory.Exists(projectsDirectory)) return null;
        var wanted = Normalize(folder);
        foreach (var file in Directory.EnumerateFiles(projectsDirectory, "*.json"))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (!root.TryGetProperty("id", out var idEl) || idEl.GetString() is not { Length: > 0 } id) continue;
                if (!root.TryGetProperty("projectResources", out var res) || !res.TryGetProperty("resources", out var list)
                    || list.ValueKind != JsonValueKind.Array) continue;
                foreach (var r in list.EnumerateArray())
                    if (r.TryGetProperty("folderUri", out var uri) && uri.GetString() is { } u && Normalize(FromUri(u)) == wanted) return id;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException) { }
        }
        return null;
    }

    /// <summary>agy writes "file://C:/Users/…" (two slashes); also accept the standard three.</summary>
    private static string FromUri(string uri)
    {
        var path = uri.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) ? uri[8..]
            : uri.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ? uri[7..] : uri;
        return Uri.UnescapeDataString(path).Replace('/', Path.DirectorySeparatorChar);
    }

    private static string Normalize(string path)
    {
        try { path = Path.GetFullPath(path); } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
    }
}
