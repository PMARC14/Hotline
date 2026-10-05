using System.Text;

namespace Hotline.Core.Chat;

public sealed record AttachmentLimits(int MaxCount = 10, long MaxImageBytes = 20 * 1024 * 1024, long MaxTextBytes = 200 * 1024);

public sealed class AttachmentRejectedException(string reason) : Exception(reason);

public static class AttachmentFactory
{
    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".bmp"] = "image/bmp",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".jsonc", ".xml", ".yaml", ".yml", ".toml", ".ini", ".csv", ".tsv", ".log",
        ".cs", ".csproj", ".sln", ".slnx", ".py", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".html", ".htm", ".css", ".scss",
        ".sql", ".ps1", ".psm1", ".sh", ".bat", ".cmd", ".c", ".h", ".cpp", ".hpp", ".java", ".kt", ".go", ".rs", ".rb",
        ".php", ".swift", ".lua", ".r", ".tex",
    };

    public static Attachment FromBytes(string name, string? mimeType, byte[] data, AttachmentLimits limits)
    {
        var ext = Path.GetExtension(name);
        var isImageMime = mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false;
        if (ImageTypes.TryGetValue(ext, out var imageMime) || isImageMime)
        {
            if (data.LongLength > limits.MaxImageBytes)
                throw new AttachmentRejectedException($"{name} is larger than {limits.MaxImageBytes / (1024 * 1024)} MB.");
            return new Attachment(Ids.New(), name, AttachmentKind.Image, imageMime ?? mimeType!, data);
        }

        var isTextMime = mimeType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ?? false;
        if (TextExtensions.Contains(ext) || isTextMime)
        {
            if (data.LongLength > limits.MaxTextBytes)
                throw new AttachmentRejectedException($"{name} is larger than {limits.MaxTextBytes / 1024} KB.");
            if (!IsUtf8(data))
                throw new AttachmentRejectedException($"{name} isn't UTF-8 text.");
            return new Attachment(Ids.New(), name, AttachmentKind.Text, "text/plain", data);
        }

        throw new AttachmentRejectedException($"{name}: unsupported file type.");
    }

    private static bool IsUtf8(byte[] data)
    {
        try { new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data); return true; }
        catch (DecoderFallbackException) { return false; }
    }
}

/// <summary>Selected text from the app you came from, as a removable text attachment.</summary>
public static class SelectionAttachment
{
    /// <summary>Longest selection attached (characters); longer ones are cut with <see cref="TruncatedNote"/>.</summary>
    public const int MaxChars = 50_000;
    public const string TruncatedNote = "\n[… selection cut here]";

    public static Attachment? Create(string? text, string? appName)
    {
        text = text?.Trim();
        if (string.IsNullOrEmpty(text)) return null;
        if (text.Length > MaxChars)
        {
            var cut = char.IsHighSurrogate(text[MaxChars - 1]) ? MaxChars - 1 : MaxChars;
            text = text[..cut] + TruncatedNote;
        }
        var name = string.IsNullOrWhiteSpace(appName) ? "Selected text" : $"Selected text from {appName.Trim()}";
        return new Attachment(Ids.New(), name, AttachmentKind.Text, "text/plain", Encoding.UTF8.GetBytes(text));
    }
}

public sealed class AttachmentTray(AttachmentLimits limits)
{
    private readonly List<Attachment> _items = [];

    public IReadOnlyList<Attachment> Items => _items;

    public void Add(Attachment attachment)
    {
        if (_items.Count >= limits.MaxCount)
            throw new AttachmentRejectedException($"You can attach up to {limits.MaxCount} files.");
        _items.Add(attachment);
    }

    public bool Remove(string id) => _items.RemoveAll(a => a.Id == id) > 0;

    public IReadOnlyList<Attachment> TakeAll()
    {
        var all = _items.ToList();
        _items.Clear();
        return all;
    }
}
