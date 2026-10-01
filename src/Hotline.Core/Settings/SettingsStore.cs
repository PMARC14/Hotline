using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotline.Core.Settings;

/// <summary>Loads/saves settings.json. Never throws on bad content: backs it up to .bad and uses defaults.</summary>
public sealed class SettingsStore(string directory)
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string FilePath => Path.Combine(directory, FileName);

    public HotlineSettings Load()
    {
        if (!File.Exists(FilePath))
            return SaveDefaults();

        try
        {
            var s = JsonSerializer.Deserialize<HotlineSettings>(File.ReadAllText(FilePath), Options)
                    ?? throw new JsonException("settings.json contained null");
            return Normalize(s);
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".bad", overwrite: true);
            return SaveDefaults();
        }
    }

    public void Save(HotlineSettings s)
    {
        Directory.CreateDirectory(directory);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private HotlineSettings SaveDefaults()
    {
        var d = new HotlineSettings();
        Save(d);
        return d;
    }

    private static HotlineSettings Normalize(HotlineSettings s)
    {
        s.Activation ??= new ActivationSettings();
        s.Window ??= new WindowSettings();
        s.Window.Width = Math.Clamp(s.Window.Width, 320, 4000);
        s.Window.Height = Math.Clamp(s.Window.Height, 200, 4000);
        s.SchemaVersion = HotlineSettings.CurrentSchemaVersion;
        return s;
    }
}
