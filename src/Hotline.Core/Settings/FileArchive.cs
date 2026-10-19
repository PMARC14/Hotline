namespace Hotline.Core.Settings;

/// <summary>Keeps a safety copy of a file before a migration rewrites it.</summary>
public static class FileArchive
{
    /// <summary>
    /// Copies <paramref name="path"/> to <c>path.old</c>, or the first free <c>.old2</c>, <c>.old3</c>… so an earlier
    /// archive is never overwritten. The original is left in place. Returns the archive path, or null when the file
    /// doesn't exist or the copy fails (the caller carries on without an archive).
    /// </summary>
    public static string? KeepOld(string path)
    {
        if (!File.Exists(path)) return null;
        for (var n = 1; ; n++)
        {
            var archive = path + (n == 1 ? ".old" : ".old" + n);
            if (File.Exists(archive)) continue;
            try
            {
                File.Copy(path, archive, overwrite: false);
                return archive;
            }
            catch (IOException) when (File.Exists(archive)) { /* lost a race for this name; try the next */ }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
        }
    }
}
