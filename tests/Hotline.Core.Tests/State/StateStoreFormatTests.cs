using System.Text;
using System.Text.Json.Nodes;
using Hotline.Core.Settings;
using Hotline.Core.State;

namespace Hotline.Core.Tests.State;

/// <summary>Pins the state.json bytes and fixture behaviour so the on-disk format cannot drift.</summary>
public sealed class StateStoreFormatTests : TempDirTest
{
    private string StatePath => Path.Combine(_dir, StateStore.FileName);
    private static readonly DateTimeOffset T = new(2026, 11, 14, 20, 0, 0, TimeSpan.Zero);

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "state", "v1", name));

    private StateStore Store(string? fixture = null)
    {
        Directory.CreateDirectory(_dir);
        if (fixture is not null) File.WriteAllText(StatePath, Fixture(fixture));
        return new StateStore(StatePath);
    }

    [Fact]
    public void Typical_write_produces_exact_bytes_without_bom()
    {
        var store = Store();
        Assert.True(store.MarkShown(ShownFlag.FirstRun, T));
        Assert.True(store.SavePanelSpot(640, WidthOver.FromPercent("40%"), null, null,
            new PanelAnchor { Monitor = "active", X = 0.3, Y = 0.8 }, new PanelAnchor { Monitor = "active", X = 0.5, Y = 0.8 }));
        Assert.True(store.AddGrant("files", "read_file", "abc", T));
        Assert.True(store.SetLastWritten("connections\\claude.json", "def"));
        Assert.True(store.SetUpgrade(new UpgradeRecord(T, null)));

        var bytes = File.ReadAllBytes(StatePath);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        const string expected = """
            {
              "version": 1,
              "shown": {
                "firstRun": "2026-11-14T20:00:00.0000000+00:00"
              },
              "panel": {
                "width": {
                  "value": 640,
                  "over": "40%"
                },
                "chatHeight": {
                  "value": null,
                  "over": null
                },
                "anchor": {
                  "value": {
                    "monitor": "active",
                    "x": 0.3,
                    "y": 0.8
                  },
                  "over": {
                    "monitor": "active",
                    "x": 0.5,
                    "y": 0.8
                  }
                }
              },
              "grants": [
                {
                  "server": "files",
                  "tool": "read_file",
                  "launch": "abc",
                  "at": "2026-11-14T20:00:00.0000000+00:00"
                }
              ],
              "lastWritten": {
                "connections/claude.json": "def"
              },
              "upgrade": {
                "at": "2026-11-14T20:00:00.0000000+00:00",
                "backup": null
              }
            }
            """;
        Assert.Equal(expected.ReplaceLineEndings("\n"), Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Default_fixture_reads_every_section()
    {
        var store = Store("default.json");
        Assert.Equal(T, store.GetShown(ShownFlag.FirstRun));
        Assert.Null(store.GetShown(ShownFlag.SupportNote));
        var spot = store.GetPanelSpot();
        Assert.Equal(640, spot.Width!.Value);
        Assert.Equal(500, spot.ChatHeight!.Value);
        Assert.Equal(0.3, spot.Anchor!.Value.X);
        Assert.True(store.IsGranted("files", "read_file", "a1b2c3d4e5f60718293a4b5c6d7e8f901234567890abcdef1234567890abcdef"));
        Assert.Equal(2, store.GetAllLastWritten().Count);
        Assert.Equal("backups\\20261114-200000-upgrade", store.GetUpgrade()!.Backup);
    }

    [Fact]
    public void Unknown_keys_in_fixture_survive_a_write_to_every_section()
    {
        var store = Store("unknown-keys.json");
        Assert.True(store.SetShown(ShownFlag.SupportNote, T));
        Assert.True(store.SavePanelSpot(700, null, 400, null, new PanelAnchor { Monitor = "active", X = 0.1, Y = 0.2 }, null));
        Assert.True(store.AddGrant("other", "tool", "h", T));
        Assert.True(store.SetLastWritten("a.json", "x"));
        Assert.True(store.SetUpgrade(new UpgradeRecord(T.AddHours(1), "b")));

        var root = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        Assert.Equal("kept", root["futureTopState"]!.GetValue<string>());
        Assert.NotNull(root["shown"]!["futureShownFlag"]);
        Assert.Equal(1, root["panel"]!["futurePanelKey"]!.GetValue<int>());
        Assert.True(root["panel"]!["width"]!["futureWidthMeta"]!.GetValue<bool>());
        Assert.Equal("ok", root["panel"]!["chatHeight"]!["futureHeightMeta"]!.GetValue<string>());
        Assert.Equal(7, root["panel"]!["anchor"]!["value"]!["futureAnchorValueProp"]!.GetValue<int>());
        Assert.Equal("yes", root["panel"]!["anchor"]!["futureAnchorEntryProp"]!.GetValue<string>());
        Assert.Equal("preserved", root["grants"]![0]!["futureGrantProp"]!.GetValue<string>());
        Assert.True(root["upgrade"]!["futureUpgradeProp"]!.GetValue<bool>());
    }

    [Fact]
    public void Newer_version_fixture_is_read_never_written()
    {
        var store = Store("newer-version.json");
        var before = File.ReadAllBytes(StatePath);
        Assert.NotNull(store.NewerVersionFile);
        Assert.False(store.MarkShown(ShownFlag.FirstRun));
        Assert.False(store.AddGrant("s", "t", "h"));
        Assert.False(store.Clear());
        Assert.Equal(before, File.ReadAllBytes(StatePath));
    }

    [Fact]
    public void Broken_fixture_gives_defaults_and_stays_untouched_until_a_write()
    {
        var store = Store("broken.json");
        var before = File.ReadAllBytes(StatePath);
        Assert.Equal(new ShownState(), store.GetShown());
        Assert.Empty(store.GetGrants());
        Assert.Equal(before, File.ReadAllBytes(StatePath));
    }

    [Fact]
    public void Hand_cased_keys_are_read_and_keep_their_spelling_on_write()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, "{\"Version\":1,\"Shown\":{\"FirstRun\":\"2026-11-14T20:00:00Z\"},\"Grants\":[{\"Server\":\"s\",\"Tool\":\"t\",\"Launch\":\"h\",\"At\":\"2026-11-14T20:00:00Z\",\"Extra\":1}]}");
        var store = new StateStore(StatePath);
        Assert.True(store.IsShown(ShownFlag.FirstRun));
        Assert.True(store.IsGranted("s", "t", "h"));

        Assert.True(store.SetShown(ShownFlag.SupportNote, T));
        Assert.True(store.AddGrant("s2", "t2", "h2", T));

        var root = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        Assert.Equal(["Version", "Shown", "Grants"], root.Select(p => p.Key).ToArray());
        Assert.Equal(1, root["Grants"]![0]!["Extra"]!.GetValue<int>());
        Assert.Equal(2, root["Grants"]!.AsArray().Count);
        Assert.NotNull(root["Shown"]!["supportNote"]);
    }

    // Reset settings resets state.json: every known section goes, readable or not (a later 1.x may read more shapes, so a
    // "reset" grant must not come back), and unknown keys stay.
    [Fact]
    public void Clear_removes_known_sections_even_when_unreadable_and_keeps_unknown_keys()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, """
            {
              "version": 1,
              "shown": { "firstRun": 5, "custom": true },
              "panel": { "width": { "value": "x", "over": null, "note": 1 }, "chatHeight": 7, "anchor": null },
              "grants": [ { "server": "bad" }, 7 ],
              "lastWritten": { "toolbar.json": 5, "icons/a.png": "" },
              "upgrade": { "at": "not a date", "backup": 3, "extra": "keep" },
              "future": { "a": 1 }
            }
            """);
        var store = new StateStore(StatePath);

        Assert.True(store.Clear());

        var root = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        Assert.Equal(["version", "shown", "panel", "upgrade", "future"], root.Select(p => p.Key).ToArray());
        Assert.Equal("""{"custom":true}""", root["shown"]!.ToJsonString());
        Assert.Equal("""{"width":{"note":1}}""", root["panel"]!.ToJsonString());
        Assert.Equal("""{"extra":"keep"}""", root["upgrade"]!.ToJsonString());
        Assert.Empty(store.GetGrants());
    }

    // A hand-edited path ("/Icons/A.png") is read as "Icons/A.png": updating or removing it must edit that same key, never
    // leave it behind or add a second one.
    [Fact]
    public void A_hand_written_lastWritten_path_is_updated_and_removed_in_place()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, """{ "version": 1, "lastWritten": { "/Icons/A.png": "h1", "toolbar.json": "t" } }""");
        var store = new StateStore(StatePath);
        Assert.Equal("h1", store.GetLastWritten("Icons/A.png"));

        Assert.True(store.SetLastWritten("Icons/A.png", "h2"));
        var after = JsonNode.Parse(File.ReadAllText(StatePath))!["lastWritten"]!.AsObject();
        Assert.Equal(["/Icons/A.png", "toolbar.json"], after.Select(p => p.Key).ToArray());
        Assert.Equal("h2", after["/Icons/A.png"]!.GetValue<string>());

        Assert.True(store.SetLastWritten("Icons/A.png", null));
        after = JsonNode.Parse(File.ReadAllText(StatePath))!["lastWritten"]!.AsObject();
        Assert.Equal(["toolbar.json"], after.Select(p => p.Key).ToArray());
    }

    [Fact]
    public void Changing_a_returned_anchor_does_not_change_the_store()
    {
        var store = Store();
        Assert.True(store.SavePanelSpot(640, null, null, null, new PanelAnchor { Monitor = "active", X = 0.3, Y = 0.8 }, null));
        store.GetPanelSpot().Anchor!.Value.X = 0.9;
        Assert.Equal(0.3, store.GetPanelSpot().Anchor!.Value.X);
    }
}
