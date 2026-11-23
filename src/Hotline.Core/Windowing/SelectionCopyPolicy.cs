namespace Hotline.Core.Windowing;

/// <summary>
/// Whether Hotline may send Ctrl+C to the window it came from to read the selection (chat.attachSelection: clipboard, only where
/// UI Automation reported no text pattern). Ctrl+C is an interrupt (SIGINT) in terminals and is forwarded by remote and virtual
/// machine windows, so those never get it. Everything else may, except a window whose process cannot be identified.
/// </summary>
public static class SelectionCopyPolicy
{
    private static readonly HashSet<string> DeniedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "PseudoConsoleWindow", "mintty", "VirtualConsoleClass",
        "PuTTY", "KiTTY", "org.wezfurlong.wezterm", "Alacritty", "TscShellContainerClass", "RAIL_WINDOW", "VMUIFrame",
        "UIWindow", "Hyper", "XShell", "SecureCRT",
    };

    // Terminals, shells, remote desktop clients, virtual machine consoles, and IDEs and editors with an embedded terminal.
    private static readonly HashSet<string> DeniedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "putty", "kitty", "wezterm", "wezterm-gui", "alacritty", "windowsterminal", "wt", "conhost", "openconsole",
        "mintty", "conemu", "conemu64", "cmd", "powershell", "pwsh", "bash", "wsl", "wslhost", "tabby", "hyper", "terminus",
        "xshell", "securecrt", "mobaxterm", "rio", "ghostty", "warp", "mstsc", "msrdc", "msrdcw", "vmware", "vmware-vmx",
        "vmplayer", "vmconnect", "virtualbox", "virtualboxvm", "vboxsvc", "parsecd", "anydesk", "teamviewer",
        "code", "code - insiders", "cursor", "windsurf", "devenv", "rider64", "idea64", "pycharm64", "webstorm64", "clion64",
        "goland64", "datagrip64", "rubymine64", "phpstorm64",
    };

    public static bool MaySendCtrlC(string? windowClass, string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false; // unknown identity cannot be cleared
        if (windowClass is not null && DeniedClasses.Contains(windowClass)) return false;
        return !DeniedProcesses.Contains(processName);
    }
}
