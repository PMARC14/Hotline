using Hotline.Core.Backends;
using Hotline.Core.Backends.Api;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Plan 6 task 5: screenshot text (OCR) for connections whose model can't read images.</summary>
public sealed class OcrPlanTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Theory]
    [InlineData(OcrMode.Auto, false, true, false)]   // text-only model: text instead of the image
    [InlineData(OcrMode.Auto, true, false, true)]    // image model: nothing to do
    [InlineData(OcrMode.Always, true, true, true)]   // text alongside the image
    [InlineData(OcrMode.Always, false, true, false)]
    [InlineData(OcrMode.Off, false, false, true)]    // the image stays (and the send is refused as before)
    [InlineData(OcrMode.Off, true, false, true)]
    public void Decides_when_to_read_text_and_whether_to_keep_the_image(OcrMode mode, bool takesImages, bool run, bool keep)
    {
        var plan = OcrPlan.Decide(mode, takesImages);
        Assert.Equal(run, plan.Run);
        Assert.Equal(keep, plan.KeepImages);
    }

    [Fact]
    public void Recognized_text_becomes_a_text_attachment()
    {
        var a = OcrPlan.TextAttachment("screenshot.png", "Hello\nWorld");
        Assert.Equal(AttachmentKind.Text, a.Kind);
        Assert.Equal("Text in screenshot.png", a.Name);
        Assert.Equal("Text in screenshot:\nHello\nWorld", a.AsText());
    }

    [Fact]
    public void An_image_without_text_says_so()
    {
        Assert.Equal("Text in screenshot:\n(no text found)", OcrPlan.TextAttachment("x.png", "  ").AsText());
    }

    [Fact]
    public void Local_connections_take_no_images_unless_set()
    {
        Assert.False(ConnectionTypes.TakesImages(new BackendProfile { Type = BackendType.Local }));
        Assert.True(ConnectionTypes.TakesImages(new BackendProfile { Type = BackendType.Local, Images = true }));
        Assert.True(ConnectionTypes.TakesImages(new BackendProfile { Type = BackendType.OpenAiCompatible }));
        Assert.False(ConnectionTypes.TakesImages(new BackendProfile { Type = BackendType.Gemini, Images = false }));
    }

    [Fact]
    public void Api_backends_report_image_support_from_the_connection()
    {
        var log = new FileLog(Path.Combine(_dir, "h.log"));
        var local = new OpenAiBackend(new BackendProfile { Id = "l", Type = BackendType.Local }, new HttpClient(), new InMemorySecretStore(), _ => "", log);
        Assert.False(local.Capabilities.Images);
        var gemini = new GeminiBackend(new BackendProfile { Id = "g", Type = BackendType.Gemini, Images = false }, new HttpClient(), new InMemorySecretStore(), _ => "", log);
        Assert.False(gemini.Capabilities.Images);
    }

    [Fact]
    public void Images_flag_round_trips_in_the_connection_file()
    {
        var store = new SettingsStore(_dir);
        var s = store.Load();
        s.Chat.Backends.Add(new BackendProfile { Id = "llama", Type = BackendType.Local, Name = "llama", Images = true });
        s.Chat.Ocr = OcrMode.Always;
        store.Save(s);
        var loaded = new SettingsStore(_dir).Load();
        Assert.True(loaded.Chat.Backends.Single(b => b.Id == "llama").Images);
        Assert.Equal(OcrMode.Always, loaded.Chat.Ocr);
        Assert.Null(loaded.Chat.Backends.Single(b => b.Id == "agy").Images);
    }

    [Fact]
    public void Ocr_defaults_to_auto_and_is_on_the_chat_settings_page()
    {
        Assert.Equal(OcrMode.Auto, new ChatSettings().Ocr);
        Assert.Contains(SettingsSchema.Items, i => i.Page == SettingsPage.Chat && i.Header == "Screenshot text (OCR)");
    }

    [Fact]
    public void Controller_exposes_the_current_backends_capabilities()
    {
        var backend = new FakeBackend { Capabilities = new(Images: false, TextFiles: true) };
        var c = new ChatController(_ => backend, null, new ManualTimeProvider(), new FileLog(Path.Combine(_dir, "h.log"))) { BackendId = "fake" };
        Assert.False(c.CurrentCapabilities!.Images);
    }
}
