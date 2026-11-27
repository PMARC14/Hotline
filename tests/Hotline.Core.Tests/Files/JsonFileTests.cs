using System.Text;
using System.Text.Json.Nodes;
using Hotline.Core.Files;
using Hotline.Core.State;

namespace Hotline.Core.Tests.Files;

public sealed class JsonFileTests
{
    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    [Theory]
    [InlineData("{\"version\":3}", 3)]
    [InlineData("{\"Version\":2}", 2)]
    [InlineData("{\"VERSION\":2,\"version\":5}", 5)]
    [InlineData("{\"version\":1.5}", 2)]
    [InlineData("﻿{\"version\":4}", 4)]
    [InlineData("{ /*c*/ \"version\": 7, }", 7)]
    [InlineData("{\"version\":\"3\"}", null)]
    [InlineData("{}", null)]
    [InlineData("[1]", null)]
    [InlineData("{ broken", null)]
    [InlineData("", null)]
    public void ReadVersion_is_case_insensitive_and_lenient(string json, int? expected) =>
        Assert.Equal(expected, JsonFile.ReadVersion(B(json)));

    [Fact]
    public void Merge_keeps_the_disk_key_spelling_and_unknown_keys()
    {
        var tree = JsonNode.Parse("{\"Version\":1,\"Shown\":{\"firstRun\":\"x\",\"extra\":1},\"future\":true}")!.AsObject();
        var before = JsonNode.Parse("{\"version\":1,\"shown\":{\"firstRun\":\"x\"}}")!.AsObject();
        var after = JsonNode.Parse("{\"version\":1,\"shown\":{\"firstRun\":\"x\",\"supportNote\":\"y\"}}")!.AsObject();

        JsonFile.Merge(tree, before, after);

        Assert.Equal(["Version", "Shown", "future"], tree.Select(p => p.Key).ToArray());
        Assert.Equal("y", tree["Shown"]!["supportNote"]!.GetValue<string>());
        Assert.Equal(1, tree["Shown"]!["extra"]!.GetValue<int>());
    }

    [Fact]
    public void Save_writes_skips_identical_bytes_and_refuses_newer_files()
    {
        var fs = new FakeFileSystem();
        var path = Path.GetFullPath(Path.Combine("cfg", "state.json"));

        Assert.Equal(SafeFileResult.Written, JsonFile.Save(fs, path, B("{\"version\":1}"), 1).Result);
        Assert.Equal(SafeFileResult.Unchanged, JsonFile.Save(fs, path, B("{\"version\":1}"), 1).Result);
        Assert.Equal(SafeFileResult.Written, JsonFile.Save(fs, path, B("{\"version\":1,\"a\":1}"), 1).Result);

        fs.Files[path] = B("{\"Version\":9}");
        var newer = JsonFile.Save(fs, path, B("{\"version\":1}"), 1);
        Assert.Equal(SafeFileResult.NewerVersion, newer.Result);
        Assert.Equal(9, newer.DiskVersion);
        Assert.Equal(B("{\"Version\":9}"), fs.Files[path]);
    }

    [Fact]
    public void Save_reports_write_failure()
    {
        var fs = new FakeFileSystem { Fail = c => c.Operation == "WriteDurable" };
        Assert.Equal(SafeFileResult.WriteFailed, JsonFile.Save(fs, Path.GetFullPath("s.json"), B("{}"), 1).Result);
    }

    [Fact]
    public void StateStore_runs_on_an_injected_file_system()
    {
        var fs = new FakeFileSystem();
        var path = Path.GetFullPath(Path.Combine("cfg", StateStore.FileName));
        var store = new StateStore(path, fileSystem: fs);

        Assert.Equal(new ShownState(), store.GetShown());
        Assert.Empty(fs.Writes);
        Assert.True(store.MarkShown(ShownFlag.FirstRun, new DateTimeOffset(2026, 11, 14, 20, 0, 0, TimeSpan.Zero)));
        Assert.True(fs.Files.ContainsKey(path));
        Assert.True(new StateStore(path, fileSystem: fs).IsShown(ShownFlag.FirstRun));
        Assert.False(File.Exists(path));
    }
}
