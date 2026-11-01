using Hotline.Core.Settings;
using Hotline.Core.Text;

namespace Hotline.Core.Tests;

/// <summary>Fixes from the full-codebase review (text and the language picker; the settings-store ones live in SettingsStoreTests).</summary>
public sealed class SettingsReviewTests
{
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
