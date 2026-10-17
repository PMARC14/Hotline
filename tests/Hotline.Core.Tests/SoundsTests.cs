using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class SoundsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
    private string SoundsDir => Path.Combine(_dir, "sounds");

    [Theory]
    [InlineData(null, SoundKind.Off, "")]
    [InlineData("", SoundKind.Off, "")]
    [InlineData("off", SoundKind.Off, "")]
    [InlineData("windows:Notification.Default", SoundKind.Windows, "Notification.Default")]
    [InlineData("hotline.wav", SoundKind.File, "hotline.wav")]
    [InlineData("windows:", SoundKind.Off, "")]
    public void Parses_the_setting(string? value, SoundKind kind, string name)
        => Assert.Equal(new SoundChoice(kind, name), Sounds.Parse(value));

    [Fact]
    public void A_file_must_be_a_wav_inside_the_sounds_folder()
    {
        Directory.CreateDirectory(SoundsDir);
        File.WriteAllBytes(Path.Combine(SoundsDir, "ding.wav"), [1]);
        File.WriteAllBytes(Path.Combine(_dir, "outside.wav"), [1]);
        File.WriteAllBytes(Path.Combine(SoundsDir, "notes.txt"), [1]);
        Assert.Equal(Path.Combine(SoundsDir, "ding.wav"), Sounds.Resolve(SoundsDir, new SoundChoice(SoundKind.File, "ding.wav"), _ => null));
        Assert.Null(Sounds.Resolve(SoundsDir, new SoundChoice(SoundKind.File, @"..\outside.wav"), _ => null));
        Assert.Null(Sounds.Resolve(SoundsDir, new SoundChoice(SoundKind.File, Path.Combine(_dir, "outside.wav")), _ => null));
        Assert.Null(Sounds.Resolve(SoundsDir, new SoundChoice(SoundKind.File, "notes.txt"), _ => null));
        Assert.Null(Sounds.Resolve(SoundsDir, new SoundChoice(SoundKind.File, "missing.wav"), _ => null));
        Assert.Null(Sounds.Resolve(SoundsDir, new SoundChoice(SoundKind.Off, ""), _ => null));
    }

    [Fact]
    public void A_windows_event_resolves_through_the_sound_scheme()
    {
        Directory.CreateDirectory(_dir);
        var wav = Path.Combine(_dir, "Windows Notify.wav");
        File.WriteAllBytes(wav, [1]);
        Assert.Equal(wav, Sounds.Resolve(SoundsDir, new SoundChoice(SoundKind.Windows, "Notification.Default"), e => e == "Notification.Default" ? wav : null));
        Assert.Null(Sounds.Resolve(SoundsDir, new SoundChoice(SoundKind.Windows, "SystemHand"), _ => "")); // no sound in the scheme
        Assert.Null(Sounds.Resolve(SoundsDir, new SoundChoice(SoundKind.Windows, @"..\x"), _ => wav)); // not an event name
    }

    [Fact]
    public void The_shipped_sound_is_copied_once_and_never_overwritten()
    {
        Sounds.EnsureShipped(SoundsDir, [1, 2, 3]);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(SoundsDir, Sounds.ShippedFile)));
        File.WriteAllBytes(Path.Combine(SoundsDir, Sounds.ShippedFile), [9]);
        Sounds.EnsureShipped(SoundsDir, [1, 2, 3]);
        Assert.Equal([9], File.ReadAllBytes(Path.Combine(SoundsDir, Sounds.ShippedFile)));
    }

    [Fact]
    public void Settings_default_to_off()
    {
        var w = new WindowSettings();
        Assert.Equal(("off", "off"), (w.OpenSound, w.CloseSound));
    }
}
