namespace Hotline.Core.Diagnostics;

/// <summary>Minimal thread-safe append logger with one-file rotation. Never throws. Every message goes through <see cref="LogText.Clean"/>.</summary>
public sealed class FileLog(string path, long maxBytes = 1_000_000)
{
    private readonly Lock _gate = new();

    /// <summary>When true, <see cref="Debug"/> lines are written.</summary>
    public bool Verbose { get; set; }

    public void Debug(string message)
    {
        if (Verbose) Write("DEBUG", message);
    }

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            try
            {
                if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder); // a bare file name has none
                if (File.Exists(path) && new FileInfo(path).Length > maxBytes)
                    File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {level} {LogText.Clean(message)}{Environment.NewLine}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
