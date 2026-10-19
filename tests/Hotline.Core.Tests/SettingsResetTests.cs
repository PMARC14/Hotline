using Hotline.Core.Backends;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class SettingsResetTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
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
        return (store, s, local.Id);
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
        Assert.True(File.Exists(Path.Combine(backup, "toolbar.json")));
        Assert.True(File.Exists(Path.Combine(backup, SettingsStore.ConnectionsFolder, local + ".json")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_dir, "memory.md")));
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
    public void Reset_settings_restores_the_toolbar_after_backing_it_up_and_leaves_mcp_alone()
    {
        var (store, s, _) = Customised();
        File.WriteAllText(Path.Combine(_dir, "toolbar.json"), "{\"version\":2,\"custom\":true}");
        File.WriteAllText(Path.Combine(_dir, "mcp.json"), "{\"mcp\":1}");
        var (backup, _) = SettingsReset.Reset(store, _dir, s, everything: false, Now);
        Assert.Equal("{\"version\":2,\"custom\":true}", File.ReadAllText(Path.Combine(backup, "toolbar.json")));
        Assert.Equal(ToolbarModel.Defaults.ToJson(), File.ReadAllText(Path.Combine(_dir, "toolbar.json")));
        Assert.Equal("{\"mcp\":1}", File.ReadAllText(Path.Combine(_dir, "mcp.json")));
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

    [Fact]
    public void Reset_settings_replaces_a_broken_toolbar_and_backs_it_up_byte_for_byte()
    {
        var (store, s, _) = Customised();
        byte[] broken = [0x7B, 0x22, 0x62, 0xFF, 0x00, 0x7B];
        File.WriteAllBytes(Path.Combine(_dir, "toolbar.json"), broken);
        var (backup, _) = SettingsReset.Reset(store, _dir, s, everything: false, Now);
        Assert.Equal(broken, File.ReadAllBytes(Path.Combine(backup, "toolbar.json")));
        Assert.Equal(ToolbarModel.Defaults.ToJson(), File.ReadAllText(Path.Combine(_dir, "toolbar.json")));
    }

    [Fact]
    public void Reset_everything_deletes_the_toolbar_and_mcp_but_backs_both_up()
    {
        var (store, s, _) = Customised();
        File.WriteAllText(Path.Combine(_dir, "toolbar.json"), "custom bar");
        File.WriteAllText(Path.Combine(_dir, "mcp.json"), "custom mcp");
        var (backup, _) = SettingsReset.Reset(store, _dir, s, everything: true, Now);
        Assert.False(File.Exists(Path.Combine(_dir, "toolbar.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "mcp.json")));
        Assert.Equal("custom bar", File.ReadAllText(Path.Combine(backup, "toolbar.json")));
        Assert.Equal("custom mcp", File.ReadAllText(Path.Combine(backup, "mcp.json")));
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
