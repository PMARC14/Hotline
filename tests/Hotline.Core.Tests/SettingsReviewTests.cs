using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Text;

namespace Hotline.Core.Tests;

/// <summary>Fixes from the full-codebase review (settings and text).</summary>
public sealed class SettingsReviewTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose()
    {
        if (!Directory.Exists(_dir)) return;
        foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_dir, recursive: true);
    }

    private string Conn(string id) => Path.Combine(_dir, "connections", id + ".json");

    [Fact]
    public void Duplicate_connection_ids_in_an_old_backends_list_do_not_break_a_reload()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName),
            """{ "schemaVersion": 7, "chat": { "backends": [ { "id": "a", "type": "local", "name": "One" }, { "id": "A", "type": "local", "name": "Two" } ] } }""");
        var store = new SettingsStore(_dir);
        var service = new SettingsService(store, store.Load(), new FileLog(Path.Combine(_dir, "h.log")));
        var edited = store.TryRead()!;
        edited.Window.FontSize = 20;
        Assert.True(service.Reload(edited));
        Assert.Single(service.Current.Chat.Backends);
    }

    [Fact]
    public void A_connection_file_added_while_running_is_deleted_with_its_connection()
    {
        Directory.CreateDirectory(_dir);
        var store = new SettingsStore(_dir);
        var s = store.Load();
        Directory.CreateDirectory(Path.Combine(_dir, "connections"));
        File.WriteAllText(Conn("late"), """{ "type": "local", "name": "Late" }""");
        s.Chat.Backends = store.TryRead()!.Chat.Backends; // what a live reload takes in
        Assert.Contains(s.Chat.Backends, b => b.Id == "late");
        Hotline.Core.Backends.ConnectionEditor.Remove(s.Chat, "late");
        store.Save(s);
        Assert.False(File.Exists(Conn("late")));
    }

    [Fact]
    public void A_read_only_connection_file_does_not_stop_saving()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "connections"));
        File.WriteAllText(Conn("ro"), """{ "type": "local", "name": "Ro" }""");
        var store = new SettingsStore(_dir);
        var s = store.Load();
        File.SetAttributes(Conn("ro"), FileAttributes.ReadOnly);
        Hotline.Core.Backends.ConnectionEditor.Remove(s.Chat, "ro");
        store.Save(s); // must not throw UnauthorizedAccessException
        Assert.True(File.Exists(Conn("ro")));
    }

    [Fact]
    public void Saving_never_exposes_a_null_connection_list_to_other_threads()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "connections"));
        File.WriteAllText(Conn("a"), """{ "type": "local", "name": "A" }""");
        var store = new SettingsStore(_dir);
        var s = store.Load();
        var seenNull = false;
        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() => { while (!stop.IsCancellationRequested) if (s.Chat.Backends is null) seenNull = true; });
        for (var i = 0; i < 3000; i++) SettingsStore.Serialize(s);
        stop.Cancel();
        reader.Wait();
        Assert.False(seenNull);
    }

    [Theory]
    [InlineData("en-us", "en-US")]
    [InlineData(" DE ", "de")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("klingon", "system")]
    public void Language_picker_shows_the_canonical_tag(string stored, string shown)
    {
        var item = SettingsSchema.Items.OfType<ChoiceItem>().First(i => i.Header == nameof(Hotline.Core.Text.Localization.Strings.Set_Language));
        var s = new HotlineSettings();
        s.General.Language = stored;
        Assert.Equal(shown, item.Get(s));
    }

    [Fact]
    public void Deeply_nested_latex_does_not_overflow_the_stack()
    {
        var braces = new string('{', 200_000);
        Assert.Equal(braces, LatexText.ToUnicode(braces));
        var fracs = string.Concat(Enumerable.Repeat(@"\frac", 200_000));
        Assert.Equal(fracs, LatexText.ToUnicode(fracs));
    }

    [Theory]
    [InlineData("> intro\n> <svg viewBox=\"0 0 1 1\"></svg>\n")]
    [InlineData("- item\n  <svg viewBox=\"0 0 1 1\"></svg>\n")]
    public void Svg_inside_a_quote_or_list_is_not_left_as_a_token(string markdown) =>
        Assert.DoesNotContain("HOTLINESVGBLOCK", System.Text.Json.JsonSerializer.Serialize(MarkdownModel.Parse(markdown), new System.Text.Json.JsonSerializerOptions { IncludeFields = true }));
}
