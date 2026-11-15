namespace Hotline.Core.Files;

public sealed class RealFileSystem : IFileSystem
{
    // File.Exists/Directory.Exists hide access errors, which must not look like a missing config file.
    private static FileAttributes? Attributes(string path)
    {
        try { return File.GetAttributes(path); }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    public bool FileExists(string path) => Attributes(path) is { } attributes && !attributes.HasFlag(FileAttributes.Directory);
    public bool DirectoryExists(string path) => Attributes(path) is { } attributes && attributes.HasFlag(FileAttributes.Directory);
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void WriteDurable(string path, byte[] bytes) => DurableFile.WriteBytes(path, bytes);
    public IEnumerable<string> EnumerateDirectories(string path) => Directory.EnumerateDirectories(path);
    public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);

    public void CopyFile(string source, string destination)
    {
        var created = false;
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            input.CopyTo(output);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            try { if (created) File.Delete(destination); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }
}
