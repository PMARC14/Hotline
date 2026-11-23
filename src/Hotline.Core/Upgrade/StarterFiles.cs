using Hotline.Core.Backends;
using Hotline.Core.Files;
using Hotline.Core.State;

namespace Hotline.Core.Upgrade;

/// <summary>Creates missing configuration before loading; keeps embedded reference copies current.</summary>
public static class StarterFiles
{
    internal static readonly string[] Families = ["settings", "toolbar", "mcp"];

    public static byte[] DefaultBytes(string family)
    {
        using var stream = typeof(StarterFiles).Assembly.GetManifestResourceStream($"Hotline.Core.Defaults.{family}.json")
            ?? throw new ArgumentException("Unknown defaults family.", nameof(family));
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return bytes.ToArray();
    }

    public static IReadOnlyList<string> Ensure(string directory, IFileSystem files, StateStore state, ISecretStore secrets)
    {
        var failed = new List<string>();
        var writer = new SafeFile(files);
        var backup = new BackupSet(directory, BackupReason.Save, files);
        foreach (var family in Families)
        {
            var name = family + ".json";
            var bytes = DefaultBytes(family);
            try
            {
                var path = Path.Combine(directory, name);
                if (!files.FileExists(path))
                {
                    var result = writer.Replace(path, bytes, backup, family == "toolbar" ? 3 : 1);
                    if (result == SafeFileResult.Written) state.SetLastWritten(name, SafeFile.Hash(bytes));
                    else if (result != SafeFileResult.Unchanged) failed.Add(name);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add(name); }
            try
            {
                var folder = Path.Combine(directory, Settings.SettingsReset.DefaultsFolder);
                var path = Path.Combine(folder, name);
                if (files.FileExists(path) && files.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
                files.CreateDirectory(folder);
                if (files is RealFileSystem) new DirectoryInfo(folder).Attributes |= FileAttributes.Hidden;
                files.WriteDurable(path, bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed.Add($".defaults/{name}"); }
        }
        return failed;
    }
}
