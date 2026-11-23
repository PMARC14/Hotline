using System.Text;
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Diagnostics;
using Hotline.Core.Files;
using Hotline.Core.State;
using Hotline.Core.Tests.Files;
using Hotline.Core.Tools;

namespace Hotline.Core.Tests;

public sealed class ToolsTests : TempDirTest
{
    private string McpPath => Path.Combine(_dir, "mcp.json");
    private string StatePath => Path.Combine(_dir, "state.json");

    private void WriteMcp(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(McpPath, json);
    }

    // ---- mcp.json ------------------------------------------------------------------------------

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("\"allow\"")]
    [InlineData("""{ "files/write_file": "allow" }""")]
    public void Approvals_key_of_any_kind_is_ignored_with_a_warning(string approvals)
    {
        var json = $$"""{ "mcpServers": { "a": { "command": "x" } }, "approvals": {{approvals}} }""";
        WriteMcp(json);
        var config = McpConfig.Load(McpPath);
        Assert.Null(config.Error);
        Assert.Single(config.Warnings);
        Assert.Single(config.Servers);
        Assert.Equal(json, File.ReadAllText(McpPath));
    }

    [Fact]
    public void Parses_the_common_mcpServers_format_and_ignores_approvals()
    {
        WriteMcp("""
            {
              // pasted from another app
              "mcpServers": {
                "files": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Notes"], "env": { "X": "1" } },
                "off": { "command": "x.exe", "disabled": true }
              },
              "approvals": { "files/write_file": "deny", "files/*": "allow" },
            }
            """);
        var config = McpConfig.Load(McpPath);
        var files = config.Servers.Single(s => s.Name == "files");
        Assert.Equal("npx", files.Command);
        Assert.Equal(["-y", "@modelcontextprotocol/server-filesystem", @"C:\Notes"], files.Args);
        Assert.Equal("1", files.Env["X"]);
        Assert.True(config.Servers.Single(s => s.Name == "off").Disabled);
        Assert.Single(config.Warnings);
    }

    [Fact]
    public void Missing_file_is_not_created_and_broken_file_is_reported_not_overwritten()
    {
        var config = McpConfig.Load(McpPath);
        Assert.Null(config.Error);
        Assert.Empty(config.Servers);
        Assert.False(File.Exists(McpPath));

        WriteMcp("{ broken");
        var broken = McpConfig.Load(McpPath);
        Assert.NotNull(broken.Error);
        Assert.Equal("{ broken", File.ReadAllText(McpPath));
    }

    [Fact]
    public void Non_string_env_values_are_skipped_with_warning_never_logging_values()
    {
        var config = McpConfig.Parse("""
            {
              "mcpServers": {
                "a": {
                  "command": "x.exe",
                  "env": { "OK": "plain", "N": 4242, "T": true, "NIL": null, "OBJ": { "k": "SECRET_VAL" } }
                }
              }
            }
            """);
        Assert.Null(config.Error);
        var server = Assert.Single(config.Servers);
        Assert.Equal(["OK"], server.Env.Keys);
        Assert.Equal("plain", server.Env["OK"]);
        Assert.Equal(4, config.Warnings.Count);
        Assert.DoesNotContain(config.Warnings, w => w.Contains("4242") || w.Contains("SECRET_VAL"));
    }

    [Fact]
    public void Env_keys_differing_only_in_case_take_the_last_value()
    {
        var config = McpConfig.Parse("""{ "mcpServers": { "a": { "command": "x.exe", "env": { "PATH": "C:\\one", "Path": "C:\\two" } } } }""");
        Assert.Equal(@"C:\two", Assert.Single(config.Servers).Env["PATH"]);
    }

    [Theory]
    [InlineData("""{ "mcpServers": { "a": { "command": "x.exe" }, "a": { "command": "y.exe" } } }""")]
    [InlineData("""{ "mcpServers": { "a": { "command": "x.exe", "env": { "K": "1", "K": "2" } } } }""")]
    public void Duplicate_keys_make_the_file_unreadable_not_a_crash(string json)
    {
        WriteMcp(json);
        var config = McpConfig.Load(McpPath);
        Assert.NotNull(config.Error);
        Assert.Empty(config.Servers);
        Assert.Equal(json, File.ReadAllText(McpPath)); // left as it is
    }

    [Fact]
    public void Config_with_a_non_string_command_cwd_or_name_skips_only_that_part()
    {
        var config = McpConfig.Parse("""
            {
              "mcpServers": {
                "bad": { "command": true },
                "num": { "command": "tool", "cwd": 123, "displayName": [1] },
                "ok": { "command": "tool2", "cwd": "C:\\work", "displayName": "Okay" }
              }
            }
            """);
        Assert.Null(config.Error);
        Assert.Equal(["num", "ok"], config.Servers.Select(s => s.Name));
        Assert.Null(config.Servers[0].WorkingDirectory);
        Assert.Null(config.Servers[0].DisplayName);
        Assert.Equal("Okay", config.Servers[1].DisplayName);
    }

    // ---- policy --------------------------------------------------------------------------------

    [Theory]
    [InlineData(true, false, "tool", null, ToolApproval.Allow, false)]                   // read-only: no prompt, no Always allow
    [InlineData(false, false, "tool", null, ToolApproval.Ask, true)]                    // unruled + no grant: ask with Always allow
    [InlineData(false, true, "tool", null, ToolApproval.Allow, false)]                  // unruled + grant: allow
    [InlineData(true, true, "tool", "files/tool=deny", ToolApproval.Deny, false)]       // explicit deny wins over readOnly and grant
    [InlineData(false, false, "tool", "files/tool=allow", ToolApproval.Allow, false)]
    [InlineData(false, true, "tool", "files/tool=ask", ToolApproval.Ask, false)]        // ask rule overrides grant and hides Always allow
    [InlineData(false, true, "tool", "files/*=ask", ToolApproval.Ask, false)]           // wildcard ask rule overrides grant and hides Always allow
    [InlineData(false, false, "write", "files/*=allow,files/delete=deny", ToolApproval.Allow, false)]
    [InlineData(false, false, "delete", "files/*=allow,files/delete=deny", ToolApproval.Deny, false)]
    [InlineData(false, false, "write", "files/*=allow,files/write=ask", ToolApproval.Ask, false)] // specific ask beats wildcard allow
    [InlineData(false, false, "write", "files/*=deny,files/write=allow", ToolApproval.Deny, false)] // deny always wins
    public void Policy_evaluates_rules_grants_and_defaults(bool readOnly, bool hasGrant, string tool, string? rulesText, ToolApproval expected, bool expectedCanAlways)
    {
        var rules = ParseRules(rulesText);
        var result = ToolPolicy.Evaluate(rules, "files", tool, readOnly, hasGrant);
        Assert.Equal(expected, result.Approval);
        Assert.Equal(expectedCanAlways, result.CanAlwaysAllow);
        if (!hasGrant) Assert.Equal(expected, ToolPolicy.Decide(rules, "files", tool, readOnly));
    }

    [Fact]
    public void Policy_matches_rules_case_insensitively_even_from_ordinal_dictionaries()
    {
        var rules = new Dictionary<string, ToolApproval>(StringComparer.Ordinal)
        {
            ["Files/Write"] = ToolApproval.Allow,
            ["files/write"] = ToolApproval.Deny,
        };
        var result = ToolPolicy.Evaluate(rules, "FILES", "WRITE", readOnly: false, hasGrant: true);
        Assert.Equal(ToolApproval.Deny, result.Approval);
        Assert.False(result.CanAlwaysAllow);
    }

    [Theory]
    [InlineData("files/write", ToolApproval.Allow, "Files", "Write", false, ToolApproval.Ask, true, "files/write")]
    [InlineData("files/*", ToolApproval.Allow, "Files", "Write", false, ToolApproval.Ask, true, "files/*")]
    [InlineData("Files/Write", ToolApproval.Allow, "Files", "Write", false, ToolApproval.Allow, false, null)]
    [InlineData("Files/*", ToolApproval.Allow, "Files", "Write", false, ToolApproval.Allow, false, null)]
    [InlineData("FILES/WRITE", ToolApproval.Ask, "Files", "Write", false, ToolApproval.Ask, false, null)]
    [InlineData("FILES/*", ToolApproval.Ask, "Files", "Write", true, ToolApproval.Ask, false, null)]
    [InlineData("FILES/*", ToolApproval.Deny, "Files", "Write", true, ToolApproval.Deny, false, null)]
    public void Policy_allow_requires_exact_case_while_deny_and_ask_ignore_case(
        string ruleKey, ToolApproval ruleValue, string server, string tool, bool readOnly,
        ToolApproval expected, bool expectedCanAlways, string? expectedMismatch)
    {
        var rules = new Dictionary<string, ToolApproval>(StringComparer.Ordinal) { [ruleKey] = ruleValue };
        var result = ToolPolicy.Evaluate(rules, server, tool, readOnly);
        Assert.Equal(expected, result.Approval);
        Assert.Equal(expectedCanAlways, result.CanAlwaysAllow);
        Assert.Equal(expectedMismatch, result.CaseMismatchAllowRule);
    }

    [Fact]
    public void Unknown_approval_values_count_as_deny_with_a_warning()
    {
        var raw = new Dictionary<string, string>
        {
            ["a/x"] = "3",
            ["a/y"] = "allow, deny",
            ["a/z"] = "denied",
            ["a/ok"] = "  ALLOW  ",
            ["a/ask"] = "Ask",
        };
        var rules = ToolRules.Parse(raw, out var warnings);
        Assert.Equal(ToolApproval.Deny, rules["a/x"]);
        Assert.Equal(ToolApproval.Deny, rules["a/y"]);
        Assert.Equal(ToolApproval.Deny, rules["a/z"]);
        Assert.Equal(ToolApproval.Allow, rules["a/ok"]);
        Assert.Equal(ToolApproval.Ask, rules["a/ask"]);
        Assert.Equal(3, warnings.Count);
    }

    // ---- secrets -------------------------------------------------------------------------------

    [Theory]
    [InlineData("${hotline:secret:api_token.1-A}", true, true, "api_token.1-A")]
    [InlineData("${hotline:secret:a}\n", false, true, "")]
    [InlineData("${hotline:secret:a}\r\n", false, true, "")]
    [InlineData("${hotline:secret:}", false, true, "")]
    [InlineData("${hotline:secret:bad space}", false, true, "")]
    [InlineData("${hotline:secret:a}extra", false, true, "")]
    [InlineData("${hotline:other:x}", false, true, "")]
    [InlineData("plain-value", false, false, "")]
    [InlineData("prefix ${hotline:secret:a}", false, false, "")]
    public void Secret_token_parser_accepts_only_exact_token_and_flags_bad_hotline_prefixes(
        string value, bool valid, bool isHotlinePrefix, string expectedName)
    {
        var ok = McpSecrets.TryParseToken(value, out var name, out var flagged);
        Assert.Equal(valid, ok);
        Assert.Equal(isHotlinePrefix, flagged);
        Assert.Equal(expectedName, name);
    }

    [Fact]
    public void Scrub_replaces_resolved_secrets_and_long_env_values_longest_first()
    {
        var raw = new McpServerConfig("gh", "npx", [], new Dictionary<string, string>
        {
            ["SHORT_SEC"] = "${hotline:secret:short_s}",
            ["LONG_SEC"] = "${hotline:secret:long_s}",
            ["LONG_PLAIN"] = "12345678_plain",
            ["SHORT_PLAIN"] = "ok",
        });
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["SHORT_SEC"] = "xyz",
            ["LONG_SEC"] = "xyz_longer_secret",
            ["LONG_PLAIN"] = "12345678_plain",
            ["SHORT_PLAIN"] = "ok",
        };
        var scrubValues = McpSecrets.CollectScrubValues(raw, resolved);
        var scrubbed = McpSecrets.Scrub("failed with xyz_longer_secret, xyz, 12345678_plain, ok", scrubValues);
        Assert.Equal("failed with ***, ***, ***, ok", scrubbed);
    }

    [Fact]
    public void Resolve_env_reads_secrets_from_locker_and_never_exposes_values_in_errors()
    {
        var secrets = new InMemorySecretStore();
        secrets.Set("mcp:secret:github_pat", "SUPER_SECRET_VALUE");
        var okServer = new McpServerConfig("gh", "npx", [], new Dictionary<string, string>
        {
            ["TOKEN"] = "${hotline:secret:github_pat}",
            ["MODE"] = "readonly",
        });
        Assert.True(McpSecrets.TryResolveEnv(okServer, secrets, out var resolved, out var okErr));
        Assert.Null(okErr);
        Assert.Equal("SUPER_SECRET_VALUE", resolved["TOKEN"]);
        Assert.Equal("readonly", resolved["MODE"]);

        var missing = new McpServerConfig("gh", "npx", [], new Dictionary<string, string> { ["TOKEN"] = "${hotline:secret:missing_tok}" });
        Assert.False(McpSecrets.TryResolveEnv(missing, secrets, out _, out var missingErr));
        Assert.Contains("missing_tok", missingErr);

        var malformed = new McpServerConfig("gh", "npx", [], new Dictionary<string, string> { ["BAD"] = "${hotline:secret:LEAK_ME!}" });
        Assert.False(McpSecrets.TryResolveEnv(malformed, secrets, out _, out var badErr));
        Assert.Contains("BAD", badErr);
        Assert.DoesNotContain("LEAK_ME!", badErr);
    }

    [Fact]
    public void FindPlaintextEnv_skips_secret_tokens_and_never_includes_values()
    {
        var config = McpConfig.Parse("""
            {
              "mcpServers": {
                "a": { "command": "npx", "env": { "API_KEY": "raw-val", "TOKEN": "${hotline:secret:tok}", "BAD": "${hotline:oops}" } }
              }
            }
            """);
        var entries = McpSecrets.FindPlaintextEnv(config);
        var item = Assert.Single(entries);
        Assert.Equal("a", item.Server);
        Assert.Equal("API_KEY", item.EnvKey);
        Assert.False(string.IsNullOrEmpty(item.SuggestedSecretName));
    }

    [Fact]
    public void MoveToLocker_backs_up_replaces_with_token_preserves_unknown_keys_and_rolls_back_on_error()
    {
        var files = new FakeFileSystem();
        var dataDir = Path.Combine("C:", "fake", "data");
        var mcpPath = Path.Combine(dataDir, "mcp.json");
        files.CreateDirectory(dataDir);
        const string initial = """
            {
              "mcpServers": {
                "files": {
                  "command": "npx",
                  "customNote": 7,
                  "env": {
                    "OTHER": "keep",
                    "API_KEY": "TOP_SECRET"
                  }
                }
              },
              "futureField": "stay"
            }
            """;
        files.WriteDurable(mcpPath, Encoding.UTF8.GetBytes(initial));
        var secrets = new InMemorySecretStore();
        secrets.Set("mcp:secret:files_api_key", "OLD_SECRET");
        var state = new StateStore(StatePath);
        var now = new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc);

        // Backup failure leaves mcp.json untouched and rolls back Locker to OLD_SECRET.
        files.Fail = call => call.Operation == nameof(FakeFileSystem.CopyFile);
        var failed = McpSecrets.MoveToLocker(dataDir, "files", "API_KEY", "files_api_key", secrets, state, files, () => now);
        Assert.False(failed.Succeeded);
        Assert.Equal(SafeFileResult.BackupFailed, failed.FileResult);
        Assert.Equal("OLD_SECRET", secrets.Get("mcp:secret:files_api_key"));
        Assert.Equal(initial, Encoding.UTF8.GetString(files.Files[mcpPath]));

        // Succeeds: writes Locker, saves backup, preserves unknown keys, records lastWritten.
        files.Fail = null;
        var ok = McpSecrets.MoveToLocker(dataDir, "files", "API_KEY", "files_api_key", secrets, state, files, () => now);
        Assert.True(ok.Succeeded);
        Assert.Equal(SafeFileResult.Written, ok.FileResult);
        Assert.Equal("TOP_SECRET", secrets.Get("mcp:secret:files_api_key"));
        var backupFile = files.Files.Keys.Single(p => p.Contains("backups") && p.Contains("-save") && p.EndsWith("mcp.json"));
        Assert.Equal(initial, Encoding.UTF8.GetString(files.Files[backupFile]));

        var updated = Encoding.UTF8.GetString(files.Files[mcpPath]);
        Assert.DoesNotContain("TOP_SECRET", updated);
        Assert.Contains("${hotline:secret:files_api_key}", updated);
        Assert.Contains("customNote", updated);
        Assert.Contains("futureField", updated);
        Assert.Equal(SafeFile.Hash(files.Files[mcpPath]), state.GetLastWritten("mcp.json"));

        // Second move STILL creates a backup (never passes lastWrittenHash for plaintext-secret writes).
        var copiesBefore = files.Calls.Count(c => c.Operation == nameof(FakeFileSystem.CopyFile));
        var second = McpSecrets.MoveToLocker(dataDir, "files", "OTHER", "files_other", secrets, state, files, () => now.AddSeconds(5));
        Assert.True(second.Succeeded);
        Assert.Equal(McpMoveStatus.Moved, second.Status);
        Assert.Equal(copiesBefore + 1, files.Calls.Count(c => c.Operation == nameof(FakeFileSystem.CopyFile)));

        // Already-moved token or missing entry -> ChangedNothingMoved, no secrets written.
        var unchanged = McpSecrets.MoveToLocker(dataDir, "files", "API_KEY", "other_name", secrets, state, files, () => now);
        Assert.False(unchanged.Succeeded);
        Assert.Equal(McpMoveStatus.ChangedNothingMoved, unchanged.Status);
        Assert.NotEmpty(unchanged.Error!);
        Assert.Null(secrets.Get("mcp:secret:other_name"));

        // SafeFile WriteFailed + Locker rollback succeeds -> Failed with previous secret restored.
        files.WriteDurable(mcpPath, Encoding.UTF8.GetBytes("""{ "mcpServers": { "a": { "command": "x", "env": { "K": "NEW_VAL" } } } }"""));
        secrets.Set("mcp:secret:k_rollback", "PREV_VAL");
        files.Fail = call => call.Operation == nameof(FakeFileSystem.WriteDurable);
        var writeFailed = McpSecrets.MoveToLocker(dataDir, "a", "K", "k_rollback", secrets, state, files, () => now.AddSeconds(10));
        Assert.False(writeFailed.Succeeded);
        Assert.Equal(McpMoveStatus.Failed, writeFailed.Status);
        Assert.Equal(SafeFileResult.WriteFailed, writeFailed.FileResult);
        Assert.Equal("PREV_VAL", secrets.Get("mcp:secret:k_rollback"));
        files.Fail = null;

        // Initial Locker Set failure -> Failed with zero file writes/backups.
        var failingSecrets = new FailOnSetSecretStore { FailOnCall = 1 };
        var writesBefore = files.Writes.Count;
        var setFailed = McpSecrets.MoveToLocker(dataDir, "a", "K", "k_fail", failingSecrets, state, files, () => now.AddSeconds(15));
        Assert.False(setFailed.Succeeded);
        Assert.Equal(McpMoveStatus.Failed, setFailed.Status);
        Assert.Equal(writesBefore, files.Writes.Count);

        // WriteFailed AND rollback Set failure -> LockerUpdatedFileFailed naming secret and key, file unchanged.
        var rollbackFails = new FailOnSetSecretStore { FailOnCall = 2 };
        files.Fail = call => call.Operation == nameof(FakeFileSystem.WriteDurable);
        var bothFailed = McpSecrets.MoveToLocker(dataDir, "a", "K", "k_stuck", rollbackFails, state, files, () => now.AddSeconds(20));
        Assert.False(bothFailed.Succeeded);
        Assert.Equal(McpMoveStatus.LockerUpdatedFileFailed, bothFailed.Status);
        Assert.Contains("k_stuck", bothFailed.Error);
        Assert.Contains("mcp:secret:k_stuck", bothFailed.Error);
        Assert.Equal("NEW_VAL", rollbackFails.Get("mcp:secret:k_stuck"));
        Assert.Contains("NEW_VAL", Encoding.UTF8.GetString(files.Files[mcpPath]));
        files.Fail = null;

        // Newer version is refused and never writes Locker or file.
        files.WriteDurable(mcpPath, Encoding.UTF8.GetBytes("""{ "version": 99, "mcpServers": { "a": { "command": "x", "env": { "K": "V" } } } }"""));
        var newer = McpSecrets.MoveToLocker(dataDir, "a", "K", "k_sec", secrets, state, files, () => now);
        Assert.False(newer.Succeeded);
        Assert.Equal(McpMoveStatus.Failed, newer.Status);
        Assert.Equal(SafeFileResult.NewerVersion, newer.FileResult);
        Assert.Null(secrets.Get("mcp:secret:k_sec"));
    }

    private sealed class UnreadableSecretStore : ISecretStore
    {
        internal readonly InMemorySecretStore Values = new();
        public string? Get(string key) => null;
        public bool TryGet(string key, out string? value) { value = null; return false; }
        public void Set(string key, string? value) => Values.Set(key, value);
    }

    [Fact]
    public void MoveToLocker_never_touches_an_unreadable_entry_or_moves_an_empty_value()
    {
        var files = new FakeFileSystem();
        var dataDir = Path.Combine("C:", "fake", "data");
        var mcpPath = Path.Combine(dataDir, "mcp.json");
        files.CreateDirectory(dataDir);
        var text = """{ "mcpServers": { "a": { "command": "x", "env": { "K": "V", "E": "" } } } }""";
        files.WriteDurable(mcpPath, Encoding.UTF8.GetBytes(text));
        var state = new StateStore(StatePath);
        var unreadable = new UnreadableSecretStore();
        unreadable.Values.Set("mcp:secret:k", "EXISTING");
        // A backup failure would roll back to "previous": an unreadable previous must stop the move before anything changes.
        files.Fail = call => call.Operation == nameof(FakeFileSystem.CopyFile);
        var moved = McpSecrets.MoveToLocker(dataDir, "a", "K", "k", unreadable, state, files, () => DateTime.UtcNow);
        Assert.Equal(McpMoveStatus.Failed, moved.Status);
        Assert.Equal("EXISTING", unreadable.Values.Get("mcp:secret:k"));
        files.Fail = null;
        var secrets = new InMemorySecretStore();
        var empty = McpSecrets.MoveToLocker(dataDir, "a", "E", "e", secrets, state, files, () => DateTime.UtcNow);
        Assert.Equal(McpMoveStatus.ChangedNothingMoved, empty.Status);
        Assert.Equal(text, Encoding.UTF8.GetString(files.Files[mcpPath]));
    }

    private sealed class FailOnSetSecretStore : ISecretStore
    {
        private readonly Dictionary<string, string?> _store = new(StringComparer.Ordinal);
        private int _setCalls;
        public int? FailOnCall { get; init; }
        public string? Get(string key) => _store.GetValueOrDefault(key);
        public void Set(string key, string? value)
        {
            if (++_setCalls == FailOnCall) throw new InvalidOperationException("Credential Locker error");
            _store[key] = value;
        }
    }

    // ---- Windows on-device agent registry ------------------------------------------------------

    [Fact]
    public void Odr_list_becomes_windows_servers()
    {
        const string json = """
            [
              { "id": "FilesAgentMcpServer", "manifest": { "name": "file-mcp-server", "display_name": "File Explorer",
                  "server": { "mcp_config": { "command": "odr.exe", "args": ["mcp", "run", "--id", "FilesAgentMcpServer"] } } } },
              { "id": "broken" }
            ]
            """;
        var servers = OdrDiscovery.Parse(json);
        var files = Assert.Single(servers);
        Assert.Equal("windows-FilesAgentMcpServer", files.Name);
        Assert.Equal("odr.exe", files.Command);
        Assert.Equal(["mcp", "run", "--id", "FilesAgentMcpServer"], files.Args);
        Assert.Equal("File Explorer", files.DisplayName);
        Assert.Empty(OdrDiscovery.Parse("not json"));
    }

    // ---- tool names for the APIs ---------------------------------------------------------------

    [Fact]
    public void Api_tool_names_are_safe_unique_and_short()
    {
        var names = new HashSet<string>();
        var a = ToolNames.ForApi("windows-files", "search files!", names);
        var b = ToolNames.ForApi("windows-files", "search files?", names);
        Assert.Matches("^[A-Za-z0-9_-]{1,64}$", a);
        Assert.NotEqual(a, b);
        Assert.True(ToolNames.ForApi(new string('s', 80), new string('t', 80), names).Length <= 64);
    }

    // ---- tool host -----------------------------------------------------------------------------

    private McpToolHost Host(FakeMcpSession session, Func<ToolCallRequest, CancellationToken, Task<ToolDecision>> approve,
        string mcpJson, Func<IReadOnlyDictionary<string, ToolApproval>>? rules = null, StateStore? grants = null, ISecretStore? secrets = null)
    {
        WriteMcp(mcpJson);
        return new McpToolHost(
            () => McpConfig.Load(McpPath),
            () => [],
            rules ?? (() => new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase)),
            grants ?? new StateStore(StatePath),
            secrets ?? new InMemorySecretStore(),
            (_, _) => Task.FromResult<IMcpSession>(session),
            approve,
            new FileLog(Path.Combine(_dir, "h.log")));
    }

    private const string OneServer = """{ "mcpServers": { "files": { "command": "x" } } }""";

    [Theory]
    [InlineData(true, 0, false, "tool done", 1)]  // read-only tools run without asking
    [InlineData(false, 1, true, "declined", 0)]   // others ask; deny is an error result, not a call
    public async Task Tools_ask_unless_read_only_and_deny_is_an_error_result_not_a_call(bool readOnly, int expectedAsks, bool expectError, string expectedText, int expectedCalls)
    {
        var session = new FakeMcpSession(("tool", readOnly));
        var asked = 0;
        await using var host = Host(session, (_, _) => { asked++; return Task.FromResult(ToolDecision.Deny); }, OneServer);
        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        var result = await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(expectError, result.IsError);
        Assert.Contains(expectedText, result.Text);
        Assert.Equal(expectedAsks, asked);
        Assert.Equal(expectedCalls, session.Called.Count);
    }

    [Fact]
    public async Task Allow_always_adds_grant_to_state_store_without_modifying_mcp_json()
    {
        var session = new FakeMcpSession(("write", false));
        var asked = 0;
        var state = new StateStore(StatePath);
        await using var host = Host(session, (_, _) => { asked++; return Task.FromResult(ToolDecision.AllowAlways); }, OneServer, grants: state);
        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.Equal(1, asked);
        Assert.Equal(OneServer, File.ReadAllText(McpPath));
        Assert.True(state.IsGranted("files", "write", StateStore.ComputeLaunchHash("x")));
    }

    [Fact]
    public async Task Ask_rule_hides_always_allow_and_never_stores_a_grant()
    {
        var session = new FakeMcpSession(("write", false));
        var state = new StateStore(StatePath);
        bool? sawAllowAlways = null;
        var rules = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase) { ["files/write"] = ToolApproval.Ask };
        await using var host = Host(session, (req, _) => { sawAllowAlways = req.AllowAlways; return Task.FromResult(ToolDecision.AllowAlways); },
            OneServer, rules: () => rules, grants: state);
        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        await host.CallAsync(tools[0], JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken);
        Assert.False(sawAllowAlways);
        Assert.Empty(state.GetGrants());
    }

    [Fact]
    public async Task Denied_tools_are_not_offered_and_a_failing_server_does_not_break_others()
    {
        WriteMcp("""{ "mcpServers": { "good": { "command": "x" }, "bad": { "command": "y" } } }""");
        var good = new FakeMcpSession(("read", true), ("secret", true));
        var rules = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase) { ["good/secret"] = ToolApproval.Deny };
        await using var host = new McpToolHost(() => McpConfig.Load(McpPath), () => [], () => rules, new StateStore(StatePath), new InMemorySecretStore(),
            (server, _) => server.Name == "bad" ? throw new InvalidOperationException("won't start") : Task.FromResult<IMcpSession>(good),
            (_, _) => Task.FromResult(ToolDecision.Deny), new FileLog(Path.Combine(_dir, "h.log")));
        var tools = await host.GetToolsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["read"], tools.Select(t => t.Tool));
        Assert.Contains(host.Status, s => s.Name == "bad" && s.State == McpServerState.Failed && s.Error!.Contains("won't start"));
    }

    [Fact]
    public async Task Unknown_tool_name_is_an_error_result()
    {
        await using var host = Host(new FakeMcpSession(("read", true)), (_, _) => Task.FromResult(ToolDecision.Deny), OneServer);
        await host.GetToolsAsync(TestContext.Current.CancellationToken);
        var stranger = new ToolSpec("x__nope", "x", "nope", "", JsonDocument.Parse("{}").RootElement, true);
        Assert.True((await host.CallAsync(stranger, JsonDocument.Parse("{}").RootElement, TestContext.Current.CancellationToken)).IsError);
    }

    private static Dictionary<string, ToolApproval> ParseRules(string? text)
    {
        var rules = new Dictionary<string, ToolApproval>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in text?.Split(',', StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            var parts = item.Split('=');
            rules[parts[0]] = Enum.Parse<ToolApproval>(parts[1], ignoreCase: true);
        }
        return rules;
    }
}
