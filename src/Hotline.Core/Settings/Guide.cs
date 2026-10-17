namespace Hotline.Core.Settings;

/// <summary>
/// Settings › Guide: how to use every feature, one section each. Each section's texts are the resource strings
/// Guide_&lt;name&gt;_Title and Guide_&lt;name&gt;_Body (paragraphs separated by a line break), in every language.
/// </summary>
public static class Guide
{
    public static readonly IReadOnlyList<string> Sections =
    [
        "Open", "Connections", "Attach", "Voice", "Actions", "Memory", "History", "Background",
        "Move", "Direction", "Rows", "Sounds", "Files",
    ];

    public static string TitleKey(string section) => $"Guide_{section}_Title";
    public static string BodyKey(string section) => $"Guide_{section}_Body";
}
