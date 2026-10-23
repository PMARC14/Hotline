using System.Text;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarModelTests
{
    private const string Valid = """
        {
          "version": 2,
          // a comment
          "top":   { "left": ["effort", "model", "provider"], "right": [] },
          "items": { "left": ["pin", "captureWindow", "summarise"], "right": ["newChat", "settings", "thanks"] },
          "style": { "size": "wide", "outline": true, "filled": false },
          "overrides": { "pin": { "outline": false, "width": 44 } },
          "buttons": {
            "summarise": { "label": "Summarise", "icon": "E8A5", "quickAction": "summarise" },
            "thanks":    { "label": "Say thanks", "icon": "E8BD", "text": "Thanks!", "send": true },
          },
        }
        """;

    private static ToolbarModel Bottom(string left, string extra = "") =>
        ToolbarModel.Resolve($$"""{ "version": 2, "items": { "left": [{{left}}] } {{extra}} }""");

    private static string Many(string item, int count) => string.Join(", ", Enumerable.Repeat($"\"{item}\"", count));

    [Fact]
    public void Valid_v2_file_resolves_and_round_trips()
    {
        var m = ToolbarModel.Resolve(Valid);
        Assert.Empty(m.Warnings);
        Assert.Equal(["effort", "model", "provider"], m.Items(Quadrant.TopLeft));
        Assert.Empty(m.Items(Quadrant.TopRight));
        Assert.Equal(["pin", "captureWindow", "summarise"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["newChat", "settings", "thanks"], m.Items(Quadrant.BottomRight));
        Assert.Equal(new ButtonStyle(ButtonSize.Wide, true, false), m.Style);
        Assert.Equal(new ButtonStyle(Outline: false, Width: 44), m.Overrides["pin"]);
        Assert.Equal(
            [new CustomButton("summarise", "Summarise", "E8A5", "summarise", null, false), new CustomButton("thanks", "Say thanks", "E8BD", null, "Thanks!", true)],
            m.Buttons);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
        Assert.Empty(ToolbarModel.Resolve(m.ToJson()).Warnings);
    }

    [Fact]
    public void ToJson_has_the_header_and_version()
    {
        var json = ToolbarModel.Defaults.ToJson();
        Assert.Contains("\"version\": 2", json);
        Assert.Contains("//", json);
        foreach (var id in ToolbarModel.BuiltIn) Assert.Contains(id, json);
        Assert.Equal(ToolbarModel.Defaults, ToolbarModel.Resolve(json));
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
    public void Defaults_are_the_owners_layout()
    {
        var d = ToolbarModel.Defaults;
        Assert.Equal(["pin", "captureWindow", "captureScreen", "captureRegion"], d.Items(Quadrant.BottomLeft));
        Assert.Equal(["effort"], d.Items(Quadrant.TopLeft));
        Assert.Equal(["recent", "newChat", "settings"], d.Items(Quadrant.BottomRight));
        Assert.Equal(["provider"], d.Items(Quadrant.TopRight));
        Assert.Equal(new ButtonStyle(ButtonSize.Normal, false, false), d.Style);
        Assert.Empty(d.Overrides);
        Assert.Empty(d.Buttons);
        Assert.Equal(2, ToolbarModel.CurrentVersion);
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
            { "version": 2, "top": { "left": ["pin", "settings"], "right": ["newChat"] },
              "items": { "left": ["recent", "recent"], "right": ["newChat", "pin"] } }
            """);
        Assert.Equal(["recent"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["newChat", "pin"], m.Items(Quadrant.BottomRight));
        Assert.Equal(["settings"], m.Items(Quadrant.TopLeft));
        Assert.Empty(m.Items(Quadrant.TopRight));
    }

    [Fact]
    public void Legacy_spacer_is_ignored() =>
        Assert.Equal(["pin", "settings"], Bottom("\"pin\", \"spacer\", \"settings\"").Items(Quadrant.BottomLeft));

    [Fact]
    public void Width_and_height_are_clamped()
    {
        var m = Bottom("\"pin\", \"settings\"", """, "overrides": { "pin": { "width": 5, "height": 999 }, "settings": { "width": 999 } }""");
        Assert.Equal(new ButtonStyle(Width: 20, Height: 80), m.Overrides["pin"]);
        Assert.Equal(80, m.Overrides["settings"].Width);
    }

    [Fact]
    public void Bad_style_values_fall_back_per_field()
    {
        var m = Bottom("\"pin\"", """, "style": { "size": "huge", "outline": "yes", "filled": true }""");
        Assert.Equal(new ButtonStyle(ButtonSize.Normal, false, true), m.Style);
        Assert.Contains(m.Warnings, w => w.Contains("outline"));
        Assert.Contains(m.Warnings, w => w.Contains("huge"));
    }

    [Fact]
    public void EffectiveStyle_takes_override_then_bar_then_defaults()
    {
        var m = Bottom("\"pin\", \"settings\"", """, "style": { "size": "compact", "outline": true }, "overrides": { "pin": { "outline": false, "height": 40 } }""");
        Assert.Equal(new ButtonStyle(ButtonSize.Compact, false, false, null, 40), m.EffectiveStyle("pin"));
        Assert.Equal(new ButtonStyle(ButtonSize.Compact, true, false), m.EffectiveStyle("settings"));
    }

    [Theory]
    [InlineData("COMPACT", ButtonSize.Compact)]
    [InlineData("normal", ButtonSize.Normal)]
    [InlineData("Wide", ButtonSize.Wide)]
    public void The_three_sizes_resolve_per_bar_and_per_item_and_round_trip(string text, ButtonSize size)
    {
        var m = Bottom("\"pin\", \"settings\"", $$""", "style": { "size": "{{text}}" }, "overrides": { "pin": { "size": "compact" } }""");
        Assert.Empty(m.Warnings);
        Assert.Equal(size, m.Style.Size);
        Assert.Equal(ButtonSize.Compact, m.EffectiveStyle("pin").Size);
        Assert.Equal(size, m.EffectiveStyle("settings").Size);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Theory]
    [InlineData("double")]
    [InlineData("triple")]
    [InlineData("quadruple")]
    public void Any_other_size_is_normal_with_a_warning_naming_the_three(string text)
    {
        var m = Bottom("\"pin\"", $$""", "style": { "size": "{{text}}" }""");
        Assert.Equal(ButtonSize.Normal, m.Style.Size);
        Assert.Contains(m.Warnings, w => w.Contains(text) && w.Contains("compact, normal or wide"));
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

    [Fact]
    public void A_custom_button_called_spacer_is_ignored_with_a_warning()
    {
        var m = Bottom("\"pin\"", """, "buttons": { "spacer": { "label": "x", "text": "t" } }""");
        Assert.Empty(m.Buttons);
        Assert.Contains(m.Warnings, w => w.Contains("spacer"));
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
    public void Overrides_for_hidden_ids_are_ignored()
    {
        var m = Bottom("\"pin\"", """, "overrides": { "settings": { "outline": true }, "PIN": { "filled": true } }""");
        Assert.Equal(["pin"], m.Overrides.Keys);
        Assert.Equal(new ButtonStyle(Filled: true), m.Overrides["pin"]);
    }

    [Fact]
    public void Pickers_take_no_overrides()
    {
        var m = ToolbarModel.Resolve("""{ "version": 2, "items": { "left": ["pin", "model"] }, "overrides": { "model": { "outline": true }, "pin": { "filled": true } } }""");
        Assert.Equal(["pin"], m.Overrides.Keys);
        Assert.Contains(m.Warnings, w => w.Contains("model"));
    }

    [Fact]
    public void Repeated_keys_are_fixed_per_field_with_a_warning()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 2, "items": { "left": ["pin"], "left": ["settings", "a"] },
              "style": { "size": "wide" },
              "overrides": { "settings": { "outline": true }, "settings": { "filled": true } },
              "buttons": { "a": { "label": "x", "text": "t" }, "a": { "label": "y", "text": "u" } } }
            """);
        Assert.Equal(["settings", "a"], m.Items(Quadrant.BottomLeft)); // the last value wins
        Assert.Equal("y", Assert.Single(m.Buttons).Label);
        Assert.Equal(new ButtonStyle(Filled: true), m.Overrides["settings"]);
        Assert.Equal(ButtonSize.Wide, m.Style.Size); // the rest of the file still applies
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
        var m = ToolbarModel.Resolve("""{ "version": 3, "items": { "left": ["pin"] }, "future": true }""");
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
        Assert.Contains(m.Warnings, w => w.Contains('3'));
    }

    [Fact]
    public void V1_file_gives_the_defaults_and_one_warning()
    {
        var m = ToolbarModel.Resolve("""{ "items": ["settings", "spacer", "pin"], "top": [] }""");
        Assert.Equal(ToolbarModel.Defaults with { Warnings = m.Warnings }, m);
        Assert.Equal([Hotline.Core.Text.Localization.Strings.Bar_VersionOneUnsupported], m.Warnings);
    }

    [Fact]
    public void V1_file_with_only_one_row_as_an_array_is_also_not_read()
    {
        var m = ToolbarModel.Resolve("""{ "version": 1, "items": { "left": ["pin"] }, "top": ["effort"] }""");
        Assert.Equal(ToolbarModel.Defaults.Items(Quadrant.BottomLeft), m.Items(Quadrant.BottomLeft));
        Assert.Single(m.Warnings);
    }

    [Fact]
    public void V2_file_with_a_row_as_an_array_is_read_per_field()
    {
        var m = ToolbarModel.Resolve("""{ "version": 2, "top": ["effort"], "items": { "left": ["pin"], "right": [] } }""");
        Assert.Equal(["pin"], m.Items(Quadrant.BottomLeft));
        Assert.DoesNotContain(Hotline.Core.Text.Localization.Strings.Bar_VersionOneUnsupported, m.Warnings);
    }

    [Fact]
    public void Explicitly_empty_bar_is_kept()
    {
        var m = ToolbarModel.Resolve("""{ "version": 2, "top": { "left": [], "right": [] }, "items": { "left": [], "right": [] } }""");
        foreach (var q in Enum.GetValues<Quadrant>()) Assert.Empty(m.Items(q));
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Theory]
    [InlineData("""{ "version": 2 }""")]
    [InlineData("""{ "version": 2, "top": { "left": [], "right": [] }, "items": { "left": [] } }""")]
    [InlineData("""{ "version": 2, "top": { "left": [], "right": [] }, "items": { "left": [], "right": 5 } }""")]
    [InlineData("""{ "version": 2, "top": { "left": ["bogus"], "right": [] }, "items": { "left": ["spacer"], "right": [] } }""")]
    public void Accidentally_empty_bar_gets_the_defaults(string json)
    {
        var m = ToolbarModel.Resolve(json);
        Assert.Equal(ToolbarModel.Defaults.Placed.Values.SelectMany(v => v), m.Placed.Values.SelectMany(v => v));
        Assert.Equal(ToolbarModel.Defaults, m);
        Assert.NotEmpty(m.Warnings);
    }

    [Fact]
    public void Pickers_stay_where_they_are_listed_each_in_its_own_area()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 2, "top": { "left": ["effort"], "right": ["provider"] },
              "items": { "left": ["pin", "model", "prompt"], "right": ["settings"] } }
            """);
        Assert.Empty(m.Warnings);
        Assert.Equal(["pin", "model", "prompt"], m.Items(Quadrant.BottomLeft));
        Assert.Equal(["effort"], m.Items(Quadrant.TopLeft));
        Assert.Equal(["provider"], m.Items(Quadrant.TopRight));
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Fact]
    public void Pickers_sit_between_buttons_in_any_order_and_a_second_listing_is_a_duplicate()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 2, "top": { "left": ["provider", "pin"], "center": ["model"], "right": ["effort", "prompt"] },
              "items": { "left": ["pin", "effort", "recent"], "center": ["provider"], "right": ["settings", "model"] } }
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
    public void Each_picker_keeps_its_own_span_wherever_it_is()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 2, "top": { "left": ["effort"], "right": ["model"] }, "items": { "left": ["provider"] },
              "overrides": { "effort": { "span": 5 }, "model": { "span": 1 }, "provider": { "span": 7 } } }
            """);
        Assert.Equal([5, 1, 7], [m.EffectiveSpan("effort"), m.EffectiveSpan("model"), m.EffectiveSpan("provider")]);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
    }

    [Fact]
    public void Resolve_is_idempotent_through_ToJson()
    {
        var m = ToolbarModel.Resolve("""
            { "version": 2, "top": { "left": ["Effort", "x"], "right": ["b1"] },
              "items": { "left": ["pin", "spacer", "pin"], "right": ["model"] },
              "style": { "size": "COMPACT", "outline": 1 },
              "overrides": { "b1": { "size": "wide", "height": 3.6 }, "pin": {} },
              "buttons": { "b1": { "label": " \"quoted\" <b>é😀 ", "icon": "f8ff", "text": "line\nnext", "send": true } } }
            """);
        Assert.Equal(m, ToolbarModel.Resolve(m.ToJson()));
        Assert.Equal(m.ToJson(), ToolbarModel.Resolve(m.ToJson()).ToJson());
    }

    // ---- iconFile ----

    private static ToolbarModel WithIconFile(string iconFile) => ToolbarModel.Resolve($$"""
        { "version": 2, "items": { "left": ["pin", "b1"] },
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
    [InlineData("\"b1.PNG\"")]
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
