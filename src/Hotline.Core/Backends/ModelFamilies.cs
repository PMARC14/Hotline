using System.Text.RegularExpressions;

namespace Hotline.Core.Backends;

/// <summary>One model with its effort variants (agy lists "Gemini 3.8 Flash (High)" etc. as separate ids).</summary>
public sealed record ModelFamily(string Name, IReadOnlyList<(string Level, string Id)> Variants)
{
    public bool HasLevels => Variants.Count > 1 || Variants[0].Level.Length > 0;
    public IEnumerable<string> Levels => Variants.Select(v => v.Level).Where(l => l.Length > 0);
}

/// <summary>
/// Groups a flat model list into families + effort levels, so the Model picker shows clean names and the Effort
/// picker shows only the levels that model really has. Levels are the agy label suffixes (Low/Medium/High/Max).
/// </summary>
public static partial class ModelFamilies
{
    private static readonly string[] LevelOrder = ["low", "medium", "high", "max"];

    public static IReadOnlyList<ModelFamily> Group(IReadOnlyList<ModelInfo> models)
    {
        var families = new List<(string Name, List<(string Level, string Id)> Variants)>();
        foreach (var m in models)
        {
            var match = LevelSuffix().Match(m.Label);
            var (name, level) = match.Success ? (match.Groups[1].Value.Trim(), match.Groups[2].Value.ToLowerInvariant()) : (m.Label, "");
            var family = families.FirstOrDefault(f => f.Name == name);
            if (family.Name is null) families.Add(family = (name, []));
            family.Variants.Add((level, m.Id));
        }
        return families.Select(f => new ModelFamily(f.Name,
            f.Variants.OrderBy(v => Array.IndexOf(LevelOrder, v.Level)).ToList())).ToList();
    }

    /// <summary>The family and level a stored model id belongs to (null when the id isn't in the list).</summary>
    public static (ModelFamily Family, string Level)? Locate(IReadOnlyList<ModelFamily> families, string? id)
    {
        foreach (var f in families)
            foreach (var v in f.Variants)
                if (v.Id == id) return (f, v.Level);
        return null;
    }

    /// <summary>
    /// The model id for a family at a level. A level the family lacks falls back to the closest one it has
    /// (by position low → max), so switching families never produces an invalid model.
    /// </summary>
    public static string Resolve(ModelFamily family, string? level)
    {
        if (level is not null && family.Variants.FirstOrDefault(v => v.Level == level) is { Id: not null } exact) return exact.Id;
        if (level is null || !family.HasLevels) return family.Variants[family.Variants.Count / 2].Id;
        var want = Array.IndexOf(LevelOrder, level);
        return family.Variants.OrderBy(v => Math.Abs(Array.IndexOf(LevelOrder, v.Level) - want)).ThenByDescending(v => Array.IndexOf(LevelOrder, v.Level)).First().Id;
    }

    [GeneratedRegex(@"^(.*)\((Low|Medium|High|Max)\)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex LevelSuffix();
}
