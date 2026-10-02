using Hotline.Core.Chat;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// agy's working directory: holds the custom "hotline" agent and per-message image attachments.
/// agy may read files inside its (trusted) workspace without prompting; reads elsewhere are denied
/// in headless mode, which is the boundary we want.
/// </summary>
public sealed class AgyWorkspace(string root, TimeProvider clock)
{
    public string Root { get; } = root;

    private string AttachmentsDir => Path.Combine(Root, "attachments");

    // Do NOT add excludeDefaultComponents: it drops the default permissions (workspace reads get denied).
    public static string AgentMarkdownFor(string systemPrompt) => $"""
        ---
        name: hotline
        description: Fast conversational assistant for the Hotline popup.
        tools:
          - view_file
        ---
        {systemPrompt.Trim()}

        When the user lists attached images under ./attachments, use view_file to look at them.
        Do not run commands, browse the web or edit files in this mode.
        """;

    public void Ensure(string systemPrompt)
    {
        var agentDir = Path.Combine(Root, ".agents", "agents");
        Directory.CreateDirectory(agentDir);
        Directory.CreateDirectory(AttachmentsDir);
        var agentFile = Path.Combine(agentDir, "hotline.md");
        var markdown = AgentMarkdownFor(systemPrompt);
        if (!File.Exists(agentFile) || File.ReadAllText(agentFile) != markdown)
            File.WriteAllText(agentFile, markdown);
    }

    public IReadOnlyList<string> SaveImages(string messageId, IReadOnlyList<Attachment> images)
    {
        if (images.Count == 0) return [];
        var safeId = Sanitize(messageId);
        var dir = Path.Combine(AttachmentsDir, safeId);
        Directory.CreateDirectory(dir);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (var image in images)
        {
            var name = Sanitize(Path.GetFileName(image.Name.Replace('\\', '/').Split('/')[^1]));
            if (name.Length == 0) name = "image.png";
            var unique = name;
            for (var i = 2; !used.Add(unique); i++)
                unique = $"{Path.GetFileNameWithoutExtension(name)}-{i}{Path.GetExtension(name)}";
            File.WriteAllBytes(Path.Combine(dir, unique), image.Data);
            paths.Add($"attachments/{safeId}/{unique}");
        }
        return paths;
    }

    public int PruneAttachments(TimeSpan olderThan)
    {
        if (!Directory.Exists(AttachmentsDir)) return 0;
        var cutoff = clock.GetUtcNow().UtcDateTime - olderThan;
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(AttachmentsDir))
        {
            if (Directory.GetLastWriteTimeUtc(dir) >= cutoff) continue;
            Directory.Delete(dir, recursive: true);
            removed++;
        }
        return removed;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || c == ':' ? '_' : c).ToArray()).Trim('.', ' ');
        return cleaned.Replace("..", "_");
    }
}
