using System.Text.RegularExpressions;

namespace Hotline.Core.Chat;

/// <summary>System prompts as editable Markdown files: %USERPROFILE%\.hotline\prompts\&lt;name&gt;.md.</summary>
public sealed partial class PromptLibrary(string directory)
{
    public const string DefaultName = "default";
    public const string DefaultText =
        "You are Hotline, a fast desktop assistant opened from the Windows Copilot key. Answer directly and concisely in Markdown.";

    public string Directory => directory;

    public static bool IsValidName(string name) => SafeName().IsMatch(name);

    public string PathFor(string name) => Path.Combine(directory, name + ".md");

    public void EnsureDefault()
    {
        System.IO.Directory.CreateDirectory(directory);
        if (!File.Exists(PathFor(DefaultName))) File.WriteAllText(PathFor(DefaultName), DefaultText);
    }

    public IReadOnlyList<string> List()
    {
        if (!System.IO.Directory.Exists(directory)) return [DefaultName];
        var names = System.IO.Directory.EnumerateFiles(directory, "*.md").Select(Path.GetFileNameWithoutExtension)
            .OfType<string>().Where(IsValidName).Where(n => n != DefaultName).Order(StringComparer.OrdinalIgnoreCase).ToList();
        names.Insert(0, DefaultName);
        return names;
    }

    /// <summary>The prompt text; missing, unsafe or empty prompts fall back to default.md, then to the built-in text.</summary>
    public string Read(string? name)
    {
        foreach (var candidate in new[] { name, DefaultName })
        {
            if (candidate is null || !IsValidName(candidate)) continue;
            try
            {
                var path = PathFor(candidate);
                if (File.Exists(path) && File.ReadAllText(path) is { } text && !string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return DefaultText;
    }

    /// <summary>Creates a new prompt (a copy of the default text) with a unique name; returns the name.</summary>
    public string Create(string? baseName = null)
    {
        System.IO.Directory.CreateDirectory(directory);
        var stem = baseName is not null && IsValidName(baseName) ? baseName : "prompt";
        var name = stem;
        for (var i = 2; File.Exists(PathFor(name)); i++) name = $"{stem}-{i}";
        File.WriteAllText(PathFor(name), Read(DefaultName));
        return name;
    }

    public bool Delete(string name)
    {
        if (name == DefaultName || !IsValidName(name) || !File.Exists(PathFor(name))) return false;
        File.Delete(PathFor(name));
        return true;
    }

    [GeneratedRegex(@"^[\w\- ]+$")]
    private static partial Regex SafeName();
}
