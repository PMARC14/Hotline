namespace Hotline.Core.Platform;

/// <summary>The Windows build and revision (e.g. 26200.9457).</summary>
public readonly record struct WindowsBuild(int Build, int Revision)
{
    /// <summary>From "10.0.26200.9457" (or "26200.9457"); 0.0 when it can't be read.</summary>
    public static WindowsBuild Parse(string version)
    {
        var parts = version.Split('.');
        int At(int fromEnd) => parts.Length >= fromEnd && int.TryParse(parts[^fromEnd], out var n) ? n : 0;
        return parts.Length >= 4 ? new WindowsBuild(At(2), At(1)) : new WindowsBuild(At(parts.Length >= 2 ? 2 : 1), parts.Length >= 2 ? At(1) : 0);
    }

    public override string ToString() => $"{Build}.{Revision}";
}

/// <summary>
/// Which Windows features exist on a build — one place for the thresholds, so every feature checks the same way
/// before it's used (and Settings › About can say why something isn't there).
/// </summary>
public static class PlatformSupport
{
    /// <summary>App Actions on Windows (Click to Do, the action catalog): Windows 11 24H2 (26100) and later.</summary>
    public static bool AppActions(WindowsBuild v) => v.Build >= 26100;

    /// <summary>The on-device agent registry (odr.exe) with Windows' MCP connectors: 26220.7262 and later.</summary>
    public static bool AgentConnectors(WindowsBuild v) => v.Build > 26220 || (v.Build == 26220 && v.Revision >= 7262);
}
