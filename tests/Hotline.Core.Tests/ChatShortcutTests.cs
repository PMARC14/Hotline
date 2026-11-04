using System.IO.Compression;
using System.Text.Json;
using Hotline.Core.Activation;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ChatShortcutTests : TempDirTest
{
    private (HotlineSettings Settings, SettingsStore Store) Read(string chat)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), "{\"schemaVersion\":15,\"chat\":" + chat + "}");
        var store = new SettingsStore(_dir);
        return (store.Load(), store);
    }

    [Theory]
    [InlineData("F5", "F5")]
    [InlineData("control+shift+c", "Ctrl+Shift+C")]
    [InlineData("Ctrl+Period", "Ctrl+.")]
    public void Uses_the_toolbar_shortcut_parser_and_canonical_spelling(string text, string expected)
    {
        var (settings, store) = Read(JsonSerializer.Serialize(new { stopShortcut = "Ctrl+J", shortcuts = new { copyLastAnswer = text } }));
        Assert.Equal(expected, settings.Chat.Shortcuts["copyLastAnswer"]);
        Assert.Empty(store.SettingsProblems);
    }

    [Fact]
    public void Defaults_cover_all_real_toolbar_ids_without_clashes_or_system_or_editing_keys()
    {
        Assert.Equal(ToolbarModel.BuiltIn.Order(), ChatShortcuts.Defaults.Keys.Order());
        var keys = ChatShortcuts.Defaults.Values.Select(text =>
        {
            Assert.True(Hotkey.TryParseShortcut(text, out var key));
            Assert.Contains(key.Modifiers, new[] { HotkeyModifiers.Control, HotkeyModifiers.Control | HotkeyModifiers.Shift });
            Assert.True(key.VirtualKey is >= 'A' and <= 'Z' or 0xBC); // no system keys, language switches or Ctrl+Esc
            Assert.DoesNotContain(key, new[] { 'C', 'V', 'X', 'A', 'Z', 'Y' }.Select(c => new Hotkey(HotkeyModifiers.Control, c)));
            Assert.NotEqual(new Hotkey(HotkeyModifiers.None, 0x1B), key);
            return key;
        }).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        var second = new ChatSettings();
        second.Shortcuts["pin"] = "Ctrl+Q";
        Assert.Equal("Ctrl+P", new ChatSettings().Shortcuts["pin"]);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("[]")]
    [InlineData("true")]
    [InlineData("\"Ctrl+Bogus\"")]
    [InlineData("\"Enter\"")]
    public void A_bad_entry_defaults_alone_and_warns_without_rewriting(string value)
    {
        var text = "{\"shortcuts\":{\"pin\":" + value + ",\"recent\":\"Ctrl+Q\"},\"memory\":false}";
        var (s, store) = Read(text);
        Assert.Equal("Ctrl+P", s.Chat.Shortcuts["pin"]);
        Assert.Equal("Ctrl+Q", s.Chat.Shortcuts["recent"]);
        Assert.False(s.Chat.Memory);
        Assert.Single(store.SettingsProblems);
        Assert.Contains("shortcuts.pin", store.SettingsProblems[0]);
        Assert.Contains(text, File.ReadAllText(store.FilePath));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    public void A_bad_map_defaults_and_warns(string value)
    {
        var (s, store) = Read("{\"shortcuts\":" + value + "}");
        Assert.Equal(ChatShortcuts.Defaults, s.Chat.Shortcuts);
        Assert.Single(store.SettingsProblems);
    }

    [Fact]
    public void Duplicate_combos_use_the_first_in_file_order_and_fallback_per_entry()
    {
        var (s, store) = Read("""{"shortcuts":{"recent":"Ctrl+Q","pin":"control+q","model":"Ctrl+Q"}}""");
        Assert.Equal("Ctrl+Q", s.Chat.Shortcuts["recent"]);
        Assert.Equal("Ctrl+P", s.Chat.Shortcuts["pin"]);
        Assert.Equal("Ctrl+M", s.Chat.Shortcuts["model"]);
        Assert.Equal(2, store.SettingsProblems.Count);
    }

    [Fact]
    public void A_taken_default_is_disabled_and_remains_disabled_after_round_trip()
    {
        var (s, store) = Read("""{"shortcuts":{"recent":"Ctrl+P","pin":"Ctrl+P"}}""");
        Assert.Equal("Ctrl+P", s.Chat.Shortcuts["recent"]);
        Assert.Equal("", s.Chat.Shortcuts["pin"]);
        Assert.Single(store.SettingsProblems);
        store.Save(s);
        var again = new SettingsStore(_dir).Load();
        Assert.Equal(s.Chat.Shortcuts, again.Chat.Shortcuts);
    }

    [Fact]
    public void Duplicate_ids_are_case_insensitive_and_keep_the_last()
    {
        var (s, store) = Read("""{"shortcuts":{"PIN":42,"pin":"Ctrl+Q"}}""");
        Assert.Equal("Ctrl+Q", s.Chat.Shortcuts["pin"]);
        Assert.Single(store.SettingsProblems);
        Assert.DoesNotContain("PIN", s.Chat.Shortcuts.Keys);
    }

    [Fact]
    public void Unknown_ids_warn_and_do_not_cost_other_commands()
    {
        var (s, store) = Read("""{"shortcuts":{"not-a-button":"Ctrl+Q","pin":"Ctrl+K"}}""");
        Assert.Equal("Ctrl+K", s.Chat.Shortcuts["pin"]);
        Assert.DoesNotContain("not-a-button", s.Chat.Shortcuts.Keys);
        Assert.Single(store.SettingsProblems);
    }

    [Theory]
    [InlineData("{\"shortcuts\":{\"pin\":\"Ctrl+Q\"},\"stopShortcut\":\"Ctrl+Q\"}")]
    [InlineData("{\"stopShortcut\":\"Ctrl+Q\",\"shortcuts\":{\"pin\":\"Ctrl+Q\"}}")]
    public void Stop_wins_regardless_of_json_order(string chat)
    {
        var (s, store) = Read(chat);
        Assert.Equal("Ctrl+P", s.Chat.Shortcuts["pin"]);
        Assert.Single(store.SettingsProblems);
    }

    [Fact]
    public void Stop_conflicts_with_omitted_defaults_are_also_reported()
    {
        var (s, store) = Read("""{"stopShortcut":"Ctrl+N"}""");
        Assert.Equal("", s.Chat.Shortcuts["newChat"]);
        Assert.Single(store.SettingsProblems);
    }

    [Fact]
    public void Normalization_repairs_programmatic_values_and_null_maps()
    {
        var s = new HotlineSettings();
        s.Chat.Shortcuts = new() { ["pin"] = "nope", ["unknown"] = "Esc" };
        Assert.Equal(ChatShortcuts.Defaults, SettingsStore.Normalize(s).Chat.Shortcuts);
        s.Chat.Shortcuts = null!;
        Assert.Equal(ChatShortcuts.Defaults, SettingsStore.Normalize(s).Chat.Shortcuts);
    }

    [Fact]
    public void Settings_rows_expose_every_command_with_the_shared_parser()
    {
        foreach (var (id, combo) in ChatShortcuts.Defaults)
        {
            var row = Assert.IsType<TextItem>(SettingsSchema.Items.Single(i => i.Header == "Bar_" + id));
            Assert.Equal(combo, row.Get(new HotlineSettings()));
            Assert.False(row.Validate!("Esc"));
            Assert.True(row.Validate!("Ctrl+Q"));
            Assert.False(row.Validate!("Enter"));
        }
    }

    [Fact]
    public void Reload_replaces_bindings_and_reset_restores_omitted_commands()
    {
        var (s, store) = Read("{}");
        var log = new Hotline.Core.Diagnostics.FileLog(Path.Combine(_dir, "test.log"));
        var service = new SettingsService(store, s, log);
        var fromDisk = new HotlineSettings { Chat = { Shortcuts = new() { ["pin"] = "Ctrl+Q" } } };
        Assert.True(service.Reload(SettingsStore.Normalize(fromDisk)));
        Assert.Equal("Ctrl+Q", service.Current.Chat.Shortcuts["pin"]);
        Assert.True(service.Reload(new HotlineSettings()));
        Assert.Equal(ChatShortcuts.Defaults, service.Current.Chat.Shortcuts);
    }

    [Fact]
    public void Save_reload_reset_defaults_and_backup_export_include_the_map()
    {
        var (s, store) = Read("""{"shortcuts":{"pin":"Ctrl+Q","captureRegion":""}}""");
        store.Save(s);
        var reloaded = new SettingsStore(_dir).Load();
        Assert.Equal(s.Chat.Shortcuts, reloaded.Chat.Shortcuts);
        Assert.Equal(HotlineSettings.CurrentSchemaVersion, reloaded.SchemaVersion);
        using var zip = new MemoryStream();
        SettingsPackage.Export(_dir, [SettingsCategory.Settings], zip, DateTimeOffset.UtcNow, "test");
        zip.Position = 0;
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true))
        using (var reader = new StreamReader(archive.GetEntry("settings.json")!.Open()))
        using (var document = JsonDocument.Parse(reader.ReadToEnd()))
            Assert.Equal("Ctrl+Q", document.RootElement.GetProperty("chat").GetProperty("shortcuts").GetProperty("pin").GetString());
        zip.Position = 0;
        var importedDir = Path.Combine(_dir, "imported");
        var imported = SettingsPackage.Import(importedDir, zip, [SettingsCategory.Settings], Path.Combine(_dir, "backups"));
        Assert.Null(imported.Error);
        Assert.Equal(s.Chat.Shortcuts, new SettingsStore(importedDir).Load().Chat.Shortcuts);
        SettingsReset.WriteDefaults(_dir);
        Assert.DoesNotContain("\"shortcuts\"", File.ReadAllText(SettingsReset.DefaultsPath(_dir)));
        Assert.Empty(SettingsReset.Reset(store, _dir, s, false, DateTimeOffset.UtcNow).Failed);
        Assert.Equal(ChatShortcuts.Defaults, new SettingsStore(_dir).Load().Chat.Shortcuts);
    }
}
