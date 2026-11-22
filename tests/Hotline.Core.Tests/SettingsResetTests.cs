using System.Text;
using Hotline.Core.Backends;
using Hotline.Core.Files;
using Hotline.Core.Settings;
using Hotline.Core.State;
using Hotline.Core.Tests.Files;
using Hotline.Core.Upgrade;

namespace Hotline.Core.Tests;

public sealed class SettingsResetTests : TempDirTest
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 20, 0, 0, TimeSpan.Zero);
    private StateStore State => new(Path.Combine(_dir, StateStore.FileName));

    private (SettingsStore Store, HotlineSettings Settings, string LocalId) Customised()
    {
        var store = new SettingsStore(_dir);
        var settings = store.Load();
        var local = ConnectionEditor.Add(settings.Chat, BackendType.Local);
        ConnectionEditor.SetDefault(settings.Chat, local.Id);
        settings.Window.FontSize = 20;
        settings.Chat.Memory = false;
        settings.Chat.ContinueIn = [new(ContinueKind.OpenWebUi, "Open WebUI", Url: "http://localhost:3000")];
        settings.Chat.Connections[0].ContinueIn = settings.Chat.ContinueIn;
        store.Save(settings);
        File.WriteAllBytes(Path.Combine(_dir, "toolbar.json"), "{\"version\":3,\"buttons\":{}}"u8.ToArray());
        File.WriteAllText(Path.Combine(_dir, "memory.md"), "keep me");
        File.WriteAllText(Path.Combine(_dir, "mcp.json"), "{\"mcpServers\":{}}");
        return (store, settings, local.Id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reset_uses_one_set_embedded_defaults_and_clears_state(bool everything)
    {
        var (store, settings, local) = Customised();
        State.MarkShown(ShownFlag.FirstRun);
        State.RecordUpgrade("old-backup");
        State.AddGrant("server", "tool", "hash");
        var result = SettingsReset.Reset(store, _dir, settings, everything, Now, state: State);
        Assert.Empty(result.Failed);
        Assert.Contains("-reset", result.Backup);
        Assert.Single(Directory.GetDirectories(Path.Combine(_dir, "backups")), p => p.EndsWith("-reset"));
        Assert.False(File.Exists(Path.Combine(result.Backup, "state.json")));
        Assert.False(State.IsShown(ShownFlag.FirstRun));
        Assert.Null(State.GetUpgrade());
        Assert.Empty(State.GetGrants());
        Assert.Equal(StarterFiles.DefaultBytes("toolbar"), File.ReadAllBytes(Path.Combine(_dir, "toolbar.json")));
        Assert.Equal("keep me", File.ReadAllText(Path.Combine(_dir, "memory.md")));
        var back = new SettingsStore(_dir).Load();
        Assert.Null(back.Window.FontSize);
        Assert.True(back.Chat.Memory);
        Assert.Null(back.Chat.ContinueIn);
        Assert.Equal(everything ? "" : local, back.Chat.DefaultBackend);
        Assert.Equal(everything ? new[] { local } : [], result.Removed);
        if (everything)
        {
            Assert.Empty(back.Chat.Connections);
            Assert.Equal(StarterFiles.DefaultBytes("mcp"), File.ReadAllBytes(Path.Combine(_dir, "mcp.json")));
            Assert.True(File.Exists(Path.Combine(result.Backup, "connections", local + ".json")));
        }
        else
        {
            Assert.Single(Assert.Single(back.Chat.Connections).ContinueIn!);
            Assert.False(Directory.Exists(Path.Combine(result.Backup, "connections")));
            Assert.Equal("{\"mcpServers\":{}}", File.ReadAllText(Path.Combine(_dir, "mcp.json")));
        }
    }

    [Theory]
    [InlineData(false, "settings.json")]
    [InlineData(false, "toolbar.json")]
    [InlineData(true, "settings.json")]
    [InlineData(true, "toolbar.json")]
    [InlineData(true, "mcp.json")]
    [InlineData(true, "connections/local.json")]
    public void Reset_leaves_each_file_alone_when_its_backup_fails(bool everything, string blocked)
    {
        var files = new FakeFileSystem();
        files.CreateDirectory(_dir);
        files.CreateDirectory(Path.Combine(_dir, "connections"));
        var old = new Dictionary<string, string>
        {
            ["settings.json"] = "{\"version\":1,\"chat\":{\"memory\":false}}",
            ["toolbar.json"] = "{\"version\":3}",
            ["mcp.json"] = "{\"version\":1}",
            ["connections/local.json"] = "{\"version\":1,\"type\":\"local\"}"
        };
        foreach (var (name, text) in old) files.Files[Path.Combine(_dir, name.Replace('/', Path.DirectorySeparatorChar))] = Encoding.UTF8.GetBytes(text);
        var path = Path.Combine(_dir, blocked.Replace('/', Path.DirectorySeparatorChar));
        files.Fail = c => c.Operation == "CopyFile" && c.Path == path;
        var store = new SettingsStore(_dir, files, State);
        var current = store.Load();
        var result = SettingsReset.Reset(store, _dir, current, everything, Now, files, State);
        Assert.Contains(Path.GetFileName(path), result.Failed);
        Assert.Equal(old[blocked], Encoding.UTF8.GetString(files.Files[path]));
        if (blocked.StartsWith("connections/")) Assert.DoesNotContain("local", result.Removed);
    }

    [Theory]
    [InlineData("settings.json")]
    [InlineData("toolbar.json")]
    [InlineData("mcp.json")]
    [InlineData("connections/local.json")]
    public void Reset_refuses_newer_files(string name)
    {
        var (store, settings, _) = Customised();
        var path = Path.Combine(_dir, name.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string newer = "{\"version\":99}";
        File.WriteAllText(path, newer);
        var result = SettingsReset.Reset(store, _dir, settings, true, Now);
        Assert.Contains(Path.GetFileName(path), result.Failed);
        Assert.Equal(newer, File.ReadAllText(path));
    }

    [Fact]
    public void Defaults_are_hidden_embedded_bytes_and_current_copies_are_not_rewritten()
    {
        StarterFiles.Ensure(_dir, new RealFileSystem(), State, new InMemorySecretStore());
        var folder = Path.Combine(_dir, SettingsReset.DefaultsFolder);
        Assert.True(new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.Hidden));
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var family in new[] { "settings", "toolbar", "mcp" })
        {
            var path = Path.Combine(folder, family + ".json");
            Assert.Equal(StarterFiles.DefaultBytes(family), File.ReadAllBytes(path));
            File.SetLastWriteTimeUtc(path, stamp);
        }
        StarterFiles.Ensure(_dir, new RealFileSystem(), State, new InMemorySecretStore());
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(SettingsReset.DefaultsToolbarPath(_dir)));
    }

    [Theory]
    [InlineData("7B2276657273696F6E223A322C22637573746F6D223A747275657D")]
    [InlineData("7B2262FF007B")]
    public void Reset_backs_up_exact_old_or_broken_bytes(string hex)
    {
        var (store, settings, _) = Customised();
        var path = Path.Combine(_dir, "toolbar.json");
        var bytes = Convert.FromHexString(hex);
        File.WriteAllBytes(path, bytes);
        var result = SettingsReset.Reset(store, _dir, settings, false, Now);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(result.Backup, "toolbar.json")));
        Assert.Equal(StarterFiles.DefaultBytes("toolbar"), File.ReadAllBytes(path));
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
