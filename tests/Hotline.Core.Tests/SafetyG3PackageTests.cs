using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

/// <summary>Backup export/import must accept exactly what the app's own readers accept, and every file it writes must be importable.</summary>
public sealed class SafetyG3PackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hotline-pkg-" + Guid.NewGuid().ToString("N"));
    private string Data => Path.Combine(_root, "data");
    private string Target => Path.Combine(_root, "target");
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    public SafetyG3PackageTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private void Put(string rel, string text)
    {
        var path = Path.Combine(Data, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private (ExportResult Export, ImportResult Import, PackageInfo Info) RoundTrip(params SettingsCategory[] categories)
    {
        var ms = new MemoryStream();
        var export = SettingsPackage.Export(Data, categories, ms, Now, "1.0");
        ms.Position = 0;
        var info = SettingsPackage.Inspect(ms);
        ms.Position = 0;
        var import = SettingsPackage.Import(Target, ms, categories, Path.Combine(Target, "backups"), Now);
        return (export, import, info);
    }

    // ---- item 4: the same reading as loading ---------------------------------------------------

    [Fact]
    public void Hand_edited_settings_with_a_wrong_value_and_a_newer_enum_word_round_trip()
    {
        Put("settings.json", """
            // mine
            { "window": { "fontSize": "big", "width": 700 }, "activation": { "hold": "aWordFromTheFuture" }, "chat": { "memory": false } }
            """);
        var (export, import, info) = RoundTrip(SettingsCategory.Settings);
        Assert.Equal(1, export.FileCount);
        Assert.True(info.IsValid);
        Assert.Equal(["settings.json"], import.Written);
        Assert.Empty(import.Skipped);
        var loaded = new SettingsStore(Target).Load(); // and the app reads what was imported
        Assert.Equal(700, loaded.Window.Width);
        Assert.False(loaded.Chat.Memory);
    }

    [Fact]
    public void A_connection_file_whose_id_differs_from_its_name_round_trips()
    {
        Put("connections/renamed.json", """{ "id": "other", "type": "local", "name": "Renamed", "maxTokens": "lots" }""");
        var (_, import, _) = RoundTrip(SettingsCategory.Connections);
        Assert.Equal(["connections/renamed.json"], import.Written);
        Assert.Empty(import.Skipped);
        Assert.Equal("other", Assert.Single(new SettingsStore(Target).Load().Chat.Backends).Id);
    }

    [Fact]
    public void An_unknown_connection_type_and_a_non_object_are_still_refused()
    {
        Put("connections/future.json", """{ "id": "f", "type": "quantum", "name": "F" }""");
        Put("connections/array.json", "[1, 2]");
        Put("connections/ok.json", """{ "id": "ok", "type": "local", "name": "Ok" }""");
        var (_, import, _) = RoundTrip(SettingsCategory.Connections);
        Assert.Equal(["connections/ok.json"], import.Written);
        Assert.Equal(2, import.Skipped.Count);
    }

    [Fact]
    public void Settings_that_are_not_an_object_are_refused()
    {
        Put("settings.json", "[1]");
        var (_, import, _) = RoundTrip(SettingsCategory.Settings);
        Assert.Empty(import.Written);
        Assert.Single(import.Skipped);
    }

    // ---- item 5: the writer stays inside the reader's limits ----------------------------------------

    [Fact]
    public void A_repetitive_large_text_file_is_exported_in_a_way_that_can_be_imported_again()
    {
        Put("prompts/long.md", new string('a', 3 * 1024 * 1024)); // deflates to a few KB: ratio far beyond what the reader accepts
        var (export, import, info) = RoundTrip(SettingsCategory.Prompts);
        Assert.Equal(1, export.FileCount);
        Assert.True(info.IsValid, string.Join(" ", info.Problems));
        Assert.Equal(["prompts/long.md"], import.Written);
        Assert.Equal(3 * 1024 * 1024, new FileInfo(Path.Combine(Target, "prompts", "long.md")).Length);
    }

    [Fact]
    public void A_folder_that_cannot_be_listed_is_reported_as_skipped_not_silently_empty()
    {
        if (!OperatingSystem.IsWindows()) return;
        Put("prompts/a.md", "x");
        var folder = new DirectoryInfo(Path.Combine(Data, "prompts"));
        var me = WindowsIdentity.GetCurrent().User!;
        var deny = new FileSystemAccessRule(me, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var acl = folder.GetAccessControl();
        acl.AddAccessRule(deny);
        folder.SetAccessControl(acl);
        try
        {
            var result = SettingsPackage.Export(Data, [SettingsCategory.Prompts], new MemoryStream(), Now, "1.0");
            Assert.Equal(0, result.FileCount);
            var skipped = Assert.Single(result.Skipped);
            Assert.StartsWith("prompts", skipped.Path);
            Assert.False(string.IsNullOrWhiteSpace(skipped.Text));
        }
        finally
        {
            acl.RemoveAccessRule(deny);
            folder.SetAccessControl(acl);
        }
    }

    [Fact]
    public void Skip_reasons_and_problems_come_in_the_interface_language_keys()
    {
        // Every reason the code can give has a resource key (so none reaches the UI as hard-coded English).
        Assert.All(SkipReasons.Keys, kv => Assert.False(string.IsNullOrWhiteSpace(Hotline.Core.Text.Localization.L.Get(kv.Value)), kv.Key));
        var bad = new MemoryStream(Encoding.ASCII.GetBytes("not a zip"));
        Assert.Equal(Hotline.Core.Text.Localization.Strings.Backup_Err_NotZip, Assert.Single(SettingsPackage.Inspect(bad).Problems));
    }
}
