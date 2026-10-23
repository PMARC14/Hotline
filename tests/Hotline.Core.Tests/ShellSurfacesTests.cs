using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class ShellSurfacesTests
{
    [Theory]
    [InlineData("Shell_TrayWnd", true)]
    [InlineData("Shell_SecondaryTrayWnd", true)]
    [InlineData("Progman", true)]
    [InlineData("WorkerW", true)]
    [InlineData("Windows.UI.Core.CoreWindow", true)]   // Start / Search flyouts
    [InlineData("XamlExplorerHostIslandWindow", true)] // Win11 shell flyouts (task view, widgets)
    [InlineData("CopilotKeyPressFocusFrame", true)]    // 26H2: Explorer holds focus for a moment on each Copilot key press
    [InlineData("Chrome_WidgetWin_1", false)]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS", false)]
    [InlineData("WindowsForms10.Window.8.app.0.1", false)]
    public void Only_shell_surfaces_are_skipped_as_capture_targets(string cls, bool isShell)
        => Assert.Equal(isShell, ShellSurfaces.IsShell(cls));
}
