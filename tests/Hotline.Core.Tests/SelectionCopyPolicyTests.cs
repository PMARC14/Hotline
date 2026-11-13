using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class SelectionCopyPolicyTests
{
    [Theory]
    [InlineData("PuTTY", "putty")]
    [InlineData("org.wezfurlong.wezterm", "wezterm-gui")]
    [InlineData("Alacritty", "alacritty")]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS", "WindowsTerminal")]
    [InlineData("ConsoleWindowClass", "conhost")]
    [InlineData("TscShellContainerClass", "mstsc")]
    [InlineData("RAIL_WINDOW", "msrdc")]
    [InlineData("VMUIFrame", "vmware")]
    [InlineData("Qt5152QWindowIcon", "VirtualBoxVM")]
    [InlineData("mintty", "mintty")]
    [InlineData("VirtualConsoleClass", "ConEmu64")]
    [InlineData("PseudoConsoleWindow", "OpenConsole")]
    [InlineData("Chrome_WidgetWin_1", "Code")]      // an editor with an integrated terminal
    [InlineData("SunAwtFrame", "idea64")]
    [InlineData("SomeWindowClass", "PowerShell")]
    [InlineData("RAIL_WINDOW", "notepad")]           // the class alone is enough
    [InlineData("Notepad", "putty")]                 // so is the process
    public void Terminals_remote_consoles_and_virtual_machines_never_receive_ctrl_c(string cls, string process)
        => Assert.False(SelectionCopyPolicy.MaySendCtrlC(cls, process));

    [Theory]
    [InlineData("Notepad", null)]
    [InlineData("Notepad", "")]
    [InlineData(null, null)]
    public void A_window_whose_process_cannot_be_identified_never_receives_ctrl_c(string? cls, string? process)
        => Assert.False(SelectionCopyPolicy.MaySendCtrlC(cls, process));

    [Theory]
    [InlineData("Notepad", "notepad")]
    [InlineData("OpusApp", "WINWORD")]
    [InlineData("WordPadClass", "wordpad")]
    [InlineData("Chrome_WidgetWin_1", "chrome")]
    [InlineData("SomeLegacyWin32Class", "legacyapp")]
    [InlineData(null, "legacyapp")]
    public void Other_apps_may_receive_ctrl_c_in_clipboard_mode(string? cls, string process)
        => Assert.True(SelectionCopyPolicy.MaySendCtrlC(cls, process));
}
