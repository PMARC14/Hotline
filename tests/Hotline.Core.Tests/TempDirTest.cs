namespace Hotline.Core.Tests;

/// <summary>An isolated folder for tests whose only teardown is deleting their temporary files.</summary>
public abstract class TempDirTest : IDisposable
{
    protected readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (!Directory.Exists(_dir)) return;
            foreach (var file in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
