using System.Text.Json;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// Groups Hotline's agy conversations in one agy project. agy names a project after the folder it was created in
/// (Hotline's chat-only folder is called "Hotline") and keeps it in ~/.gemini/config/projects/&lt;id&gt;.json with that
/// folder. Hotline only reads those files: a new conversation joins the project for its folder, or creates it with
/// --new-project when there is none yet. Resumed conversations get no project flags, so one the user moved elsewhere
/// stays there.
/// </summary>
public sealed class AgyProjects(string projectsDirectory)
{
    private bool _created;

    public static string DirectoryFor(string homeDirectory) => Path.Combine(homeDirectory, ".gemini", "config", "projects");

    /// <summary>Flags for a new conversation run in <paramref name="folder"/>.</summary>
    public IReadOnlyList<string> ArgsFor(string folder)
    {
        if (Find(folder) is { } id && Chat.CliSession.IsValidId(id)) return ["--project", id];
        if (_created) return []; // asked agy to create it once and still can't see it: don't make one per chat
        _created = true;
        return ["--new-project"];
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
