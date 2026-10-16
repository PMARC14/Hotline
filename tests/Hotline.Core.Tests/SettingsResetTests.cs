using Hotline.Core.Backends;
using Hotline.Core.Settings;

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
}
