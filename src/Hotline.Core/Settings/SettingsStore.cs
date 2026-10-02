using Hotline.Core.Activation;
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
            var text = File.ReadAllText(FilePath);
            var s = JsonSerializer.Deserialize<HotlineSettings>(text, Options)
                    ?? throw new JsonException("settings.json contained null");
            var loadedVersion = s.SchemaVersion;
            Normalize(s);
            if (loadedVersion < HotlineSettings.CurrentSchemaVersion)
                TrySave(s);
            else if (HasMissingOptions(text, s))
            {
                // The file is the configuration: write newly added options into it so every setting is visible and
                // editable there. Keep the previous file (it may hold comments) as settings.json.bak.
                try { File.Copy(FilePath, FilePath + ".bak", overwrite: true); } catch (IOException) { }
                TrySave(s);
            }
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

    /// <summary>True when the file lacks an option the current schema has (compared by property names, recursively).</summary>
    private static bool HasMissingOptions(string fileText, HotlineSettings s)
    {
        var docOptions = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var file = JsonDocument.Parse(fileText, docOptions);
        using var full = JsonDocument.Parse(JsonSerializer.Serialize(s, Options));
        return Missing(full.RootElement, file.RootElement);

        static bool Missing(JsonElement expected, JsonElement actual)
        {
            if (expected.ValueKind == JsonValueKind.Object)
            {
                if (actual.ValueKind != JsonValueKind.Object) return false;
                foreach (var p in expected.EnumerateObject())
                {
                    var found = actual.EnumerateObject().FirstOrDefault(a => string.Equals(a.Name, p.Name, StringComparison.OrdinalIgnoreCase));
                    if (found.Value.ValueKind == JsonValueKind.Undefined) return p.Value.ValueKind != JsonValueKind.Null;
                    if (Missing(p.Value, found.Value)) return true;
                }
            }
            else if (expected.ValueKind == JsonValueKind.Array && actual.ValueKind == JsonValueKind.Array)
            {
                foreach (var (e, a) in expected.EnumerateArray().Zip(actual.EnumerateArray()))
                    if (Missing(e, a)) return true;
            }
            return false;
        }
    }

    private void TrySave(HotlineSettings s)
    {
        try { Save(s); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* keep running on in-memory settings */ }
    }

    private static void Migrate(HotlineSettings s)
    {
        // → v6: text size follows Windows unless chosen. 14 was the old fixed default.
        if (s.SchemaVersion < 6 && s.Window.FontSize == 14)
            s.Window.FontSize = null;
        // → v6: the empty panel hugs the message bar (no reserved space). Move untouched old default heights.
        if (s.SchemaVersion < 6 && s.Window.Height is 120 or 320 or 520)
            s.Window.Height = 0;
        // → v5: the panel starts as a minimal input bar again (user feedback). Move untouched old default heights.
        // v2 → v3: long press now starts a new chat. Only move users still on the old default.
        if (s.SchemaVersion < 3 && s.Activation.Hold == KeyAction.ShowPopup)
            s.Activation.Hold = KeyAction.NewChat;
    }

    public static HotlineSettings Normalize(HotlineSettings s)
    {
        s.Activation ??= new ActivationSettings();
        s.Window ??= new WindowSettings();
        s.Diagnostics ??= new DiagnosticsSettings();
        s.Chat ??= new ChatSettings();
        s.Chat.Backends = (s.Chat.Backends ?? []).Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Id)).ToList();
        if (s.Chat.Backends.Count == 0) s.Chat.Backends = ChatSettings.DefaultBackends();
        foreach (var b in s.Chat.Backends.Where(b => string.IsNullOrWhiteSpace(b.Name))) b.Name = b.Id;
        if (!s.Chat.Backends.Any(b => b.Id == s.Chat.DefaultBackend)) s.Chat.DefaultBackend = s.Chat.Backends[0].Id;
        s.Chat.MaxImagePixels = Math.Clamp(s.Chat.MaxImagePixels, 256, 8192);
        s.Chat.HistoryRetentionDays = Math.Clamp(s.Chat.HistoryRetentionDays, 1, 3650);
        if (s.Window.FontSize is { } fontSize) s.Window.FontSize = Math.Clamp(fontSize, 10, 32);
        s.Window.Caret ??= new CaretSettings();
        s.Window.Caret.Width = Math.Clamp(s.Window.Caret.Width, 1, 8);
        s.Window.Caret.BlinkMs = Math.Clamp(s.Window.Caret.BlinkMs, 200, 2000);
        if (s.Window.Caret.Color is { } caretColor && !Theming.ThemeColor.TryParse(caretColor, out _)) s.Window.Caret.Color = null;
        s.Window.VerticalPosition = Math.Clamp(s.Window.VerticalPosition, 0.0, 1.0);
        s.Window.TintOpacity = Math.Clamp(s.Window.TintOpacity, 0.0, 1.0);
        s.Window.LuminosityOpacity = Math.Clamp(s.Window.LuminosityOpacity, 0.0, 1.0);
        Migrate(s);
        s.Window.WidthPercent = Math.Clamp(s.Window.WidthPercent, 20, 90);
        s.Window.MinWidth = Math.Clamp(s.Window.MinWidth, 320, 4000);
        s.Window.MaxWidth = Math.Clamp(s.Window.MaxWidth, s.Window.MinWidth, 4000);
        s.Window.Height = Math.Clamp(s.Window.Height, 0, 4000);
        s.Window.MaxHeightPercent = Math.Clamp(s.Window.MaxHeightPercent, 30, 95);
        s.SchemaVersion = HotlineSettings.CurrentSchemaVersion;
        return s;
    }
}
