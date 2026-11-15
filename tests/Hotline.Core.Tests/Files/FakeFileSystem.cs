using Hotline.Core.Files;

namespace Hotline.Core.Tests.Files;

internal sealed class FakeFileSystem : IFileSystem
{
    internal sealed record Call(string Operation, string Path, string? Destination = null);
    public Dictionary<string, byte[]> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<Call> Calls { get; } = [];
    public List<Call> Writes { get; } = [];
    public Func<Call, bool>? Fail { get; set; }
    public int? FailOnCall { get; set; }

    private void Record(string operation, string path, string? destination = null, bool write = false)
    {
        var call = new Call(operation, path, destination);
        Calls.Add(call);
        if (write) Writes.Add(call);
        if (Calls.Count == FailOnCall || Fail?.Invoke(call) == true) throw new IOException("Injected failure: " + operation);
    }

    public bool FileExists(string path) { Record(nameof(FileExists), path); return Files.ContainsKey(path); }
    public bool DirectoryExists(string path) { Record(nameof(DirectoryExists), path); return Directories.Contains(path); }
    public byte[] ReadAllBytes(string path)
    {
        Record(nameof(ReadAllBytes), path);
        return Files.TryGetValue(path, out var bytes) ? bytes.ToArray() : throw new FileNotFoundException();
    }
    public void CreateDirectory(string path)
    {
        Record(nameof(CreateDirectory), path, write: true);
        for (var parent = path; parent is not null; parent = Path.GetDirectoryName(parent)) Directories.Add(parent);
    }
    public void CopyFile(string source, string destination)
    {
        Record(nameof(CopyFile), source, destination, write: true);
        if (!Directories.Contains(Path.GetDirectoryName(destination)!)) throw new DirectoryNotFoundException();
        if (!Files.TryAdd(destination, Files[source].ToArray())) throw new IOException("Destination exists");
    }
    public void WriteDurable(string path, byte[] bytes)
    {
        Record(nameof(WriteDurable), path, write: true);
        if (!Directories.Contains(Path.GetDirectoryName(path)!)) throw new DirectoryNotFoundException();
        Files[path] = bytes.ToArray();
    }
    public IEnumerable<string> EnumerateDirectories(string path)
    {
        Record(nameof(EnumerateDirectories), path);
        if (!Directories.Contains(path)) throw new DirectoryNotFoundException();
        return Directories.Where(d => string.Equals(Path.GetDirectoryName(d), path, StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public void DeleteDirectory(string path, bool recursive)
    {
        Record(nameof(DeleteDirectory), path, write: true);
        var prefix = path + Path.DirectorySeparatorChar;
        if (!recursive && (Files.Keys.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
                           Directories.Any(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))) throw new IOException("Not empty");
        foreach (var file in Files.Keys.Where(p => p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray()) Files.Remove(file);
        Directories.RemoveWhere(p => p.Equals(path, StringComparison.OrdinalIgnoreCase) || p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
