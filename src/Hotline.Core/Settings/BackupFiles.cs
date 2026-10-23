namespace Hotline.Core.Settings;

/// <summary>The timestamped copies a file keeps before it is overwritten (<c>mcp.json.bak-20261022-153000</c>).</summary>
public static class BackupFiles
{
    /// <summary>Keeps the newest <paramref name="keep"/> copies next to <paramref name="path"/> and deletes the older ones (best effort).</summary>
    public static void Prune(string path, int keep = 10)
    {
        try
        {
            if (Path.GetDirectoryName(path) is not { Length: > 0 } folder || !Directory.Exists(folder)) return;
            // The stamp sorts by time as text; a "-n" suffix (same second) sorts after its original.
            var older = new DirectoryInfo(folder).EnumerateFiles(Path.GetFileName(path) + ".bak-*")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(Math.Max(0, keep)).ToList();
            foreach (var file in older)
                try { file.Delete(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* in use or read-only: leave it */ }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
