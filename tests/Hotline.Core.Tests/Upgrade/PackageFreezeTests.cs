using System.IO.Compression;
using System.Text;
using Hotline.Core.Backends;
using Hotline.Core.Files;
using Hotline.Core.Settings;
using Hotline.Core.State;
using Hotline.Core.Tests.Files;
using Hotline.Core.Upgrade;

namespace Hotline.Core.Tests.Upgrade;

public sealed class PackageFreezeTests : TempDirTest
{
    private string Put(string name, string text)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    [Fact]
    public void Export_keeps_Mcp_bytes_and_tokens_reports_plaintext_names_and_omits_state()
    {
        const string mcp = """{"mcpServers":{"server":{"command":"program","env":{"KEY":"${hotline:secret:key}","DEBUG":"private-value"}}}}""";
        Put("mcp.json", mcp);
        Put("state.json", """{"version":1,"grants":[]}""");
        using var zip = new MemoryStream();
        var exported = SettingsPackage.Export(_dir, SettingsPackage.All, zip);
        var entry = Assert.Single(exported.PlaintextEnv);
        Assert.Equal("server", entry.Server);
        Assert.Equal("DEBUG", entry.EnvKey);
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        Assert.Null(archive.GetEntry("state.json"));
        using var reader = new StreamReader(archive.GetEntry("mcp.json")!.Open());
        Assert.Equal(mcp, reader.ReadToEnd());
    }

    [Fact]
    public void Import_masks_env_orders_allow_rules_and_lists_missing_secret_servers()
    {
        Put("settings.json", """{"version":1,"tools":{"approvals":{"server/z":"deny","server/a":"allow","server/b":"ask"}}}""");
        Put("mcp.json", """{"mcpServers":{"server":{"command":"program","env":{"KEY":"${hotline:secret:key}","TOKEN":"private-value"}}}}""");
        using var zip = new MemoryStream();
        SettingsPackage.Export(_dir, SettingsPackage.All, zip);
        zip.Position = 0;
        var plan = SettingsPackage.PrepareImport(Path.Combine(_dir, "target"), zip, SettingsPackage.All);
        Assert.Contains("KEY=***", plan.ExecutionReview);
        Assert.Contains("TOKEN=***", plan.ExecutionReview);
        Assert.DoesNotContain("private-value", plan.ExecutionReview);
        Assert.DoesNotContain("${hotline:", plan.ExecutionReview);
        Assert.True(plan.ExecutionReview.IndexOf("server/a: allow", StringComparison.Ordinal) < plan.ExecutionReview.IndexOf("server/z: deny", StringComparison.Ordinal));
        var secrets = new InMemorySecretStore();
        Assert.Equal(new[] { "server" }, SettingsPackage.ServersNeedingSecrets(plan, secrets));
        secrets.Set("mcp:secret:key", "secret");
        Assert.Empty(SettingsPackage.ServersNeedingSecrets(plan, secrets));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Import_uses_one_set_refuses_newer_files_and_never_writes_after_backup_failure(bool failBackup)
    {
        Put("settings.json", """{"version":1,"general":{"supportReminder":false}}""");
        Put("toolbar.json", """{"version":3}""");
        using var zip = new MemoryStream();
        SettingsPackage.Export(_dir, SettingsPackage.All, zip);
        zip.Position = 0;
        var target = Path.Combine(_dir, "target");
        var plan = SettingsPackage.PrepareImport(target, zip, SettingsPackage.All);
        var files = new FakeFileSystem();
        files.CreateDirectory(target);
        files.Files[Path.Combine(target, "settings.json")] = "{\"version\":99}"u8.ToArray();
        files.Files[Path.Combine(target, "toolbar.json")] = "{\"version\":3,\"extension\":true}"u8.ToArray();
        files.Fail = c => failBackup && c.Operation == "CopyFile";
        var result = SettingsPackage.Import(plan, Path.Combine(target, "backups"), true, fileSystem: files,
            state: new StateStore(Path.Combine(target, "state.json")));
        Assert.Equal("{\"version\":99}", Encoding.UTF8.GetString(files.Files[Path.Combine(target, "settings.json")]));
        if (failBackup) { Assert.NotNull(result.Error); Assert.Empty(result.Written); }
        else
        {
            Assert.Contains("toolbar.json", result.Written);
            Assert.Contains("-import", result.BackupFolder!);
        Assert.Single(files.Directories, p => Path.GetFileName(p).EndsWith("-import"));
        }
    }
}
