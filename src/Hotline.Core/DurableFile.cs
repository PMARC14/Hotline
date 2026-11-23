using System.Runtime.CompilerServices;
using System.Text;

[assembly: InternalsVisibleTo("Hotline.Core.Tests")]

namespace Hotline.Core;

/// <summary>Publishes a complete file after its contents have been flushed to disk.</summary>
public static class DurableFile
{
    public static void WriteText(string path, string content) => WriteBytes(path, Encoding.UTF8.GetBytes(content));

    public static void WriteBytes(string path, byte[] content) => WriteBytes(path, content, stream => stream.Flush(flushToDisk: true));

    internal static void WriteBytes(string path, byte[] content, Action<FileStream> flush)
    {
        path = Path.GetFullPath(path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var created = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                stream.Write(content);
                flush(stream);
            }
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            try { if (created) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
