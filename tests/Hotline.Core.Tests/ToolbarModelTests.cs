using System.Text;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarModelTests
{
    private const string Valid = """
        {
          "version": 3,
          // a comment
          "top":   { "left": ["effort", "model", "provider"], "right": [] },
          "bottom": { "left": ["pin", "captureWindow", "summarise"], "right": ["newChat", "settings", "thanks"] },
          "style": { "width": 2, "outline": true, "filled": false },
          "overrides": { "pin": { "outline": false, "width": 2 } },
          "buttons": {
            "summarise": { "label": "Summarise", "icon": "E8A5", "quickAction": "summarise" },
            "thanks":    { "label": "Say thanks", "icon": "E8BD", "text": "Thanks!", "send": true },
          },
        }
        """;

    private static ToolbarModel Bottom(string left, string extra = "") =>
        ToolbarModel.Resolve($$"""{ "version": 3, "top":{"left":[],"center":[],"right":[]}, "bottom": { "left": [{{left}}],"center":[],"right":[] } {{extra}} }""");

    private static string Many(string item, int count) => string.Join(", ", Enumerable.Repeat($"\"{item}\"", count));

    [Fact]
    public void Valid_v3_file_resolves_and_round_trips()
    {
        var m = ToolbarModel.Resolve(Valid);
        Assert.Empty(m.Warnings);
        Assert.Equal(["effort", "model", "provider"], m.Items(Quadrant.TopLeft));
        Assert.Empty(m.Items(Quadrant.TopRight));
        Assert.Equal(["pin", "captureWindow", "summarise"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["newChat", "settings", "thanks"], m.Items(Quadrant.BottomRight));
        Assert.Equal(new ButtonStyle(2, true, false, Fill: true), m.Style);
        Assert.Equal(new ButtonStyle(Outline: false, Width: 2), m.Overrides["pin"]);
        Assert.Equal(
            [new CustomButton("summarise", "Summarise", "E8A5", "summarise", null, false), new CustomButton("thanks", "Say thanks", "E8BD", null, "Thanks!", true)],
            m.Buttons);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
        Assert.Empty(ToolbarModel.Resolve(m.ToJson()).Warnings);
    }

    [Fact]
    public void ToJson_has_no_header_and_uses_v3_numeric_width()
    {
        var json = ToolbarModel.Defaults.ToJson();
        Assert.Contains("\"version\": 3", json);
        Assert.DoesNotContain("//", json);
        Assert.Contains("\"bottom\"", json);
        Assert.Equal(ToolbarModel.Defaults, ToolbarModel.Resolve(json));
        Assert.DoesNotContain("\"cells\"", json);
        Assert.DoesNotContain("\"size\"", json);
        Assert.Contains("\"style\": { \"width\": 1, \"fill\": true, \"outline\": false, \"filled\": false }", json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    public void Unreadable_or_wrong_root_gives_the_defaults_with_a_warning(string json)
    {
        var m = ToolbarModel.Resolve(json);
        Assert.Equal(ToolbarModel.Defaults, m);
        Assert.NotEmpty(m.Warnings);
    }

    [Fact]
    public void Bom_and_utf16_decoded_text_resolve()
    {
        Assert.Equal(ToolbarModel.Resolve(Valid), ToolbarModel.Resolve("\uFEFF" + Valid));
        var utf16 = Encoding.Unicode.GetString([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Valid)]);
        Assert.StartsWith("\uFEFF", utf16);
        var m = ToolbarModel.Resolve(utf16);
        Assert.Empty(m.Warnings);
        Assert.Equal(ToolbarModel.Resolve(Valid), m);
    }

    [Fact]
    public void Ids_are_case_insensitive_and_unknown_ids_are_dropped_with_a_warning()
    {
        var m = Bottom("\"PIN\", \"newchat\", \"bogus\"");
        Assert.Equal(["pin", "newChat"], m.Items(Quadrant.BottomLeft));
        Assert.Contains(m.Warnings, w => w.Contains("bogus"));
    }

    [Fact]
    public void An_item_in_two_quadrants_keeps_the_first_in_quadrant_order()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 3, "top": { "left": ["pin", "settings"], "right": ["newChat"] },
              "bottom": { "left": ["recent", "recent"], "right": ["newChat", "pin"] } }
            """);
        Assert.Equal(["recent"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["newChat", "pin"], m.Items(Quadrant.BottomRight));
        Assert.Equal(["settings"], m.Items(Quadrant.TopLeft));
        Assert.Empty(m.Items(Quadrant.TopRight));
    }

    [Theory]
    [InlineData("0.5", 0.5)]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData("4", 4)]
    [InlineData("2.0", 2)]
    public void Width_takes_numbers_and_round_trips(string width, double cells)
    {
        var m = Bottom("\"pin\", \"settings\"", $$""", "style": { "width": {{width}} }, "overrides": { "pin": { "width": {{width}} } }""");
        Assert.Empty(m.Warnings);
        Assert.Equal(cells, m.Style.Width);
        Assert.Equal(cells, m.Overrides["pin"].Width);
        Assert.Equal(cells, m.EffectiveStyle("pin").Width);
        Assert.Equal(cells, m.EffectiveStyle("settings").Width);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
        Assert.Empty(ToolbarModel.Resolve(m.ToJson()).Warnings);
    }

    [Theory]
    [InlineData(0.5, "\"width\": 0.5")]
    [InlineData(1, "\"width\": 1")]
    [InlineData(2, "\"width\": 2")]
    [InlineData(3, "\"width\": 3")]
    [InlineData(4, "\"width\": 4")]
    public void ToJson_writes_numeric_widths(double cells, string text)
    {
        var m = ToolbarModel.Defaults with { Style = new ButtonStyle(cells, false, false), Overrides = new Dictionary<string, ButtonStyle> { ["pin"] = new(cells) } };
        var json = m.ToJson();
        Assert.Contains($"\"style\": {{ {text}, ", json);
        Assert.Contains($"\"pin\": {{ {text} }}", json);
        Assert.DoesNotContain("\"size\"", json);
        Assert.DoesNotContain("\"cells\"", json);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("5")]
    [InlineData("1.3")]
    [InlineData("-1")]
    [InlineData("80")]
    [InlineData("\"2\"")]
    [InlineData("\"huge\"")]
    [InlineData("\"normal\"")]
    [InlineData("\"compact\"")]
    [InlineData("\"wide\"")]
    [InlineData("\"double\"")]
    [InlineData("\"triple\"")]
    [InlineData("\"quadruple\"")]
    [InlineData("true")]
    [InlineData("[1]")]
    public void An_invalid_width_is_ignored_with_one_warning_and_the_default_applies(string width)
    {
        var m = Bottom("\"pin\"", $$""", "style": { "width": {{width}} }, "overrides": { "pin": { "outline": true, "width": {{width}} } }""");
        Assert.Equal(1, m.Style.Width);
        Assert.Equal(new ButtonStyle(Outline: true), m.Overrides["pin"]);
        Assert.Equal(1, m.Warnings.Count(w => w.Contains("style.width")));
        Assert.Equal(1, m.Warnings.Count(w => w.Contains("overrides.pin.width")));
        Assert.Equal(40, ToolbarPlan.Metrics(m, "pin").Width);
        Assert.Empty(ToolbarModel.Resolve(m.ToJson()).Warnings);
    }

    [Fact]
    public void An_old_dip_width_and_height_are_ignored_and_height_keeps_its_one_warning_per_file()
    {
        var m = Bottom("\"pin\", \"settings\"", """, "style": { "width": 30, "height": 20 }, "overrides": { "pin": { "width": 5, "height": 999 }, "settings": { "width": 999, "outline": true } }""");
        Assert.DoesNotContain(m.Warnings, w => w.Contains("height"));
        Assert.Equal(3, m.Warnings.Count(w => w.Contains("width"))); // style.width, pin.width and settings.width
        Assert.False(m.Overrides.ContainsKey("pin"));
        Assert.Equal(new ButtonStyle(Outline: true), m.Overrides["settings"]);
        Assert.Equal(new ButtonMetrics(40, 36, 16), ToolbarPlan.Metrics(m, "pin"));
        Assert.DoesNotContain("\"height\"", m.ToJson());
    }

    [Theory]
    [InlineData("\"size\": \"wide\"")]
    [InlineData("\"size\": \"COMPACT\"")]
    [InlineData("\"cells\": 3")]
    [InlineData("\"cells\": 0.5")]
    [InlineData("\"size\": \"compact\", \"cells\": 4")]
    public void The_old_size_and_cells_keys_are_unknown_and_do_not_set_width(string alias)
    {
        var m = Bottom("\"pin\"", $$""", "style": { {{alias}} }, "overrides": { "pin": { {{alias}} } }""");
        Assert.Empty(m.Warnings);
        Assert.Equal(1, m.Style.Width);
        Assert.False(m.Overrides.ContainsKey("pin"));
        var again = ToolbarModel.Resolve(m.ToJson());
        Assert.Equal(m, again);
        Assert.DoesNotContain("\"size\"", m.ToJson());
        Assert.DoesNotContain("\"cells\"", m.ToJson());
    }

    [Theory]
    [InlineData("\"width\": 3, \"size\": \"compact\"")]
    [InlineData("\"width\": 3, \"cells\": 4")]
    [InlineData("\"width\": 3, \"size\": \"wide\", \"cells\": 1")]
    public void Numeric_width_applies_and_old_names_are_ignored_without_warning(string both)
    {
        var m = Bottom("\"pin\"", $$""", "overrides": { "pin": { {{both}} } }""");
        Assert.Equal(3, m.Overrides["pin"].Width);
        Assert.Empty(m.Warnings);
    }

    [Fact]
    public void Invalid_numeric_width_warns_and_old_names_never_supply_a_fallback()
    {
        var m = Bottom("\"pin\", \"settings\"", """, "overrides": { "pin": { "width": 80, "size": "wide" }, "settings": { "size": "huge", "cells": 1.3 } }""");
        Assert.False(m.Overrides.ContainsKey("pin"));
        Assert.False(m.Overrides.ContainsKey("settings"));
        Assert.Contains("overrides.pin.width", Assert.Single(m.Warnings));
    }

    [Fact]
    public void The_owners_own_toolbar_json_style_size_normal_loads_without_a_warning()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 3,
              "top": { "left": ["effort"], "center": ["model"], "right": ["provider"] },
              "bottom": { "left": ["pin", "captureWindow", "captureScreen", "captureRegion"], "center": ["prompt", "continueIn"], "right": ["recent", "newChat", "settings"] },
              "menu": [],
              "style": { "size": "normal", "outline": false, "filled": false },
              "overrides": { "effort": { "span": 3 }, "model": { "span": 5 }, "provider": { "span": 4 } },
              "buttons": {} }
            """);
        Assert.Empty(m.Warnings);
        Assert.Equal(new ButtonStyle(1, false, false, Fill: true), m.Style);
        Assert.Equal(5, m.EffectiveSpan("model"));
    }

    [Fact]
    public void The_width_is_the_override_else_the_bar_else_one_cell()
    {
        var m = Bottom("\"pin\", \"settings\", \"recent\"", """, "style": { "width": 3 }, "overrides": { "pin": { "width": 1 }, "settings": { "width": 4 } }""");
        Assert.Equal(3, m.EffectiveStyle("recent").Width);   // inherits the bar's
        Assert.Equal(1, m.EffectiveStyle("pin").Width);      // its own wins
        Assert.Equal(4, m.EffectiveStyle("settings").Width);
        Assert.Equal(1, Bottom("\"pin\"").EffectiveStyle("pin").Width);
    }

    [Fact]
    public void Bad_style_values_fall_back_per_field()
    {
        var m = Bottom("\"pin\"", """, "style": { "width": "huge", "outline": "yes", "filled": true }""");
        Assert.Equal(new ButtonStyle(1, false, true, Fill: true), m.Style);
        Assert.Contains(m.Warnings, w => w.Contains("outline"));
        Assert.Contains(m.Warnings, w => w.Contains("huge"));
    }

    [Fact]
    public void EffectiveStyle_takes_override_then_bar_then_defaults()
    {
        var m = Bottom("\"pin\", \"settings\"", """, "style": { "width": 0.5, "outline": true }, "overrides": { "pin": { "outline": false, "width": 3 } }""");
        Assert.Equal(new ButtonStyle(3, false, false), m.EffectiveStyle("pin"));
        Assert.Equal(new ButtonStyle(0.5, true, false), m.EffectiveStyle("settings"));
    }

    [Fact]
    public void Outline_color_is_read_upper_cased_inherited_per_field_and_round_trips()
    {
        var m = Bottom("\"pin\", \"settings\", \"recent\"",
            """, "style": { "outline": true, "outlineColor": "#e81123" }, "overrides": { "pin": { "outlineColor": "#00b7c3" }, "recent": { "filled": true } }""");
        Assert.Empty(m.Warnings);
        Assert.Equal("#E81123", m.Style.OutlineColor);
        Assert.Equal(new ButtonStyle(OutlineColor: "#00B7C3"), m.Overrides["pin"]);
        Assert.Equal("#00B7C3", m.EffectiveStyle("pin").OutlineColor);
        Assert.Equal("#E81123", m.EffectiveStyle("settings").OutlineColor);
        Assert.Equal("#E81123", m.EffectiveStyle("recent").OutlineColor);
        Assert.True(m.EffectiveStyle("pin").Outline);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
        Assert.Contains("\"outlineColor\": \"#E81123\"", m.ToJson());
        Assert.NotEqual(m, m with { Style = m.Style with { OutlineColor = "#0063B1" } });
    }

    [Fact]
    public void No_outline_color_means_the_accent_and_is_not_written()
    {
        var m = Bottom("\"pin\"", """, "style": { "outline": true }""");
        Assert.Null(m.Style.OutlineColor);
        Assert.Null(m.EffectiveStyle("pin").OutlineColor);
        Assert.DoesNotContain("outlineColor\":", m.ToJson());
    }

    [Theory]
    [InlineData("\"red\"")]
    [InlineData("\"#FFF\"")]
    [InlineData("\"#80E81123\"")]
    [InlineData("\"#GG1123\"")]
    [InlineData("\" #E81123\"")]
    [InlineData("\"E81123\"")]
    [InlineData("\"url(#E81123)\"")]
    [InlineData("\"<Brush/>\"")]
    [InlineData("14881059")]
    [InlineData("true")]
    [InlineData("{}")]
    public void Bad_outline_colors_are_dropped_with_a_warning(string value)
    {
        var m = Bottom("\"pin\"", $$""", "style": { "outline": true, "outlineColor": {{value}} }, "overrides": { "pin": { "outlineColor": {{value}} } }""");
        Assert.Null(m.Style.OutlineColor);
        Assert.False(m.Overrides.ContainsKey("pin"));
        Assert.Equal(2, m.Warnings.Count(w => w.Contains("outlineColor")));
        Assert.True(m.Style.Outline);
    }

    private static CustomButton? Button(string body, string id = "b")
    {
        var m = Bottom($"\"{id}\"", $$""", "buttons": { "{{id}}": {{body}} }""");
        return m.Buttons.SingleOrDefault();
    }

    [Theory]
    [InlineData("spacer")]
    [InlineData("SPACER")]
    public void Legacy_spacer_is_ignored_as_an_item_and_as_a_button_name(string spacer)
    {
        Assert.Equal(["pin", "settings"], Bottom($"\"pin\", \"{spacer}\", \"settings\"").Items(Quadrant.BottomLeft));
        var m = Bottom("\"pin\"", $$""", "buttons": { "{{spacer}}": { "label": "x", "text": "t" } }""");
        Assert.Empty(m.Buttons);
        Assert.Contains(m.Warnings, w => w.Contains("spacer", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Icons_must_be_private_use_hex()
    {
        Assert.Equal(ToolbarModel.DefaultIcon, Button("""{ "label": "x", "icon": "ZZ", "text": "t" }""")!.Icon);
        Assert.Equal("E8A5", Button("""{ "label": "x", "icon": "e8a5", "text": "t" }""")!.Icon);
        Assert.Equal(ToolbarModel.DefaultIcon, Button("""{ "label": "x", "icon": "0041", "text": "t" }""")!.Icon);
        Assert.Equal(ToolbarModel.DefaultIcon, Button("""{ "label": "x", "icon": 59557, "text": "t" }""")!.Icon);
    }

    [Fact]
    public void Labels_are_cut_to_40_and_stripped_of_control_characters()
    {
        Assert.Equal(new string('a', 40), Button($$"""{ "label": "{{new string('a', 60)}}", "text": "t" }""")!.Label);
        Assert.Equal("ab", Button("""{ "label": "a\u0007b\n", "text": "t" }""")!.Label);
        Assert.Equal(new string('a', 40), Button($$"""{ "label": "{{new string(' ', 10) + new string('a', 60)}}", "text": "t" }""")!.Label);
    }

    [Fact]
    public void Text_actions_are_capped_at_2000()
    {
        var b = Button($$"""{ "label": "x", "text": "{{new string('t', 3000)}}" }""")!;
        Assert.Equal(2000, b.Text!.Length);
    }

    [Fact]
    public void At_most_24_custom_buttons()
    {
        var buttons = string.Join(", ", Enumerable.Range(0, 25).Select(i => $$"""
            "b{{i}}": { "label": "B", "text": "t" }
            """));
        var m = Bottom("\"pin\"", $$""", "buttons": { {{buttons}} }""");
        Assert.Equal(24, m.Buttons.Count);
        Assert.Equal("b23", m.Buttons[^1].Id);
        Assert.NotEmpty(m.Warnings);
    }

    [Fact]
    public void At_most_40_items_per_quadrant_are_read()
    {
        // The cap applies to the file's entries: everything after the 40th is ignored.
        var m = Bottom($"{Many("pin", 40)}, \"settings\"");
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
        Assert.NotEmpty(m.Warnings);
        Assert.Equal(["pin", "settings"], Bottom($"{Many("pin", 39)}, \"settings\"").Items(Quadrant.BottomLeft));
    }

    [Fact]
    public void Bad_custom_ids_are_ignored()
    {
        Assert.Null(Button("""{ "label": "x", "text": "t" }""", "pin"));
        Assert.Null(Button("""{ "label": "x", "text": "t" }""", "newchat"));
        Assert.Null(Button("""{ "label": "x", "text": "t" }""", "Bad Id!"));
        Assert.Null(Button("""{ "label": "x", "text": "t" }""", new string('a', 33)));
        Assert.NotNull(Button("""{ "label": "x", "text": "t" }""", "a-1"));
    }

    [Fact]
    public void A_button_needs_exactly_one_action()
    {
        Assert.Null(Button("""{ "label": "x", "text": "t", "quickAction": "q" }"""));
        Assert.Null(Button("""{ "label": "x" }"""));
        Assert.Null(Button("""{ "label": "x", "text": 5 }"""));
        var q = Button("""{ "label": "x", "quickAction": "q", "send": true }""")!;
        Assert.Equal(("q", (string?)null, false), (q.QuickAction, q.Text, q.Send));
    }

    [Fact]
    public void A_placed_custom_button_without_a_valid_definition_is_dropped()
    {
        var m = Bottom("\"pin\", \"ghost\"");
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
    }

    [Fact]
    public void Overrides_for_hidden_known_ids_are_kept()
    {
        var m = Bottom("\"pin\"", """, "overrides": { "settings": { "outline": true }, "PIN": { "filled": true } }""");
        Assert.Equal(new[] { "settings", "pin" }, m.Overrides.Keys);
        Assert.Empty(m.Warnings);
        Assert.Equal(new ButtonStyle(Filled: true), m.Overrides["pin"]);
    }

    [Fact]
    public void Pickers_take_the_look_keys_and_span_but_nothing_else()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 3, "bottom": { "left": ["pin", "model"] },
              "overrides": { "model": { "outline": true, "outlineColor": "#abcdef", "filled": false, "span": 5, "width": 2, "size": "wide", "cells": 3, "height": 9 }, "pin": { "filled": true } } }
            """);
        Assert.Equal(["pin", "model"], m.Overrides.Keys.OrderBy(k => k == "pin" ? 0 : 1));
        Assert.Equal(new ButtonStyle(Outline: true, Filled: false, OutlineColor: "#ABCDEF"), m.Overrides["model"]);
        Assert.Equal(5, m.EffectiveSpan("model"));
        var w = Assert.Single(m.Warnings);
        Assert.Contains("model", w);
        Assert.Contains("\"span\"", w);
        // Written back with the look and the span, never a width.
        var again = ToolbarModel.Resolve(m.ToJson());
        Assert.Empty(again.Warnings);
        Assert.Equal(m, again);
        Assert.Contains("\"model\": { \"span\": 5, \"outline\": true, \"filled\": false, \"outlineColor\": \"#ABCDEF\" }", m.ToJson());
    }

    [Fact]
    public void A_pickers_look_is_per_field_override_then_bar_then_default_and_has_no_width()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 3, "bottom": { "left": ["pin"], "right": ["effort", "model"] },
              "style": { "width": 3, "outline": true, "outlineColor": "#0078D4" },
              "overrides": { "effort": { "filled": true }, "model": { "outline": false, "outlineColor": "#E81123" } } }
            """);
        Assert.Empty(m.Warnings);
        Assert.Equal(new ButtonStyle(null, true, true, "#0078D4"), m.EffectiveStyle("effort"));
        Assert.Equal(new ButtonStyle(null, false, false, "#E81123"), m.EffectiveStyle("model"));
        Assert.Equal(new ButtonStyle(null, true, false, "#0078D4"), m.EffectiveStyle("provider"));
        Assert.Equal(3, m.EffectiveStyle("pin").Width);
    }

    [Fact]
    public void A_hidden_pickers_look_is_kept_like_its_span()
    {
        var m = Bottom("\"pin\"", """, "overrides": { "model": { "filled": true, "span": 6 }, "effort": "no" }""");
        Assert.Equal(new ButtonStyle(Filled: true), m.Overrides["model"]);
        Assert.Equal(6, m.EffectiveSpan("model"));
        Assert.Contains(m.Warnings, w => w.Contains("effort") && w.Contains("\"outlineColor\""));
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Fact]
    public void A_bad_picker_look_value_warns_and_is_ignored()
    {
        var m = Bottom("\"model\"", """, "overrides": { "model": { "outline": "yes", "outlineColor": "red" } }""");
        Assert.False(m.Overrides.ContainsKey("model"));
        Assert.Contains(m.Warnings, w => w.Contains("overrides.model.outline"));
        Assert.Contains(m.Warnings, w => w.Contains("overrides.model.outlineColor"));
    }

    [Fact]
    public void Repeated_keys_are_fixed_per_field_with_a_warning()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 3, "bottom": { "left": ["pin"], "left": ["settings", "a"] },
              "style": { "width": 2 },
              "overrides": { "settings": { "outline": true }, "settings": { "filled": true } },
              "buttons": { "a": { "label": "x", "text": "t" }, "a": { "label": "y", "text": "u" } } }
            """);
        Assert.Equal(["settings", "a"], m.Items(Quadrant.BottomLeft)); // the last value wins
        Assert.Equal("y", Assert.Single(m.Buttons).Label);
        Assert.Equal(new ButtonStyle(Filled: true), m.Overrides["settings"]);
        Assert.Equal(2, m.Style.Width); // the rest of the file still applies
        Assert.Contains(m.Warnings, w => w.Contains("Repeated key"));
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Theory]
    [InlineData(@"..\..\x")]
    [InlineData("a/b")]
    [InlineData(@"a\b")]
    [InlineData("..")]
    [InlineData(@"C:\actions\x")]
    [InlineData("C:x")]
    [InlineData("x.")]
    [InlineData(".x")]
    [InlineData("a*b")]
    [InlineData("fix my text")] // "/fix my text" would run "fix": names are what QuickActions parses ([\w-]+)
    [InlineData("a.b")]
    [InlineData("résumé 日本")]
    public void Quick_actions_must_be_plain_names(string name)
    {
        var m = Bottom("\"b\"", $$""", "buttons": { "b": { "label": "x", "quickAction": {{System.Text.Json.JsonSerializer.Serialize(name)}} } }""");
        Assert.Empty(m.Buttons);
        Assert.Contains(m.Warnings, w => w.Contains("quickAction"));
    }

    [Theory]
    [InlineData("summarise")]
    [InlineData("to-do-list")]
    [InlineData("my_action")]
    [InlineData("résumé日本")] // \w takes letters of any script
    public void Quick_action_names_may_have_hyphens_underscores_and_unicode_letters(string name) =>
        Assert.Equal(name, Button($$"""{ "label": "x", "quickAction": {{System.Text.Json.JsonSerializer.Serialize(name)}} }""")!.QuickAction);

    [Fact]
    public void Quick_action_names_are_at_most_64_characters()
    {
        Assert.NotNull(Button($$"""{ "label": "x", "quickAction": "{{new string('q', 64)}}" }"""));
        Assert.Null(Button($$"""{ "label": "x", "quickAction": "{{new string('q', 65)}}" }"""));
    }

    [Fact]
    public void Unreadable_file_warning_is_short()
    {
        var m = ToolbarModel.Resolve("{ \"" + new string('k', 5000) + "\" nope");
        Assert.True(Assert.Single(m.Warnings).Length < 300);
    }

    [Fact]
    public void Newer_version_is_read_tolerantly_with_a_warning()
    {
        var m = ToolbarModel.Resolve("""{ "version": 99, "bottom": { "left": ["pin"] }, "future": true }""");
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
        Assert.Contains(m.Warnings, w => w.Contains("99"));
    }

    [Fact]
    public void V1_file_gives_the_defaults_and_one_warning()
    {
        var m = ToolbarModel.Resolve("""{ "bottom": ["settings", "spacer", "pin"], "top": [] }""");
        Assert.Equal(ToolbarModel.Defaults with { Warnings = m.Warnings }, m);
        Assert.Equal([Hotline.Core.Text.Localization.Strings.Bar_OldVersionUnsupported], m.Warnings);
    }

    [Fact]
    public void V1_file_with_only_one_row_as_an_array_is_also_not_read()
    {
        var m = ToolbarModel.Resolve("""{ "version": 1, "bottom": { "left": ["pin"] }, "top": ["effort"] }""");
        Assert.Equal(ToolbarModel.Defaults.Items(Quadrant.BottomLeft), m.Items(Quadrant.BottomLeft));
        Assert.Single(m.Warnings);
    }

    [Fact]
    public void V2_file_with_a_row_as_an_array_is_read_per_field()
    {
        var m = ToolbarModel.Resolve("""{ "version": 3, "top": ["effort"], "bottom": { "left": ["pin"], "right": [] } }""");
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
        Assert.DoesNotContain(Hotline.Core.Text.Localization.Strings.Bar_OldVersionUnsupported, m.Warnings);
    }

    [Fact]
    public void Explicitly_empty_bar_is_kept()
    {
        var m = ToolbarModel.Resolve("""{ "version": 3, "top": { "left": [], "center": [], "right": [] }, "bottom": { "left": [], "center": [], "right": [] } }""");
        foreach (var q in Enum.GetValues<Quadrant>()) Assert.Empty(m.Items(q));
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Fact]
    public void Missing_rows_take_embedded_defaults_without_warning()
    {
        var m = ToolbarModel.Resolve("""{"version":3}""");
        Assert.Equal(ToolbarModel.Defaults, m);
        Assert.Empty(m.Warnings);
    }

    [Fact]
    public void Pickers_sit_between_buttons_in_any_order_and_a_second_listing_is_a_duplicate()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 3, "top": { "left": ["provider", "pin"], "center": ["model"], "right": ["effort", "prompt"] },
              "bottom": { "left": ["pin", "effort", "recent"], "center": ["provider"], "right": ["settings", "model"] } }
            """);
        Assert.Equal(["pin", "effort", "recent"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["provider"], m.Items(Quadrant.BottomCenter));
        Assert.Equal(["settings", "model"], m.Items(Quadrant.BottomRight));
        Assert.Empty(m.Items(Quadrant.TopLeft));
        Assert.Empty(m.Items(Quadrant.TopCenter));
        Assert.Equal(["prompt"], m.Items(Quadrant.TopRight));
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Fact]
    public void Resolve_is_idempotent_through_ToJson()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 3, "top": { "left": ["Effort", "x"], "right": ["b1"] },
              "bottom": { "left": ["pin", "spacer", "pin"], "right": ["model"] },
              "style": { "size": "COMPACT", "outline": 1 },
              "overrides": { "b1": { "width": 2, "cells": 3 }, "pin": {}, "model": { "outline": true, "width": 2 } },
              "buttons": { "b1": { "label": " \"quoted\" <b>é😀 ", "icon": "f8ff", "text": "line\nnext", "send": true } } }
            """);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
        Assert.Equal(m.ToJson(), ToolbarModel.Resolve(m.ToJson()).ToJson());
    }

    // ---- iconFile ----

    private static ToolbarModel WithIconFile(string iconFile) => ToolbarModel.Resolve($$"""
        { "version": 3, "bottom": { "left": ["pin", "b1"] },
          "buttons": { "b1": { "label": "B", "icon": "E8A5", "iconFile": {{iconFile}}, "text": "hi" } } }
        """);

    [Fact]
    public void IconFile_is_read_and_round_trips()
    {
        var m = WithIconFile("\"b1.png\"");
        Assert.Empty(m.Warnings);
        Assert.Equal(new CustomButton("b1", "B", "E8A5", null, "hi", false, "b1.png"), m.Buttons.Single());
        Assert.Contains("\"iconFile\": \"b1.png\"", m.ToJson());
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
        Assert.Equal(m.ToJson(), ToolbarModel.Resolve(m.ToJson()).ToJson());

        var none = Bottom("\"b1\"", """, "buttons": { "b1": { "label": "B", "icon": "E8A5", "text": "hi" } }""");
        Assert.Null(none.Buttons.Single().IconFile);
        Assert.DoesNotContain("iconFile", none.ToJson().Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        Assert.NotEqual(none, m); // part of equality
    }

    [Theory]
    [InlineData("\"../x.png\"")]
    [InlineData("\"..\\\\b1.png\"")]
    [InlineData("\"b2.png\"")]
    [InlineData("\"C:\\\\b1.png\"")]
    [InlineData("\"\\\\\\\\server\\\\b1.png\"")]
    [InlineData("\"b1.svg\"")]
    [InlineData("\"\"")]
    [InlineData("42")]
    [InlineData("null")]
    public void A_bad_iconFile_is_dropped_and_the_glyph_stays(string iconFile)
    {
        var m = WithIconFile(iconFile);
        var b = m.Buttons.Single();
        Assert.Null(b.IconFile);
        Assert.Equal("E8A5", b.Icon);
        if (iconFile != "null") Assert.Contains(m.Warnings, w => w.Contains("iconFile"));
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }
}
