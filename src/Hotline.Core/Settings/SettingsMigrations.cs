using System.Text.Json;

namespace Hotline.Core.Settings;

/// <summary>Embedded v1 defaults; future version migrations belong in SettingsMigrations.</summary>
internal static class SettingsDefaults
{
    private static readonly JsonDocument Document = Load();
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false) },
    };

    private static JsonDocument Load()
    {
        using var stream = typeof(HotlineSettings).Assembly.GetManifestResourceStream("Hotline.Core.Defaults.settings.json")
            ?? throw new InvalidOperationException("Missing settings defaults.");
        return JsonDocument.Parse(stream);
    }

    internal static T Get<T>(string section, string key) => Document.RootElement.GetProperty(section).GetProperty(key).Deserialize<T>(Options)!;
    internal static T Anchor<T>(string key) => Document.RootElement.GetProperty("window").GetProperty("anchor").GetProperty(key).Deserialize<T>(Options)!;
    internal static double? Number(string section, string key)
    {
        var value = Document.RootElement.GetProperty(section).GetProperty(key);
        return value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;
    }
}

/// <summary>No v1 migrations. Pre-freeze conversion runs separately before loading.</summary>
public static class SettingsMigrations
{
    public const int CurrentVersion = 1;
}
