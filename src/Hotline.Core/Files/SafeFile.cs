using System.Security.Cryptography;
using System.Text.Json;

namespace Hotline.Core.Files;

public enum SafeFileResult { Written, Unchanged, NewerVersion, BackupFailed, WriteFailed }

/// <summary>Checks the disk version and preserves the original before publishing complete config bytes.</summary>
public sealed class SafeFile(IFileSystem? fileSystem = null)
{
    private readonly IFileSystem _files = fileSystem ?? new RealFileSystem();

    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <summary>The explicit unbacked-write exception applies only to an Upgrade backup set.</summary>
    public SafeFileResult Replace(string path, byte[] bytes, BackupSet backupSet, int version,
        string? lastWrittenHash = null, bool allowUnbackedUpgrade = false)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(backupSet);
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        path = Path.GetFullPath(path);
        backupSet.RelativePath(path);
        try
        {
            if (_files.FileExists(path))
            {
                var original = _files.ReadAllBytes(path);
                if (IsNewer(original, version)) return SafeFileResult.NewerVersion;
                if (original.AsSpan().SequenceEqual(bytes)) return SafeFileResult.Unchanged;
                if (!string.Equals(Hash(original), lastWrittenHash, StringComparison.OrdinalIgnoreCase) &&
                    !backupSet.TryBackup(path) && !(allowUnbackedUpgrade && backupSet.Reason == BackupReason.Upgrade))
                    return SafeFileResult.BackupFailed;
            }
            _files.CreateDirectory(Path.GetDirectoryName(path)!);
            _files.WriteDurable(path, bytes);
            return SafeFileResult.Written;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return SafeFileResult.WriteFailed; }
    }

    /// <summary>True when the JSON object's <c>version</c> (any case, BOM allowed) is above <paramref name="version"/>.</summary>
    internal static bool IsNewer(byte[] bytes, int version)
    {
        try
        {
            ReadOnlyMemory<byte> json = bytes;
            if (json.Span.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) json = json[3..];
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            JsonElement? found = null;
            foreach (var member in document.RootElement.EnumerateObject())
                if (member.Name.Equals("version", StringComparison.OrdinalIgnoreCase)) found = member.Value;
            return found is { ValueKind: JsonValueKind.Number } v && v.TryGetDouble(out var number) && number > version;
        }
        catch (JsonException) { return false; }
    }
}
