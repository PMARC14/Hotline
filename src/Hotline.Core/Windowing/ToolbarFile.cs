using System.Text.Json;
using System.Text.Json.Nodes;
using Hotline.Core.Settings;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>What <see cref="ToolbarFile.Load"/> found. <see cref="Error"/> is set when the file couldn't be read (the model is
/// then the defaults and the file is untouched).</summary>
public sealed record ToolbarLoad(ToolbarModel Model, string? Error);

/// <summary>~/.hotline/toolbar.json: reading it (creating a missing one) and writing it.</summary>
public static class ToolbarFile
{
    /// <summary>
    /// Never throws. A missing file is created with the defaults. Any existing file is read and never rewritten (one in the
    /// old version 1 format gives the defaults and a warning, see <see cref="ToolbarModel.Resolve"/>); an unreadable one is
    /// left untouched.
    /// </summary>
    public static ToolbarLoad Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                TrySave(path, ToolbarModel.Defaults);
                return new ToolbarLoad(ToolbarModel.Defaults, null);
            }
            var text = File.ReadAllText(path);
            JsonNode? root;
            try { root = ToolbarModel.Parse(text, []); }
            catch (JsonException ex) { return Unreadable(ex.Message); }
            if (root is not JsonObject) return Unreadable("the file isn't a JSON object");

            return new ToolbarLoad(ToolbarModel.Resolve(text), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Unreadable(ex.Message);
        }
    }

    private static ToolbarLoad Unreadable(string reason) =>
        new(ToolbarModel.Defaults, L.F(Strings.Bar_Unreadable, reason));

    /// <summary>Writes the model as a v2 file: a temp file in the same folder, then moved over the old one. The file it
    /// replaces is kept as <c>toolbar.json.bak</c> first (<paramref name="keepPrevious"/>): Settings rewrites a hand-edited
    /// file from the model, which drops its comments and anything that was ignored. If the copy fails, nothing is replaced.</summary>
    public static void Save(string path, ToolbarModel model, bool keepPrevious = true)
    {
        if (Path.GetDirectoryName(path) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
        var tmp = path + ".tmp";
        try
        {
            if (keepPrevious && File.Exists(path)) File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllText(tmp, model.ToJson());
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* nothing more to do */ }
            throw;
        }
    }

    private static bool TrySave(string path, ToolbarModel model)
    {
        try { Save(path, model, keepPrevious: false); return true; } // only used for a missing file: nothing to keep
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
