using Hotline.Core.Activation;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Plan 6 task 6: push-to-talk voice input.</summary>
public sealed class VoiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Hold_set_to_voice_starts_on_press_and_stops_on_release()
    {
        var s = new ActivationSettings { Hold = KeyAction.Voice };
        Assert.Equal(KeyAction.Voice, KeyActionResolver.Resolve(KeyEvent.HoldStart, s));
        Assert.Equal(KeyAction.Voice, KeyActionResolver.Resolve(KeyEvent.HoldStop, s));
    }

    [Fact]
    public void Release_still_does_nothing_for_other_hold_actions() =>
        Assert.Equal(KeyAction.None, KeyActionResolver.Resolve(KeyEvent.HoldStop, new ActivationSettings { Hold = KeyAction.NewChat }));

    [Theory]
    [InlineData("", "Hello there.", "Hello there.")]
    [InlineData("Translate this:", "good morning", "Translate this: good morning")]
    [InlineData("ends with space ", "x", "ends with space x")]
    [InlineData("line\n", "next", "line\nnext")]
    [InlineData("keep", "  ", "keep")]
    [InlineData("hello", ".", "hello.")]
    [InlineData("hello", ", how are you", "hello, how are you")]
    [InlineData("hello", "?", "hello?")]
    public void Dictation_is_appended_to_the_draft(string draft, string dictated, string expected) =>
        Assert.Equal(expected, VoiceText.Append(draft, dictated));

    [Fact]
    public void Voice_settings_default_and_round_trip()
    {
        Assert.False(new ChatSettings().VoiceAutoSend);
        var store = new SettingsStore(_dir);
        var s = store.Load();
        s.Activation.Hold = KeyAction.Voice;
        s.Chat.VoiceAutoSend = true;
        store.Save(s);
        Assert.Contains("\"hold\": \"voice\"", File.ReadAllText(store.FilePath));
        var loaded = new SettingsStore(_dir).Load();
        Assert.Equal(KeyAction.Voice, loaded.Activation.Hold);
        Assert.True(loaded.Chat.VoiceAutoSend);
    }

    [Fact]
    public void Settings_window_offers_voice_for_the_long_press_and_auto_send()
    {
        var hold = Assert.IsType<ChoiceItem>(SettingsSchema.Items.Single(i => i.Header == "Long press of the Copilot key"));
        Assert.Contains(hold.Options, o => o.Value == nameof(KeyAction.Voice));
        Assert.Contains(SettingsSchema.Items, i => i.Page == SettingsPage.Chat && i.Header == "Send after dictation");
    }

    [Fact]
    public void Voice_toggle_on_the_general_page_maps_to_the_long_press()
    {
        var toggle = Assert.IsType<ToggleItem>(SettingsSchema.Items.Single(i => i.Page == SettingsPage.General && i.Header == "Voice input"));
        Assert.True(toggle.RefreshPage);
        var s = new HotlineSettings();
        Assert.False(toggle.Get(s));
        toggle.Set(s, true);
        Assert.Equal(KeyAction.Voice, s.Activation.Hold);
        Assert.True(toggle.Get(s));
        toggle.Set(s, false);
        Assert.Equal(KeyAction.NewChat, s.Activation.Hold);
        s.Activation.Hold = KeyAction.CaptureWindow;
        toggle.Set(s, false); // off doesn't touch another long-press choice
        Assert.Equal(KeyAction.CaptureWindow, s.Activation.Hold);
    }
}
