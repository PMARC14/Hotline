namespace Hotline.Core.Windowing;

/// <summary>
/// Shell windows (taskbar, desktop, Start/Search flyouts) can be "foreground" when the Copilot key is pressed,
/// but they are never what the user means by "the window I was in" — skip them as capture/placement anchors.
/// </summary>
public static class ShellSurfaces
{
    private static readonly HashSet<string> Classes = new(StringComparer.Ordinal)
    {
        "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Progman", "WorkerW",
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow",
    };

    public static bool IsShell(string className) => Classes.Contains(className);
}
