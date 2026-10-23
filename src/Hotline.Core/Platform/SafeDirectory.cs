namespace Hotline.Core.Platform;

/// <summary>
/// Is a path from stored data or a link on this PC's own drives? Windows connects to a share the moment a UNC path
/// is touched (even Directory.Exists), sending the user's NetNTLM hash, so such paths are refused before any
/// filesystem call. The check is textual and treats / like \ (Windows accepts both).
/// </summary>
public static class SafeDirectory
{
    /// <summary>A rooted path on a drive letter (C:\… or C:/…): never a share, device path, relative or drive-relative path.</summary>
    public static bool IsLocal(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0')) return false;
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/')) return false;
        return Path.IsPathFullyQualified(path);
    }
}
