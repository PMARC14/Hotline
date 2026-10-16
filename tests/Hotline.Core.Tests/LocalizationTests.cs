using System.Collections;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Tests;

/// <summary>The tests compare English messages: run them in English whatever the machine's language.</summary>
internal static class EnglishTestCulture
{
    [ModuleInitializer]
    internal static void Pin()
    {
        var en = CultureInfo.GetCultureInfo("en-US");
        CultureInfo.DefaultThreadCurrentUICulture = en;
        CultureInfo.DefaultThreadCurrentCulture = en;
        CultureInfo.CurrentUICulture = en;
        CultureInfo.CurrentCulture = en;
    }
}

public sealed partial class LocalizationTests
{
    private static Dictionary<string, string> Set(CultureInfo culture) =>
        Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)?
            .Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!) ?? [];

    private static readonly Dictionary<string, string> English = Set(CultureInfo.InvariantCulture);

    public static TheoryData<string> Translations => [.. Languages.Supported.Where(t => t != "en-US")];

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static string[] Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order()];

    [Fact]
    public void English_is_the_neutral_resource_and_not_empty()
    {
        Assert.True(English.Count > 300);
        Assert.All(English, e => Assert.False(string.IsNullOrWhiteSpace(e.Value), e.Key));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_language_has_every_text(string tag)
    {
        var translated = Set(CultureInfo.GetCultureInfo(tag));
        Assert.Empty(English.Keys.Except(translated.Keys).Order());
        Assert.Empty(translated.Keys.Except(English.Keys).Order()); // a renamed or removed key left behind
        Assert.All(translated, e => Assert.False(string.IsNullOrWhiteSpace(e.Value), $"{tag} {e.Key}"));
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Translations_keep_the_placeholders(string tag)
    {
        var problems = Set(CultureInfo.GetCultureInfo(tag))
            .Where(e => English.TryGetValue(e.Key, out var en) && !Placeholders(en).SequenceEqual(Placeholders(e.Value)))
            .Select(e => $"{e.Key}: [{string.Join(",", Placeholders(English[e.Key]))}] vs [{string.Join(",", Placeholders(e.Value))}]");
        Assert.Empty(problems);
    }

    [Theory]
    [MemberData(nameof(Translations))]
    public void Every_message_formats(string tag)
    {
        var args = Enumerable.Range(0, 5).Select(i => (object)$"<{i}>").ToArray();
        foreach (var (key, text) in Set(CultureInfo.GetCultureInfo(tag)))
            if (Placeholders(text).Length > 0)
            {
                var formatArgs = key == "Recent_Tip" ? new object[] { 3, DateTime.Now } : key == "Mcp_StartTimeout" ? [20.0] : args;
                var ex = Record.Exception(() => string.Format(CultureInfo.GetCultureInfo(tag), text, formatArgs));
                Assert.True(ex is null, $"{tag} {key}: {ex?.Message}");
            }
    }

    [Fact]
    public void Settings_text_is_resource_keys()
    {
        static bool Plain(string? s) => s is null or "px" or "%" or "DIP" || !s.Any(char.IsLetter) || s.StartsWith("Ctrl", StringComparison.Ordinal)
            || s is "Esc" or "Segoe UI Variable" || s.StartsWith('#'); // a colour code
        var missing = new List<string>();
        void Check(string? key) { if (!Plain(key) && !English.ContainsKey(key!)) missing.Add(key!); }
        foreach (var item in SettingsSchema.Items)
        {
            Check(item.Header);
            Check(item.Description);
            switch (item)
            {
                case ChoiceItem c when c.Header != nameof(Strings.Set_Language):
                    foreach (var o in c.Options) Check(o.Label);
                    break;
                case NumberItem n: Check(n.Unit); break;
                case TextItem t: Check(t.Placeholder); Check(t.Invalid); break;
            }
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void Lookups_follow_the_interface_language()
    {
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.NotEqual(English[nameof(Strings.Set_Language)], L.Get(nameof(Strings.Set_Language)));
            Assert.Equal(Strings.Set_Language, L.Get(nameof(Strings.Set_Language)));
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-CA"); // falls back to fr
            Assert.Equal(Set(CultureInfo.GetCultureInfo("fr"))[nameof(Strings.Common_Cancel)], Strings.Common_Cancel);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fi"); // not shipped: English
            Assert.Equal("Cancel", Strings.Common_Cancel);
        }
        finally { CultureInfo.CurrentUICulture = before; }
        Assert.Equal("px", L.Get("px")); // not a key: shown as is
        Assert.Equal("Low", L.Effort("low"));
        Assert.Equal("ultra", L.Effort("ultra"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("system", null)]
    [InlineData("SYSTEM", null)]
    [InlineData("de", "de")]
    [InlineData("PT-br", "pt-BR")]
    [InlineData("zh-hans", "zh-Hans")]
    [InlineData("klingon", null)]
    [InlineData("qps-ploc", "qps-Ploc")] // scripts/pseudo-loc.sh; not in the picker
    public void Language_setting_resolves(string? setting, string? culture) =>
        Assert.Equal(culture, Languages.Resolve(setting)?.Name);

    [Fact]
    public void Language_picker_names_each_language_in_itself()
    {
        Assert.Equal("Deutsch", Languages.NativeName("de"));
        Assert.Equal("日本語", Languages.NativeName("ja"));
        Assert.StartsWith("Español", Languages.NativeName("es"));
        Assert.Equal(Languages.Supported.Count, Languages.Supported.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var item = Assert.IsType<ChoiceItem>(SettingsSchema.Items.Single(i => i.Header == nameof(Strings.Set_Language)));
        Assert.True(item.RequiresRestart);
        Assert.Equal(Languages.System, item.Options[0].Value);
        Assert.Equal(Languages.Supported, item.Options.Skip(1).Select(o => o.Value));
    }

    [Fact]
    public void Language_defaults_to_windows_and_round_trips()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, SettingsStore.FileName), """{ "schemaVersion": 8 }""");
            var store = new SettingsStore(dir);
            var s = store.Load();
            Assert.Equal(Languages.System, s.General.Language);
            Assert.Equal(HotlineSettings.CurrentSchemaVersion, s.SchemaVersion);
            s.General.Language = "ja";
            store.Save(s);
            Assert.Contains("\"language\": \"ja\"", File.ReadAllText(store.FilePath));
            Assert.Equal("ja", new SettingsStore(dir).Load().General.Language);
            var item = Assert.IsType<ChoiceItem>(SettingsSchema.Items.Single(i => i.Header == nameof(Strings.Set_Language)));
            s.General.Language = "unknown";
            Assert.Equal(Languages.System, item.Get(s)); // an unknown tag shows (and works) as "Use Windows language"
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
}
