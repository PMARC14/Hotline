using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SettingsPackageTests : IDisposable
{
    private static readonly byte[] Png16 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAVSURBVDhPY2AYXuA/GI2CUTAKyAEAeu0B/zEZFcQAAAAASUVORK5CYII=");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hotline-pkg-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(_root, "data");
    private string Target => Path.Combine(_root, "target");
    private string Backups => Path.Combine(Target, "backups");
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 15, 30, 5, TimeSpan.Zero);

    public SettingsPackageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException) { }
    }

    // ---- fixtures ----------------------------------------------------------------------------

    private static string Wav(string marker = "") => "RIFF\0\0\0\0WAVEfmt " + marker;
    private static byte[] WavBytes(string marker = "") => Encoding.Latin1.GetBytes(Wav(marker));

    private static string Conn(string id, string type = "openAiCompatible") => $"{{\"id\":\"{id}\",\"type\":\"{type}\",\"name\":\"{id}\"}}";

    private static void Put(string dir, string rel, string text) => PutBytes(dir, rel, Encoding.UTF8.GetBytes(text));

    private static void PutBytes(string dir, string rel, byte[] bytes)
    {
        var path = Path.Combine(dir, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    /// <summary>A data folder with something in every category, plus files that must never be exported.</summary>
    private void FillData(string dir)
    {
        Directory.CreateDirectory(dir);
        Put(dir, "settings.json", SettingsStore.Serialize(SettingsStore.Normalize(new HotlineSettings())));
        Put(dir, "toolbar.json", """{ "version": 2 }""");
        PutBytes(dir, "icons/my-btn.png", Png16);
        Put(dir, "mcp.json", """{ "mcpServers": {}, "approvals": {} }""");
        PutBytes(dir, "sounds/ding.wav", WavBytes());
        PutBytes(dir, "sounds/hotline.wav", WavBytes("b"));
        Put(dir, "connections/work.json", Conn("work"));
        Put(dir, "actions/fix.md", "Fix it.\n");
        Put(dir, "actions/shout.md", "SHOUT\n");
        Put(dir, "prompts/default.md", "You are Hotline.");
        // never exported
        Put(dir, "memory.md", "private notes");
        Put(dir, "history/chat1.json", "{}");
        Put(dir, "logs/hotline.log", "log");
        Put(dir, "backups/old/settings.json", "{}");
        Put(dir, ".defaults/settings.json", "{}");
        Put(dir, "settings.json.bak", "{}");
        Put(dir, "actions/fix.md.old", "old");
        Put(dir, "mcp.json.bak-20261022-153000", "{}");
        Put(dir, "sounds/readme.txt", "x");
    }

    private static MemoryStream Export(string dir, SettingsCategory[] categories, out ExportResult result)
    {
        var ms = new MemoryStream();
        result = SettingsPackage.Export(dir, categories, ms, Now, "1.2.3");
        ms.Position = 0;
        return ms;
    }

    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    /// <summary>A zip with the given entries and a correct manifest (unless told otherwise).</summary>
    private static MemoryStream Zip(IEnumerable<(string Name, byte[] Data)> entries, int format = 1, bool manifest = true,
        Func<string, string>? tamper = null, CompressionLevel level = CompressionLevel.Optimal)
    {
        var list = entries.ToList();
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, data) in list)
            {
                var e = zip.CreateEntry(name, level);
                using var s = e.Open();
                s.Write(data);
            }
            if (manifest)
            {
                var files = list.Select(f => new { path = f.Name, size = f.Data.Length, sha256 = Sha(f.Data) });
                var json = JsonSerializer.Serialize(new
                {
                    format, appVersion = "1.2.3", created = Now.ToString("o"), categories = new[] { "settings" }, files,
                });
                if (tamper is not null) json = tamper(json);
                var m = zip.CreateEntry("hotline-export.json");
                using var s = m.Open();
                s.Write(Encoding.UTF8.GetBytes(json));
            }
        }
        ms.Position = 0;
        return ms;
    }

    private static (string, byte[]) E(string name, string text = "x") => (name, Encoding.UTF8.GetBytes(text));

    private ImportResult Import(Stream package, params SettingsCategory[] categories) =>
        SettingsPackage.Import(Target, package, categories.Length == 0 ? SettingsPackage.All : categories, Backups, Now);

    private static IEnumerable<string> Relative(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/')).Order(StringComparer.Ordinal)
            : [];

    // ---- export ------------------------------------------------------------------------------

    [Fact]
    public void The_file_name_has_the_time()
    {
        Assert.Equal($"hotline-settings-{Now.ToLocalTime():yyyyMMdd-HHmm}.zip", SettingsPackage.FileName(Now));
    }

    [Fact]
    public void Every_category_has_a_localized_name()
    {
        foreach (var c in SettingsPackage.All) Assert.False(string.IsNullOrWhiteSpace(SettingsPackage.Name(c)));
        Assert.Equal(7, SettingsPackage.All.Count);
    }

    [Fact]
    public void Export_holds_the_ticked_categories_and_a_matching_manifest()
    {
        FillData(Data);
        using var zip = Export(Data, [.. SettingsPackage.All], out var result);

        using var archive = new ZipArchive(zip);
        var names = archive.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            ["actions/fix.md", "actions/shout.md", "connections/work.json", "hotline-export.json", "icons/my-btn.png", "mcp.json",
             "prompts/default.md", "settings.json", "sounds/ding.wav", "sounds/hotline.wav", "toolbar.json"], names);
        Assert.Equal(10, result.FileCount);

        using var doc = JsonDocument.Parse(archive.GetEntry("hotline-export.json")!.Open());
        var root = doc.RootElement;
        Assert.Equal(1, root.GetProperty("format").GetInt32());
        Assert.Equal("1.2.3", root.GetProperty("appVersion").GetString());
        Assert.Equal(Now, root.GetProperty("created").GetDateTimeOffset());
        Assert.Equal(7, root.GetProperty("categories").GetArrayLength());
        var files = root.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(10, files.Count);
        var fix = files.Single(f => f.GetProperty("path").GetString() == "actions/fix.md");
        Assert.Equal(Encoding.UTF8.GetByteCount("Fix it.\n"), fix.GetProperty("size").GetInt32());
        Assert.Equal(Sha(Encoding.UTF8.GetBytes("Fix it.\n")), fix.GetProperty("sha256").GetString());
    }

    [Theory]
    [InlineData(SettingsCategory.Settings, new[] { "settings.json" })]
    [InlineData(SettingsCategory.Toolbar, new[] { "icons/my-btn.png", "toolbar.json" })]
    [InlineData(SettingsCategory.Mcp, new[] { "mcp.json" })]
    [InlineData(SettingsCategory.Sounds, new[] { "sounds/ding.wav", "sounds/hotline.wav" })]
    [InlineData(SettingsCategory.Connections, new[] { "connections/work.json" })]
    [InlineData(SettingsCategory.Actions, new[] { "actions/fix.md", "actions/shout.md" })]
    [InlineData(SettingsCategory.Prompts, new[] { "prompts/default.md" })]
    public void Each_category_alone_exports_and_imports_only_its_files(SettingsCategory category, string[] expected)
    {
        FillData(Data);
        using var zip = Export(Data, [category], out var result);
        Assert.Equal(expected.Length, result.FileCount);
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true))
            Assert.Equal(expected, archive.Entries.Select(e => e.FullName).Where(n => n != "hotline-export.json").Order(StringComparer.Ordinal));

        zip.Position = 0;
        var info = SettingsPackage.Inspect(zip);
        Assert.True(info.IsValid, string.Join("; ", info.Problems));
        Assert.Equal([category], info.Counts.Keys);
        Assert.Equal(expected.Length, info.Counts[category]);

        zip.Position = 0;
        var imported = Import(zip);
        Assert.Null(imported.Error);
        Assert.Equal(expected, imported.Written.Order(StringComparer.Ordinal));
        Assert.Equal(expected, Relative(Target));
    }

    [Fact]
    public void Unticked_categories_are_absent()
    {
        FillData(Data);
        using var zip = Export(Data, [SettingsCategory.Actions, SettingsCategory.Mcp], out _);
        using var archive = new ZipArchive(zip);
        Assert.DoesNotContain(archive.Entries, e => e.FullName.StartsWith("sounds/") || e.FullName == "settings.json" || e.FullName.StartsWith("connections/"));
    }

    [Fact]
    public void Nothing_else_is_exported_even_when_everything_is_ticked()
    {
        FillData(Data);
        using var zip = Export(Data, [.. SettingsPackage.All], out _);
        using var archive = new ZipArchive(zip);
        foreach (var bad in new[] { "memory", "history", "logs", "backups", ".defaults", ".bak", ".old", "readme" })
            Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains(bad, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Round_trip_into_a_fresh_folder_gives_identical_files()
    {
        FillData(Data);
        using var zip = Export(Data, [.. SettingsPackage.All], out _);
        var result = Import(zip);
        Assert.Null(result.Error);
        Assert.Empty(result.Skipped);
        Assert.Null(result.BackupFolder); // nothing existed to back up
        Assert.Equal(10, result.Written.Count);
        foreach (var rel in Relative(Target))
            Assert.Equal(File.ReadAllBytes(Path.Combine(Data, rel)), File.ReadAllBytes(Path.Combine(Target, rel)));
        Assert.Equal(10, Relative(Target).Count());
    }

    [Fact]
    public void Exporting_a_missing_data_folder_gives_an_empty_package()
    {
        using var zip = Export(Path.Combine(_root, "nope"), [.. SettingsPackage.All], out var result);
        Assert.Equal(0, result.FileCount);
        var info = SettingsPackage.Inspect(zip);
        Assert.True(info.IsValid);
        Assert.Empty(info.Counts);
    }

    [Fact]
    public void Export_skips_a_file_that_would_not_import_and_says_why()
    {
        FillData(Data);
        PutBytes(Data, "sounds/huge.wav", new byte[SettingsPackage.MaxEntryBytes + 1]);
        using var zip = Export(Data, [SettingsCategory.Sounds], out var result);
        Assert.Contains(result.Skipped, s => s.Path == "sounds/huge.wav");
        using var archive = new ZipArchive(zip);
        Assert.Null(archive.GetEntry("sounds/huge.wav"));
    }

    // ---- inspect -----------------------------------------------------------------------------

    [Fact]
    public void Inspect_reports_version_date_and_counts()
    {
        FillData(Data);
        using var zip = Export(Data, [.. SettingsPackage.All], out _);
        var info = SettingsPackage.Inspect(zip);
        Assert.True(info.IsValid);
        Assert.Equal(1, info.Format);
        Assert.Equal("1.2.3", info.AppVersion);
        Assert.Equal(Now, info.Created);
        Assert.Equal(2, info.Counts[SettingsCategory.Toolbar]);
        Assert.Equal(2, info.Counts[SettingsCategory.Sounds]);
        Assert.Equal(1, info.Counts[SettingsCategory.Mcp]);
    }

    [Fact]
    public void A_file_that_is_not_a_zip_is_a_problem_not_an_exception()
    {
        var info = SettingsPackage.Inspect(new MemoryStream("not a zip"u8.ToArray()));
        Assert.False(info.IsValid);
        Assert.NotEmpty(info.Problems);
    }

    [Fact]
    public void A_zip_without_a_manifest_is_refused()
    {
        var info = SettingsPackage.Inspect(Zip([E("mcp.json", "{}")], manifest: false));
        Assert.False(info.IsValid);
    }

    [Fact]
    public void A_manifest_hash_that_does_not_match_is_refused_and_nothing_is_written()
    {
        var data = Encoding.UTF8.GetBytes("{}");
        using var zip = Zip([("mcp.json", data)], tamper: j => j.Replace(Sha(data), new string('0', 64)));
        Assert.False(SettingsPackage.Inspect(zip).IsValid);
        zip.Position = 0;
        var result = Import(zip);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Written);
        Assert.Empty(Relative(Target));
    }

    [Fact]
    public void A_manifest_size_that_does_not_match_is_refused()
    {
        using var zip = Zip([E("mcp.json", "{}")], tamper: j => j.Replace("\"size\":2", "\"size\":3"));
        Assert.False(SettingsPackage.Inspect(zip).IsValid);
    }

    [Fact]
    public void A_file_the_manifest_does_not_list_is_refused()
    {
        using var zip = Zip([E("mcp.json", "{}"), E("actions/a.md", "x")], tamper: j => j.Replace("\"actions/a.md\"", "\"actions/b.md\""));
        Assert.False(SettingsPackage.Inspect(zip).IsValid);
    }

    [Fact]
    public void A_newer_format_is_refused_with_a_clear_message()
    {
        using var zip = Zip([E("mcp.json", "{}")], format: 2);
        var info = SettingsPackage.Inspect(zip);
        Assert.False(info.IsValid);
        Assert.Contains(info.Problems, p => p.Contains("newer", StringComparison.OrdinalIgnoreCase));
        zip.Position = 0;
        var result = Import(zip);
        Assert.Contains("newer", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Relative(Target));
    }

    // ---- zip-slip and name rules -------------------------------------------------------------

    [Theory]
    [InlineData("../x.md")]
    [InlineData("..\\x.md")]
    [InlineData("/abs/x.md")]
    [InlineData("\\abs\\x.md")]
    [InlineData("C:\\x.md")]
    [InlineData("C:/x.md")]
    [InlineData("a/../../b.md")]
    [InlineData("actions/../../b.md")]
    [InlineData("actions/../mcp.json")]
    [InlineData("./mcp.json")]
    [InlineData("actions/./fix.md")]
    [InlineData("connections/..%2f.json")]
    [InlineData("connections/..%2fx.json")]
    [InlineData("sounds/a.wav:stream")]
    [InlineData("sounds/a.wav::$DATA")]
    [InlineData("sounds/con.wav")]
    [InlineData("sounds/CON.wav")]
    [InlineData("sounds/nul.wav")]
    [InlineData("sounds/com1.wav")]
    [InlineData("actions/aux.md")]
    [InlineData("prompts/LPT3.md")]
    [InlineData("actions/fix.md/")]
    [InlineData("actions//fix.md")]
    [InlineData("actions\\fix.md")]
    [InlineData("actions/fix .md")]
    [InlineData("actions/fix.md ")]
    [InlineData("actions/fix.md.")]
    [InlineData("sounds/a..wav")]
    [InlineData("sounds/.wav")]
    [InlineData("icons/Upper.png")]
    [InlineData("icons/spacer.png")]
    [InlineData("icons/a_b.png")]
    [InlineData("icons/sub/a.png")]
    [InlineData("sounds/sub/a.wav")]
    [InlineData("connections/a b.json")]
    [InlineData("history/chat.json")]
    [InlineData("memory.md")]
    [InlineData("logs/hotline.log")]
    [InlineData(".defaults/settings.json")]
    [InlineData("backups/x/settings.json")]
    [InlineData("settings.json.bak")]
    [InlineData("toolbar.json.old")]
    [InlineData("Settings.json")]
    [InlineData("sub/settings.json")]
    [InlineData("actions/fix.txt")]
    [InlineData("sounds/a.mp3")]
    [InlineData("sounds/a\0.wav")]
    [InlineData("hotline-export.json.bak")]
    public void A_name_outside_the_allowed_shapes_refuses_the_whole_package(string name)
    {
        using var zip = Zip([E("mcp.json", "{}"), E(name)]);
        var info = SettingsPackage.Inspect(zip);
        Assert.False(info.IsValid, name);
        zip.Position = 0;
        var result = Import(zip);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Written);
        Assert.Empty(Relative(Target));
        Assert.All(Relative(_root), f => Assert.StartsWith("target/", f)); // nothing escaped
    }

    [Fact]
    public void Names_the_project_uses_are_accepted()
    {
        using var zip = Zip([
            E("settings.json", "{}"), E("toolbar.json", "{}"), E("mcp.json", "{}"),
            ("icons/my-btn-2.png", Png16), ("sounds/Ding dong (2).wav", WavBytes()), ("sounds/a.b.wav", WavBytes()),
            E("connections/my-conn_1.json", Conn("my-conn_1")), E("actions/fix_it-2.md"), E("prompts/my prompt.md"),
        ]);
        var info = SettingsPackage.Inspect(zip);
        Assert.True(info.IsValid, string.Join("; ", info.Problems));
    }

    [Fact]
    public void Case_colliding_entries_are_refused()
    {
        using var zip = Zip([E("actions/Fix.md"), E("actions/fix.md")]);
        var info = SettingsPackage.Inspect(zip);
        Assert.False(info.IsValid);
        Assert.Contains(info.Problems, p => p.Contains("twice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Duplicate_entries_are_refused()
    {
        using var zip = Zip([E("actions/fix.md", "a"), E("actions/fix.md", "b")]);
        Assert.False(SettingsPackage.Inspect(zip).IsValid);
    }

    // ---- limits ------------------------------------------------------------------------------

    [Fact]
    public void More_than_400_entries_are_refused()
    {
        using var zip = Zip(Enumerable.Range(0, 401).Select(i => E($"actions/a{i}.md")));
        var info = SettingsPackage.Inspect(zip);
        Assert.False(info.IsValid);
        Assert.Contains(info.Problems, p => p.Contains("400"));
    }

    [Fact]
    public void Exactly_400_files_are_fine()
    {
        using var zip = Zip(Enumerable.Range(0, 399).Select(i => E($"actions/a{i}.md")));
        Assert.True(SettingsPackage.Inspect(zip).IsValid); // 399 + the manifest
    }

    [Fact]
    public void An_entry_over_8_MB_is_refused()
    {
        var big = new byte[SettingsPackage.MaxEntryBytes + 1];
        Array.Fill(big, (byte)'a');
        using var zip = Zip([("prompts/big.md", big)], level: CompressionLevel.NoCompression);
        var info = SettingsPackage.Inspect(zip);
        Assert.False(info.IsValid);
        Assert.Contains(info.Problems, p => p.Contains("big.md"));
    }

    [Fact]
    public void More_than_64_MB_in_total_is_refused()
    {
        var chunk = new byte[7 * 1024 * 1024];
        new Random(1).NextBytes(chunk);
        for (var i = 0; i < chunk.Length; i++) chunk[i] = (byte)((chunk[i] % 90) + 32); // text, no NUL
        using var zip = Zip(Enumerable.Range(0, 10).Select(i => ($"prompts/p{i}.md", chunk)), level: CompressionLevel.NoCompression);
        var info = SettingsPackage.Inspect(zip);
        Assert.False(info.IsValid);
        Assert.Contains(info.Problems, p => p.Contains("64"));
    }

    [Fact]
    public void A_zip_bomb_ratio_is_refused()
    {
        var zeros = new byte[6 * 1024 * 1024]; // squeezes to a few KB
        using var zip = Zip([("prompts/bomb.md", zeros)]);
        Assert.True(zip.Length < 100_000);
        var info = SettingsPackage.Inspect(zip);
        Assert.False(info.IsValid);
        Assert.Contains(info.Problems, p => p.Contains("compress", StringComparison.OrdinalIgnoreCase));
    }

    // ---- content validation on import --------------------------------------------------------

    [Fact]
    public void Corrupt_content_is_skipped_with_a_reason_and_the_rest_is_written()
    {
        using var zip = Zip([
            E("settings.json", "{ not json"),
            E("toolbar.json", "{ not json"),
            E("mcp.json", "[1,2]"),
            ("icons/bad.png", "not a png"u8.ToArray()),
            ("sounds/bad.wav", "RIFFxxxxNOPE"u8.ToArray()),
            ("sounds/short.wav", "RIFF"u8.ToArray()),
            E("connections/c1.json", "{ nope"),
            E("connections/c2.json", Conn("other")),     // id differs from the file name
            E("actions/nul.md".Replace("nul", "withnul"), "a\0b"),
            E("prompts/nul.md".Replace("nul", "pn"), "a\0b"),
            E("actions/good.md", "Good."),
            ("sounds/good.wav", WavBytes()),
            ("icons/good.png", Png16),
            E("connections/good.json", Conn("good")),
        ]);
        var result = Import(zip);
        Assert.Null(result.Error);
        Assert.Equal(["actions/good.md", "connections/good.json", "icons/good.png", "sounds/good.wav"], result.Written.Order(StringComparer.Ordinal));
        Assert.Equal(10, result.Skipped.Count);
        Assert.All(result.Skipped, s => Assert.False(string.IsNullOrWhiteSpace(s.Reason)));
        Assert.Equal(["actions/good.md", "connections/good.json", "icons/good.png", "sounds/good.wav"], Relative(Target));
    }

    [Fact]
    public void A_toolbar_that_resolves_with_warnings_is_written_as_is()
    {
        const string odd = "{ \"version\": 2, \"buttons\": { \"x\": 7 }, \"mystery\": true, // comment\n}";
        using var zip = Zip([E("toolbar.json", odd)]);
        var result = Import(zip);
        Assert.Equal(["toolbar.json"], result.Written);
        Assert.Equal(odd, File.ReadAllText(Path.Combine(Target, "toolbar.json")));
    }

    [Fact]
    public void A_connection_file_with_no_id_takes_its_file_name_and_one_with_a_safe_form_of_its_id_is_kept()
    {
        using var zip = Zip([
            E("connections/noid.json", """{ "type": "local", "name": "n" }"""),
            E("connections/My-Conn.json", Conn("My Conn")), // the store names this file My-Conn.json
        ]);
        var result = Import(zip);
        Assert.Equal(["connections/My-Conn.json", "connections/noid.json"], result.Written.Order(StringComparer.Ordinal));
    }

    // ---- backup and writing ------------------------------------------------------------------

    [Fact]
    public void Files_that_will_change_are_backed_up_with_the_same_relative_paths()
    {
        FillData(Data);
        using var zip = Export(Data, [SettingsCategory.Actions, SettingsCategory.Mcp, SettingsCategory.Sounds], out _);
        Put(Target, "actions/fix.md", "MY OWN FIX");
        Put(Target, "mcp.json", """{"mcpServers":{"mine":{"command":"x"}}}""");
        Put(Target, "settings.json", "untouched");        // category not in the package
        Put(Target, "actions/unrelated.md", "keep me");   // not in the package
        Put(Target, "memory.md", "notes");

        var result = Import(zip);

        var backup = result.BackupFolder;
        Assert.NotNull(backup);
        Assert.Equal(Path.Combine(Backups, $"import-{Now.ToLocalTime():yyyyMMdd-HHmmss}"), backup);
        Assert.Equal(["actions/fix.md", "mcp.json"], Relative(backup!));
        Assert.Equal("MY OWN FIX", File.ReadAllText(Path.Combine(backup!, "actions", "fix.md")));
        Assert.Contains("mine", File.ReadAllText(Path.Combine(backup!, "mcp.json")));
        // the new content is in place, everything else is as it was
        Assert.Equal("Fix it.\n", File.ReadAllText(Path.Combine(Target, "actions", "fix.md")));
        Assert.Equal("untouched", File.ReadAllText(Path.Combine(Target, "settings.json")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(Target, "actions", "unrelated.md")));
        Assert.Equal("notes", File.ReadAllText(Path.Combine(Target, "memory.md")));
        Assert.True(File.Exists(Path.Combine(Target, "actions", "shout.md")));
        Assert.Equal(5, result.Written.Count);
    }

    [Fact]
    public void A_second_import_in_the_same_second_gets_its_own_backup_folder()
    {
        Put(Target, "actions/fix.md", "v0");
        using var first = Zip([E("actions/fix.md", "v1")]);
        var r1 = Import(first);
        using var second = Zip([E("actions/fix.md", "v2")]);
        var r2 = Import(second);
        Assert.NotEqual(r1.BackupFolder, r2.BackupFolder);
        Assert.Equal("v0", File.ReadAllText(Path.Combine(r1.BackupFolder!, "actions", "fix.md")));
        Assert.Equal("v1", File.ReadAllText(Path.Combine(r2.BackupFolder!, "actions", "fix.md")));
    }

    [Fact]
    public void If_the_backup_fails_nothing_is_written()
    {
        Put(Target, "actions/fix.md", "mine");
        Directory.CreateDirectory(Target);
        File.WriteAllText(Backups, "a file where the backups folder should be");
        using var zip = Zip([E("actions/fix.md", "new"), E("actions/other.md", "o")]);
        var result = Import(zip);
        Assert.NotNull(result.Error);
        Assert.Empty(result.Written);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Target, "actions", "fix.md")));
        Assert.False(File.Exists(Path.Combine(Target, "actions", "other.md")));
    }

    [Fact]
    public void The_data_folder_is_created_when_missing()
    {
        Assert.False(Directory.Exists(Target));
        using var zip = Zip([E("actions/fix.md", "new")]);
        var result = Import(zip);
        Assert.Equal(["actions/fix.md"], result.Written);
        Assert.Equal("new", File.ReadAllText(Path.Combine(Target, "actions", "fix.md")));
    }

    [Fact]
    public void A_read_only_target_is_reported_and_the_other_files_still_go_in()
    {
        Put(Target, "actions/locked.md", "mine");
        File.SetAttributes(Path.Combine(Target, "actions", "locked.md"), FileAttributes.ReadOnly);
        using var zip = Zip([E("actions/locked.md", "new"), E("actions/free.md", "free")]);
        var result = Import(zip);
        Assert.Equal(["actions/free.md"], result.Written);
        Assert.Contains(result.Skipped, s => s.Path == "actions/locked.md");
        Assert.Equal("mine", File.ReadAllText(Path.Combine(Target, "actions", "locked.md")));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(Target, "actions")).Length); // no temp file left behind
    }

    [Fact]
    public void Only_the_ticked_categories_are_imported()
    {
        FillData(Data);
        using var zip = Export(Data, [.. SettingsPackage.All], out _);
        var result = Import(zip, SettingsCategory.Sounds);
        Assert.Equal(["sounds/ding.wav", "sounds/hotline.wav"], result.Written.Order(StringComparer.Ordinal));
        Assert.Equal(["sounds/ding.wav", "sounds/hotline.wav"], Relative(Target));
    }

    [Fact]
    public void Connections_are_replaced_only_when_that_category_is_ticked()
    {
        Put(Target, "connections/work.json", Conn("work", "local"));
        FillData(Data);
        using var zip = Export(Data, [.. SettingsPackage.All], out _);
        Import(zip, SettingsCategory.Actions);
        Assert.Contains("local", File.ReadAllText(Path.Combine(Target, "connections", "work.json")));
        zip.Position = 0;
        Import(zip, SettingsCategory.Connections);
        Assert.Contains("openAiCompatible", File.ReadAllText(Path.Combine(Target, "connections", "work.json")));
    }

    [Fact]
    public void An_import_never_deletes()
    {
        Put(Target, "sounds/mine.wav", Wav());
        Put(Target, "connections/mine.json", Conn("mine"));
        using var zip = Zip([("sounds/theirs.wav", WavBytes())]);
        Import(zip);
        Assert.True(File.Exists(Path.Combine(Target, "sounds", "mine.wav")));
        Assert.True(File.Exists(Path.Combine(Target, "connections", "mine.json")));
    }

    // ---- connections that still need a key ---------------------------------------------------

    [Fact]
    public void Imported_connections_without_a_stored_key_are_listed()
    {
        using var zip = Zip([
            E("connections/gpt.json", Conn("gpt", "openAiCompatible")),
            E("connections/haskey.json", Conn("haskey", "anthropic")),
            E("connections/local.json", Conn("local", "local")),           // key optional
            E("connections/claude.json", Conn("claude", "claudeCode")),    // CLI
        ]);
        var result = Import(zip);
        var secrets = new InMemorySecretStore();
        secrets.Set(SecretKeys.ApiKey("haskey"), "k");
        Assert.Equal(["gpt"], SettingsPackage.ConnectionsNeedingKey(Target, result, secrets));
    }

    // ---- logging helper ----------------------------------------------------------------------

    [Fact]
    public void Log_names_are_cleaned()
    {
        var text = SettingsPackage.LogName("actions/a\nb.md");
        Assert.DoesNotContain('\n', text);
    }
}
