using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotline.Core.Settings;

/// <summary>
/// Loads/saves settings.json. Never throws: bad content is backed up to .bad and replaced with defaults;
/// an unreadable/locked file yields in-memory defaults (the file is left untouched).
/// </summary>
public sealed class SettingsStore(string directory)
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
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
            var loadedVersion = s.SchemaVersion;
            Normalize(s);
            if (loadedVersion < HotlineSettings.CurrentSchemaVersion)
                TrySave(s);
            return s;
        }
        catch (JsonException)
        {
            try
            {
                File.Copy(FilePath, FilePath + ".bad", overwrite: true);
                return SaveDefaults();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new HotlineSettings();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new HotlineSettings();
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

    private void TrySave(HotlineSettings s)
    {
        try { Save(s); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* keep running on in-memory settings */ }
    }

    private static void Migrate(HotlineSettings s)
    {
        // v1 → v2: the popup became a compact bar. Only move users still on the untouched v1 default size.
        if (s.SchemaVersion < 2 && s.Window.Width == 640 && s.Window.Height == 520)
            (s.Window.Width, s.Window.Height) = (560, 120);
    }

    private static HotlineSettings Normalize(HotlineSettings s)
    {
        s.Activation ??= new ActivationSettings();
        s.Window ??= new WindowSettings();
        s.Diagnostics ??= new DiagnosticsSettings();
        s.Window.TintOpacity = Math.Clamp(s.Window.TintOpacity, 0.0, 1.0);
        s.Window.LuminosityOpacity = Math.Clamp(s.Window.LuminosityOpacity, 0.0, 1.0);
        Migrate(s);
        s.Window.Width = Math.Clamp(s.Window.Width, 320, 4000);
        s.Window.Height = Math.Clamp(s.Window.Height, 80, 4000);
        s.SchemaVersion = HotlineSettings.CurrentSchemaVersion;
        return s;
    }
}
