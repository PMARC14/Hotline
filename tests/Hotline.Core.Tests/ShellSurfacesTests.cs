using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class ShellSurfacesTests
{
    [Theory]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Windows.UI.Core.CoreWindow")]   // Start / Search flyouts
    [InlineData("XamlExplorerHostIslandWindow")] // Win11 shell flyouts (task view, widgets)
    public void Shell_surfaces_are_not_capture_targets(string cls)
        => Assert.True(ShellSurfaces.IsShell(cls));

    [Theory]
    [InlineData("Chrome_WidgetWin_1")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS")]
    [InlineData("WindowsForms10.Window.8.app.0.1")]
    public void App_windows_are_capture_targets(string cls)
        => Assert.False(ShellSurfaces.IsShell(cls));
}
