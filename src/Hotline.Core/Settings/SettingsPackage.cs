using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Diagnostics;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Settings;

/// <summary>What a backup package can hold (Settings › Backup). Each is a tick box.</summary>
public enum SettingsCategory { Settings, Toolbar, Mcp, Sounds, Connections, Actions, Prompts }

/// <summary>A file left out of an export or import, with the reason (a plain English note, never the file's content).</summary>
public sealed record SkippedFile(string Path, string Reason)
{
    /// <summary>The reason in the interface language (<see cref="Reason"/> stays English: it is what the log gets).</summary>
    public string Text => SkipReasons.Keys.TryGetValue(Reason, out var key) ? L.Get(key) : Reason;
}

/// <summary>The skip reasons and their resource keys.</summary>
public static class SkipReasons
{
    public const string FolderUnreadable = "couldn't be listed";

    public static readonly Dictionary<string, string> Keys = new()
    {
        ["too many files"] = nameof(Strings.Backup_Why_TooMany),
        ["a link, not a file"] = nameof(Strings.Backup_Why_Link),
        ["larger than 8 MB"] = nameof(Strings.Backup_Why_Big),
        ["the backup would pass 64 MB"] = nameof(Strings.Backup_Why_Total),
        ["couldn't be read"] = nameof(Strings.Backup_Why_Unreadable),
        ["not a name a backup can hold"] = nameof(Strings.Backup_Why_BadName),
        [FolderUnreadable] = nameof(Strings.Backup_Why_Folder),
        ["not a usable path"] = nameof(Strings.Backup_Why_BadPath),
        ["outside the settings folder"] = nameof(Strings.Backup_Why_Outside),
        ["its folder is a link"] = nameof(Strings.Backup_Why_FolderLink),
        ["couldn't be written (in use or read-only)"] = nameof(Strings.Backup_Why_Locked),
        ["not a .wav sound"] = nameof(Strings.Backup_Why_NotWav),
        ["not a usable picture (PNG, 16-256 px)"] = nameof(Strings.Backup_Why_NotPng),
        ["not a text file"] = nameof(Strings.Backup_Why_NotText),
        ["not readable as settings"] = nameof(Strings.Backup_Why_NotSettings),
        ["not a JSON object"] = nameof(Strings.Backup_Why_NotObject),
        ["not readable"] = nameof(Strings.Backup_Why_NotReadable),
        ["larger than a quick action can be (64 KB)"] = nameof(Strings.Backup_Why_ActionBig),
        ["not readable as a connection"] = nameof(Strings.Backup_Why_NotConnection),
    };
}

public sealed record ExportResult(int FileCount, IReadOnlyList<SkippedFile> Skipped);

/// <summary>What a package says it is. <see cref="Problems"/> are the reasons it can't be imported at all.</summary>
public sealed record PackageInfo(int Format, string? AppVersion, DateTimeOffset? Created,
    IReadOnlyDictionary<SettingsCategory, int> Counts, IReadOnlyList<string> Problems)
{
    public bool IsValid => Problems.Count == 0;
}

/// <param name="Written">Relative paths (forward slashes) of the files now in the data folder.</param>
/// <param name="BackupFolder">Where the files that were replaced were copied; null when nothing was replaced.</param>
/// <param name="Error">Set when nothing was imported at all (an unusable package, or the backup could not be made).</param>
public sealed record ImportResult(IReadOnlyList<string> Written, IReadOnlyList<SkippedFile> Skipped, string? BackupFolder, string? Error);

/// <summary>
/// Settings export and import: one .zip holding the ticked parts of the data folder plus <c>hotline-export.json</c> (format,
/// app version, date, and every file's size and SHA-256). Never exported: logs, history, memory, backups, the .defaults copies,
/// .bak/.old files, and API keys (they live in the Credential Locker, not in a file). An import adds and replaces; it never
/// deletes, and copies every file it replaces to backups\import-&lt;time&gt; first.
/// </summary>
public static partial class SettingsPackage
{
    public const int CurrentFormat = 1, MaxEntries = 400;
    public const int MaxEntryBytes = 8 * 1024 * 1024;
    public const long MaxTotalBytes = 64L * 1024 * 1024;
    public const string ManifestName = "hotline-export.json";

    // Deflate can't pass ~1000:1, so this only catches a squeezed run of one byte; the size limits do the real work.
    private const int MaxRatio = 500, RatioFloor = 1024 * 1024, MaxManifestBytes = 1024 * 1024;

    public static IReadOnlyList<SettingsCategory> All { get; } = Enum.GetValues<SettingsCategory>();

    public static string Name(SettingsCategory category) => category switch
    {
        SettingsCategory.Settings => Strings.Backup_Cat_Settings,
        SettingsCategory.Toolbar => Strings.Backup_Cat_Toolbar,
        SettingsCategory.Mcp => Strings.Backup_Cat_Mcp,
        SettingsCategory.Sounds => Strings.Backup_Cat_Sounds,
        SettingsCategory.Connections => Strings.Backup_Cat_Connections,
        SettingsCategory.Actions => Strings.Backup_Cat_Actions,
        _ => Strings.Backup_Cat_Prompts,
    };

    /// <summary>The default file name: <c>hotline-settings-20261022-1530.zip</c> (local time).</summary>
    public static string FileName(DateTimeOffset now) => $"hotline-settings-{now.ToLocalTime():yyyyMMdd-HHmm}.zip";

    /// <summary>A package file name made safe for the log: clean, one line, short. Names are the only package text ever logged.</summary>
    public static string LogName(string? name)
    {
        var text = LogText.Clean(name).ReplaceLineEndings(" ");
        return text.Length > 80 ? text[..80] + "..." : text;
    }

    // ---- export ------------------------------------------------------------------------------

    private sealed record Candidate(string Path, string FullPath, SettingsCategory Category);

    /// <summary>
    /// Writes the ticked categories of <paramref name="dataDirectory"/> to <paramref name="output"/> (a writable stream; left
    /// open). Files that couldn't be imported again (a name or size outside the limits, a link, an unreadable file) are left out
    /// and listed in the result.
    /// </summary>
    public static ExportResult Export(string dataDirectory, IEnumerable<SettingsCategory> categories, Stream output,
        DateTimeOffset? now = null, string? appVersion = null)
    {
        var ticked = categories.Distinct().Order().ToList();
        var skipped = new List<SkippedFile>();
        var files = new List<(string Path, byte[] Data)>();
        long total = 0;
        foreach (var candidate in Candidates(dataDirectory, ticked, skipped))
        {
            if (files.Count >= MaxEntries - 1) { skipped.Add(new(candidate.Path, "too many files")); continue; }
            try
            {
                var info = new FileInfo(candidate.FullPath);
                if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) { skipped.Add(new(candidate.Path, "a link, not a file")); continue; }
                if (info.Length > MaxEntryBytes) { skipped.Add(new(candidate.Path, "larger than 8 MB")); continue; }
                if (total + info.Length > MaxTotalBytes) { skipped.Add(new(candidate.Path, "the backup would pass 64 MB")); continue; }
                using var stream = new FileStream(candidate.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var data = new byte[checked((int)stream.Length)];
                stream.ReadExactly(data);
                total += data.Length;
                files.Add((candidate.Path, data));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new(candidate.Path, "couldn't be read"));
            }
        }

        var stamp = now ?? DateTimeOffset.Now;
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, data) in files)
            {
                // Pictures and sounds are compressed already (and a stored entry keeps the import's ratio check out of the way).
                var level = path.EndsWith(".png", StringComparison.Ordinal) || path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase)
                            || WouldTripRatioCheck(data)
                    ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                var entry = zip.CreateEntry(path, level);
                entry.LastWriteTime = stamp;
                using var stream = entry.Open();
                stream.Write(data);
            }
            var manifest = zip.CreateEntry(ManifestName, CompressionLevel.Optimal);
            manifest.LastWriteTime = stamp;
            using var m = manifest.Open();
            WriteManifest(m, ticked, files, stamp, appVersion ?? DefaultAppVersion());
        }
        return new ExportResult(files.Count, skipped);
    }

    /// <summary>
    /// True when compressing <paramref name="data"/> would pass the ratio the reader refuses (see <see cref="MaxRatio"/>): a very
    /// repetitive text file. Such a file is stored uncompressed instead, so every file this writer produces can be imported again.
    /// </summary>
    private static bool WouldTripRatioCheck(byte[] data)
    {
        if (data.Length <= RatioFloor) return false;
        using var counter = new CountingStream();
        using (var deflate = new DeflateStream(counter, CompressionLevel.Optimal, leaveOpen: true)) deflate.Write(data);
        return counter.Count * (MaxRatio / 2) < data.Length; // a margin of two: the zip's own deflate may differ a little
    }

    private sealed class CountingStream : Stream
    {
        public long Count { get; private set; }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Count += count;
        public override void Write(ReadOnlySpan<byte> buffer) => Count += buffer.Length;
    }

    private static IEnumerable<Candidate> Candidates(string dataDirectory, List<SettingsCategory> ticked, List<SkippedFile> skipped)
    {
        foreach (var category in ticked)
        {
            var names = new List<string>();
            switch (category)
            {
                case SettingsCategory.Settings: names.Add("settings.json"); break;
                case SettingsCategory.Toolbar: names.Add("toolbar.json"); names.AddRange(Listing(dataDirectory, "icons", "*.png", skipped)); break;
                case SettingsCategory.Mcp: names.Add("mcp.json"); break;
                case SettingsCategory.Sounds: names.AddRange(Listing(dataDirectory, "sounds", "*.wav", skipped)); break;
                case SettingsCategory.Connections: names.AddRange(Listing(dataDirectory, "connections", "*.json", skipped)); break;
                case SettingsCategory.Actions: names.AddRange(Listing(dataDirectory, "actions", "*.md", skipped)); break;
                case SettingsCategory.Prompts: names.AddRange(Listing(dataDirectory, "prompts", "*.md", skipped)); break;
            }
            foreach (var name in names.Order(StringComparer.Ordinal))
            {
                var full = Path.Combine(dataDirectory, name.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full)) continue;
                // The same check an import makes: a name it would refuse (or one that isn't this category's) isn't exported.
                if (Classify(name) is { } ok && ok.Category == category) yield return new Candidate(name, full, category);
                else skipped.Add(new(LogName(name), "not a name a backup can hold"));
            }
        }
    }

    /// <summary>The files of one folder as "folder/name" (the pattern also matches longer extensions on Windows; Classify sorts that out).</summary>
    private static IEnumerable<string> Listing(string dataDirectory, string folder, string pattern, List<SkippedFile> skipped)
    {
        try
        {
            var path = Path.Combine(dataDirectory, folder);
            return Directory.Exists(path)
                ? Directory.EnumerateFiles(path, pattern).Select(f => folder + "/" + Path.GetFileName(f)).ToList()
                : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            skipped.Add(new(folder + "/", SkipReasons.FolderUnreadable)); // not silently: the user must know this part is missing
            return [];
        }
    }

    private static void WriteManifest(Stream stream, List<SettingsCategory> ticked, List<(string Path, byte[] Data)> files, DateTimeOffset created, string appVersion)
    {
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        json.WriteStartObject();
        json.WriteNumber("format", CurrentFormat);
        json.WriteString("appVersion", appVersion);
        json.WriteString("created", created.ToString("o"));
        json.WriteStartArray("categories");
        foreach (var c in ticked) json.WriteStringValue(c.ToString().ToLowerInvariant());
        json.WriteEndArray();
        json.WriteStartArray("files");
        foreach (var (path, data) in files)
        {
            json.WriteStartObject();
            json.WriteString("path", path);
            json.WriteNumber("size", data.Length);
            json.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(data)));
            json.WriteEndObject();
        }
        json.WriteEndArray();
        json.WriteEndObject();
    }

    private static string DefaultAppVersion() =>
        typeof(SettingsPackage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "unknown";

    // ---- inspect -----------------------------------------------------------------------------

    private sealed record Loaded(PackageInfo Info, Dictionary<string, byte[]> Files);

    /// <summary>
    /// Checks a package without writing anything: the manifest against the zip (every file listed with its size and SHA-256,
    /// nothing unlisted), every name against the allowed shapes, and the size limits. Never throws for a bad file.
    /// </summary>
    public static PackageInfo Inspect(Stream package) => Read(package).Info;

    private static Loaded Refuse(string problem, int format = 0, string? version = null, DateTimeOffset? created = null) =>
        new(new PackageInfo(format, version, created, new Dictionary<SettingsCategory, int>(), [problem]), []);

    private static Loaded Read(Stream input)
    {
        try
        {
            var stream = input;
            if (!input.CanSeek)
            {
                var copy = new MemoryStream();
                var buffer = new byte[81920];
                int n;
                while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    copy.Write(buffer, 0, n);
                    if (copy.Length > MaxTotalBytes + MaxManifestBytes) return Refuse(Strings.Backup_Err_TooLarge);
                }
                copy.Position = 0;
                stream = copy;
            }
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return Read(zip);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return Refuse(Strings.Backup_Err_NotZip);
        }
    }

    private static Loaded Read(ZipArchive zip)
    {
        var entries = zip.Entries;
        if (entries.Count > MaxEntries) return Refuse(L.F(Strings.Backup_Err_TooManyFiles, MaxEntries));

        // Structure first, before anything is decompressed: names, duplicates, declared sizes.
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ZipArchiveEntry? manifestEntry = null;
        var members = new List<(ZipArchiveEntry Entry, SettingsCategory Category)>();
        long declared = 0;
        foreach (var entry in entries)
        {
            var name = entry.FullName;
            if (!seen.Add(name)) { problems.Add(L.F(Strings.Backup_Err_Twice, LogName(name))); continue; }
            if (name == ManifestName) { manifestEntry = entry; continue; }
            if (Classify(name) is not { } known) { problems.Add(L.F(Strings.Backup_Err_Unexpected, LogName(name))); continue; }
            if (entry.Length > MaxEntryBytes) { problems.Add(L.F(Strings.Backup_Err_EntryTooBig, LogName(name))); continue; }
            if (entry.Length > RatioFloor && entry.CompressedLength * MaxRatio < entry.Length)
                problems.Add(L.F(Strings.Backup_Err_Ratio, LogName(name)));
            declared += entry.Length;
            members.Add((entry, known.Category));
        }
        if (declared > MaxTotalBytes) problems.Add(Strings.Backup_Err_TotalTooBig);
        if (manifestEntry is null) problems.Add(Strings.Backup_Err_NoManifest);
        else if (manifestEntry.Length > MaxManifestBytes) problems.Add(Strings.Backup_Err_ManifestBig);
        if (problems.Count > 0) return Refuse(problems);

        var manifest = ParseManifest(manifestEntry!);
        if (manifest.Problem is not null) return Refuse(manifest.Problem, manifest.Format, manifest.AppVersion, manifest.Created);

        // The declared sizes can lie: read at most one byte more than declared and compare.
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var counts = new Dictionary<SettingsCategory, int>();
        long actual = 0;
        foreach (var (entry, category) in members)
        {
            using var s = entry.Open();
            var data = ReadBounded(s, entry.Length);
            if (data is null || data.Length != entry.Length) { problems.Add(L.F(Strings.Backup_Err_SizeLies, LogName(entry.FullName))); continue; }
            actual += data.Length;
            if (actual > MaxTotalBytes) { problems.Add(Strings.Backup_Err_TotalTooBig); break; }
            files[entry.FullName] = data;
            counts[category] = counts.GetValueOrDefault(category) + 1;
        }
        if (problems.Count == 0)
        {
            foreach (var path in manifest.Files.Keys.Where(p => !files.ContainsKey(p)))
                problems.Add(L.F(Strings.Backup_Err_Missing, LogName(path)));
            foreach (var (path, data) in files)
            {
                if (!manifest.Files.TryGetValue(path, out var listed)) problems.Add(L.F(Strings.Backup_Err_Unlisted, LogName(path)));
                else if (listed.Size != data.Length || !listed.Sha256.Equals(Convert.ToHexStringLower(SHA256.HashData(data)), StringComparison.OrdinalIgnoreCase))
                    problems.Add(L.F(Strings.Backup_Err_Mismatch, LogName(path)));
            }
        }
        if (problems.Count > 0) return Refuse(problems, manifest.Format, manifest.AppVersion, manifest.Created);
        return new Loaded(new PackageInfo(manifest.Format, manifest.AppVersion, manifest.Created, counts, []), files);
    }

    private static Loaded Refuse(List<string> problems, int format = 0, string? version = null, DateTimeOffset? created = null) =>
        new(new PackageInfo(format, version, created, new Dictionary<SettingsCategory, int>(), problems), []);

    private static byte[]? ReadBounded(Stream s, long declared)
    {
        var limit = Math.Min(declared, MaxEntryBytes) + 1;
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        while (ms.Length < limit)
        {
            var n = s.Read(buffer, 0, (int)Math.Min(buffer.Length, limit - ms.Length));
            if (n <= 0) break;
            ms.Write(buffer, 0, n);
        }
        return ms.ToArray();
    }

    private sealed record Manifest(int Format, string? AppVersion, DateTimeOffset? Created, Dictionary<string, (long Size, string Sha256)> Files, string? Problem);

    private static Manifest ParseManifest(ZipArchiveEntry entry)
    {
        var files = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
        Manifest Bad(string problem, int format = 0) => new(format, null, null, files, problem);
        try
        {
            using var stream = entry.Open();
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("format", out var f) || !f.TryGetInt32(out var format) || format < 1)
                return Bad(Strings.Backup_Err_ManifestInvalid);
            if (format > CurrentFormat)
                return Bad(Strings.Backup_Err_Newer, format);

            var version = root.TryGetProperty("appVersion", out var v) && v.ValueKind == JsonValueKind.String ? LogName(v.GetString()) : null;
            DateTimeOffset? created = root.TryGetProperty("created", out var c) && c.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(c.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var when)
                ? when : null;
            if (!root.TryGetProperty("files", out var list) || list.ValueKind != JsonValueKind.Array)
                return Bad(Strings.Backup_Err_ListMissing);
            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("size", out var z) || !z.TryGetInt64(out var size)
                    || !item.TryGetProperty("sha256", out var h) || h.ValueKind != JsonValueKind.String
                    || !files.TryAdd(p.GetString()!, (size, h.GetString()!)))
                    return Bad(Strings.Backup_Err_ListInvalid, format);
            }
            return new Manifest(format, version, created, files, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or InvalidOperationException)
        {
            return Bad(Strings.Backup_Err_ManifestUnreadable);
        }
    }
}
