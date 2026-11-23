using System.Text.Json;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ChatShortcutTests
{
    private static ToolbarModel Read(string map) => ToolbarModel.Resolve("{\"version\":3,\"shortcuts\":" + map + "}");

    [Theory]
    [InlineData("F5", "F5")]
    [InlineData("control+shift+c", "Ctrl+Shift+C")]
    [InlineData("Ctrl+Period", "Ctrl+.")]
    public void Uses_shared_parser_and_canonical_spelling(string text, string expected)
    {
        var model = Read(JsonSerializer.Serialize(new { copyLastAnswer = text }));
        Assert.Equal(expected, model.EffectiveShortcuts()["copyLastAnswer"]);
        Assert.Empty(model.Warnings);
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("42", true)]
    [InlineData("[]", true)]
    [InlineData("true", true)]
    [InlineData("\"Ctrl+Bogus\"", true)]
    [InlineData("\"Enter\"", true)]
    public void Bad_entry_defaults_alone_and_null_defaults_silently(string value, bool warns)
    {
        var model = Read("{\"pin\":" + value + ",\"recent\":\"Ctrl+Q\"}");
        Assert.Equal("Ctrl+P", model.EffectiveShortcuts()["pin"]);
        Assert.Equal("Ctrl+Q", model.EffectiveShortcuts()["recent"]);
        Assert.Equal(warns, model.Warnings.Count > 0);
        Assert.Equal(model, ToolbarModel.Resolve(model.ToJson()));
    }

    [Theory]
    [InlineData("null", false)]
    [InlineData("[]", true)]
    [InlineData("42", true)]
    public void Bad_map_defaults_and_null_defaults_silently(string value, bool warns)
    {
        var model = Read(value);
        Assert.Equal(ChatShortcuts.Defaults, model.EffectiveShortcuts());
        Assert.Equal(warns, model.Warnings.Count > 0);
    }

    [Fact]
    public void Defaults_cover_stop_and_all_panel_commands_without_conflicts()
    {
        Assert.Equal(ToolbarModel.BuiltIn.Append("stop").Order(), ChatShortcuts.Defaults.Keys.Order());
        Assert.Equal(ChatShortcuts.Defaults, Read("{}").EffectiveShortcuts());
        Assert.Equal("Esc", ChatShortcuts.Defaults["stop"]);
        var copy = ChatShortcuts.CreateDefaults();
        copy["pin"] = "Ctrl+Q";
        Assert.Equal("Ctrl+P", ChatShortcuts.Defaults["pin"]);
    }

    [Fact]
    public void Settings_rows_edit_reset_and_validate_every_command_in_toolbar()
    {
        foreach (var (id, combo) in ChatShortcuts.Defaults)
        {
            var row = Assert.IsType<TextItem>(SettingsSchema.Items.Single(i => i is TextItem t && t.ToolbarShortcutId == id));
            var settings = new HotlineSettings();
            Assert.Equal(combo, row.Get(settings));
            Assert.Equal(id == "stop", row.Validate!("Esc"));
            Assert.True(row.Validate!("Ctrl+Q"));
            Assert.False(row.Validate!("Enter"));
            row.Set(settings, "");
            Assert.Equal("", row.Get(settings));
            row.Reset!(settings);
            Assert.Equal(combo, row.Get(settings));
        }
    }
}
