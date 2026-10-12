namespace Hotline.Core.Processes;

/// <summary>
/// When Hotline last started a helper program (a CLI, an MCP server). A console window that takes focus soon after is
/// almost certainly one of those (or something it started), not the user clicking away, so the panel keeps focus.
/// </summary>
public static class ProcessLaunches
{
    private static long _lastMs = long.MinValue / 2;

    public static void Noted() => Interlocked.Exchange(ref _lastMs, Environment.TickCount64);

    public static bool Recent(TimeSpan window) => Environment.TickCount64 - Interlocked.Read(ref _lastMs) < (long)window.TotalMilliseconds;
}
