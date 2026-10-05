using System.Text;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Plan 6 task 4: the selected text in the app you came from becomes a removable text attachment.</summary>
public sealed class SelectionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Selection_becomes_a_named_text_attachment()
    {
        var a = SelectionAttachment.Create("  some text\r\n", "Notepad");
        Assert.NotNull(a);
        Assert.Equal("Selected text from Notepad", a!.Name);
        Assert.Equal(AttachmentKind.Text, a.Kind);
        Assert.Equal("text/plain", a.MimeType);
        Assert.Equal("some text", a.AsText());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n\t ")]
    public void No_attachment_for_an_empty_selection(string? text) => Assert.Null(SelectionAttachment.Create(text, "Notepad"));

    [Fact]
    public void Unknown_app_gets_a_plain_name()
    {
        Assert.Equal("Selected text", SelectionAttachment.Create("x", null)!.Name);
        Assert.Equal("Selected text", SelectionAttachment.Create("x", "  ")!.Name);
    }

    [Fact]
    public void Long_selections_are_cut_with_a_note()
    {
        var a = SelectionAttachment.Create(new string('a', SelectionAttachment.MaxChars + 500), "Edge")!;
        var text = a.AsText();
        Assert.StartsWith(new string('a', SelectionAttachment.MaxChars), text);
        Assert.EndsWith(SelectionAttachment.TruncatedNote, text);
        Assert.True(Encoding.UTF8.GetByteCount(text) <= new AttachmentLimits().MaxTextBytes);
    }

    [Fact]
    public void A_cut_never_splits_a_surrogate_pair()
    {
        var text = new string('a', SelectionAttachment.MaxChars - 1) + "😀" + "tail";
        var cut = SelectionAttachment.Create(text, null)!.AsText();
        Assert.DoesNotContain('�', cut);
        Assert.False(char.IsHighSurrogate(cut[cut.Length - SelectionAttachment.TruncatedNote.Length - 1]));
    }

    [Fact]
    public void Attach_selection_defaults_to_auto_and_round_trips()
    {
        Assert.Equal(AttachSelectionMode.Auto, new ChatSettings().AttachSelection);
        var store = new SettingsStore(_dir);
        var s = store.Load();
        s.Chat.AttachSelection = AttachSelectionMode.Clipboard;
        store.Save(s);
        Assert.Contains("\"attachSelection\": \"clipboard\"", File.ReadAllText(store.FilePath));
        Assert.Equal(AttachSelectionMode.Clipboard, new SettingsStore(_dir).Load().Chat.AttachSelection);
    }

    [Fact]
    public void Schema_7_files_migrate_to_8_with_the_new_chat_defaults()
    {
        Assert.Equal(8, HotlineSettings.CurrentSchemaVersion);
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), """{ "schemaVersion": 7, "chat": { "saveHistory": false } }""");
        var s = new SettingsStore(_dir).Load();
        Assert.Equal(8, s.SchemaVersion);
        Assert.Equal(AttachSelectionMode.Auto, s.Chat.AttachSelection);
        Assert.False(s.Chat.SaveHistory);
    }

    [Fact]
    public void Settings_window_offers_the_option_on_the_chat_page() =>
        Assert.Contains(SettingsSchema.Items, i => i.Page == SettingsPage.Chat && i.Header == "Attach selected text");
}
