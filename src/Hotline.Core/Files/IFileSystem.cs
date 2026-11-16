namespace Hotline.Core.Files;

/// <summary>File access used by the config writer and its backup sets. Failures throw IO or access exceptions.</summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    byte[] ReadAllBytes(string path);
    void CreateDirectory(string path);
    /// <summary>Copies and flushes a file, refusing an existing destination.</summary>
    void CopyFile(string source, string destination);
    /// <summary>Flushes complete contents before atomically publishing them at the destination.</summary>
    void WriteDurable(string path, byte[] bytes);
    IEnumerable<string> EnumerateDirectories(string path);
    IEnumerable<string> EnumerateFiles(string path, string pattern);
    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);
}
