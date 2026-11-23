using Hotline.Core.Files;

namespace Hotline.Core.Tests;

/// <summary>Real temp files with an injectable backup failure for service integration tests.</summary>
internal sealed class SettingsFileSystem : IFileSystem
{
    private readonly RealFileSystem _real = new();
    internal Action<string>? BeforeCopy { get; set; }
    public bool FileExists(string path) => _real.FileExists(path);
    public bool DirectoryExists(string path) => _real.DirectoryExists(path);
    public byte[] ReadAllBytes(string path) => _real.ReadAllBytes(path);
    public void CreateDirectory(string path) => _real.CreateDirectory(path);
    public void CopyFile(string source, string destination) { BeforeCopy?.Invoke(source); _real.CopyFile(source, destination); }
    public void WriteDurable(string path, byte[] bytes) => _real.WriteDurable(path, bytes);
    public IEnumerable<string> EnumerateDirectories(string path) => _real.EnumerateDirectories(path);
    public IEnumerable<string> EnumerateFiles(string path, string pattern) => _real.EnumerateFiles(path, pattern);
    public void DeleteFile(string path) => _real.DeleteFile(path);
    public void DeleteDirectory(string path, bool recursive) => _real.DeleteDirectory(path, recursive);
}

internal static class SettingsTestFiles
{
    internal static string[] Backups(string directory, string fileName) => Directory.Exists(Path.Combine(directory, "backups"))
        ? Directory.GetFiles(Path.Combine(directory, "backups"), fileName, SearchOption.AllDirectories) : [];
}
