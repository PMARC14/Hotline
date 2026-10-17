using System.Runtime.InteropServices;
using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;
using Microsoft.Win32;

namespace Hotline.App.Interop;

/// <summary>
/// window.openSound / window.closeSound (Core <see cref="Sounds"/> decides what may play). PlaySound runs on the thread
/// pool: even asynchronous, it can stall while a sleeping audio device wakes. SND_NODEFAULT: a missing sound plays
/// nothing rather than the default beep.
/// </summary>
internal static class SoundPlayer
{
    private const uint SND_ASYNC = 0x1, SND_NODEFAULT = 0x2, SND_FILENAME = 0x20000;
    private const string Scheme = @"AppEvents\Schemes\Apps\.Default";

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)] private static extern bool PlaySound(string? sound, nint module, uint flags);

    public static string Directory(string dataDirectory) => Path.Combine(dataDirectory, "sounds");

    public static void Play(string? setting, string soundsDirectory, FileLog log)
    {
        var choice = Sounds.Parse(setting);
        if (choice.Kind == SoundKind.Off) return;
        var path = Sounds.Resolve(soundsDirectory, choice, EventPath);
        if (path is null) { log.Info($"sound '{setting}' not found; nothing played"); return; }
        _ = Task.Run(() =>
        {
            try { PlaySound(path, 0, SND_FILENAME | SND_ASYNC | SND_NODEFAULT); }
            catch (Exception ex) { log.Error("playing a sound failed", ex); }
        });
    }

    /// <summary>A Windows sound event's .wav in the current scheme (empty when the event has no sound).</summary>
    public static string? EventPath(string name)
    {
        try { return Registry.CurrentUser.OpenSubKey($@"{Scheme}\{name}\.Current")?.GetValue("") as string; }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return null; }
    }

    /// <summary>The Windows sound events that have a sound in the current scheme (for the Settings picker).</summary>
    public static IReadOnlyList<string> WindowsEvents()
    {
        try
        {
            using var apps = Registry.CurrentUser.OpenSubKey(Scheme);
            return apps?.GetSubKeyNames().Where(n => !string.IsNullOrWhiteSpace(EventPath(n))).Order(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return []; }
    }

    /// <summary>The .wav files in the sounds folder.</summary>
    public static IReadOnlyList<string> Files(string soundsDirectory)
    {
        try { return System.IO.Directory.Exists(soundsDirectory) ? System.IO.Directory.EnumerateFiles(soundsDirectory, "*.wav").Select(Path.GetFileName).OfType<string>().Order(StringComparer.OrdinalIgnoreCase).ToList() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
}
