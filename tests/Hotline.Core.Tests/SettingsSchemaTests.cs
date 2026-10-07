using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class SettingsSchemaTests
{
    [Fact]
    public void Every_page_has_items_and_headers_are_unique_per_page()
    {
        foreach (var page in Enum.GetValues<SettingsPage>())
        {
            var items = SettingsSchema.Items.Where(i => i.Page == page).ToList();
            Assert.NotEmpty(items);
            Assert.Equal(items.Count, items.Select(i => i.Header).Distinct().Count());
        }
    }

    [Fact]
    public void Toggles_round_trip()
    {
        foreach (var t in SettingsSchema.Items.OfType<ToggleItem>())
        {
            var s = new HotlineSettings();
            var flipped = !t.Get(s);
            t.Set(s, flipped);
            Assert.Equal(flipped, t.Get(SettingsStore.Normalize(s)));
        }
    }

    [Fact]
    public void Number_ranges_survive_normalize()
    {
        foreach (var n in SettingsSchema.Items.OfType<NumberItem>())
        {
            var s = new HotlineSettings();
            n.Set(s, n.Max);
            Assert.Equal(n.Max, n.Get(SettingsStore.Normalize(s)), 3);
            var low = new HotlineSettings();
            n.Set(low, n.Min);
            Assert.InRange(n.Get(SettingsStore.Normalize(low)), n.Min, n.Max);
        }
    }

    [Fact]
    public void Choices_round_trip_and_defaults_are_listed()
    {
        foreach (var c in SettingsSchema.Items.OfType<ChoiceItem>())
        {
            Assert.Contains(c.Options, o => o.Value == c.Get(new HotlineSettings()));
            foreach (var option in c.Options)
            {
                var s = new HotlineSettings();
                c.Set(s, option.Value);
                Assert.Equal(option.Value, c.Get(SettingsStore.Normalize(s)));
            }
        }
    }

    [Fact]
    public void Text_items_round_trip_and_blank_resets_to_the_default()
    {
        foreach (var t in SettingsSchema.Items.OfType<TextItem>())
        {
            var s = new HotlineSettings();
            t.Set(s, "Ctrl+Alt+H");
            Assert.Equal("Ctrl+Alt+H", t.Get(s));
            t.Set(s, "  ");
            // Unset, or (for settings that always need a value) the default shown as the placeholder.
            Assert.True(t.Get(s) is null || t.Get(s) == t.Placeholder, $"{t.Header}: {t.Get(s)}");
        }
    }
}
