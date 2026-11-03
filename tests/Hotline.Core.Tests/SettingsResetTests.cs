using Hotline.Core.Backends;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class SettingsResetTests : TempDirTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);

    private (SettingsStore Store, HotlineSettings Settings, string LocalId) Customised()
    {
        var store = new SettingsStore(_dir);
        var s = store.Load();
        var local = ConnectionEditor.Add(s.Chat, BackendType.Local);
        ConnectionEditor.SetDefault(s.Chat, local.Id);
        s.Window.FontSize = 20;
        s.Chat.Memory = false;
        store.Save(s);
        File.WriteAllText(Path.Combine(_dir, "toolbar.json"), "{}");
        File.WriteAllText(Path.Combine(_dir, "memory.md"), "keep me");
        File.WriteAllText(Path.Combine(_dir, "mcp.json"), "{\"mcp\":1}");
        return (store, s, local.Id);
    }

    [Fact]
    public void Reset_does_not_replace_settings_or_connections_whose_backup_failed()
    {
        var (store, settings, local) = Customised();
        var path = store.FilePath;
        var connection = store.SourcePath(local)!;
        var before = File.ReadAllBytes(path); // exactly what this process last wrote: Save would skip its own backup
        var handEdited = """{ "id": "local-model", "type": "local", "name": "Hand edited", "extra": "keep" }""";
        File.WriteAllText(connection, handEdited);
        var result = SettingsReset.Reset(store, _dir, settings, everything: false, Now, (file, to) =>
        {
            if (file == path || file == connection) Directory.CreateDirectory(to); // a destination that cannot be written as a file
            File.Copy(file, to);
        });
        Assert.Contains(SettingsStore.FileName, result.Failed);
        Assert.Contains(Path.GetFileName(connection), result.Failed);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Equal(handEdited, File.ReadAllText(connection));
        Assert.Empty(result.Removed);
        Assert.Equal(ToolbarModel.Defaults.ToJson(), File.ReadAllText(Path.Combine(_dir, "toolbar.json"))); // other files still reset
    }

    [Theory]
    [InlineData(false, "toolbar.json")]
    [InlineData(true, "toolbar.json")]
    [InlineData(true, "mcp.json")]
    [InlineData(true, "settings.json")]
    [InlineData(true, "connections")]
    public void Reset_leaves_each_file_alone_when_its_backup_destination_is_unwritable(bool everything, string blocked)
    {
        var (store, settings, local) = Customised();
        var path = blocked == "connections" ? store.SourcePath(local)! : Path.Combine(_dir, blocked);
        var before = File.ReadAllBytes(path);
        var result = SettingsReset.Reset(store, _dir, settings, everything, Now, (file, to) =>
        {
            if (file == path) Directory.CreateDirectory(to);
            File.Copy(file, to);
        });
        Assert.Contains(Path.GetFileName(path), result.Failed);
        Assert.Equal(before, File.ReadAllBytes(path));
        if (blocked == "connections") Assert.DoesNotContain(local, result.Removed); // caller must keep its credential
        else if (everything) Assert.Contains(local, result.Removed); // successful removals are still reported
    }

    [Fact]
    public void Reset_settings_keeps_connections_and_backs_up()
    {
        var (store, s, local) = Customised();
        var (backup, removed) = SettingsReset.Reset(store, _dir, s, everything: false, Now);
        var back = new SettingsStore(_dir).Load();
        Assert.Null(back.Window.FontSize);
        Assert.True(back.Chat.Memory);
        Assert.Equal(local, back.Chat.DefaultBackend);
        Assert.Single(back.Chat.Backends);
        Assert.Empty(removed);
        Assert.Contains("\"fontSize\": 20", File.ReadAllText(Path.Combine(backup, SettingsStore.FileName)));
        Assert.True(File.Exists(Path.Combine(_dir, "toolbar.json")));
        Assert.Equal("{\"mcp\":1}", File.ReadAllText(Path.Combine(_dir, "mcp.json"))); // MCP servers are not settings
    }

    [Fact]
    public void Reset_everything_removes_connections_and_toolbar_but_not_memory()
    {
        var (store, s, local) = Customised();
        var (backup, removed) = SettingsReset.Reset(store, _dir, s, everything: true, Now);
        var back = new SettingsStore(_dir).Load();
        Assert.Empty(back.Chat.Backends);
        Assert.Equal("", back.Chat.DefaultBackend);
        Assert.Equal([local], removed);
        Assert.False(File.Exists(Path.Combine(_dir, "toolbar.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "mcp.json")));
        Assert.Equal("{}", File.ReadAllText(Path.Combine(backup, "toolbar.json")));
        Assert.Equal("{\"mcp\":1}", File.ReadAllText(Path.Combine(backup, "mcp.json")));
        Assert.True(File.Exists(Path.Combine(backup, SettingsStore.ConnectionsFolder, local + ".json")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_dir, "memory.md")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reset_removes_shared_OpenWebUi_targets_and_reports_only_removed_connections(bool everything)
    {
        var (store, settings, local) = Customised();
        var target = new ContinueTarget(ContinueKind.OpenWebUi, "Open WebUI", Url: "http://localhost:3000");
        settings.Chat.ContinueIn = [target];
        settings.Chat.Backends[0].ContinueIn = [target];
        store.Save(settings);

        var result = SettingsReset.Reset(store, _dir, settings, everything, Now);

        var back = new SettingsStore(_dir).Load();
        Assert.Null(back.Chat.ContinueIn);
        Assert.Equal(everything ? [local] : Array.Empty<string>(), result.Removed);
        if (everything) Assert.Empty(back.Chat.Backends);
        else Assert.Equal(target, Assert.Single(Assert.Single(back.Chat.Backends).ContinueIn!));
    }

    [Fact]
    public void Defaults_copy_is_hidden_and_loads_as_defaults()
    {
        SettingsReset.WriteDefaults(_dir);
        var folder = new DirectoryInfo(Path.Combine(_dir, SettingsReset.DefaultsFolder));
        Assert.True(folder.Attributes.HasFlag(FileAttributes.Hidden));
        var other = Path.Combine(_dir, "swap");
        Directory.CreateDirectory(other);
        File.Copy(SettingsReset.DefaultsPath(_dir), Path.Combine(other, SettingsStore.FileName));
        var loaded = new SettingsStore(other).Load();
        Assert.Equal(new HotlineSettings().Chat.HistoryListSize, loaded.Chat.HistoryListSize);
        Assert.Empty(loaded.Chat.Backends);
    }

    private static string DefaultsToolbar(string dir) => Path.Combine(dir, SettingsReset.DefaultsFolder, "toolbar.json");

    [Fact]
    public void WriteDefaults_writes_a_hidden_toolbar_copy_and_keeps_it_current()
    {
        SettingsReset.WriteDefaults(_dir);
        var path = DefaultsToolbar(_dir);
        Assert.Equal(ToolbarModel.Defaults.ToJson(), File.ReadAllText(path));
        Assert.True(new DirectoryInfo(Path.GetDirectoryName(path)!).Attributes.HasFlag(FileAttributes.Hidden));

        File.WriteAllText(path, "stale");
        SettingsReset.WriteDefaults(_dir);
        Assert.Equal(ToolbarModel.Defaults.ToJson(), File.ReadAllText(path));

        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, stamp);
        SettingsReset.WriteDefaults(_dir);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(path)); // current: left alone
    }


    [Fact]
    public void Reset_settings_writes_the_default_toolbar_when_there_was_none()
    {
        var store = new SettingsStore(_dir);
        var s = store.Load();
        var (backup, _) = SettingsReset.Reset(store, _dir, s, everything: false, Now);
        Assert.Equal(ToolbarModel.Defaults.ToJson(), File.ReadAllText(Path.Combine(_dir, "toolbar.json")));
        Assert.False(File.Exists(Path.Combine(backup, "toolbar.json")));
    }

    [Theory]
    [InlineData("7B2276657273696F6E223A322C22637573746F6D223A747275657D")] // a customised toolbar
    [InlineData("7B2262FF007B")]                                           // broken: the backup must be byte for byte
    public void Reset_settings_restores_the_toolbar_after_backing_it_up(string toolbarHex)
    {
        var (store, s, _) = Customised();
        var before = Convert.FromHexString(toolbarHex);
        File.WriteAllBytes(Path.Combine(_dir, "toolbar.json"), before);
        var (backup, _) = SettingsReset.Reset(store, _dir, s, everything: false, Now);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(backup, "toolbar.json")));
        Assert.Equal(ToolbarModel.Defaults.ToJson(), File.ReadAllText(Path.Combine(_dir, "toolbar.json")));
    }
}

public sealed class GuideTests
{
    [Fact]
    public void Every_guide_section_has_a_title_and_a_body()
    {
        foreach (var section in Hotline.Core.Settings.Guide.Sections)
        {
            Assert.False(string.IsNullOrWhiteSpace(Hotline.Core.Text.Localization.Strings.ResourceManager.GetString(Hotline.Core.Settings.Guide.TitleKey(section), System.Globalization.CultureInfo.InvariantCulture)), section);
            Assert.False(string.IsNullOrWhiteSpace(Hotline.Core.Text.Localization.Strings.ResourceManager.GetString(Hotline.Core.Settings.Guide.BodyKey(section), System.Globalization.CultureInfo.InvariantCulture)), section);
        }
    }
}
