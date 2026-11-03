using System.Globalization;
using System.Text.Json;
using Hotline.Core.Activation;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class CustomButtonShortcutTests
{
    private static ToolbarModel Read(string value, ChatSettings? chat = null) => ToolbarModel.Resolve(
        """{"version":2,"items":{"left":["my","pin"],"right":[]},"buttons":{"my":{"label":"Mine","text":"hello","shortcut": """ + value + "}}}", chat);

    [Theory]
    [InlineData("control+alt+1", "Ctrl+Alt+1")]
    [InlineData("Ctrl+Period", "Ctrl+.")]
    [InlineData("F5", "F5")]
    [InlineData("Esc", "Esc")]
    public void Parse_uses_the_shared_panel_parser_and_canonical_spelling(string text, string expected)
    {
        var model = Read(JsonSerializer.Serialize(text));
        Assert.Equal(expected, Assert.Single(model.Buttons).Shortcut);
        Assert.Empty(model.Warnings);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("\"Ctrl+Bogus\"")]
    [InlineData("\"Enter\"")]
    [InlineData("\"Ctrl+Enter\"")]
    [InlineData("\"   \"")]
    public void Bad_values_drop_only_the_shortcut_and_warn_without_throwing(string value)
    {
        var model = Read(value);
        var button = Assert.Single(model.Buttons);
        Assert.Null(button.Shortcut);
        Assert.Equal("hello", button.Text);
        Assert.Equal(new[] { "my", "pin" }, model.Items(Quadrant.BottomLeft));
        Assert.Contains("buttons.my.shortcut", Assert.Single(model.Warnings));
    }

    [Fact]
    public void Omitted_or_empty_shortcuts_are_optional_and_do_not_warn()
    {
        var omitted = ToolbarModel.Resolve("""{"items":{"left":["my"]},"buttons":{"my":{"quickAction":"summarize"}}}""");
        var empty = Read("\"\"");
        Assert.Null(Assert.Single(omitted.Buttons).Shortcut);
        Assert.Null(Assert.Single(empty.Buttons).Shortcut);
        Assert.Empty(omitted.Warnings);
        Assert.Empty(empty.Warnings);
        using var json = JsonDocument.Parse(empty.ToJson(), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        Assert.False(json.RootElement.GetProperty("buttons").GetProperty("my").TryGetProperty("shortcut", out _));
    }

    [Theory]
    [InlineData("z-first", "a-second")]
    [InlineData("a-second", "z-first")]
    public void Duplicate_custom_combos_keep_the_first_in_json_order(string first, string second)
    {
        var model = ToolbarModel.Resolve($$$"""
            {"items":{"left":["pin"]},"buttons":{
              "{{{first}}}":{"text":"first","shortcut":"Ctrl+Alt+1"},
              "{{{second}}}":{"quickAction":"summarize","shortcut":"control+alt+1"}
            }}
            """);
        Assert.Equal(first, model.Buttons[0].Id);
        Assert.Equal("Ctrl+Alt+1", model.Buttons[0].Shortcut);
        Assert.Equal(second, model.Buttons[1].Id);
        Assert.Null(model.Buttons[1].Shortcut);
        Assert.Contains($"buttons.{second}.shortcut", Assert.Single(model.Warnings));
        Assert.Equal(model, ToolbarModel.Resolve(model.ToJson()));
    }

    [Theory]
    [InlineData("Ctrl+N")]
    [InlineData("control+shift+c")]
    [InlineData("Esc")]
    public void Built_in_defaults_and_stop_reserve_their_combos_even_for_hidden_buttons(string combo)
    {
        var model = Read(JsonSerializer.Serialize(combo), new ChatSettings());
        Assert.Null(Assert.Single(model.Buttons).Shortcut);
        Assert.Contains("buttons.my.shortcut", Assert.Single(model.Warnings));
    }

    [Fact]
    public void Stop_wins_then_effective_built_ins_then_customs()
    {
        var chat = new ChatSettings { StopShortcut = "Ctrl+Alt+1", Shortcuts = new() { ["pin"] = "Ctrl+Alt+1" } };
        var builtIns = ChatShortcuts.Normalize(chat.Shortcuts, chat.StopShortcut);
        Assert.Equal("Ctrl+P", builtIns["pin"]); // built-in keeps its existing fallback rule
        Assert.Null(Assert.Single(Read("\"Ctrl+Alt+1\"", chat).Buttons).Shortcut);
        Assert.Null(Assert.Single(Read("\"Ctrl+P\"", chat).Buttons).Shortcut);
        chat.StopShortcut = "Esc";
        Assert.Null(Assert.Single(Read("\"Ctrl+Alt+1\"", chat).Buttons).Shortcut);
        chat.Shortcuts["pin"] = "";
        var freed = Read("\"Ctrl+Alt+1\"", chat);
        Assert.Equal("Ctrl+Alt+1", Assert.Single(freed.Buttons).Shortcut);
        Assert.Empty(freed.Warnings);
    }

    [Fact]
    public void Remapping_and_disabling_built_ins_frees_their_old_combos()
    {
        var chat = new ChatSettings { Shortcuts = new() { ["newChat"] = "Ctrl+Q", ["pin"] = "" } };
        Assert.Equal("Ctrl+N", Assert.Single(Read("\"Ctrl+N\"", chat).Buttons).Shortcut);
        Assert.Equal("Ctrl+P", Assert.Single(Read("\"Ctrl+P\"", chat).Buttons).Shortcut);
        Assert.Null(Assert.Single(Read("\"Ctrl+Q\"", chat).Buttons).Shortcut);
    }

    [Fact]
    public void Live_conflict_resolution_preserves_source_and_restores_a_freed_combo()
    {
        var model = Read("\"Ctrl+N\"");
        var chat = new ChatSettings();
        var blocked = model.ResolveShortcuts(chat);
        Assert.Null(Assert.Single(blocked.Buttons).Shortcut);
        Assert.Single(blocked.Warnings);
        Assert.Equal("Ctrl+N", Assert.Single(model.Buttons).Shortcut);
        chat.Shortcuts["newChat"] = "";
        var freed = model.ResolveShortcuts(chat);
        Assert.Equal("Ctrl+N", Assert.Single(freed.Buttons).Shortcut);
        Assert.Empty(freed.Warnings);
    }

    [Fact]
    public void Quick_action_and_text_send_round_trip_and_equality_include_shortcuts()
    {
        var model = ToolbarModel.Resolve("""
            {"buttons":{
              "quick":{"quickAction":"summarize","shortcut":"Ctrl+Alt+1","iconFile":"quick.png"},
              "text":{"text":"/literal","send":true,"shortcut":"Ctrl+Alt+2"}
            }}
            """);
        var json = model.ToJson();
        Assert.Contains("Optional \"shortcut\": \"Ctrl+Alt+1\"", json);
        Assert.Contains("\"version\": 2", json);
        var again = ToolbarModel.Resolve(json);
        Assert.Equal(model, again);
        Assert.Empty(again.Warnings);
        Assert.Equal("summarize", again.Buttons[0].QuickAction);
        Assert.Equal("quick.png", again.Buttons[0].IconFile);
        Assert.True(again.Buttons[1].Send);
        var changed = model.Buttons[0] with { Shortcut = "Ctrl+Alt+3" };
        Assert.NotEqual(model.Buttons[0], changed);
        Assert.NotEqual(model, model with { Buttons = [changed, model.Buttons[1]] });
    }

    [Fact]
    public void Editor_keeps_shortcuts_through_content_edits_moves_hide_show_and_menu()
    {
        var model = Read("\"Ctrl+Alt+1\"");
        var edited = Assert.Single(model.Buttons) with { Label = "Renamed", QuickAction = "summarize", Text = null };
        model = ToolbarEditor.UpdateCustom(model, edited);
        Assert.Equal(edited, Assert.Single(model.Buttons));
        model = ToolbarEditor.Move(model, "my", 1);
        Assert.Equal(new[] { "pin", "my" }, model.Items(Quadrant.BottomLeft));
        model = ToolbarEditor.MoveTo(model, "my", Quadrant.TopRight, 0);
        model = ToolbarEditor.Hide(model, "my");
        model = ToolbarEditor.Show(model, "my", Quadrant.BottomRight);
        model = ToolbarEditor.Show(model, "more", Quadrant.BottomLeft);
        model = ToolbarEditor.MoveToMenu(model, "my", 0);
        Assert.Contains("my", model.Menu);
        Assert.Equal(edited, Assert.Single(model.Buttons));
        model = ToolbarEditor.AddCustom(model, new("another", "Another", ToolbarModel.DefaultIcon, null, "text", false, Shortcut: "Ctrl+Alt+2"));
        Assert.Equal("Ctrl+Alt+2", model.Buttons[1].Shortcut);
        Assert.Equal(model, ToolbarModel.Resolve(model.ToJson()));
        Assert.Empty(model.Warnings);
    }

    [Theory]
    [InlineData(HotkeyModifiers.None, 0x74, HotkeyCapture.Outcome.Bound, "F5")]
    [InlineData(HotkeyModifiers.Control | HotkeyModifiers.Alt, '1', HotkeyCapture.Outcome.Bound, "Ctrl+Alt+1")]
    [InlineData(HotkeyModifiers.None, 0x0D, HotkeyCapture.Outcome.Unsupported, null)]
    [InlineData(HotkeyModifiers.Control, 0x0D, HotkeyCapture.Outcome.Unsupported, null)]
    [InlineData(HotkeyModifiers.None, 0x1B, HotkeyCapture.Outcome.Cancel, null)]
    [InlineData(HotkeyModifiers.None, 0x08, HotkeyCapture.Outcome.Clear, null)]
    [InlineData(HotkeyModifiers.Control, 0x11, HotkeyCapture.Outcome.Waiting, null)]
    public void Panel_capture_shares_global_capture_controls_but_uses_the_panel_parser(HotkeyModifiers held, uint key,
        HotkeyCapture.Outcome outcome, string? text)
    {
        var result = HotkeyCapture.Press(held, key, panel: true);
        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(text, result.Text);
    }

    [Fact]
    public void New_shortcut_strings_are_present_without_fallback_in_all_seventeen_languages()
    {
        foreach (var tag in Languages.Supported)
        {
            var culture = tag == "en-US" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(tag);
            var resources = Strings.ResourceManager.GetResourceSet(culture, true, false)!;
            Assert.False(string.IsNullOrWhiteSpace(resources.GetString(nameof(Strings.Bar_CustomShortcut))));
            foreach (var key in new[] { nameof(Strings.BarW_ShortcutInvalid), nameof(Strings.BarW_ShortcutConflict) })
            {
                var text = resources.GetString(key);
                Assert.NotNull(text);
                Assert.Contains("buttons.my.shortcut", string.Format(culture, text, "my"));
            }
        }
    }
}
