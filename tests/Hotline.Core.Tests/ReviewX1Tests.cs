using System.Diagnostics;
using Hotline.Core.Activation;
using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Platform;
using Hotline.Core.Processes;
using Hotline.Core.Settings;
using Hotline.Core.Theming;

namespace Hotline.Core.Tests;

/// <summary>Fixes from the full-codebase review of the core infrastructure and the CLI backends (Activation, Platform, Diagnostics, Processes, Theming, Backends root, Agy).</summary>
public sealed class ReviewX1Tests : IDisposable
{
    private readonly string _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    // ---- Platform ----

    [Theory]
    [InlineData("10.0.26100", 26100, 0)]
    [InlineData("10.0.26200.9457", 26200, 9457)]
    [InlineData("26200.9457", 26200, 9457)]
    [InlineData("26100", 26100, 0)]
    [InlineData("", 0, 0)]
    [InlineData("nonsense", 0, 0)]
    public void WindowsBuild_reads_three_part_and_odd_versions(string version, int build, int revision)
        => Assert.Equal(new WindowsBuild(build, revision), WindowsBuild.Parse(version));

    [Fact]
    public void WindowsBuild_of_null_is_zero() => Assert.Equal(new WindowsBuild(0, 0), WindowsBuild.Parse(null));

    [Fact]
    public void CommandResolver_survives_a_command_with_a_nul_character()
        => Assert.Null(CommandResolver.Find("np\0x", @"C:\work", @"C:\bin", ".EXE", _ => false));

    [Fact]
    public void CommandResolver_never_touches_a_network_path()
    {
        var touched = new List<string>();
        bool Exists(string p) { touched.Add(p); return false; }
        Assert.Null(CommandResolver.Find(@"\\evil\share\tool.exe", null, "", ".EXE", Exists));
        Assert.Null(CommandResolver.Find(@"\\evil\share\tool", null, "", ".EXE", Exists));
        Assert.Null(CommandResolver.Find("tool", @"\\evil\share", "", ".EXE", Exists));
        Assert.Null(CommandResolver.Find(".\\tool", @"\\evil\share", "", ".EXE", Exists));
        Assert.Null(CommandResolver.Find("tool", null, @"\\evil\share", ".EXE", Exists));
        Assert.Empty(touched);
    }

    // ---- Diagnostics ----

    [Fact]
    public void FileLog_with_a_bare_file_name_does_not_throw()
    {
        var name = $"hotline-x1-{Guid.NewGuid():N}.log";
        try
        {
            new FileLog(name).Info("hello");
            Assert.Contains("hello", File.ReadAllText(name));
        }
        finally { File.Delete(name); }
    }

    [Theory]
    [InlineData("MY_TOKEN=abcdef123456", "abcdef123456")]
    [InlineData("export OPENAI_API_KEY=abcdef123456", "abcdef123456")]
    [InlineData("Authorization: Basic dXNlcjpwYXNzd29yZA==", "dXNlcjpwYXNzd29yZA")]
    public void LogText_redacts_secrets_in_variable_names_and_basic_credentials(string text, string secret)
        => Assert.DoesNotContain(secret, LogText.Clean(text));

    // ---- Activation ----

    [Theory]
    [InlineData("hotline://settings?state=tap")]
    [InlineData("hotline://tray?state=Down")]
    [InlineData("hotline://open?state=up")]
    public void ParseUri_only_reads_state_from_the_key_host(string uri)
        => Assert.Null(ActivationParser.ParseUri(new Uri(uri)));

    [Fact]
    public void ParseUri_host_is_case_insensitive() => Assert.Equal(KeyEvent.Tap, ActivationParser.ParseUri(new Uri("hotline://KEY?state=tap")));

    [Fact]
    public void A_settings_link_with_a_state_opens_settings_and_is_not_a_key()
    {
        var plan = ActivationPlanner.Plan(new ActivationRequest(ActivationKind.Protocol, new Uri("hotline://settings?state=tap"), false));
        Assert.Null(plan.Key);
        Assert.True(plan.OpenSettings);
    }

    [Fact]
    public void Deduper_drops_the_late_copy_of_a_whole_press_and_release_from_the_other_channel()
    {
        var clock = new ManualTimeProvider();
        var d = new KeyEventDeduper(clock, TimeSpan.FromMilliseconds(1000));
        Assert.True(d.ShouldHandle(KeyEvent.HoldStart, KeySource.FastPath));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.True(d.ShouldHandle(KeyEvent.HoldStop, KeySource.FastPath));
        clock.Advance(TimeSpan.FromMilliseconds(300)); // the protocol launch arrives late
        Assert.False(d.ShouldHandle(KeyEvent.HoldStart, KeySource.Protocol));
        Assert.False(d.ShouldHandle(KeyEvent.HoldStop, KeySource.Protocol));
    }

    [Fact]
    public void Deduper_is_safe_to_call_from_several_threads()
    {
        var d = new KeyEventDeduper(TimeProvider.System, TimeSpan.FromMilliseconds(50));
        Parallel.For(0, 20_000, i => d.ShouldHandle((KeyEvent)(i % 3), i % 2 == 0 ? KeySource.FastPath : KeySource.Protocol));
    }

    [Fact]
    public void App_action_inputs_with_keys_that_differ_only_by_case_do_not_throw()
    {
        var inputs = new Dictionary<string, string> { ["text"] = "one", ["Text"] = "two" };
        var request = AppActionRequest.Parse(new Uri("hotline-action://ask"), inputs, null);
        Assert.NotNull(request);
        Assert.NotNull(request.Text);
    }

    [Fact]
    public void Image_path_check_never_throws_for_a_drive_that_is_not_there()
    {
        var unused = Enumerable.Range('A', 26).Select(c => (char)c).First(c => !Directory.Exists(c + @":\"));
        _ = AppActionRequest.IsSafeLocalImagePath(unused + @":\pic.png"); // an unmounted drive: not a crash
    }

    [Fact]
    public void Both_link_parsers_read_plus_as_a_space_and_percent_escapes()
    {
        var request = AppActionRequest.Parse(new Uri("hotline-action://ask?text=a+b%20c"), null, null);
        Assert.Equal("a b c", request!.Text);
        Assert.Equal(KeyEvent.Tap, ActivationParser.ParseUri(new Uri("hotline://key?x=1&state=ta%70")));
    }

    // ---- Theming ----

    [Fact]
    public void ThemeColor_TryParse_of_null_is_false() => Assert.False(ThemeColor.TryParse(null!, out _));

    [Fact]
    public void A_malformed_windows_accent_falls_back_to_the_built_in_one()
    {
        var windows = new WindowsColors("garbage", "garbage", null, true);
        var tokens = ThemeTokens.For(true, new WindowSettings { Accent = "windows" }, windows);
        Assert.Equal(ThemeTokens.Dark.Accent, tokens.Accent);
    }

    // ---- Processes ----

    private sealed class ScriptedReader(params Func<Memory<char>, CancellationToken, ValueTask<int>>[] steps) : TextReader
    {
        private int _step;
        public override ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken ct = default) => steps[_step++](buffer, ct);
    }

    [Fact]
    public async Task A_cancelled_read_does_not_lose_the_start_of_a_long_line()
    {
        var reader = new ScriptedReader(
            (b, _) => { "abc".CopyTo(b.Span); return new(3); },
            (_, ct) => throw new OperationCanceledException(ct),
            (b, _) => { "def\nnext\n".CopyTo(b.Span); return new(9); });
        var lines = new BoundedLineReader(reader, 100);
        await Assert.ThrowsAsync<OperationCanceledException>(() => lines.ReadLineAsync(default));
        Assert.Equal("abcdef", await lines.ReadLineAsync(default));
        Assert.Equal("next", await lines.ReadLineAsync(default));
    }

    [Fact]
    public async Task A_process_whose_start_hook_throws_is_not_left_running()
    {
        if (!OperatingSystem.IsWindows()) return;
        var pid = 0;
        var factory = new SystemLineProcessFactory(p => { pid = p.Id; throw new InvalidOperationException("hook failed"); });
        Assert.Throws<InvalidOperationException>(() => factory.Start("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep 60"], Path.GetTempPath()));
        Assert.NotEqual(0, pid);
        try
        {
            using var child = Process.GetProcessById(pid);
            Assert.True(child.WaitForExit(10_000), "the child was left running");
        }
        catch (ArgumentException) { } // already gone: what we want
    }

    // ---- Backends ----

    private static readonly IReadOnlyList<ModelFamily> Families = ModelFamilies.Group(ModelListParsers.Agy("""
        gemini-3.8-flash-high	Gemini 3.8 Flash (High)
        gemini-3.8-flash-medium	Gemini 3.8 Flash (Medium)
        gemini-3.8-flash-low	Gemini 3.8 Flash (Low)
        gemini-3.1-pro-high	Gemini 3.1 Pro (High)
        gemini-3.1-pro-low	Gemini 3.1 Pro (Low)
        """));

    [Theory]
    [InlineData(0, "High", "gemini-3.8-flash-high")]
    [InlineData(0, "xhigh", "gemini-3.8-flash-high")]
    [InlineData(0, "ultra", "gemini-3.8-flash-high")]
    [InlineData(1, "xhigh", "gemini-3.1-pro-high")]
    [InlineData(0, "bogus", "gemini-3.8-flash-medium")] // unknown: the middle one, like no choice
    public void Resolve_handles_case_and_levels_above_max(int family, string level, string expected)
        => Assert.Equal(expected, ModelFamilies.Resolve(Families[family], level));

    [Fact]
    public void A_connection_of_an_undefined_type_has_info_instead_of_throwing()
    {
        var p = new BackendProfile { Id = "x", Type = (BackendType)99, Name = "x" };
        Assert.NotNull(ConnectionTypes.Of(p.Type));
        _ = ConnectionTypes.TakesImages(p);
    }

    [Fact]
    public void Duplicate_copies_the_images_choice()
    {
        var chat = new ChatSettings();
        var source = ConnectionEditor.Add(chat, BackendType.Local);
        source.Images = true;
        Assert.True(ConnectionEditor.Duplicate(chat, source.Id).Images);
    }

    [Fact]
    public void Agy_permission_file_with_duplicate_keys_reads_as_no_rules()
        => Assert.Empty(AgyPermissions.Rules("""{"permissions":{"allow":["a"]},"permissions":{"allow":["b"]}}""", "allow"));

    [Fact]
    public void Resume_does_not_use_a_network_folder_from_a_hand_edited_record()
    {
        var command = CliHandoff.Resume(BackendType.ClaudeCode, "claude.exe", new CliSession("0b1c2d3e-0000-4000-8000-000000000000", @"\\evil\share"));
        Assert.Equal(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), command.WorkingDirectory);
    }

    // ---- Agy ----

    [Fact]
    public void Pruning_skips_a_folder_that_is_in_use()
    {
        var clock = new ManualTimeProvider();
        var ws = new AgyWorkspace(_dir, clock);
        ws.SaveImages("busy", [new Attachment("1", "a.png", AttachmentKind.Image, "image/png", [1])]);
        ws.SaveImages("free", [new Attachment("2", "a.png", AttachmentKind.Image, "image/png", [1])]);
        foreach (var name in new[] { "busy", "free" }) Directory.SetLastWriteTimeUtc(System.IO.Path.Combine(_dir, "attachments", name), clock.GetUtcNow().UtcDateTime.AddDays(-3));
        using var held = new FileStream(System.IO.Path.Combine(_dir, "attachments", "busy", "a.png"), FileMode.Open, FileAccess.Read, FileShare.None);
        Assert.Equal(1, ws.PruneAttachments(TimeSpan.FromDays(1)));
        Assert.True(Directory.Exists(System.IO.Path.Combine(_dir, "attachments", "busy")));
    }

    [Theory]
    [InlineData("con.png")]
    [InlineData("NUL.png")]
    [InlineData("com1.jpg")]
    public void Windows_device_names_are_not_used_as_attachment_names(string name)
    {
        var ws = new AgyWorkspace(_dir, new ManualTimeProvider());
        var paths = ws.SaveImages("m", [new Attachment("1", name, AttachmentKind.Image, "image/png", [1])]);
        Assert.True(File.Exists(System.IO.Path.Combine(_dir, paths[0])));
    }

    [Theory]
    [InlineData("""{"event":"result","result":null}""")]
    [InlineData("""{"event":5}""")]
    [InlineData("""{"event":"step_update","step_update":null}""")]
    [InlineData("""{"event":"step_update","step_update":{"step_type":"agent_response","text_delta":5}}""")]
    [InlineData("""{"event":"step_update","step_update":{"step_type":"agent_response","text_delta":"x","step_index":"one"}}""")]
    [InlineData("""{"event":"result","result":{"status":"SUCCESS","response":"","denied_actions":"no"}}""")]
    [InlineData("""{"event":"result","result":{"status":"SUCCESS","response":"","denied_actions":[5,null]}}""")]
    [InlineData("[1,2]")]
    public void The_agy_parser_skips_lines_with_unexpected_value_types(string line)
        => _ = new AgyTurnParser().Feed(line).ToList();

    [Fact]
    public void Denied_actions_of_odd_shape_still_end_the_turn_without_throwing()
    {
        var parser = new AgyTurnParser();
        _ = parser.Feed("""{"event":"result","result":{"status":"SUCCESS","response":"","denied_actions":[{"action":"rm"}]}}""").ToList();
        Assert.True(parser.Completed);
        Assert.Contains("rm", parser.Error);
    }
}
