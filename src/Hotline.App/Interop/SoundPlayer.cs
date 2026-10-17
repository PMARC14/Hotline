using Hotline.Core.Diagnostics;
using Hotline.Core.Windowing;
using Microsoft.Win32;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace Hotline.App.Interop;

/// <summary>
/// window.openSound / window.closeSound (Core <see cref="Sounds"/> decides what may play). Each sound is a preloaded
/// MediaPlayer (sound-effects category): it starts at once, and an open and a close sound in quick succession both play
/// (PlaySound plays one sound per process and cut the earlier one off; loading the file on every play made it late).
/// </summary>
internal static class SoundPlayer
{
    private const string Scheme = @"AppEvents\Schemes\Apps\.Default";
    private static readonly Dictionary<string, MediaPlayer> Players = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string?> Resolved = new(StringComparer.OrdinalIgnoreCase);

    public static string Directory(string dataDirectory) => Path.Combine(dataDirectory, "sounds");

    /// <summary>Loads the sounds the settings name now (at start and after a settings change), so a play is instant.</summary>
    public static void Preload(IEnumerable<string?> settings, string soundsDirectory, FileLog log)
    {
        Resolved.Clear(); // a file or the sound scheme may have changed
        foreach (var setting in settings) _ = PlayerFor(setting, soundsDirectory, log);
    }

    public static void Play(string? setting, string soundsDirectory, FileLog log)
    {
        if (Sounds.Parse(setting).Kind == SoundKind.Off) return;
        if (PlayerFor(setting, soundsDirectory, log) is not { } player) return;
        try
        {
            player.PlaybackSession.Position = TimeSpan.Zero;
            player.Play();
        }
        catch (Exception ex) { log.Error("playing a sound failed", ex); }
    }

    private static MediaPlayer? PlayerFor(string? setting, string soundsDirectory, FileLog log)
    {
        var key = setting?.Trim() ?? "";
        if (Sounds.Parse(key).Kind == SoundKind.Off) return null;
        if (!Resolved.TryGetValue(key, out var path))
        {
            path = Sounds.Resolve(soundsDirectory, Sounds.Parse(key), EventPath);
            Resolved[key] = path;
            if (path is null) log.Info($"sound '{key}' not found; nothing will play");
        }
        if (path is null) return null;
        if (Players.TryGetValue(path, out var existing)) return existing;
        try
        {
            var player = new MediaPlayer
            {
                AudioCategory = MediaPlayerAudioCategory.SoundEffects,
                Source = MediaSource.CreateFromUri(new Uri(path)),
                AutoPlay = false,
            };
            Players[path] = player;
            return player;
        }
        catch (Exception ex) { log.Error($"loading sound {path} failed", ex); return null; }
    }

    /// <summary>A Windows sound event's .wav in the current scheme (empty when the event has no sound).</summary>
    public static string? EventPath(string name)
    {
        if (!Sounds.IsEventName(name)) return null; // never a path into other keys
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{Scheme}\{name}\.Current");
            return key?.GetValue("") as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException) { return null; }
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
