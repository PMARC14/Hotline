using System.Text.Json.Nodes;
using Hotline.Core.Settings;
using Hotline.Core.State;

namespace Hotline.Core.Tests.State;

public sealed class StateStoreTests : TempDirTest
{
    private string StatePath => Path.Combine(_dir, StateStore.FileName);

    private static DateTimeOffset Utc(int hour, int minute = 0) =>
        new(2026, 11, 14, hour, minute, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private StateStore CreateStore(DateTimeOffset? now = null) =>
        new(StatePath, new FixedClock(now ?? Utc(20)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ broken")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    public void Missing_or_unreadable_file_gives_empty_state_and_load_never_writes(string? diskContent)
    {
        if (diskContent is not null)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(StatePath, diskContent);
        }

        var store = CreateStore();

        Assert.Null(store.NewerVersionFile);
        Assert.Equal(new ShownState(), store.GetShown());
        Assert.Equal(new PanelSpot(), store.GetPanelSpot());
        Assert.Empty(store.GetGrants());
        Assert.Empty(store.GetAllLastWritten());
        Assert.Null(store.GetLastWritten("settings.json"));
        Assert.Null(store.GetUpgrade());

        if (diskContent is null)
            Assert.False(File.Exists(StatePath));
        else
            Assert.Equal(diskContent, File.ReadAllText(StatePath));
    }

    [Fact]
    public void Corrupt_file_is_replaced_on_subsequent_write()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, "{ broken");

        var store = CreateStore(Utc(21));
        Assert.True(store.MarkShown(ShownFlag.FirstRun));

        var reloaded = CreateStore();
        Assert.Equal(Utc(21), reloaded.GetShown(ShownFlag.FirstRun));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(99)]
    public void Newer_version_file_gives_empty_state_and_is_never_written(int version)
    {
        Directory.CreateDirectory(_dir);
        var original = $$"""
            {
              "version": {{version}},
              "shown": { "firstRun": "2026-11-14T20:00:00Z" },
              "grants": [ { "server": "files", "tool": "read_file", "launch": "abc", "at": "2026-11-14T20:00:00Z" } ],
              "lastWritten": { "settings.json": "deadbeef" },
              "upgrade": { "at": "2026-11-14T20:00:00Z", "backup": "backups\\up" }
            }
            """;
        File.WriteAllText(StatePath, original);

        var store = CreateStore();

        Assert.Equal(version, store.NewerVersionFile);
        Assert.Equal(new ShownState(), store.GetShown());
        Assert.Empty(store.GetGrants());
        Assert.Null(store.GetLastWritten("settings.json"));
        Assert.Null(store.GetUpgrade());

        Assert.False(store.MarkShown(ShownFlag.FirstRun));
        Assert.False(store.SavePanelSpot(640, "40%", 500, null, new PanelAnchor(), new PanelAnchor()));
        Assert.False(store.AddGrant("files", "write_file", "hash"));
        Assert.False(store.RevokeGrant("files", "read_file"));
        Assert.False(store.SetLastWritten("settings.json", "new"));
        Assert.False(store.RecordUpgrade("backups\\x"));
        Assert.False(store.Clear());

        Assert.Equal(original, File.ReadAllText(StatePath));
    }

    [Fact]
    public void External_newer_version_written_after_construction_is_not_overwritten()
    {
        var store = CreateStore();
        Assert.True(store.MarkShown(ShownFlag.FirstRun));

        var newer = """{"version": 2, "future": true}""";
        File.WriteAllText(StatePath, newer);

        Assert.False(store.MarkShown(ShownFlag.SupportNote));
        Assert.Equal(2, store.NewerVersionFile);
        Assert.Equal(newer, File.ReadAllText(StatePath));
    }

    [Fact]
    public void Malformed_fields_inside_v1_object_are_ignored_safely()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, """
            {
              "version": 1,
              "shown": { "firstRun": 123, "supportNote": "not-a-date", "upgradeNotice": true },
              "panel": {
                "width": { "value": "bad", "over": "40%" },
                "chatHeight": { "value": "bad", "over": null },
                "anchor": { "value": { "monitor": 5, "x": "bad", "y": 0.5 }, "over": null }
              },
              "grants": [ 123, { "server": "files" }, { "server": "ok", "tool": "run", "launch": "h1", "at": "bad" } ],
              "lastWritten": { "settings.json": 42, "toolbar.json": "abc" },
              "upgrade": { "at": "not-a-date", "backup": 123 }
            }
            """);

        var store = CreateStore();

        Assert.Equal(new ShownState(), store.GetShown());
        Assert.Equal(new PanelSpot(), store.GetPanelSpot());
        Assert.Empty(store.GetGrants());
        Assert.Null(store.GetLastWritten("settings.json"));
        Assert.Equal("abc", store.GetLastWritten("toolbar.json"));
        Assert.Null(store.GetUpgrade());
    }

    [Theory]
    [InlineData(ShownFlag.FirstRun)]
    [InlineData(ShownFlag.SupportNote)]
    [InlineData(ShownFlag.UpgradeNotice)]
    public void Shown_flags_round_trip_and_can_be_cleared(ShownFlag flag)
    {
        var now = Utc(19, 30);
        var store = CreateStore(now);

        Assert.False(store.IsShown(flag));
        Assert.Null(store.GetShown(flag));

        Assert.True(store.MarkShown(flag));
        Assert.True(store.IsShown(flag));
        Assert.Equal(now, store.GetShown(flag));

        var reloaded = CreateStore();
        Assert.True(reloaded.IsShown(flag));
        Assert.Equal(now, reloaded.GetShown(flag));

        Assert.True(reloaded.SetShown(flag, null));
        Assert.False(CreateStore().IsShown(flag));
    }

    public static TheoryData<string, WidthOver?, int?, PanelAnchor, int?, int?, PanelAnchor, bool, bool, bool> PanelResolutionCases() =>
        new()
        {
            {
                "all-equal",
                "40%", null, new PanelAnchor { Monitor = "active", X = 0.5, Y = 0.8 },
                640, 500, new PanelAnchor { Monitor = "active", X = 0.3, Y = 0.8 },
                true, true, true
            },
            {
                "width-percent-changed",
                "50%", null, new PanelAnchor { Monitor = "active", X = 0.5, Y = 0.8 },
                null, 500, new PanelAnchor { Monitor = "active", X = 0.3, Y = 0.8 },
                false, true, true
            },
            {
                "width-switched-to-pixels",
                640, null, new PanelAnchor { Monitor = "active", X = 0.5, Y = 0.8 },
                null, 500, new PanelAnchor { Monitor = "active", X = 0.3, Y = 0.8 },
                false, true, true
            },
            {
                "chat-height-changed",
                "40%", 420, new PanelAnchor { Monitor = "active", X = 0.5, Y = 0.8 },
                640, 420, new PanelAnchor { Monitor = "active", X = 0.3, Y = 0.8 },
                true, false, true
            },
            {
                "anchor-monitor-changed",
                "40%", null, new PanelAnchor { Monitor = "primary", X = 0.5, Y = 0.8 },
                640, 500, new PanelAnchor { Monitor = "primary", X = 0.5, Y = 0.8 },
                true, true, false
            },
            {
                "anchor-x-changed",
                "40%", null, new PanelAnchor { Monitor = "active", X = 0.6, Y = 0.8 },
                640, 500, new PanelAnchor { Monitor = "active", X = 0.6, Y = 0.8 },
                true, true, false
            },
        };

    [Theory]
    [MemberData(nameof(PanelResolutionCases))]
    public void Saved_panel_spot_applies_each_item_only_while_setting_equals_over(
        string _,
        WidthOver? currentWidth,
        int? currentChatHeight,
        PanelAnchor currentAnchor,
        int? expectedWidth,
        int? expectedChatHeight,
        PanelAnchor expectedAnchor,
        bool widthApplied,
        bool chatHeightApplied,
        bool anchorApplied)
    {
        var store = CreateStore();
        var savedAnchor = new PanelAnchor { Monitor = "active", X = 0.3, Y = 0.8 };
        var overAnchor = new PanelAnchor { Monitor = "active", X = 0.5, Y = 0.8 };
        Assert.True(store.SavePanelSpot(640, "40%", 500, null, savedAnchor, overAnchor));

        // Mutating caller anchor after save must not leak into store.
        savedAnchor.X = 0.99;

        var reloaded = CreateStore();
        var resolved = reloaded.ResolvePanelSpot(currentWidth, currentChatHeight, currentAnchor);

        Assert.Equal(widthApplied, resolved.WidthApplied);
        Assert.Equal(chatHeightApplied, resolved.ChatHeightApplied);
        Assert.Equal(anchorApplied, resolved.AnchorApplied);
        Assert.Equal(expectedWidth, resolved.Width);
        Assert.Equal(expectedChatHeight, resolved.ChatHeight);
        Assert.Equal(expectedAnchor.Monitor, resolved.Anchor.Monitor);
        Assert.Equal(expectedAnchor.X, resolved.Anchor.X, 9);
        Assert.Equal(expectedAnchor.Y, resolved.Anchor.Y, 9);

        // Resolving never deleted stepped-aside overrides: restoring settings brings them back.
        var restored = reloaded.ResolvePanelSpot("40%", null, new PanelAnchor { Monitor = "active", X = 0.5, Y = 0.8 });
        Assert.True(restored.WidthApplied);
        Assert.True(restored.ChatHeightApplied);
        Assert.True(restored.AnchorApplied);
        Assert.Equal(640, restored.Width);
    }

    [Fact]
    public void Saved_null_chat_height_overrides_non_null_setting_while_equal()
    {
        var store = CreateStore();
        Assert.True(store.SavePanelSpot(640, 640, null, 500, new PanelAnchor(), new PanelAnchor()));

        var resolved = CreateStore().ResolvePanelSpot(640, 500, new PanelAnchor());
        Assert.True(resolved.ChatHeightApplied);
        Assert.Null(resolved.ChatHeight);

        var steppedAside = CreateStore().ResolvePanelSpot(640, 700, new PanelAnchor());
        Assert.False(steppedAside.ChatHeightApplied);
        Assert.Equal(700, steppedAside.ChatHeight);
    }

    [Fact]
    public void Launch_hash_is_deterministic_and_distinguishes_command_args_and_cwd()
    {
        var baseHash = StateStore.ComputeLaunchHash("npx", ["-y", "server"], @"C:\work");
        Assert.Equal(64, baseHash.Length);
        Assert.Equal(baseHash, baseHash.ToLowerInvariant());

        Assert.Equal(baseHash, StateStore.ComputeLaunchHash("npx", ["-y", "server"], @"C:\work"));
        Assert.Equal(
            StateStore.ComputeLaunchHash("tool", null, null),
            StateStore.ComputeLaunchHash("tool", [], ""));

        Assert.NotEqual(baseHash, StateStore.ComputeLaunchHash("node", ["-y", "server"], @"C:\work"));
        Assert.NotEqual(baseHash, StateStore.ComputeLaunchHash("npx", ["-y server"], @"C:\work"));
        Assert.NotEqual(baseHash, StateStore.ComputeLaunchHash("npx", ["-y", "server"], @"C:\other"));
    }

    [Fact]
    public void Grants_add_match_deduplicate_and_revoke_persistently()
    {
        var hash1 = StateStore.ComputeLaunchHash("npx", ["a"], @"C:\w");
        var hash2 = StateStore.ComputeLaunchHash("npx", ["b"], @"C:\w");

        IGrantStore store = CreateStore(Utc(10));
        Assert.True(store.AddGrant("files", "read_file", hash1));
        Assert.True(store.AddGrant("files", "write_file", hash1));

        // Re-adding same (server, tool, launch) keeps first timestamp without duplicating.
        var storeLater = CreateStore(Utc(11));
        Assert.True(storeLater.AddGrant("files", "read_file", hash1.ToUpperInvariant()));
        Assert.Equal(2, storeLater.GetGrants().Count);
        Assert.Equal(Utc(10), storeLater.GetGrants().Single(g => g.Tool == "read_file").At);

        Assert.True(storeLater.IsGranted("files", "read_file", hash1));
        Assert.True(storeLater.IsGranted("files", "read_file", hash1.ToUpperInvariant()));
        Assert.False(storeLater.IsGranted("files", "read_file", hash2));
        Assert.False(storeLater.IsGranted("other", "read_file", hash1));

        // Add another launch hash for same tool and verify selective revoke.
        Assert.True(storeLater.AddGrant("files", "read_file", hash2));
        Assert.Equal(3, storeLater.GetGrants().Count);

        Assert.True(storeLater.RevokeGrant("files", "read_file", hash1));
        Assert.False(storeLater.IsGranted("files", "read_file", hash1));
        Assert.True(storeLater.IsGranted("files", "read_file", hash2));

        // Revoke without launchHash removes all grants for that (server, tool).
        Assert.True(storeLater.RevokeGrant("files", "read_file"));
        Assert.False(storeLater.IsGranted("files", "read_file", hash2));
        Assert.Single(CreateStore().GetGrants());

        // Revoke non-existent grant returns false.
        Assert.False(storeLater.RevokeGrant("files", "read_file"));

        Assert.True(((StateStore)storeLater).RevokeServerGrants("files"));
        Assert.Empty(CreateStore().GetGrants());
    }

    [Fact]
    public void Last_written_and_upgrade_round_trip()
    {
        var store = CreateStore(Utc(20));

        Assert.True(store.SetLastWritten(@"connections\Claude.json", "hash1"));
        Assert.True(store.SetLastWritten("settings.json", "hash2"));
        Assert.Equal("hash1", store.GetLastWritten("connections/claude.json"));
        Assert.Equal("hash2", store.GetLastWritten("settings.json"));

        Assert.True(store.SetLastWritten("connections/claude.json", "hash1b"));
        Assert.True(store.RecordUpgrade(@"backups\20261114-200000-upgrade"));

        var reloaded = CreateStore();
        Assert.Equal("hash1b", reloaded.GetLastWritten(@"connections\claude.json"));
        Assert.Equal(2, reloaded.GetAllLastWritten().Count);
        var upgrade = reloaded.GetUpgrade();
        Assert.NotNull(upgrade);
        Assert.Equal(Utc(20), upgrade.At);
        Assert.Equal(@"backups\20261114-200000-upgrade", upgrade.Backup);

        Assert.True(reloaded.SetLastWritten("settings.json", null));
        Assert.Null(CreateStore().GetLastWritten("settings.json"));

        Assert.True(reloaded.RecordUpgrade(null, Utc(22)));
        var noBackupUpgrade = CreateStore().GetUpgrade();
        Assert.NotNull(noBackupUpgrade);
        Assert.Equal(Utc(22), noBackupUpgrade.At);
        Assert.Null(noBackupUpgrade.Backup);
    }

    [Fact]
    public void Unknown_keys_and_key_order_survive_every_save_including_clear()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, """
            {
              // comments and trailing commas allowed
              "unknownTop": { "nested": [1, 2] },
              "version": 1,
              "shown": { "unknownShown": "keep", "firstRun": null, "supportNote": null, "upgradeNotice": null },
              "panel": {
                "unknownPanel": 7,
                "width": { "value": 640, "over": "40%", "unknownWidth": true },
                "chatHeight": { "value": 500, "over": null, "unknownChatHeight": "x" },
                "anchor": {
                  "unknownAnchor": 9,
                  "value": { "monitor": "active", "x": 0.3, "y": 0.8, "unknownVal": 1 },
                  "over": { "monitor": "active", "x": 0.5, "y": 0.8, "unknownOver": 2 }
                }
              },
              "grants": [
                { "server": "files", "tool": "read_file", "launch": "h1", "at": "2026-11-14T20:00:00Z", "unknownGrant": "g" }
              ],
              "lastWritten": { "settings.json": "abc" },
              "upgrade": { "at": "2026-11-14T20:00:00Z", "backup": "backups\\u", "unknownUpgrade": 3 },
            }
            """);

        var store = CreateStore(Utc(23));
        Assert.True(store.MarkShown(ShownFlag.FirstRun));
        Assert.True(store.SavePanelSpot(700, "40%", 550, null,
            new PanelAnchor { Monitor = "primary", X = 0.2, Y = 0.7 },
            new PanelAnchor { Monitor = "active", X = 0.5, Y = 0.8 }));
        Assert.True(store.AddGrant("files", "write_file", "h2"));
        Assert.True(store.SetLastWritten("toolbar.json", "def"));
        Assert.True(store.RecordUpgrade(@"backups\u2"));

        var root = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        var topKeys = root.Select(p => p.Key).ToList();
        Assert.Equal("unknownTop", topKeys[0]);
        Assert.Equal("version", topKeys[1]);
        Assert.Equal("keep", root["shown"]!["unknownShown"]!.GetValue<string>());
        Assert.Equal(7, root["panel"]!["unknownPanel"]!.GetValue<int>());
        Assert.True(root["panel"]!["width"]!["unknownWidth"]!.GetValue<bool>());
        Assert.Equal("x", root["panel"]!["chatHeight"]!["unknownChatHeight"]!.GetValue<string>());
        Assert.Equal(9, root["panel"]!["anchor"]!["unknownAnchor"]!.GetValue<int>());
        Assert.Equal(1, root["panel"]!["anchor"]!["value"]!["unknownVal"]!.GetValue<int>());
        Assert.Equal(2, root["panel"]!["anchor"]!["over"]!["unknownOver"]!.GetValue<int>());
        Assert.Equal("g", root["grants"]![0]!["unknownGrant"]!.GetValue<string>());
        Assert.Equal(3, root["upgrade"]!["unknownUpgrade"]!.GetValue<int>());

        Assert.True(store.Clear());
        var cleared = CreateStore();
        Assert.Equal(new ShownState(), cleared.GetShown());
        Assert.Equal(new PanelSpot(), cleared.GetPanelSpot());
        Assert.Empty(cleared.GetGrants());
        Assert.Empty(cleared.GetAllLastWritten());
        Assert.Null(cleared.GetUpgrade());

        var afterClear = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        Assert.Equal(2, afterClear["unknownTop"]!["nested"]![1]!.GetValue<int>());
        Assert.Equal("keep", afterClear["shown"]!["unknownShown"]!.GetValue<string>());
    }

    [Fact]
    public void Clear_on_missing_file_creates_no_file_and_writes_create_no_backups()
    {
        var store = CreateStore();
        Assert.True(store.Clear());
        Assert.False(File.Exists(StatePath));

        Assert.True(store.MarkShown(ShownFlag.FirstRun));
        Assert.True(store.MarkShown(ShownFlag.SupportNote));
        Assert.Single(Directory.GetFiles(_dir));
        Assert.Empty(Directory.GetDirectories(_dir));
    }

    [Fact]
    public void Write_failure_returns_false_without_throwing()
    {
        Directory.CreateDirectory(StatePath); // directory blocks file publication
        var store = CreateStore();

        Assert.False(store.MarkShown(ShownFlag.FirstRun));
    }

    [Fact]
    public async Task Concurrent_reads_and_writes_are_thread_safe()
    {
        var store = CreateStore(Utc(20));
        var ct = TestContext.Current.CancellationToken;

        var tasks = Enumerable.Range(0, 12).Select(i => Task.Run(() =>
        {
            Assert.True(store.AddGrant("srv", $"tool_{i}", $"hash_{i}"));
            Assert.True(store.SetLastWritten($"connections/c{i}.json", $"sha_{i}"));
            _ = store.GetGrants();
            _ = store.GetAllLastWritten();
        }, ct));

        await Task.WhenAll(tasks);

        var reloaded = CreateStore();
        Assert.Equal(12, reloaded.GetGrants().Count);
        Assert.Equal(12, reloaded.GetAllLastWritten().Count);
    }

    [Fact]
    public void SavePanelSpot_null_item_or_null_over_preserves_unknown_keys()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(StatePath, """
            {
              "version": 1,
              "panel": {
                "unknownPanel": 7,
                "width": { "value": 640, "over": "40%", "unknownWidth": true },
                "chatHeight": { "value": 500, "over": 400, "unknownChatHeight": "x" },
                "anchor": {
                  "unknownAnchor": 9,
                  "value": { "monitor": "active", "x": 0.3, "y": 0.8, "unknownVal": 1 },
                  "over": { "monitor": "active", "x": 0.5, "y": 0.8 }
                }
              }
            }
            """);

        var store = CreateStore();
        var width = new PanelOverride<int, WidthOver?>(640, "40%");
        var chatHeight = new PanelOverride<int?, int?>(500, 400);
        var anchor = new PanelOverride<PanelAnchor, PanelAnchor?>(
            new PanelAnchor { Monitor = "active", X = 0.3, Y = 0.8 },
            new PanelAnchor { Monitor = "active", X = 0.5, Y = 0.8 });

        // Setting each item's "over" to null preserves unknown keys elsewhere in that item object.
        Assert.True(store.SavePanelSpot(new PanelSpot(
            width with { Over = null },
            chatHeight with { Over = null },
            anchor with { Over = null })));

        var afterNullOver = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        Assert.True(afterNullOver["panel"]!["width"]!["unknownWidth"]!.GetValue<bool>());
        Assert.Equal("x", afterNullOver["panel"]!["chatHeight"]!["unknownChatHeight"]!.GetValue<string>());
        Assert.Equal(9, afterNullOver["panel"]!["anchor"]!["unknownAnchor"]!.GetValue<int>());
        Assert.Equal(1, afterNullOver["panel"]!["anchor"]!["value"]!["unknownVal"]!.GetValue<int>());
        Assert.Null(CreateStore().GetPanelSpot().Width!.Over);
        Assert.Null(CreateStore().GetPanelSpot().ChatHeight!.Over);
        Assert.Null(CreateStore().GetPanelSpot().Anchor!.Over);

        // Saving each item as null keeps unknown keys in its object, and drops empty item objects only.
        Assert.True(store.SavePanelSpot(new PanelSpot(null, chatHeight, anchor)));
        var root1 = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        var widthObj = root1["panel"]!["width"]!.AsObject();
        Assert.True(widthObj["unknownWidth"]!.GetValue<bool>());
        Assert.False(widthObj.ContainsKey("value"));
        Assert.False(widthObj.ContainsKey("over"));
        Assert.Null(CreateStore().GetPanelSpot().Width);

        Assert.True(store.SavePanelSpot(new PanelSpot(width, null, anchor)));
        var root2 = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        var chatObj = root2["panel"]!["chatHeight"]!.AsObject();
        Assert.Equal("x", chatObj["unknownChatHeight"]!.GetValue<string>());
        Assert.False(chatObj.ContainsKey("value"));
        Assert.False(chatObj.ContainsKey("over"));
        Assert.Null(CreateStore().GetPanelSpot().ChatHeight);

        Assert.True(store.SavePanelSpot(new PanelSpot(width, chatHeight, null)));
        var root3 = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        var anchorObj = root3["panel"]!["anchor"]!.AsObject();
        Assert.Equal(9, anchorObj["unknownAnchor"]!.GetValue<int>());
        Assert.False(anchorObj.ContainsKey("value"));
        Assert.False(anchorObj.ContainsKey("over"));
        Assert.Null(CreateStore().GetPanelSpot().Anchor);

        // Empty item object (no unknown keys) is dropped when saved as null.
        anchorObj.Remove("unknownAnchor");
        File.WriteAllText(StatePath, root3.ToJsonString());
        var storeNoUnknown = CreateStore();
        Assert.True(storeNoUnknown.SavePanelSpot(new PanelSpot(width, chatHeight, anchor)));
        Assert.True(storeNoUnknown.SavePanelSpot(new PanelSpot(width, chatHeight, null)));
        var root4 = JsonNode.Parse(File.ReadAllText(StatePath))!.AsObject();
        Assert.False(root4["panel"]!.AsObject().ContainsKey("anchor"));
    }

    [Fact]
    public void Grants_compare_server_and_tool_ordinally_and_only_launch_hash_ignores_case()
    {
        var hash = StateStore.ComputeLaunchHash("npx", ["a"], @"C:\w");
        IGrantStore store = CreateStore(Utc(10));

        Assert.True(store.AddGrant("files", "read_file", hash));

        Assert.True(store.IsGranted("files", "read_file", hash));
        Assert.True(store.IsGranted("files", "read_file", hash.ToUpperInvariant()));
        Assert.False(store.IsGranted("Files", "read_file", hash));
        Assert.False(store.IsGranted("files", "Read_File", hash));

        // Different case in server or tool creates distinct grants; different case in launch hash deduplicates.
        Assert.True(store.AddGrant("Files", "read_file", hash));
        Assert.True(store.AddGrant("files", "Read_File", hash));
        Assert.True(store.AddGrant("files", "read_file", hash.ToUpperInvariant()));
        Assert.Equal(3, store.GetGrants().Count);

        // RevokeGrant compares server and tool ordinally and launch hash case-insensitively.
        Assert.False(store.RevokeGrant("FILES", "read_file"));
        Assert.False(store.RevokeGrant("files", "READ_FILE"));
        Assert.True(store.RevokeGrant("files", "read_file", hash.ToUpperInvariant()));
        Assert.False(store.IsGranted("files", "read_file", hash));
        Assert.True(store.IsGranted("Files", "read_file", hash));
        Assert.True(store.IsGranted("files", "Read_File", hash));
        Assert.Equal(2, CreateStore().GetGrants().Count);
    }

    [Fact]
    public void Noop_writes_do_not_rewrite_hand_formatted_file_or_overwrite_timestamps()
    {
        var hash = StateStore.ComputeLaunchHash("npx", ["a"], @"C:\w");
        Directory.CreateDirectory(_dir);
        var handFormatted = $$"""
            {
              // hand-formatted state.json
              "version": 1,
              "shown": { "firstRun": "2026-11-14T20:00:00+00:00" },
              "panel": {
                  "width": { "value": 640, "over": "40%" },
                  "chatHeight": { "value": 500, "over": null },
                  "anchor": { "value": { "monitor": "active", "x": 0.300, "y": 0.800 } }
              },
              "grants": [
                  { "server": "files", "tool": "read_file", "launch": "{{hash}}", "at": "2026-11-14T20:00:00+00:00" }
              ],
              "lastWritten": { "settings.json": "sha1" },
              "upgrade": { "at": "2026-11-14T20:00:00+00:00", "backup": "backups\\u" },
            }
            """;
        File.WriteAllText(StatePath, handFormatted);
        var originalBytes = File.ReadAllBytes(StatePath);

        var store = CreateStore(Utc(23));

        Assert.True(store.SetLastWritten("settings.json", "sha1"));
        Assert.True(store.SetLastWritten("missing.json", null));
        Assert.True(store.AddGrant("files", "read_file", hash.ToUpperInvariant()));
        Assert.True(store.AddGrant("files", "read_file", hash, Utc(23)));
        Assert.True(store.MarkShown(ShownFlag.FirstRun));
        Assert.True(store.MarkShown(ShownFlag.FirstRun, Utc(23)));
        Assert.True(store.SetShown(ShownFlag.FirstRun, Utc(20)));
        Assert.True(store.SetShown(ShownFlag.SupportNote, null));
        Assert.True(store.SavePanelSpot(
            640, "40%", 500, null,
            new PanelAnchor { Monitor = "active", X = 0.3, Y = 0.8 }, null));
        Assert.True(store.SetUpgrade(new UpgradeRecord(Utc(20), @"backups\u")));

        Assert.Equal(originalBytes, File.ReadAllBytes(StatePath));
        Assert.Equal(Utc(20), CreateStore().GetShown(ShownFlag.FirstRun));
        Assert.Equal(Utc(20), Assert.Single(CreateStore().GetGrants()).At);
    }
}
