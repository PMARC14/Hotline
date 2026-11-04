using System.Text.Json;
using Hotline.Core.Activation;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ShortcutHardeningTests : TempDirTest
{
    [Fact]
    public void Last_duplicate_position_decides_file_order_for_conflicts()
    {
        var model = ToolbarModel.Resolve("""{"version":2,"items":{"left":["pin"]},"shortcuts":{"pin":"Ctrl+K","recent":"Ctrl+Q","pin":"Ctrl+Q"}}""");
        Assert.Equal("Ctrl+Q", model.ShortcutText("pin"));
        Assert.Equal("Ctrl+Q", model.EffectiveShortcuts()["recent"]);
        Assert.Equal("Ctrl+P", model.EffectiveShortcuts()["pin"]);
    }

    [Fact]
    public void Resolving_again_removes_stale_conflict_warnings_when_a_binding_is_freed()
    {
        var model = ToolbarModel.Resolve("""{"version":2,"items":{"left":["pin"]},"shortcuts":{"newChat":""},"buttons":{"mine":{"text":"hi","shortcut":"Ctrl+N"}}}""", new ChatSettings { StopShortcut = "Ctrl+N" });
        Assert.Single(model.Warnings);
        var freed = model.ResolveShortcuts(new ChatSettings());
        Assert.Equal("Ctrl+N", freed.Buttons[0].Shortcut);
        Assert.Empty(freed.Warnings);
    }

    [Theory]
    [InlineData("""{"CHAT":{"SHORTCUTS":{"PIN":"Ctrl+Q"}}}""", "Ctrl+Q")]
    [InlineData("{broken", "Ctrl+P")]
    public void Legacy_read_is_case_insensitive_and_bad_settings_do_not_break_toolbar(string settings, string expected)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), settings);
        var path = Path.Combine(_dir, "toolbar.json");
        File.WriteAllText(path, """{"version":2,"items":{"left":["pin"]}}""");
        var load = ToolbarFile.Load(path);
        Assert.Null(load.Error);
        Assert.Equal(expected, load.Model.ShortcutText("pin"));
        Assert.Equal(new[] { "pin" }, load.Model.Items(Quadrant.BottomLeft));
    }

    [Fact]
    public void Case_variant_toolbar_ids_show_and_save_the_last_requested_value()
    {
        var model = ToolbarModel.Resolve("""{"version":2,"shortcuts":{"PIN":"Ctrl+K","pin":"Ctrl+Q"}}""");
        Assert.Equal("Ctrl+Q", model.ShortcutText("pin"));
        Assert.Equal("Ctrl+Q", model.EffectiveShortcuts()["pin"]);
        Assert.Contains(model.Warnings, w => w.Contains("pin", StringComparison.OrdinalIgnoreCase));
        Assert.Single(model.Shortcuts!);
    }

    [Fact]
    public void Settings_conflicts_include_defaults_restored_for_invalid_values()
    {
        var model = ToolbarModel.Resolve("""{"version":2,"shortcuts":{"model":"Space"}}""");
        Assert.False(ChatShortcuts.CanBind(model, new ChatSettings(), "pin", "Ctrl+M"));
    }

    [Fact]
    public void Absent_toolbar_map_reserves_shipped_defaults_and_editor_keeps_requested_custom_combo()
    {
        var model = ToolbarModel.Resolve("""{"version":2,"buttons":{"mine":{"text":"hi","shortcut":"Ctrl+N"}}}""");
        Assert.Null(model.Buttons[0].Shortcut);
        Assert.Equal("Ctrl+N", model.Buttons[0].RequestedShortcut);
        var moved = ToolbarEditor.Show(model, "mine", Quadrant.TopLeft);
        Assert.Contains("mine", moved.Items(Quadrant.TopLeft));
        Assert.Equal("Ctrl+N", moved.Buttons[0].RequestedShortcut);
    }

    [Fact]
    public void Shortcut_save_keeps_stamped_backup_and_legacy_raw_values()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "toolbar.json");
        const string original = """{"version":2,"shortcuts":{"pin":"Shift+A","future":"Ctrl+K","recent":""}}""";
        File.WriteAllText(path, original);
        ToolbarFile.Save(path, ToolbarEditor.SetShortcut(ToolbarFile.Load(path).Model, "model", "Ctrl+Q"));
        Assert.Equal(original, File.ReadAllText(Assert.Single(Directory.GetFiles(_dir, "toolbar.json.*.bak"))));
        var again = ToolbarFile.Load(path).Model;
        Assert.Equal("Shift+A", again.ShortcutText("pin"));
        Assert.Equal("Ctrl+Q", again.ShortcutText("model"));
    }

    [Fact]
    public void Shortcut_reference_profile_has_builtins_pickers_disabled_and_custom_bindings()
    {
        var profile = Assert.Single(ToolbarProfiles.All, p => p.Name == "shortcuts");
        var model = ToolbarModel.Resolve(profile.Json);
        Assert.Equal("Ctrl+Q", model.EffectiveShortcuts()["pin"]);
        Assert.Equal("F6", model.EffectiveShortcuts()["model"]);
        Assert.Equal("", model.EffectiveShortcuts()["recent"]);
        Assert.Equal("Ctrl+Shift+1", Assert.Single(model.Buttons).Shortcut);
        Assert.Empty(model.Warnings);
    }

    [Fact]
    public void New_messages_have_all_seventeen_translations_without_fallback()
    {
        foreach (var tag in Hotline.Core.Text.Localization.Languages.Supported)
        {
            var resources = Hotline.Core.Text.Localization.Strings.ResourceManager.GetResourceSet(System.Globalization.CultureInfo.GetCultureInfo(tag), true, false)!;
            foreach (var key in new[] { "Shortcuts_InvalidOrConflict", "Shortcuts_NeedsModifier", "Transcript_NothingToCopy" })
                Assert.False(string.IsNullOrWhiteSpace(resources.GetString(key)), tag + ": " + key);
            foreach (var key in new[] { "Shortcuts_Invalid", "Shortcuts_Conflict", "Shortcuts_Unknown", "Shortcuts_Duplicate" })
                Assert.DoesNotContain("chat.shortcuts", resources.GetString(key));
        }
    }
    [Theory]
    [InlineData("Space")]
    [InlineData("Tab")]
    [InlineData("A")]
    [InlineData("Shift+A")]
    [InlineData("Shift+Space")]
    public void Toolbar_bindings_require_a_non_shift_modifier(string text)
    {
        var warnings = new List<string>();
        Assert.Equal("Ctrl+P", ChatShortcuts.Normalize(new Dictionary<string, string> { ["pin"] = text }, "Esc", warnings.Add)["pin"]);
        Assert.Single(warnings);
        var buttons = ChatShortcuts.NormalizeCustom([new("mine", "Mine", "E734", null, "text", false, Shortcut: text)], warn: warnings.Add);
        Assert.Null(buttons[0].Shortcut);
        Assert.Equal(2, warnings.Count);
        Assert.True(Hotkey.TryParseShortcut(text, out _)); // Stop retains its rule.
    }

    [Theory]
    [InlineData(HotkeyModifiers.None, 0x20u)]
    [InlineData(HotkeyModifiers.None, 0x09u)]
    [InlineData(HotkeyModifiers.None, 0x41u)]
    [InlineData(HotkeyModifiers.Shift, 0x41u)]
    public void Panel_capture_refuses_typing_keys(HotkeyModifiers held, uint key)
        => Assert.Equal(HotkeyCapture.Outcome.NeedsModifier, HotkeyCapture.Press(held, key, panel: true).Outcome);

    [Theory]
    [InlineData(0x70u)]
    [InlineData(0x87u)]
    public void Panel_capture_allows_function_keys(uint key)
        => Assert.Equal(HotkeyCapture.Outcome.Bound, HotkeyCapture.Press(HotkeyModifiers.None, key, panel: true).Outcome);

    [Fact]
    public void Toolbar_map_preserves_disabled_invalid_unknown_and_last_duplicate_through_editor()
    {
        var model = ToolbarModel.Resolve("""{"version":2,"shortcuts":{"pin":"Ctrl+Q","pin":"Shift+A","recent":"","future":"Ctrl+K","model":42}}""");
        Assert.NotEmpty(model.Warnings);
        var moved = ToolbarEditor.MoveTo(model, "pin", Quadrant.TopLeft, 0);
        using var document = JsonDocument.Parse(moved.ToJson(), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        var map = document.RootElement.GetProperty("shortcuts");
        Assert.Equal("Shift+A", map.GetProperty("pin").GetString());
        Assert.Equal("", map.GetProperty("recent").GetString());
        Assert.Equal("Ctrl+K", map.GetProperty("future").GetString());
        Assert.Equal(42, map.GetProperty("model").GetInt32());
    }

    [Fact]
    public void Runtime_custom_conflict_does_not_overwrite_requested_value_on_save()
    {
        var model = ToolbarModel.Resolve("""{"version":2,"shortcuts":{},"buttons":{"one":{"text":"one","shortcut":"Ctrl+K"},"two":{"text":"two","shortcut":"Ctrl+K"}}}""");
        Assert.Null(model.Buttons[1].Shortcut);
        using var document = JsonDocument.Parse(model.ToJson(), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        Assert.Equal("Ctrl+K", document.RootElement.GetProperty("buttons").GetProperty("two").GetProperty("shortcut").GetString());
    }

    [Fact]
    public void Blank_settings_row_disables_instead_of_resetting()
    {
        var settings = new HotlineSettings();
        var row = Assert.IsType<TextItem>(SettingsSchema.Items.Single(i => i.Header == "Bar_pin"));
        Assert.True(row.Validate!(""));
        Assert.False(row.Validate("Shift+A"));
        row.Set(settings, "");
        Assert.Equal("", row.Get(settings));
    }

    [Fact]
    public void Legacy_explicit_values_survive_other_settings_saves()
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, """{"chat":{"shortcuts":{"pin":"Shift+A","recent":"Ctrl+P","newChat":"Ctrl+P","future":"Ctrl+K"}}}""");
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        store.Save(settings);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = doc.RootElement.GetProperty("chat").GetProperty("shortcuts");
        Assert.Equal(4, map.EnumerateObject().Count());
        Assert.Equal("Shift+A", map.GetProperty("pin").GetString());
        Assert.Equal("Ctrl+P", map.GetProperty("newChat").GetString());
        Assert.Equal("Ctrl+K", map.GetProperty("future").GetString());
    }

    [Fact]
    public void Legacy_fallback_is_raw_once_and_toolbar_key_takes_precedence()
    {
        Directory.CreateDirectory(_dir);
        var settingsPath = Path.Combine(_dir, "settings.json");
        var toolbarPath = Path.Combine(_dir, "toolbar.json");
        File.WriteAllText(settingsPath, """{"chat":{"shortcuts":{"pin":"Shift+A","recent":"","future":"Ctrl+K"}}}""");
        File.WriteAllText(toolbarPath, """{"version":2}""");
        var model = ToolbarFile.Load(toolbarPath).Model;
        Assert.Equal("Shift+A", model.ShortcutText("pin"));
        Assert.Equal("Ctrl+P", model.EffectiveShortcuts()["pin"]);
        Assert.Equal("", model.EffectiveShortcuts()["recent"]);
        File.WriteAllText(settingsPath, """{"chat":{"shortcuts":{"pin":"Ctrl+Q"}}}""");
        Assert.Equal("Shift+A", ToolbarFile.Load(toolbarPath).Model.ShortcutText("pin"));
        ToolbarFile.Save(toolbarPath, model);
        Assert.Equal("Shift+A", ToolbarFile.Load(toolbarPath).Model.ShortcutText("pin"));
        File.WriteAllText(toolbarPath, """{"version":2,"shortcuts":{}}""");
        Assert.Equal("Ctrl+P", ToolbarFile.Load(toolbarPath).Model.ShortcutText("pin"));
    }

    [Fact]
    public void Conflicts_resolve_only_effective_builtins_and_raw_values_survive_save()
    {
        var model = ToolbarModel.Resolve("""{"version":2,"items":{"left":["pin"]},"shortcuts":{"recent":"Ctrl+P","pin":"Ctrl+P","model":"Space","future":"Ctrl+K"}}""");
        Assert.Equal("", model.EffectiveShortcuts()["pin"]);
        Assert.Equal("Ctrl+P", model.ShortcutText("pin"));
        Assert.Equal("Ctrl+M", model.EffectiveShortcuts()["model"]);
        Assert.DoesNotContain("future", model.EffectiveShortcuts().Keys);
        Assert.Equal(3, model.Warnings.Count);
        Assert.Equal("Ctrl+P", ToolbarModel.Resolve(model.ToJson()).ShortcutText("pin"));
    }

    [Fact]
    public void Case_insensitive_duplicate_ids_use_last_value_with_warning()
    {
        var warnings = new List<string>();
        var result = ChatShortcuts.Normalize([new("PIN", "Ctrl+K"), new("pin", "Ctrl+Q")], "Esc", warnings.Add);
        Assert.Equal("Ctrl+Q", result["pin"]);
        Assert.Single(warnings);
    }

    [Fact]
    public void Settings_row_rejects_stop_builtin_custom_conflicts_and_reset_removes_override()
    {
        var settings = new HotlineSettings();
        settings.Chat.StopShortcut = "Ctrl+J";
        settings.Chat.Toolbar = ToolbarModel.Resolve("""{"version":2,"shortcuts":{},"buttons":{"mine":{"text":"hi","shortcut":"Ctrl+K"}}}""");
        var row = Assert.IsType<TextItem>(SettingsSchema.Items.Single(i => i.Header == "Bar_pin"));
        Assert.False(row.ValidateFor!(settings, "Ctrl+J"));
        Assert.False(row.ValidateFor(settings, "Ctrl+N"));
        Assert.False(row.ValidateFor(settings, "Ctrl+K"));
        Assert.True(row.ValidateFor(settings, "Ctrl+Q"));
        row.Set(settings, "Ctrl+Q");
        Assert.Equal("Ctrl+Q", settings.Chat.Toolbar.ShortcutText("pin"));
        Assert.Equal("Ctrl+P", settings.Chat.Shortcuts["pin"]);
        row.Reset!(settings);
        Assert.False(settings.Chat.Toolbar.Shortcuts!.ContainsKey("pin"));
    }

    [Fact]
    public void Backup_export_import_retains_toolbar_shortcut_map()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "toolbar.json"), """{"version":2,"shortcuts":{"pin":"Ctrl+Q","model":""}}""");
        using var zip = new MemoryStream();
        SettingsPackage.Export(_dir, [SettingsCategory.Toolbar], zip, DateTimeOffset.UtcNow, "test");
        zip.Position = 0;
        var destination = Path.Combine(_dir, "imported");
        SettingsPackage.Import(destination, zip, [SettingsCategory.Toolbar], Path.Combine(_dir, "backups"));
        var model = ToolbarFile.Load(Path.Combine(destination, "toolbar.json")).Model;
        Assert.Equal("Ctrl+Q", model.EffectiveShortcuts()["pin"]);
        Assert.Equal("", model.EffectiveShortcuts()["model"]);
    }
}
