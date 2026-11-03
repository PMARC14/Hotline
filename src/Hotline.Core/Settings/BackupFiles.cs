namespace Hotline.Core.Settings;

/// <summary>
/// The timestamped copies a file keeps before it is overwritten: <c>mcp.json.bak-20261022-153000</c> (mcp.json) or
/// <c>settings.json.20261022-153000.bak</c> (settings and connection files; see <see cref="CopyStamped"/>).
/// </summary>
public static class BackupFiles
{
    /// <summary>
    /// Copies <paramref name="path"/> to <c>&lt;path&gt;.&lt;yyyyMMdd-HHmmss&gt;.bak</c> (a <c>-002</c>, <c>-003</c>... before <c>.bak</c> in the same
    /// second), never over an earlier copy, then prunes to the newest <paramref name="keep"/>. Throws what <see cref="File.Copy(string,string)"/>
    /// throws when the copy can't be made; the caller must then not replace the file.
    /// </summary>
    public static string CopyStamped(string path, DateTime? now = null, int keep = 10)
    {
        var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss");
        // The next free name after every copy of this second that exists, so a name freed by pruning is never reused as an "older" copy.
        var last = 0;
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder && Directory.Exists(folder))
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder, $"{Path.GetFileName(path)}.{stamp}*.bak"))
            {
                var rest = Path.GetFileName(entry)[(Path.GetFileName(path).Length + stamp.Length + 1)..^4]; // "" or "-002"
                last = Math.Max(last, rest.Length == 0 ? 1 : rest.StartsWith('-') && int.TryParse(rest.AsSpan(1), out var n) ? n : 1);
            }
        string target;
        while (true)
        {
            target = last == 0 ? $"{path}.{stamp}.bak" : $"{path}.{stamp}-{last + 1:000}.bak";
            try { File.Copy(path, target, overwrite: false); break; }
            // EEXIST (Unix), ERROR_FILE_EXISTS / ERROR_ALREADY_EXISTS (Windows). A failed copy that created a
            // partial destination (e.g. disk full) must still fail, rather than retrying it as a name collision.
            catch (IOException ex) when ((ex.HResult & 0xffff) is 17 or 80 or 183 && File.Exists(target)) { last++; }
        }
        Prune(path, keep);
        return target;
    }

    /// <summary>Keeps the newest <paramref name="keep"/> copies next to <paramref name="path"/> and deletes the older ones (best effort).</summary>
    public static void Prune(string path, int keep = 10)
    {
        try
        {
            if (Path.GetDirectoryName(path) is not { Length: > 0 } folder || !Directory.Exists(folder)) return;
            var name = Path.GetFileName(path);
            var dir = new DirectoryInfo(folder);
            // The stamp sorts by time as text; a "-n" suffix (same second) sorts after its original (".bak" is left out of the key).
            var older = dir.EnumerateFiles(name + ".bak-*")
                .OrderByDescending(f => f.Name, StringComparer.Ordinal).Skip(Math.Max(0, keep)).ToList();
            older.AddRange(dir.EnumerateFiles(name + ".*.bak")
                .Where(f => f.Name.Length > name.Length + 5)
                .OrderByDescending(f => f.Name[..^4], StringComparer.Ordinal).Skip(Math.Max(0, keep)));
            foreach (var file in older)
                try { file.Delete(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* in use or read-only: leave it */ }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
