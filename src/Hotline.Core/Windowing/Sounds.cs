namespace Hotline.Core.Windowing;

public enum SoundKind { Off, Windows, File }

/// <summary>A sound setting: off, a Windows sound event ("windows:Notification.Default"), or a .wav in ~/.hotline/sounds.</summary>
public sealed record SoundChoice(SoundKind Kind, string Name);

/// <summary>
/// window.openSound / window.closeSound. Files are only played from the sounds folder (a path elsewhere is refused), and
/// Windows events are looked up in the current sound scheme, so a setting can't point Hotline at an arbitrary file.
/// </summary>
public static class Sounds
{
    public const string Off = "off", WindowsPrefix = "windows:", ShippedFile = "hotline.wav";

    public static SoundChoice Parse(string? value)
    {
        var v = value?.Trim() ?? "";
        if (v.Length == 0 || v.Equals(Off, StringComparison.OrdinalIgnoreCase)) return new(SoundKind.Off, "");
        if (v.StartsWith(WindowsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var name = v[WindowsPrefix.Length..].Trim();
            return name.Length == 0 ? new(SoundKind.Off, "") : new(SoundKind.Windows, name);
        }
        return new(SoundKind.File, v);
    }

    /// <summary>
    /// The .wav to play, or null (off, missing, outside the sounds folder, not a .wav, or an event without a sound).
    /// <paramref name="windowsEvent"/> returns an event's .wav path from the sound scheme (registry), or null/empty.
    /// </summary>
    public static string? Resolve(string soundsDirectory, SoundChoice choice, Func<string, string?> windowsEvent)
    {
        switch (choice.Kind)
        {
            case SoundKind.File:
            {
                string full, folder;
                try
                {
                    folder = Path.GetFullPath(soundsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    full = Path.GetFullPath(Path.Combine(folder, choice.Name));
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
                return full.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && full.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                       && File.Exists(full) ? full : null;
            }
            case SoundKind.Windows:
            {
                if (!choice.Name.All(c => char.IsLetterOrDigit(c) || c is '.' or '_' or '-')) return null; // an event name, not a path
                var path = windowsEvent(choice.Name);
                if (string.IsNullOrWhiteSpace(path)) return null;
                path = Environment.ExpandEnvironmentVariables(path);
                return File.Exists(path) ? path : null;
            }
            default:
                return null;
        }
    }

    /// <summary>Puts Hotline's own sound in the sounds folder once (never over the user's file of that name).</summary>
    public static void EnsureShipped(string soundsDirectory, byte[] wav)
    {
        Directory.CreateDirectory(soundsDirectory);
        var path = Path.Combine(soundsDirectory, ShippedFile);
        if (!File.Exists(path)) File.WriteAllBytes(path, wav);
    }
}
