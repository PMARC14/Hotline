# Hotline Plan 2 — Chat View, Attachments & Capture, Antigravity Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Typing in the Copilot-key bar chats with Gemini via the user's installed Antigravity CLI (`agy`). The bar grows upward into a translucent chat panel, and the + menu attaches files and images, accepts paste and drag-drop, and captures the previous window or the screen.

**Architecture:** `Hotline.Core` gains the chat domain, all unit-tested:
- `ChatController`: conversation state, streaming, cancel, retry, new chat, history.
- Attachments: factory and tray with limits.
- `AgyBackend`: a persistent `agy` process speaking NDJSON `stream-json`, behind an injectable line-process interface.
- Settings for chat and backends, theme tokens, popup-growth geometry.

`Hotline.App` replaces the placeholder XAML with a WebView2 chat view (static HTML/JS/CSS with CSS-variable theming). A `ChatHost` bridges JSON messages between that page and the controller, and adds Windows-only pieces: file picker, GDI window/screen capture, PNG encoding, and a job object so `agy` dies with Hotline.

**Tech Stack:**
- .NET 10 / C#, WinUI 3 (Windows App SDK 2.5.1), WebView2 (part of the Windows App SDK, so no new package).
- Web: markdown-it 14.1.0, highlight.js 11.11.1, vendored.
- Tests: xUnit v3 for Core; `node --test` (Node 24) for the web reducer.

**Spec:** `docs/superpowers/specs/2026-09-30-hotline-design.md` (milestone 2, with the backend order changed by the user on
2026-10-02: Gemini API delayed, so agy is the first live backend). Context: `docs/superpowers/notes/2026-10-01-resume-here.md`.

## Global Constraints

- **Backend:** agy is the only live backend in this plan; it's the default. The Gemini API, OpenAI-compatible and Claude Code backends are Plan 3.
  Settings already model them (`BackendType`); the factory returns null for them, shown as "isn't available yet".
- **agy invocation:**
  - exact args `--input-format stream-json --output-format stream-json -p= --agent hotline` (+ optional `--model X`, + `ExtraArgs`)
  - working directory `<LocalState>\agy-workspace`
  - stdin is UTF-8 **without BOM**
  - each turn is one line: `{"event":"user","message":{"role":"user","content":"<text>"}}`; content is text only
- **agy safety:** **Never** pass `--dangerously-skip-permissions`. Verified on 2026-10-02 that the agent's `tools:` list does not restrict it; with skipped permissions it read files outside the workspace.
- **agy agent file:** `<workspace>\.agents\agents\hotline.md`. It must **not** contain `excludeDefaultComponents` (that drops the default permissions, so workspace reads get denied).
- **agy attachments:** images are written to `<workspace>\attachments\<messageId>\` and referenced by relative path. Text files are inlined in the prompt.
- **agy output events:**
  - `init`, then `step_update`; text arrives where `step_type == "agent_response"` and `text_delta` is set
  - a new `step_index` for agent_response **restarts** the answer (emit a reset)
  - `result{status, response, error?, denied_actions?}` ends the turn; `response` is authoritative
- **Theming:** the web UI uses **only** `--hl-*` CSS variables for colors, radius and font; values come from Core `ThemeTokens`. The page background is transparent, so the acrylic backdrop shows.
- **Growth:** the popup grows upward, keeping its bottom edge, from the bar height (`window.height`, default 120) up to `chat.maxHeight` (default 560) DIPs.
- **Data folders:** the WebView2 user data folder is `<LocalState>\WebView2`; the default location under WindowsApps is read-only.
- **Dependencies:** no new NuGet packages. Commits are authored by pmarc14 <16502495+PMARC14@users.noreply.github.com> with the Co-Authored-By trailer.
- **Test commands:** `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj` (all tasks), `node --test tests/web` (web tasks),
  `powershell -File tests\smoke\smoke.ps1 -Install` (app tasks).

## Review Focus

1. **Starting a new chat (Ctrl+N or the key action) while an answer is streaming** must not leak the old partial reply into the new conversation. Pinned by `ChatControllerTests.New_chat_during_streaming_does_not_leak_into_new_conversation` (Task 2).
2. **Cancelling mid-answer and then asking a follow-up** should keep the context. Cancelling kills agy, so the next turn must replay the conversation. Pinned by `AgyBackendTests.Cancel_kills_process_and_next_turn_replays_context` (Task 5).
3. **agy not installed, not signed in, workspace not trusted, or crashing mid-turn** should each show a specific, actionable error, never hang. Pinned by `AgyBackendTests.Missing_exe_is_NotInstalled`, `Process_exit_mid_turn_fails_with_stderr_tail`, and `AgyErrorsTests` (Tasks 4–5).
4. **Attaching a 50 MB photo, a binary file, or an 11th file** should be rejected with a clear message while the rest keep working. Pinned by `AttachmentFactoryTests` and `AttachmentTrayTests` (Task 1).
5. **Opening the file picker or capturing a window** must not let the popup hide-on-blur and lose the attachment flow. This is App-layer: pinned by the manual check in Task 8 step 6 and by `PopupWindow.Modal()` covering every dialog or capture path.

---

## File Structure

```
src/Hotline.Core/
  Chat/ChatModels.cs          roles, Attachment, ChatMessage, ChatDelta, BackendException, IChatBackend, Ids
  Chat/Attachments.cs         AttachmentLimits, AttachmentRejectedException, AttachmentFactory, AttachmentTray
  Chat/ChatEvents.cs          ChatEvent records
  Chat/ChatController.cs      conversation + streaming + cancel/retry/new chat
  Chat/HistoryStore.cs        JSONL history (metadata only) + pruning
  Processes/LineProcess.cs    ILineProcess(+Factory), SystemLineProcess(+Factory)
  Backends/Agy/AgyProtocol.cs UserLine, ComposePrompt, BuildArgs, AgyLocator, AgyErrors
  Backends/Agy/AgyTurnParser.cs  NDJSON events → ChatDelta + completion/error
  Backends/Agy/AgyWorkspace.cs   workspace dir, agent file, attachment files, pruning
  Backends/Agy/AgyBackend.cs     IChatBackend over a persistent agy process
  Backends/BackendCatalog.cs     BackendFactory + BackendCache
  Settings/HotlineSettings.cs    (+ ChatSettings, BackendProfile, BackendType)
  Settings/SettingsStore.cs      (+ chat normalization)
  Theming/ThemeTokens.cs         tokens → CSS custom properties
  Windowing/PopupGeometry.cs     (+ GrowUp)   Windowing/ImageMath.cs  FitWithin
tests/Hotline.Core.Tests/        one *Tests.cs per Core file above (+ FakeLineProcess.cs)
tests/web/chat-core.test.mjs     reducer tests
src/Hotline.App/
  Web/index.html, Web/chat.css, Web/chat-core.js, Web/chat.js, Web/vendor/*   chat view
  Chat/ChatHost.cs               WebView2 init + bridge + attachments/capture orchestration
  Capture/ScreenCapture.cs       GDI grab of window/monitor rect
  Capture/ImageProcessor.cs      decode/scale/encode PNG, thumbnails
  Interop/ChildProcessJob.cs     kill-on-close job for agy
  Interop/Native.cs              (+ GDI/DWM/job imports)
  PopupWindow.xaml(.cs)          WebView2 content, GrowUp, Modal(), events
  ActivationRouter.cs, App.xaml.cs  wiring
```

---

### Task 1: Chat models and attachments (Core)

**Files:**
- Create: `src/Hotline.Core/Chat/ChatModels.cs`, `src/Hotline.Core/Chat/Attachments.cs`
- Test: `tests/Hotline.Core.Tests/AttachmentFactoryTests.cs`, `tests/Hotline.Core.Tests/AttachmentTrayTests.cs`

**Interfaces:**
- Produces (namespace `Hotline.Core.Chat`):
  - `enum ChatRole { User, Assistant }`, `enum AttachmentKind { Image, Text }`
  - `sealed record Attachment(string Id, string Name, AttachmentKind Kind, string MimeType, byte[] Data)` with `string AsText()`
  - `sealed record ChatMessage(string Id, ChatRole Role, string Text, IReadOnlyList<Attachment> Attachments, DateTimeOffset At, string? BackendId = null)`
  - `readonly record struct ChatDelta(string Text, bool ResetBefore = false)`
  - `enum BackendErrorKind { NotConfigured, NotInstalled, NotLoggedIn, Unauthorized, RateLimited, ServerDown, Unsupported, Failed }`
  - `sealed class BackendException(BackendErrorKind kind, string message, Exception? inner = null)` with `Kind`
  - `sealed record BackendCapabilities(bool Images, bool TextFiles)`
  - `interface IChatBackend : IAsyncDisposable { string Id; string DisplayName; BackendCapabilities Capabilities; IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, CancellationToken ct); }`
  - `static class Ids { static string New(); }`
  - `sealed record AttachmentLimits(int MaxCount = 10, long MaxImageBytes = 20 MiB, long MaxTextBytes = 200 KiB)`
  - `sealed class AttachmentRejectedException(string reason) : Exception`
  - `static Attachment AttachmentFactory.FromBytes(string name, string? mimeType, byte[] data, AttachmentLimits limits)`
  - `sealed class AttachmentTray(AttachmentLimits limits)` with `Items`, `Add(Attachment)`, `bool Remove(string id)`, `IReadOnlyList<Attachment> TakeAll()`

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/AttachmentFactoryTests.cs`:
```csharp
using System.Text;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class AttachmentFactoryTests
{
    private static readonly AttachmentLimits Limits = new();

    [Theory]
    [InlineData("shot.png", null, "image/png")]
    [InlineData("photo.JPG", null, "image/jpeg")]
    [InlineData("pasted", "image/webp", "image/webp")]
    public void Images_are_recognised_by_extension_or_mime(string name, string? mime, string expectedMime)
    {
        var a = AttachmentFactory.FromBytes(name, mime, [1, 2, 3], Limits);
        Assert.Equal(AttachmentKind.Image, a.Kind);
        Assert.Equal(expectedMime, a.MimeType);
        Assert.Equal(name, a.Name);
        Assert.False(string.IsNullOrEmpty(a.Id));
    }

    [Theory]
    [InlineData("notes.md", null)]
    [InlineData("Program.cs", null)]
    [InlineData("data", "text/csv")]
    public void Utf8_text_files_are_accepted(string name, string? mime)
    {
        var a = AttachmentFactory.FromBytes(name, mime, Encoding.UTF8.GetBytes("héllo"), Limits);
        Assert.Equal(AttachmentKind.Text, a.Kind);
        Assert.Equal("héllo", a.AsText());
    }

    [Fact]
    public void Oversized_image_is_rejected_with_reason()
    {
        var ex = Assert.Throws<AttachmentRejectedException>(() =>
            AttachmentFactory.FromBytes("big.png", null, new byte[21 * 1024 * 1024], Limits));
        Assert.Contains("big.png", ex.Message);
        Assert.Contains("20 MB", ex.Message);
    }

    [Fact]
    public void Oversized_text_is_rejected()
        => Assert.Throws<AttachmentRejectedException>(() =>
            AttachmentFactory.FromBytes("huge.log", null, new byte[201 * 1024], Limits));

    [Fact]
    public void Non_utf8_text_is_rejected()
        => Assert.Throws<AttachmentRejectedException>(() =>
            AttachmentFactory.FromBytes("bad.txt", null, [0xC3, 0x28, 0xFF], Limits));

    [Theory]
    [InlineData("setup.exe", null)]
    [InlineData("archive.zip", "application/zip")]
    [InlineData("noext", null)]
    public void Unsupported_types_are_rejected(string name, string? mime)
    {
        var ex = Assert.Throws<AttachmentRejectedException>(() => AttachmentFactory.FromBytes(name, mime, [1], Limits));
        Assert.Contains("unsupported", ex.Message);
    }
}
```

`tests/Hotline.Core.Tests/AttachmentTrayTests.cs`:
```csharp
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class AttachmentTrayTests
{
    private static Attachment Img(string name) => new(Ids.New(), name, AttachmentKind.Image, "image/png", [1]);

    [Fact]
    public void Add_remove_and_take_all()
    {
        var tray = new AttachmentTray(new AttachmentLimits());
        var a = Img("a.png"); var b = Img("b.png");
        tray.Add(a); tray.Add(b);
        Assert.True(tray.Remove(a.Id));
        Assert.False(tray.Remove("missing"));
        var taken = tray.TakeAll();
        Assert.Equal([b], taken);
        Assert.Empty(tray.Items);
    }

    [Fact]
    public void Eleventh_attachment_is_rejected_and_tray_unchanged()
    {
        var tray = new AttachmentTray(new AttachmentLimits(MaxCount: 10));
        for (var i = 0; i < 10; i++) tray.Add(Img($"{i}.png"));
        var ex = Assert.Throws<AttachmentRejectedException>(() => tray.Add(Img("11.png")));
        Assert.Contains("10", ex.Message);
        Assert.Equal(10, tray.Items.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with `CS0234: The type or namespace name 'Chat' does not exist in the namespace 'Hotline.Core'`.

- [ ] **Step 3: Write minimal implementation**

`src/Hotline.Core/Chat/ChatModels.cs`:
```csharp
using System.Text;

namespace Hotline.Core.Chat;

public enum ChatRole { User, Assistant }

public enum AttachmentKind { Image, Text }

public sealed record Attachment(string Id, string Name, AttachmentKind Kind, string MimeType, byte[] Data)
{
    public string AsText() => Encoding.UTF8.GetString(Data);
}

public sealed record ChatMessage(
    string Id, ChatRole Role, string Text, IReadOnlyList<Attachment> Attachments, DateTimeOffset At, string? BackendId = null);

/// <summary>A streamed piece of an answer. <paramref name="ResetBefore"/>: discard what was shown so far (the backend restarted its answer).</summary>
public readonly record struct ChatDelta(string Text, bool ResetBefore = false);

public enum BackendErrorKind { NotConfigured, NotInstalled, NotLoggedIn, Unauthorized, RateLimited, ServerDown, Unsupported, Failed }

public sealed class BackendException(BackendErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public BackendErrorKind Kind { get; } = kind;
}

public sealed record BackendCapabilities(bool Images, bool TextFiles);

public interface IChatBackend : IAsyncDisposable
{
    string Id { get; }
    string DisplayName { get; }
    BackendCapabilities Capabilities { get; }

    /// <summary>Streams the reply to the last message of <paramref name="conversation"/> (always a user message).</summary>
    IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, CancellationToken ct);
}

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N")[..12];
}
```

`src/Hotline.Core/Chat/Attachments.cs`:
```csharp
using System.Text;

namespace Hotline.Core.Chat;

public sealed record AttachmentLimits(int MaxCount = 10, long MaxImageBytes = 20 * 1024 * 1024, long MaxTextBytes = 200 * 1024);

public sealed class AttachmentRejectedException(string reason) : Exception(reason);

public static class AttachmentFactory
{
    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".bmp"] = "image/bmp",
    };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".json", ".jsonc", ".xml", ".yaml", ".yml", ".toml", ".ini", ".csv", ".tsv", ".log",
        ".cs", ".csproj", ".sln", ".slnx", ".py", ".js", ".mjs", ".ts", ".tsx", ".jsx", ".html", ".htm", ".css", ".scss",
        ".sql", ".ps1", ".psm1", ".sh", ".bat", ".cmd", ".c", ".h", ".cpp", ".hpp", ".java", ".kt", ".go", ".rs", ".rb",
        ".php", ".swift", ".lua", ".r", ".tex",
    };

    public static Attachment FromBytes(string name, string? mimeType, byte[] data, AttachmentLimits limits)
    {
        var ext = Path.GetExtension(name);
        var isImageMime = mimeType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ?? false;
        if (ImageTypes.TryGetValue(ext, out var imageMime) || isImageMime)
        {
            if (data.LongLength > limits.MaxImageBytes)
                throw new AttachmentRejectedException($"{name} is larger than {limits.MaxImageBytes / (1024 * 1024)} MB.");
            return new Attachment(Ids.New(), name, AttachmentKind.Image, imageMime ?? mimeType!, data);
        }

        var isTextMime = mimeType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ?? false;
        if (TextExtensions.Contains(ext) || isTextMime)
        {
            if (data.LongLength > limits.MaxTextBytes)
                throw new AttachmentRejectedException($"{name} is larger than {limits.MaxTextBytes / 1024} KB.");
            if (!IsUtf8(data))
                throw new AttachmentRejectedException($"{name} isn't UTF-8 text.");
            return new Attachment(Ids.New(), name, AttachmentKind.Text, "text/plain", data);
        }

        throw new AttachmentRejectedException($"{name}: unsupported file type.");
    }

    private static bool IsUtf8(byte[] data)
    {
        try { new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(data); return true; }
        catch (DecoderFallbackException) { return false; }
    }
}

public sealed class AttachmentTray(AttachmentLimits limits)
{
    private readonly List<Attachment> _items = [];

    public IReadOnlyList<Attachment> Items => _items;

    public void Add(Attachment attachment)
    {
        if (_items.Count >= limits.MaxCount)
            throw new AttachmentRejectedException($"You can attach up to {limits.MaxCount} files.");
        _items.Add(attachment);
    }

    public bool Remove(string id) => _items.RemoveAll(a => a.Id == id) > 0;

    public IReadOnlyList<Attachment> TakeAll()
    {
        var all = _items.ToList();
        _items.Clear();
        return all;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0` (previous 91 + 16 new).

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): chat models, attachment factory and tray" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: ChatController, events and history (Core)

**Files:**
- Create: `src/Hotline.Core/Chat/ChatEvents.cs`, `src/Hotline.Core/Chat/ChatController.cs`, `src/Hotline.Core/Chat/HistoryStore.cs`
- Test: `tests/Hotline.Core.Tests/FakeBackend.cs`, `tests/Hotline.Core.Tests/ChatControllerTests.cs`, `tests/Hotline.Core.Tests/HistoryStoreTests.cs`

**Interfaces:**
- Consumes: Task 1 types; `FileLog` (Plan 1).
- Produces:
  - Events: `abstract record ChatEvent`; `UserMessageAdded(ChatMessage Message)`, `AssistantStarted(string Id, string BackendName)`,
    `AssistantDelta(string Id, string Text, bool Replace)`, `AssistantCompleted(string Id)`, `AssistantCancelled(string Id)`,
    `AssistantFailed(string Id, BackendErrorKind Kind, string Message)`, `ConversationReset()`
  - `sealed class ChatController(Func<string, IChatBackend?> resolveBackend, HistoryStore? history, TimeProvider clock, FileLog log)` with:
    - `event Action<ChatEvent>? Event`; properties `string ConversationId`, `string BackendId`, `IReadOnlyList<ChatMessage> Messages`, `bool IsBusy`
    - `bool CanAccept(IReadOnlyList<Attachment>, out string? reason)`
    - `Task SendAsync(string text, IReadOnlyList<Attachment> attachments)`, `Task RetryAsync()`, `void Cancel()`, `void NewChat()`
  - `sealed class HistoryStore(string directory, TimeProvider clock)` with:
    - `void Append(string conversationId, ChatMessage m)`, `IReadOnlyList<HistoryStore.Entry> Load(string conversationId)`, `int Prune(int retentionDays)`
    - nested records `Entry(Id, Role, Text, At, BackendId, Attachments)` and `AttachmentInfo(Name, Kind, MimeType)`

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/FakeBackend.cs`:
```csharp
using System.Runtime.CompilerServices;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

/// <summary>Scriptable backend: yields the given deltas; optionally throws or waits for a gate.</summary>
public sealed class FakeBackend(params ChatDelta[] deltas) : IChatBackend
{
    public string Id => "fake";
    public string DisplayName => "Fake";
    public BackendCapabilities Capabilities { get; init; } = new(Images: true, TextFiles: true);
    public Exception? Throw { get; init; }
    public TaskCompletionSource? Gate { get; init; }
    public List<IReadOnlyList<ChatMessage>> Calls { get; } = [];

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        Calls.Add(conversation.ToList());
        foreach (var d in deltas)
        {
            yield return d;
            if (Gate is not null) await Gate.Task.WaitAsync(ct);
        }
        if (Throw is not null) throw Throw;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

`tests/Hotline.Core.Tests/ChatControllerTests.cs`:
```csharp
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

public sealed class ChatControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly List<ChatEvent> _events = [];
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private ChatController New(IChatBackend? backend, HistoryStore? history = null)
    {
        var c = new ChatController(id => id == "fake" ? backend : null, history, new ManualTimeProvider(), new FileLog(Path.Combine(_dir, "h.log")))
        { BackendId = "fake" };
        c.Event += _events.Add;
        return c;
    }

    [Fact]
    public async Task Send_streams_deltas_and_records_both_messages()
    {
        var c = New(new FakeBackend(new ChatDelta("Hel"), new ChatDelta("lo")));
        await c.SendAsync("  hi  ", []);

        Assert.Collection(_events,
            e => Assert.Equal("hi", Assert.IsType<UserMessageAdded>(e).Message.Text),
            e => Assert.Equal("Fake", Assert.IsType<AssistantStarted>(e).BackendName),
            e => Assert.Equal("Hel", Assert.IsType<AssistantDelta>(e).Text),
            e => Assert.Equal("lo", Assert.IsType<AssistantDelta>(e).Text),
            e => Assert.IsType<AssistantCompleted>(e));
        Assert.Equal([ChatRole.User, ChatRole.Assistant], c.Messages.Select(m => m.Role));
        Assert.Equal("Hello", c.Messages[1].Text);
        Assert.Equal("fake", c.Messages[1].BackendId);
        Assert.False(c.IsBusy);
    }

    [Fact]
    public async Task Reset_delta_replaces_text()
    {
        var c = New(new FakeBackend(new ChatDelta("draft"), new ChatDelta("final", ResetBefore: true)));
        await c.SendAsync("q", []);
        Assert.Contains(_events, e => e is AssistantDelta { Text: "final", Replace: true });
        Assert.Equal("final", c.Messages[1].Text);
    }

    [Fact]
    public async Task Empty_send_does_nothing()
    {
        var c = New(new FakeBackend(new ChatDelta("x")));
        await c.SendAsync("   ", []);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task Backend_failure_reports_kind_and_drops_user_message_from_context()
    {
        var c = New(new FakeBackend { Throw = new BackendException(BackendErrorKind.NotLoggedIn, "sign in") });
        await c.SendAsync("q", []);
        var failed = Assert.IsType<AssistantFailed>(_events[^1]);
        Assert.Equal(BackendErrorKind.NotLoggedIn, failed.Kind);
        Assert.Empty(c.Messages);
    }

    [Fact]
    public async Task Retry_resends_last_failed_message_without_duplicating_it()
    {
        var calls = 0;
        IChatBackend backend = new FakeBackend { Throw = new BackendException(BackendErrorKind.Failed, "boom") };
        var c = new ChatController(_ => calls++ == 0 ? backend : new FakeBackend(new ChatDelta("ok")), null,
            new ManualTimeProvider(), new FileLog(Path.Combine(_dir, "h.log"))) { BackendId = "fake" };
        c.Event += _events.Add;
        await c.SendAsync("q", []);
        _events.Clear();
        await c.RetryAsync();
        Assert.DoesNotContain(_events, e => e is UserMessageAdded);
        Assert.Equal("q", c.Messages[0].Text);
        Assert.Equal("ok", c.Messages[1].Text);
    }

    [Fact]
    public async Task Unknown_backend_fails_with_not_configured()
    {
        var c = New(null);
        await c.SendAsync("q", []);
        Assert.Equal(BackendErrorKind.NotConfigured, Assert.IsType<AssistantFailed>(_events[^1]).Kind);
    }

    [Fact]
    public async Task Cancel_keeps_partial_reply()
    {
        var gate = new TaskCompletionSource();
        var c = New(new FakeBackend(new ChatDelta("part"), new ChatDelta("never")) { Gate = gate });
        var send = c.SendAsync("q", []);
        c.Cancel();
        await send;
        Assert.IsType<AssistantCancelled>(_events[^1]);
        Assert.Equal("part", c.Messages[1].Text);
    }

    [Fact]
    public async Task Second_send_while_busy_is_ignored()
    {
        var gate = new TaskCompletionSource();
        var c = New(new FakeBackend(new ChatDelta("a"), new ChatDelta("b")) { Gate = gate });
        var first = c.SendAsync("one", []);
        await c.SendAsync("two", []);
        gate.SetResult();
        await first;
        Assert.Single(_events.OfType<UserMessageAdded>());
    }

    [Fact]
    public async Task New_chat_during_streaming_does_not_leak_into_new_conversation()
    {
        var gate = new TaskCompletionSource();
        var c = New(new FakeBackend(new ChatDelta("old"), new ChatDelta("more")) { Gate = gate });
        var send = c.SendAsync("q", []);
        var oldId = c.ConversationId;
        c.NewChat();
        await send;
        Assert.NotEqual(oldId, c.ConversationId);
        Assert.Empty(c.Messages);
        Assert.Contains(_events, e => e is ConversationReset);
    }

    [Fact]
    public void Images_rejected_when_backend_cannot_read_them()
    {
        var c = New(new FakeBackend { Capabilities = new(Images: false, TextFiles: true) });
        var ok = c.CanAccept([new Attachment("1", "a.png", AttachmentKind.Image, "image/png", [1])], out var reason);
        Assert.False(ok);
        Assert.Contains("images", reason);
    }

    [Fact]
    public async Task Completed_turn_is_written_to_history()
    {
        var history = new HistoryStore(Path.Combine(_dir, "history"), new ManualTimeProvider());
        var c = New(new FakeBackend(new ChatDelta("answer")), history);
        await c.SendAsync("question", []);
        Assert.Equal(["question", "answer"], history.Load(c.ConversationId).Select(e => e.Text));
    }
}
```

`tests/Hotline.Core.Tests/HistoryStoreTests.cs`:
```csharp
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public sealed class HistoryStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _clock = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Round_trips_messages_with_attachment_metadata_but_no_bytes()
    {
        var store = new HistoryStore(_dir, _clock);
        var att = new Attachment("a1", "shot.png", AttachmentKind.Image, "image/png", new byte[5000]);
        store.Append("c1", new ChatMessage("m1", ChatRole.User, "look", [att], _clock.GetUtcNow()));
        store.Append("c1", new ChatMessage("m2", ChatRole.Assistant, "nice", [], _clock.GetUtcNow(), "agy"));

        var entries = store.Load("c1");

        Assert.Equal(["look", "nice"], entries.Select(e => e.Text));
        Assert.Equal("shot.png", entries[0].Attachments.Single().Name);
        Assert.Equal("agy", entries[1].BackendId);
        Assert.True(new FileInfo(store.PathFor("c1")).Length < 2000);
    }

    [Fact]
    public void Missing_conversation_loads_empty()
        => Assert.Empty(new HistoryStore(_dir, _clock).Load("nope"));

    [Fact]
    public void Prune_deletes_files_older_than_retention()
    {
        var store = new HistoryStore(_dir, _clock);
        store.Append("old", new ChatMessage("m", ChatRole.User, "x", [], _clock.GetUtcNow()));
        store.Append("new", new ChatMessage("m", ChatRole.User, "y", [], _clock.GetUtcNow()));
        File.SetLastWriteTimeUtc(store.PathFor("old"), _clock.GetUtcNow().UtcDateTime.AddDays(-40));

        Assert.Equal(1, store.Prune(retentionDays: 30));
        Assert.False(File.Exists(store.PathFor("old")));
        Assert.True(File.Exists(store.PathFor("new")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with CS0246 for `ChatController`, `HistoryStore`, `UserMessageAdded`.

- [ ] **Step 3: Write minimal implementation**

`src/Hotline.Core/Chat/ChatEvents.cs`:
```csharp
namespace Hotline.Core.Chat;

public abstract record ChatEvent;
public sealed record UserMessageAdded(ChatMessage Message) : ChatEvent;
public sealed record AssistantStarted(string Id, string BackendName) : ChatEvent;
/// <summary>Replace = the text replaces what was shown for this answer (backend restarted it).</summary>
public sealed record AssistantDelta(string Id, string Text, bool Replace) : ChatEvent;
public sealed record AssistantCompleted(string Id) : ChatEvent;
public sealed record AssistantCancelled(string Id) : ChatEvent;
public sealed record AssistantFailed(string Id, BackendErrorKind Kind, string Message) : ChatEvent;
public sealed record ConversationReset : ChatEvent;
```

`src/Hotline.Core/Chat/HistoryStore.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotline.Core.Chat;

/// <summary>One JSONL file per conversation. Attachments are recorded by name/kind only, never their bytes.</summary>
public sealed class HistoryStore(string directory, TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public sealed record AttachmentInfo(string Name, AttachmentKind Kind, string MimeType);
    public sealed record Entry(string Id, ChatRole Role, string Text, DateTimeOffset At, string? BackendId, IReadOnlyList<AttachmentInfo> Attachments);

    public string PathFor(string conversationId) => Path.Combine(directory, $"{conversationId}.jsonl");

    public void Append(string conversationId, ChatMessage m)
    {
        Directory.CreateDirectory(directory);
        var entry = new Entry(m.Id, m.Role, m.Text, m.At, m.BackendId,
            m.Attachments.Select(a => new AttachmentInfo(a.Name, a.Kind, a.MimeType)).ToList());
        File.AppendAllText(PathFor(conversationId), JsonSerializer.Serialize(entry, Json) + "\n");
    }

    public IReadOnlyList<Entry> Load(string conversationId)
    {
        var path = PathFor(conversationId);
        if (!File.Exists(path)) return [];
        return File.ReadLines(path).Where(l => l.Length > 0).Select(l => JsonSerializer.Deserialize<Entry>(l, Json)!).ToList();
    }

    public int Prune(int retentionDays)
    {
        if (!Directory.Exists(directory)) return 0;
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-retentionDays);
        var deleted = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.jsonl"))
        {
            if (File.GetLastWriteTimeUtc(file) >= cutoff) continue;
            File.Delete(file);
            deleted++;
        }
        return deleted;
    }
}
```

`src/Hotline.Core/Chat/ChatController.cs`:
```csharp
using System.Text;
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Chat;

/// <summary>
/// Owns the current conversation and streams replies from the selected backend. Single-threaded by
/// design: call from the UI thread; events are raised on the caller's synchronization context.
/// </summary>
public sealed class ChatController(Func<string, IChatBackend?> resolveBackend, HistoryStore? history, TimeProvider clock, FileLog log)
{
    private readonly List<ChatMessage> _messages = [];
    private CancellationTokenSource? _cts;
    private (string Text, IReadOnlyList<Attachment> Attachments)? _lastFailed;

    public event Action<ChatEvent>? Event;
    public string ConversationId { get; private set; } = Ids.New();
    public string BackendId { get; set; } = "";
    public IReadOnlyList<ChatMessage> Messages => _messages;
    public bool IsBusy => _cts is not null;

    public bool CanAccept(IReadOnlyList<Attachment> attachments, out string? reason)
    {
        reason = null;
        var backend = resolveBackend(BackendId);
        if (backend is null) return true; // SendAsync reports the missing backend
        if (!backend.Capabilities.Images && attachments.Any(a => a.Kind == AttachmentKind.Image))
            reason = $"{backend.DisplayName} can't read images.";
        else if (!backend.Capabilities.TextFiles && attachments.Any(a => a.Kind == AttachmentKind.Text))
            reason = $"{backend.DisplayName} can't read files.";
        return reason is null;
    }

    public Task SendAsync(string text, IReadOnlyList<Attachment> attachments) => SendCoreAsync(text.Trim(), attachments, announce: true);

    public Task RetryAsync() => _lastFailed is { } f ? SendCoreAsync(f.Text, f.Attachments, announce: false) : Task.CompletedTask;

    public void Cancel() => _cts?.Cancel();

    public void NewChat()
    {
        Cancel();
        _messages.Clear();
        _lastFailed = null;
        ConversationId = Ids.New();
        Emit(new ConversationReset());
    }

    private async Task SendCoreAsync(string text, IReadOnlyList<Attachment> attachments, bool announce)
    {
        if (IsBusy || (text.Length == 0 && attachments.Count == 0)) return;

        var conversationId = ConversationId;
        var assistantId = Ids.New();
        var user = new ChatMessage(Ids.New(), ChatRole.User, text, attachments, clock.GetUtcNow());
        if (announce) Emit(new UserMessageAdded(user));

        var backend = resolveBackend(BackendId);
        if (backend is null)
        {
            _lastFailed = (text, attachments);
            Emit(new AssistantFailed(assistantId, BackendErrorKind.NotConfigured, $"The '{BackendId}' backend isn't available yet."));
            return;
        }

        _messages.Add(user);
        _lastFailed = null;
        Emit(new AssistantStarted(assistantId, backend.DisplayName));
        var cts = _cts = new CancellationTokenSource();
        var reply = new StringBuilder();
        try
        {
            await foreach (var d in backend.StreamAsync(_messages.ToList(), cts.Token).WithCancellation(cts.Token))
            {
                if (d.ResetBefore) reply.Clear();
                reply.Append(d.Text);
                Emit(new AssistantDelta(assistantId, d.Text, d.ResetBefore));
            }
            Keep(conversationId, user, assistantId, backend, reply.ToString());
            Emit(new AssistantCompleted(assistantId));
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (reply.Length > 0) Keep(conversationId, user, assistantId, backend, reply.ToString());
            else _messages.Remove(user);
            Emit(new AssistantCancelled(assistantId));
        }
        catch (BackendException ex)
        {
            Fail(user, text, attachments);
            Emit(new AssistantFailed(assistantId, ex.Kind, ex.Message));
        }
        catch (Exception ex)
        {
            log.Error("chat send failed", ex);
            Fail(user, text, attachments);
            Emit(new AssistantFailed(assistantId, BackendErrorKind.Failed, ex.Message));
        }
        finally
        {
            _cts = null;
            cts.Dispose();
        }
    }

    private void Keep(string conversationId, ChatMessage user, string assistantId, IChatBackend backend, string text)
    {
        if (conversationId != ConversationId) return; // a new chat started meanwhile: drop the old turn
        var assistant = new ChatMessage(assistantId, ChatRole.Assistant, text, [], clock.GetUtcNow(), backend.Id);
        _messages.Add(assistant);
        TryHistory(conversationId, user);
        TryHistory(conversationId, assistant);
    }

    private void Fail(ChatMessage user, string text, IReadOnlyList<Attachment> attachments)
    {
        _messages.Remove(user);
        _lastFailed = (text, attachments);
    }

    private void TryHistory(string conversationId, ChatMessage m)
    {
        try { history?.Append(conversationId, m); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("history write failed", ex); }
    }

    private void Emit(ChatEvent e)
    {
        try { Event?.Invoke(e); }
        catch (Exception ex) { log.Error($"chat event handler failed for {e.GetType().Name}", ex); }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): chat controller with streaming, cancel, retry, new chat and JSONL history" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Chat settings, theme tokens, growth and image geometry (Core)

**Files:**
- Modify: `src/Hotline.Core/Settings/HotlineSettings.cs`, `src/Hotline.Core/Settings/SettingsStore.cs`, `src/Hotline.Core/Windowing/PopupGeometry.cs`
- Create: `src/Hotline.Core/Theming/ThemeTokens.cs`, `src/Hotline.Core/Windowing/ImageMath.cs`
- Test: `tests/Hotline.Core.Tests/ChatSettingsTests.cs`, `tests/Hotline.Core.Tests/ThemeTokensTests.cs`, `tests/Hotline.Core.Tests/GrowAndFitTests.cs`

**Interfaces:**
- Produces:
  - `enum BackendType { Antigravity, Gemini, OpenAiCompatible, ClaudeCode }`
  - `sealed class BackendProfile { Id; Type; Name; Model?; Endpoint?; CliPath?; Agent?; ExtraArgs? }`
  - `sealed class ChatSettings` with `DefaultBackend = "agy"`, `List<BackendProfile> Backends`, `MaxHeight = 560`, `SaveHistory = true`,
    `HistoryRetentionDays = 30`, `MaxImagePixels = 2048`, and `static List<BackendProfile> DefaultBackends()`
  - `HotlineSettings.Chat`
  - `sealed record ThemeTokens(...)` with `static Dark`, `static Light`, `string ToCss()`
  - `static RectI PopupGeometry.GrowUp(RectI bar, int contentPx, int maxPx, RectI workArea)`
  - `static (int Width, int Height) ImageMath.FitWithin(int width, int height, int maxPx)`

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/ChatSettingsTests.cs`:
```csharp
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class ChatSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private HotlineSettings LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), json);
        return new SettingsStore(_dir).Load();
    }

    [Fact]
    public void Defaults_to_agy_backend()
    {
        var s = new SettingsStore(_dir).Load();
        Assert.Equal("agy", s.Chat.DefaultBackend);
        var agy = Assert.Single(s.Chat.Backends);
        Assert.Equal(BackendType.Antigravity, agy.Type);
        Assert.Equal("hotline", agy.Agent);
        Assert.Equal(560, s.Chat.MaxHeight);
    }

    [Fact]
    public void Empty_backend_list_falls_back_to_defaults()
        => Assert.Equal("agy", Assert.Single(LoadJson("""{ "chat": { "backends": [] } }""").Chat.Backends).Id);

    [Fact]
    public void Unknown_default_backend_falls_back_to_first()
        => Assert.Equal("agy", LoadJson("""{ "chat": { "defaultBackend": "nope" } }""").Chat.DefaultBackend);

    [Fact]
    public void Backends_without_id_are_dropped_and_blank_names_use_id()
    {
        var s = LoadJson("""{ "chat": { "backends": [ { "id": "", "type": "gemini" }, { "id": "g", "type": "gemini", "name": "" } ] } }""");
        var g = Assert.Single(s.Chat.Backends);
        Assert.Equal("g", g.Name);
        Assert.Equal(BackendType.Gemini, g.Type);
    }

    [Theory]
    [InlineData(10, 160)]
    [InlineData(99999, 4000)]
    public void Max_height_is_clamped(int value, int expected)
        => Assert.Equal(expected, LoadJson($$"""{ "chat": { "maxHeight": {{value}} } }""").Chat.MaxHeight);

    [Fact]
    public void Null_chat_section_gets_defaults()
        => Assert.Equal("agy", LoadJson("""{ "chat": null }""").Chat.DefaultBackend);
}
```

`tests/Hotline.Core.Tests/ThemeTokensTests.cs`:
```csharp
using Hotline.Core.Theming;

namespace Hotline.Core.Tests;

public class ThemeTokensTests
{
    [Fact]
    public void Css_declares_every_token_as_custom_property()
    {
        var css = ThemeTokens.Dark.ToCss();
        Assert.StartsWith(":root{", css);
        foreach (var name in new[] { "--hl-text", "--hl-muted", "--hl-accent", "--hl-surface", "--hl-surface-strong",
                                     "--hl-border", "--hl-user-bubble", "--hl-code-bg", "--hl-font", "--hl-font-size", "--hl-radius" })
            Assert.Contains(name + ":", css);
        Assert.Contains("--hl-font-size:14px", css);
    }

    [Fact]
    public void Light_and_dark_differ()
        => Assert.NotEqual(ThemeTokens.Dark.ToCss(), ThemeTokens.Light.ToCss());

    [Theory]
    [InlineData("red;}body{display:none")]
    [InlineData("</style><script>")]
    public void Unsafe_values_are_rejected(string value)
        => Assert.Throws<ArgumentException>(() => (ThemeTokens.Dark with { Accent = value }).ToCss());
}
```

`tests/Hotline.Core.Tests/GrowAndFitTests.cs`:
```csharp
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class GrowAndFitTests
{
    private static readonly RectI Work = new(0, 0, 1920, 1040);
    private static readonly RectI Bar = new(680, 736, 560, 120); // bottom edge at 856

    [Fact]
    public void Grows_upward_keeping_bottom_edge()
        => Assert.Equal(new RectI(680, 456, 560, 400), PopupGeometry.GrowUp(Bar, 400, 560, Work));

    [Fact]
    public void Never_shrinks_below_bar()
        => Assert.Equal(Bar, PopupGeometry.GrowUp(Bar, 50, 560, Work));

    [Fact]
    public void Caps_at_max_height()
        => Assert.Equal(560, PopupGeometry.GrowUp(Bar, 2000, 560, Work).Height);

    [Fact]
    public void Stays_inside_work_area_top()
    {
        var r = PopupGeometry.GrowUp(new RectI(680, 100, 560, 120), 600, 600, Work);
        Assert.Equal(0, r.Y);
        Assert.Equal(600, r.Height);
    }

    [Theory]
    [InlineData(4000, 3000, 2048, 2048, 1536)]
    [InlineData(1000, 3000, 2048, 683, 2048)]
    [InlineData(800, 600, 2048, 800, 600)]   // never upscale
    [InlineData(5000, 1, 100, 100, 1)]       // never zero
    public void Fit_within_keeps_aspect(int w, int h, int max, int ew, int eh)
        => Assert.Equal((ew, eh), ImageMath.FitWithin(w, h, max));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with CS1061 `'HotlineSettings' does not contain a definition for 'Chat'` and CS0234 for `Theming`.

- [ ] **Step 3: Write minimal implementation**

Append to `src/Hotline.Core/Settings/HotlineSettings.cs` (and add `public ChatSettings Chat { get; set; } = new();` to `HotlineSettings` after `Diagnostics`):
```csharp
public enum BackendType { Antigravity, Gemini, OpenAiCompatible, ClaudeCode }

public sealed class BackendProfile
{
    public string Id { get; set; } = "";
    public BackendType Type { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Model override (agy: e.g. "gemini-3.8-flash-low"; null = backend default).</summary>
    public string? Model { get; set; }
    public string? Endpoint { get; set; }
    /// <summary>CLI executable path; null = auto-detect.</summary>
    public string? CliPath { get; set; }
    /// <summary>agy custom agent name (default "hotline").</summary>
    public string? Agent { get; set; }
    /// <summary>Extra CLI arguments, space separated.</summary>
    public string? ExtraArgs { get; set; }
}

public sealed class ChatSettings
{
    public string DefaultBackend { get; set; } = "agy";
    public List<BackendProfile> Backends { get; set; } = DefaultBackends();
    /// <summary>Popup grows upward from the bar to at most this height (DIPs).</summary>
    public int MaxHeight { get; set; } = 560;
    public bool SaveHistory { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 30;
    /// <summary>Longest edge for attached/captured images (pixels).</summary>
    public int MaxImagePixels { get; set; } = 2048;

    public static List<BackendProfile> DefaultBackends() =>
    [
        new BackendProfile { Id = "agy", Type = BackendType.Antigravity, Name = "Gemini (Antigravity)", Agent = "hotline" },
    ];
}
```

In `SettingsStore.Normalize`, after `s.Diagnostics ??= new DiagnosticsSettings();` add:
```csharp
        s.Chat ??= new ChatSettings();
        s.Chat.Backends = (s.Chat.Backends ?? []).Where(b => b is not null && !string.IsNullOrWhiteSpace(b.Id)).ToList();
        if (s.Chat.Backends.Count == 0) s.Chat.Backends = ChatSettings.DefaultBackends();
        foreach (var b in s.Chat.Backends.Where(b => string.IsNullOrWhiteSpace(b.Name))) b.Name = b.Id;
        if (!s.Chat.Backends.Any(b => b.Id == s.Chat.DefaultBackend)) s.Chat.DefaultBackend = s.Chat.Backends[0].Id;
        s.Chat.MaxHeight = Math.Clamp(s.Chat.MaxHeight, 160, 4000);
        s.Chat.MaxImagePixels = Math.Clamp(s.Chat.MaxImagePixels, 256, 8192);
        s.Chat.HistoryRetentionDays = Math.Clamp(s.Chat.HistoryRetentionDays, 1, 3650);
```

`src/Hotline.Core/Theming/ThemeTokens.cs`:
```csharp
using System.Globalization;
using System.Text;

namespace Hotline.Core.Theming;

/// <summary>Design tokens for the chat view, emitted as CSS custom properties (spec: Theming).</summary>
public sealed record ThemeTokens(
    string Text, string Muted, string Accent, string Surface, string SurfaceStrong, string Border,
    string UserBubble, string CodeBackground, string Font, int FontSizePx, int RadiusPx)
{
    private const string FontStack = "'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif";

    public static ThemeTokens Dark { get; } = new(
        "#F3F3F3", "#A8A8A8", "#8B7CFF", "rgba(255,255,255,0.06)", "rgba(255,255,255,0.10)", "rgba(255,255,255,0.12)",
        "rgba(139,124,255,0.22)", "rgba(0,0,0,0.35)", FontStack, 14, 8);

    public static ThemeTokens Light { get; } = new(
        "#1A1A1A", "#5C5C5C", "#5B4BF5", "rgba(0,0,0,0.04)", "rgba(0,0,0,0.07)", "rgba(0,0,0,0.10)",
        "rgba(91,75,245,0.14)", "rgba(0,0,0,0.05)", FontStack, 14, 8);

    public string ToCss()
    {
        var sb = new StringBuilder(":root{");
        void Add(string name, string value) => sb.Append(name).Append(':').Append(Safe(value)).Append(';');
        Add("--hl-text", Text);
        Add("--hl-muted", Muted);
        Add("--hl-accent", Accent);
        Add("--hl-surface", Surface);
        Add("--hl-surface-strong", SurfaceStrong);
        Add("--hl-border", Border);
        Add("--hl-user-bubble", UserBubble);
        Add("--hl-code-bg", CodeBackground);
        Add("--hl-font", Font);
        Add("--hl-font-size", FontSizePx.ToString(CultureInfo.InvariantCulture) + "px");
        Add("--hl-radius", RadiusPx.ToString(CultureInfo.InvariantCulture) + "px");
        return sb.Append('}').ToString();
    }

    private static string Safe(string value)
    {
        if (value.IndexOfAny([';', '{', '}', '<', '>', '"', '\\']) >= 0)
            throw new ArgumentException($"Unsafe theme token value: {value}");
        return value;
    }
}
```

Append to `src/Hotline.Core/Windowing/PopupGeometry.cs` inside `PopupGeometry`:
```csharp
    /// <summary>
    /// Grows the popup upward from its bar: keeps the bar's bottom edge, height = content clamped to
    /// [bar height, maxPx], and never above the work area's top.
    /// </summary>
    public static RectI GrowUp(RectI bar, int contentPx, int maxPx, RectI workArea)
    {
        var h = Math.Clamp(contentPx, bar.Height, Math.Max(bar.Height, Math.Min(maxPx, workArea.Height)));
        var bottom = bar.Y + bar.Height;
        var y = Math.Max(workArea.Y, bottom - h);
        return bar with { Y = y, Height = h };
    }
```

`src/Hotline.Core/Windowing/ImageMath.cs`:
```csharp
namespace Hotline.Core.Windowing;

public static class ImageMath
{
    /// <summary>Scales (w,h) so the longest edge is at most <paramref name="maxPx"/>; never upscales, never returns 0.</summary>
    public static (int Width, int Height) FitWithin(int width, int height, int maxPx)
    {
        var longest = Math.Max(width, height);
        if (longest <= maxPx) return (width, height);
        var scale = (double)maxPx / longest;
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): chat/backend settings, theme tokens, upward growth and image fitting" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: agy protocol, turn parser, locator and errors (Core)

**Files:**
- Create: `src/Hotline.Core/Backends/Agy/AgyProtocol.cs`, `src/Hotline.Core/Backends/Agy/AgyTurnParser.cs`
- Test: `tests/Hotline.Core.Tests/AgyProtocolTests.cs`, `tests/Hotline.Core.Tests/AgyTurnParserTests.cs`, `tests/Hotline.Core.Tests/AgyErrorsTests.cs`

**Interfaces:**
- Consumes: `ChatMessage`, `ChatDelta`, `BackendException`, `BackendProfile`.
- Produces (namespace `Hotline.Core.Backends.Agy`):
  - `static class AgyProtocol` with:
    - `string UserLine(string prompt)`
    - `string ComposePrompt(string text, IReadOnlyList<string> imagePaths, IReadOnlyList<(string Name, string Content)> textFiles, IReadOnlyList<ChatMessage> priorContext)`
    - `IReadOnlyList<string> BuildArgs(BackendProfile p)`
  - `static string? AgyLocator.Find(string? configuredPath, Func<string, bool> exists, string? localAppData, string? pathEnv)`
  - `static BackendException AgyErrors.Map(string error)`
  - `sealed class AgyTurnParser` with `IEnumerable<ChatDelta> Feed(string line)`, `bool Completed`, `string? Error`

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/AgyProtocolTests.cs`:
```csharp
using System.Text.Json;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class AgyProtocolTests
{
    [Fact]
    public void User_line_is_single_line_event_json()
    {
        var line = AgyProtocol.UserLine("hi\n\"there\"");
        Assert.DoesNotContain('\n', line);
        using var doc = JsonDocument.Parse(line);
        Assert.Equal("user", doc.RootElement.GetProperty("event").GetString());
        var msg = doc.RootElement.GetProperty("message");
        Assert.Equal("user", msg.GetProperty("role").GetString());
        Assert.Equal("hi\n\"there\"", msg.GetProperty("content").GetString());
    }

    [Fact]
    public void Plain_prompt_is_just_the_text()
        => Assert.Equal("What is 2+2?", AgyProtocol.ComposePrompt("What is 2+2?", [], [], []));

    [Fact]
    public void Images_are_listed_and_text_files_inlined()
    {
        var p = AgyProtocol.ComposePrompt("Compare these", ["attachments/m1/a.png"], [("notes.md", "# Notes")], []);
        Assert.Contains("attachments/m1/a.png", p);
        Assert.Contains("view_file", p);
        Assert.Contains("notes.md", p);
        Assert.Contains("# Notes", p);
        Assert.EndsWith("Compare these", p);
    }

    [Fact]
    public void Attachment_only_prompt_gets_default_question()
        => Assert.EndsWith("Please look at the attached file(s).", AgyProtocol.ComposePrompt("", ["attachments/m1/a.png"], [], []));

    [Fact]
    public void Prior_context_is_replayed_as_transcript()
    {
        var now = DateTimeOffset.UnixEpoch;
        var prior = new List<ChatMessage>
        {
            new("1", ChatRole.User, "Remember PINEAPPLE", [], now),
            new("2", ChatRole.Assistant, "ok", [], now),
        };
        var p = AgyProtocol.ComposePrompt("What word?", [], [], prior);
        Assert.Contains("User: Remember PINEAPPLE", p);
        Assert.Contains("Assistant: ok", p);
        Assert.EndsWith("What word?", p);
    }

    [Fact]
    public void Args_use_stream_json_agent_and_never_skip_permissions()
    {
        var args = AgyProtocol.BuildArgs(new BackendProfile { Id = "agy", Agent = "hotline", Model = "gemini-3.8-flash-low", ExtraArgs = "--effort low" });
        Assert.Equal(["--input-format", "stream-json", "--output-format", "stream-json", "-p=", "--agent", "hotline",
                      "--model", "gemini-3.8-flash-low", "--effort", "low"], args);
        Assert.DoesNotContain("--dangerously-skip-permissions", AgyProtocol.BuildArgs(new BackendProfile { ExtraArgs = "--dangerously-skip-permissions" }));
    }

    [Fact]
    public void Default_agent_is_hotline()
        => Assert.Contains("hotline", AgyProtocol.BuildArgs(new BackendProfile { Id = "agy" }));

    [Fact]
    public void Locator_prefers_configured_then_localappdata_then_path()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"D:\tools\agy.exe", @"C:\L\agy\bin\agy.exe", @"C:\bin\agy.exe" };
        Assert.Equal(@"D:\tools\agy.exe", AgyLocator.Find(@"D:\tools\agy.exe", files.Contains, @"C:\L", @"C:\bin"));
        Assert.Equal(@"C:\L\agy\bin\agy.exe", AgyLocator.Find(null, files.Contains, @"C:\L", @"C:\bin"));
        Assert.Equal(@"C:\bin\agy.exe", AgyLocator.Find(null, files.Contains, @"C:\none", @"C:\x;C:\bin"));
        Assert.Null(AgyLocator.Find(@"D:\missing.exe", files.Contains, @"C:\L", @"C:\bin"));
        Assert.Null(AgyLocator.Find(null, _ => false, @"C:\L", @"C:\bin"));
    }
}
```

`tests/Hotline.Core.Tests/AgyTurnParserTests.cs`:
```csharp
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class AgyTurnParserTests
{
    // Shapes recorded from agy 1.2.14 on 2026-10-02.
    private const string Init = """{"event":"init","conversation_id":"c","init":{"cwd":"C:\\w","tools":["view_file"]}}""";
    private static string UserStep() => """{"event":"step_update","step_update":{"conversation_id":"c","step_index":0,"state":"DONE","step_type":"user_input"}}""";
    private static string Text(int step, string state, string delta) =>
        $$"""{"event":"step_update","step_update":{"conversation_id":"c","step_index":{{step}},"state":"{{state}}","step_type":"agent_response","text_delta":{{System.Text.Json.JsonSerializer.Serialize(delta)}}}}""";
    private static string Tool(int step, string state) =>
        $$"""{"event":"step_update","step_update":{"conversation_id":"c","step_index":{{step}},"state":"{{state}}","step_type":"tool","tool_name":"view_file"}}""";
    private static string Result(string status, string response, string? error = null, string? denied = null) =>
        $$"""{"event":"result","result":{"conversation_id":"c","status":"{{status}}","response":{{System.Text.Json.JsonSerializer.Serialize(response)}}{{(error is null ? "" : $",\"error\":{System.Text.Json.JsonSerializer.Serialize(error)}")}}{{(denied is null ? "" : $",\"denied_actions\":[{{\"action\":\"{denied}\",\"display_name\":\"ViewFile\"}}]")}},"usage":{"input_tokens":1}}}""";

    private static List<ChatDelta> FeedAll(AgyTurnParser p, params string[] lines) => lines.SelectMany(p.Feed).ToList();

    [Fact]
    public void Streams_text_deltas_and_completes()
    {
        var p = new AgyTurnParser();
        var d = FeedAll(p, Init, UserStep(), Text(1, "ACTIVE", "Hel"), Text(1, "DONE", "lo\n"), Result("SUCCESS", "Hello\n"));
        Assert.Equal(["Hel", "lo\n"], d.Select(x => x.Text));
        Assert.All(d, x => Assert.False(x.ResetBefore));
        Assert.True(p.Completed);
        Assert.Null(p.Error);
    }

    [Fact]
    public void New_response_step_restarts_answer()
    {
        var p = new AgyTurnParser();
        var d = FeedAll(p, Text(1, "DONE", "draft"), Tool(2, "DONE"), Text(3, "ACTIVE", "final"), Result("SUCCESS", "final"));
        Assert.Equal(new ChatDelta("final", ResetBefore: true), d[1]);
    }

    [Fact]
    public void Result_response_wins_when_streamed_text_differs()
    {
        var p = new AgyTurnParser();
        var d = FeedAll(p, Text(1, "DONE", "partial"), Result("SUCCESS", "The full answer\n"));
        Assert.Equal(new ChatDelta("The full answer\n", ResetBefore: true), d[^1]);
    }

    [Fact]
    public void Result_without_streamed_text_emits_response()
    {
        var p = new AgyTurnParser();
        Assert.Equal([new ChatDelta("7391\n")], FeedAll(p, Tool(1, "DONE"), Result("SUCCESS", "7391\n")));
    }

    [Fact]
    public void Error_result_sets_error()
    {
        var p = new AgyTurnParser();
        FeedAll(p, Result("ERROR", "", "stream input message is missing the \"event\" field"));
        Assert.True(p.Completed);
        Assert.Contains("missing the \"event\" field", p.Error);
    }

    [Fact]
    public void Denied_action_with_empty_response_is_an_error()
    {
        var p = new AgyTurnParser();
        FeedAll(p, Result("SUCCESS", "", denied: "read_file"));
        Assert.Contains("read_file", p.Error);
    }

    [Fact]
    public void Non_json_and_unknown_lines_are_ignored()
    {
        var p = new AgyTurnParser();
        Assert.Empty(FeedAll(p, "warning: something", "", """{"event":"heartbeat"}"""));
        Assert.False(p.Completed);
    }
}
```

`tests/Hotline.Core.Tests/AgyErrorsTests.cs`:
```csharp
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class AgyErrorsTests
{
    [Theory]
    [InlineData("UNAUTHENTICATED: please sign in", BackendErrorKind.NotLoggedIn)]
    [InlineData("You are not logged in", BackendErrorKind.NotLoggedIn)]
    [InlineData("RESOURCE_EXHAUSTED: quota exceeded", BackendErrorKind.RateLimited)]
    [InlineData("workspace is not trusted", BackendErrorKind.NotConfigured)]
    [InlineData("agy wasn't allowed to: read_file", BackendErrorKind.Unsupported)]
    [InlineData("something else broke", BackendErrorKind.Failed)]
    public void Maps_error_text_to_kind(string error, BackendErrorKind kind)
        => Assert.Equal(kind, AgyErrors.Map(error).Kind);

    [Fact]
    public void Messages_are_actionable()
    {
        Assert.Contains("agy", AgyErrors.Map("not logged in").Message);
        Assert.Contains("something else broke", AgyErrors.Map("something else broke").Message);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with CS0234 `'Backends' does not exist in the namespace 'Hotline.Core'`.

- [ ] **Step 3: Write minimal implementation**

`src/Hotline.Core/Backends/Agy/AgyProtocol.cs`:
```csharp
using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Agy;

/// <summary>Antigravity CLI (agy 1.2.x) stream-json protocol helpers. Verified against agy 1.2.14 (2026-10-02).</summary>
public static class AgyProtocol
{
    public const string DefaultAgent = "hotline";

    /// <summary>One NDJSON input line. agy accepts only text content.</summary>
    public static string UserLine(string prompt) =>
        JsonSerializer.Serialize(new { @event = "user", message = new { role = "user", content = prompt } });

    public static string ComposePrompt(
        string text, IReadOnlyList<string> imagePaths, IReadOnlyList<(string Name, string Content)> textFiles,
        IReadOnlyList<ChatMessage> priorContext)
    {
        var sb = new StringBuilder();
        if (priorContext.Count > 0)
        {
            sb.AppendLine("Conversation so far (for context):");
            foreach (var m in priorContext)
                sb.Append(m.Role == ChatRole.User ? "User: " : "Assistant: ").AppendLine(m.Text);
            sb.AppendLine().AppendLine("New message:");
        }
        if (imagePaths.Count > 0)
        {
            sb.AppendLine("Attached images (in the workspace; use view_file to look at them):");
            foreach (var p in imagePaths) sb.Append("- ").AppendLine(p);
            sb.AppendLine();
        }
        foreach (var (name, content) in textFiles)
            sb.Append("Attached file ").Append(name).AppendLine(":").AppendLine("```").AppendLine(content).AppendLine("```").AppendLine();
        sb.Append(text.Length > 0 ? text : "Please look at the attached file(s).");
        return sb.ToString();
    }

    public static IReadOnlyList<string> BuildArgs(BackendProfile p)
    {
        var args = new List<string>
        {
            "--input-format", "stream-json", "--output-format", "stream-json", "-p=",
            "--agent", string.IsNullOrWhiteSpace(p.Agent) ? DefaultAgent : p.Agent,
        };
        if (!string.IsNullOrWhiteSpace(p.Model)) args.AddRange(["--model", p.Model]);
        if (!string.IsNullOrWhiteSpace(p.ExtraArgs))
            args.AddRange(p.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(a => !a.Equals("--dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase)));
        return args;
    }
}

public static class AgyLocator
{
    public static string? Find(string? configuredPath, Func<string, bool> exists, string? localAppData, string? pathEnv)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            return exists(expanded) ? expanded : null;
        }
        if (!string.IsNullOrEmpty(localAppData))
        {
            var installed = Path.Combine(localAppData, "agy", "bin", "agy.exe");
            if (exists(installed)) return installed;
        }
        foreach (var dir in (pathEnv ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "agy.exe");
            if (exists(candidate)) return candidate;
        }
        return null;
    }
}

public static class AgyErrors
{
    public static BackendException Map(string error)
    {
        bool Has(params string[] words) => words.Any(w => error.Contains(w, StringComparison.OrdinalIgnoreCase));
        if (Has("unauthenticated", "sign in", "signed in", "log in", "logged in", "login"))
            return new(BackendErrorKind.NotLoggedIn, "agy isn't signed in. Run `agy` once in a terminal to sign in, then try again.");
        if (Has("resource_exhausted", "quota", "rate limit"))
            return new(BackendErrorKind.RateLimited, "Your Antigravity quota is used up for now. Try again later.");
        if (Has("not trusted", "trust"))
            return new(BackendErrorKind.NotConfigured, "agy doesn't trust Hotline's workspace yet. Run `agy` once in a terminal and trust it.");
        if (Has("wasn't allowed", "denied"))
            return new(BackendErrorKind.Unsupported, error);
        return new(BackendErrorKind.Failed, $"agy: {error}");
    }
}
```

`src/Hotline.Core/Backends/Agy/AgyTurnParser.cs`:
```csharp
using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// Turns agy stream-json output lines for one turn into answer deltas. A new agent_response step
/// restarts the answer; the final result's response is authoritative.
/// </summary>
public sealed class AgyTurnParser
{
    private readonly StringBuilder _shown = new();
    private int? _responseStep;

    public bool Completed { get; private set; }
    public string? Error { get; private set; }

    public IEnumerable<ChatDelta> Feed(string line)
    {
        JsonElement root;
        try { root = JsonDocument.Parse(line).RootElement; }
        catch (JsonException) { yield break; }
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("event", out var ev)) yield break;

        switch (ev.GetString())
        {
            case "step_update" when root.TryGetProperty("step_update", out var su):
                if (Str(su, "step_type") != "agent_response" || !su.TryGetProperty("text_delta", out var deltaEl)) yield break;
                var delta = deltaEl.GetString() ?? "";
                var step = su.TryGetProperty("step_index", out var si) ? si.GetInt32() : 0;
                var reset = _responseStep is { } current && current != step;
                _responseStep = step;
                if (reset) _shown.Clear();
                _shown.Append(delta);
                if (delta.Length > 0 || reset) yield return new ChatDelta(delta, reset);
                break;

            case "result" when root.TryGetProperty("result", out var r):
                Completed = true;
                var status = Str(r, "status");
                var response = Str(r, "response") ?? "";
                if (status != "SUCCESS")
                {
                    Error = Str(r, "error") ?? $"agy ended with status {status}";
                    yield break;
                }
                if (response.Length == 0 && r.TryGetProperty("denied_actions", out var denied) && denied.GetArrayLength() > 0)
                {
                    Error = "agy wasn't allowed to: " + string.Join(", ", denied.EnumerateArray().Select(d => Str(d, "action")));
                    yield break;
                }
                if (response.Length > 0 && response.TrimEnd() != _shown.ToString().TrimEnd())
                    yield return new ChatDelta(response, ResetBefore: _shown.Length > 0);
                break;
        }
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`. If `Result_without_streamed_text_emits_response` fails because `ResetBefore` is true, the `_shown.Length > 0` guard is wrong; fix the parser, not the test.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): agy stream-json protocol, turn parser, locator and error mapping" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Line process, agy workspace and AgyBackend (Core)

**Files:**
- Create: `src/Hotline.Core/Processes/LineProcess.cs`, `src/Hotline.Core/Backends/Agy/AgyWorkspace.cs`, `src/Hotline.Core/Backends/Agy/AgyBackend.cs`
- Test: `tests/Hotline.Core.Tests/FakeLineProcess.cs`, `tests/Hotline.Core.Tests/AgyBackendTests.cs`, `tests/Hotline.Core.Tests/AgyWorkspaceTests.cs`, `tests/Hotline.Core.Tests/SystemLineProcessTests.cs`

**Interfaces:**
- Consumes: Task 1, Task 4.
- Produces:
  - `interface ILineProcess : IAsyncDisposable { bool HasExited; string StandardErrorTail; Task WriteLineAsync(string line, CancellationToken ct); Task<string?> ReadLineAsync(CancellationToken ct); }`
  - `interface ILineProcessFactory { ILineProcess Start(string exe, IReadOnlyList<string> args, string workingDirectory); }`
  - `sealed class SystemLineProcessFactory(Action<System.Diagnostics.Process>? onStarted = null) : ILineProcessFactory`
  - `sealed class AgyWorkspace(string root, TimeProvider clock)` with:
    - `string Root`, `void Ensure()`
    - `IReadOnlyList<string> SaveImages(string messageId, IReadOnlyList<Attachment> images)` returning workspace-relative paths with `/`
    - `int PruneAttachments(TimeSpan olderThan)`
    - `const string AgentMarkdown`
  - `sealed class AgyBackend(BackendProfile profile, Func<string?> locateExe, AgyWorkspace workspace, ILineProcessFactory processes, FileLog log) : IChatBackend`

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/FakeLineProcess.cs`:
```csharp
using System.Threading.Channels;
using Hotline.Core.Processes;

namespace Hotline.Core.Tests;

/// <summary>In-memory agy stand-in: records written lines; the test scripts output lines per turn.</summary>
public sealed class FakeLineProcess : ILineProcess
{
    private readonly Channel<string?> _out = Channel.CreateUnbounded<string?>();
    public List<string> Written { get; } = [];
    public Func<string, IEnumerable<string>>? Respond { get; set; }
    public bool HasExited { get; private set; }
    public bool Disposed { get; private set; }
    public string StandardErrorTail { get; set; } = "";

    public Task WriteLineAsync(string line, CancellationToken ct)
    {
        Written.Add(line);
        foreach (var o in Respond?.Invoke(line) ?? []) _out.Writer.TryWrite(o);
        return Task.CompletedTask;
    }

    public async Task<string?> ReadLineAsync(CancellationToken ct) => await _out.Reader.ReadAsync(ct);

    /// <summary>Simulates the process dying: pending/next reads return null.</summary>
    public void Exit() { HasExited = true; _out.Writer.TryWrite(null); }

    public ValueTask DisposeAsync() { Disposed = true; HasExited = true; _out.Writer.TryWrite(null); return ValueTask.CompletedTask; }
}

public sealed class FakeLineProcessFactory : ILineProcessFactory
{
    public List<(string Exe, IReadOnlyList<string> Args, string Cwd, FakeLineProcess Process)> Started { get; } = [];
    public Func<FakeLineProcess>? Create { get; set; }

    public ILineProcess Start(string exe, IReadOnlyList<string> args, string workingDirectory)
    {
        var p = Create?.Invoke() ?? new FakeLineProcess();
        Started.Add((exe, args, workingDirectory, p));
        return p;
    }
}
```

`tests/Hotline.Core.Tests/AgyBackendTests.cs`:
```csharp
using System.Text.Json;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class AgyBackendTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeLineProcessFactory _factory = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static ChatMessage U(string id, string text, params Attachment[] a) => new(id, ChatRole.User, text, a, Now);
    private static ChatMessage A(string id, string text) => new(id, ChatRole.Assistant, text, [], Now);

    private static IEnumerable<string> Answer(string text) =>
    [
        """{"event":"init","init":{"tools":[]}}""",
        $$"""{"event":"step_update","step_update":{"step_index":1,"state":"DONE","step_type":"agent_response","text_delta":{{JsonSerializer.Serialize(text)}}}}""",
        $$"""{"event":"result","result":{"status":"SUCCESS","response":{{JsonSerializer.Serialize(text)}}}}""",
    ];

    private static string PromptOf(string line) =>
        JsonDocument.Parse(line).RootElement.GetProperty("message").GetProperty("content").GetString()!;

    private AgyBackend New(string? exe = @"C:\agy.exe") =>
        new(new BackendProfile { Id = "agy", Name = "Gemini (Antigravity)", Agent = "hotline" }, () => exe,
            new AgyWorkspace(Path.Combine(_dir, "ws"), new ManualTimeProvider()), _factory, new FileLog(Path.Combine(_dir, "h.log")));

    private static async Task<string> Collect(IAsyncEnumerable<ChatDelta> s)
    {
        var text = "";
        await foreach (var d in s) text = d.ResetBefore ? d.Text : text + d.Text;
        return text;
    }

    [Fact]
    public async Task Missing_exe_is_NotInstalled()
    {
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New(exe: null).StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotInstalled, ex.Kind);
        Assert.Empty(_factory.Started);
    }

    [Fact]
    public async Task First_turn_starts_agy_in_workspace_and_streams_answer()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("Hello!") };
        var text = await Collect(New().StreamAsync([U("1", "hi")], default));
        Assert.Equal("Hello!", text);
        var (exe, args, cwd, proc) = Assert.Single(_factory.Started);
        Assert.Equal(@"C:\agy.exe", exe);
        Assert.Contains("stream-json", args);
        Assert.EndsWith("ws", cwd);
        Assert.Equal("hi", PromptOf(Assert.Single(proc.Written)));
        Assert.True(File.Exists(Path.Combine(cwd, ".agents", "agents", "hotline.md")));
    }

    [Fact]
    public async Task Follow_up_reuses_process_without_replaying_context()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New();
        await Collect(b.StreamAsync([U("1", "first")], default));
        await Collect(b.StreamAsync([U("1", "first"), A("2", "ok"), U("3", "second")], default));
        var proc = Assert.Single(_factory.Started).Process;
        Assert.Equal("second", PromptOf(proc.Written[1]));
    }

    [Fact]
    public async Task Cancel_kills_process_and_next_turn_replays_context()
    {
        _factory.Create = () => new FakeLineProcess { Respond = l => PromptOf(l).Contains("slow") ? [] : Answer("ok") };
        var b = New();
        await Collect(b.StreamAsync([U("1", "remember PINEAPPLE")], default));
        using var cts = new CancellationTokenSource();
        var slow = Collect(b.StreamAsync([U("1", "remember PINEAPPLE"), A("2", "ok"), U("3", "slow question")], cts.Token));
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slow);
        Assert.True(_factory.Started[0].Process.Disposed);

        await Collect(b.StreamAsync([U("1", "remember PINEAPPLE"), A("2", "ok"), U("4", "which word?")], default));

        Assert.Equal(2, _factory.Started.Count);
        var replayed = PromptOf(Assert.Single(_factory.Started[1].Process.Written));
        Assert.Contains("User: remember PINEAPPLE", replayed);
        Assert.EndsWith("which word?", replayed);
    }

    [Fact]
    public async Task New_conversation_restarts_process()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New();
        await Collect(b.StreamAsync([U("1", "a")], default));
        await Collect(b.StreamAsync([U("9", "fresh chat")], default));
        Assert.Equal(2, _factory.Started.Count);
        Assert.True(_factory.Started[0].Process.Disposed);
        Assert.Equal("fresh chat", PromptOf(_factory.Started[1].Process.Written[0]));
    }

    [Fact]
    public async Task Process_exit_mid_turn_fails_with_stderr_tail()
    {
        _factory.Create = () =>
        {
            var p = new FakeLineProcess { StandardErrorTail = "panic: boom" };
            p.Respond = _ => { p.Exit(); return []; };
            return p;
        };
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New().StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.Failed, ex.Kind);
        Assert.Contains("panic: boom", ex.Message);
    }

    [Fact]
    public async Task Error_result_is_mapped()
    {
        _factory.Create = () => new FakeLineProcess
        {
            Respond = _ => ["""{"event":"result","result":{"status":"ERROR","response":"","error":"UNAUTHENTICATED"}}"""],
        };
        var ex = await Assert.ThrowsAsync<BackendException>(() => Collect(New().StreamAsync([U("1", "hi")], default)));
        Assert.Equal(BackendErrorKind.NotLoggedIn, ex.Kind);
    }

    [Fact]
    public async Task Images_are_saved_in_workspace_and_text_files_inlined()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("7391") };
        var img = new Attachment("a1", "num.png", AttachmentKind.Image, "image/png", [137, 80, 78, 71]);
        var txt = new Attachment("a2", "notes.md", AttachmentKind.Text, "text/plain", "# hi"u8.ToArray());
        await Collect(New().StreamAsync([U("m1", "what number?", img, txt)], default));

        var prompt = PromptOf(_factory.Started[0].Process.Written[0]);
        Assert.Contains("attachments/m1/num.png", prompt);
        Assert.Contains("# hi", prompt);
        Assert.True(File.Exists(Path.Combine(_factory.Started[0].Cwd, "attachments", "m1", "num.png")));
    }

    [Fact]
    public async Task Dispose_stops_process()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New();
        await Collect(b.StreamAsync([U("1", "a")], default));
        await b.DisposeAsync();
        Assert.True(_factory.Started[0].Process.Disposed);
    }
}
```

`tests/Hotline.Core.Tests/AgyWorkspaceTests.cs`:
```csharp
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public sealed class AgyWorkspaceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _clock = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Agent_file_keeps_default_components()
    {
        new AgyWorkspace(_dir, _clock).Ensure();
        var md = File.ReadAllText(Path.Combine(_dir, ".agents", "agents", "hotline.md"));
        Assert.StartsWith("---", md);
        Assert.Contains("name: hotline", md);
        Assert.DoesNotContain("excludeDefaultComponents", md);
    }

    [Fact]
    public void Image_names_are_sanitized_and_deduplicated()
    {
        var ws = new AgyWorkspace(_dir, _clock);
        var a = new Attachment("1", @"..\..\evil:name.png", AttachmentKind.Image, "image/png", [1]);
        var b = new Attachment("2", "evil_name.png", AttachmentKind.Image, "image/png", [2]);
        var paths = ws.SaveImages("m1", [a, b]);
        Assert.Equal(2, paths.Distinct().Count());
        Assert.All(paths, p => Assert.StartsWith("attachments/m1/", p));
        Assert.All(paths, p => Assert.DoesNotContain("..", p));
        Assert.All(paths, p => Assert.True(File.Exists(Path.Combine(_dir, p))));
    }

    [Fact]
    public void Prune_removes_old_attachment_folders()
    {
        var ws = new AgyWorkspace(_dir, _clock);
        ws.SaveImages("old", [new Attachment("1", "a.png", AttachmentKind.Image, "image/png", [1])]);
        ws.SaveImages("new", [new Attachment("2", "b.png", AttachmentKind.Image, "image/png", [1])]);
        Directory.SetLastWriteTimeUtc(Path.Combine(_dir, "attachments", "old"), _clock.GetUtcNow().UtcDateTime.AddDays(-3));
        Assert.Equal(1, ws.PruneAttachments(TimeSpan.FromDays(1)));
        Assert.False(Directory.Exists(Path.Combine(_dir, "attachments", "old")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "attachments", "new")));
    }
}
```

`tests/Hotline.Core.Tests/SystemLineProcessTests.cs`:
```csharp
using Hotline.Core.Processes;

namespace Hotline.Core.Tests;

public class SystemLineProcessTests
{
    [Fact]
    public async Task Writes_utf8_without_bom_and_reads_lines()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var p = new SystemLineProcessFactory().Start("powershell.exe",
            ["-NoProfile", "-Command", "$l = [Console]::In.ReadLine(); [Console]::Out.WriteLine('got:' + $l + ':' + $l.Length)"],
            Path.GetTempPath());
        await p.WriteLineAsync("hello", default);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        Assert.Equal("got:hello:5", await p.ReadLineAsync(cts.Token)); // a BOM would make the length 6+
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with CS0234 `'Processes' does not exist` and CS0246 `AgyBackend`, `AgyWorkspace`.

- [ ] **Step 3: Write minimal implementation**

`src/Hotline.Core/Processes/LineProcess.cs`:
```csharp
using System.Diagnostics;
using System.Text;

namespace Hotline.Core.Processes;

/// <summary>A child process driven by newline-delimited stdin/stdout (agy, later claude).</summary>
public interface ILineProcess : IAsyncDisposable
{
    bool HasExited { get; }
    string StandardErrorTail { get; }
    Task WriteLineAsync(string line, CancellationToken ct);
    /// <summary>Next stdout line, or null when the process has ended.</summary>
    Task<string?> ReadLineAsync(CancellationToken ct);
}

public interface ILineProcessFactory
{
    ILineProcess Start(string exe, IReadOnlyList<string> args, string workingDirectory);
}

public sealed class SystemLineProcessFactory(Action<Process>? onStarted = null) : ILineProcessFactory
{
    public ILineProcess Start(string exe, IReadOnlyList<string> args, string workingDirectory) =>
        new SystemLineProcess(exe, args, workingDirectory, onStarted);
}

public sealed class SystemLineProcess : ILineProcess
{
    private const int StderrKeep = 4000;
    private readonly Process _process;
    private readonly StringBuilder _stderr = new();

    public SystemLineProcess(string exe, IReadOnlyList<string> args, string workingDirectory, Action<Process>? onStarted)
    {
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), // agy rejects a BOM
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        _process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        onStarted?.Invoke(_process);
        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            lock (_stderr)
            {
                _stderr.AppendLine(e.Data);
                if (_stderr.Length > StderrKeep) _stderr.Remove(0, _stderr.Length - StderrKeep);
            }
        };
        _process.BeginErrorReadLine();
    }

    public bool HasExited => _process.HasExited;

    public string StandardErrorTail { get { lock (_stderr) return _stderr.ToString().Trim(); } }

    public async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _process.StandardInput.WriteAsync((line + "\n").AsMemory(), ct);
        await _process.StandardInput.FlushAsync(ct);
    }

    public Task<string?> ReadLineAsync(CancellationToken ct) => _process.StandardOutput.ReadLineAsync(ct).AsTask();

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException) { /* already gone */ }
        _process.Dispose();
    }
}
```

`src/Hotline.Core/Backends/Agy/AgyWorkspace.cs`:
```csharp
using Hotline.Core.Chat;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// agy's working directory: holds the custom "hotline" agent and per-message image attachments.
/// agy may read files inside its (trusted) workspace without prompting; reads elsewhere are denied
/// in headless mode, which is the boundary we want.
/// </summary>
public sealed class AgyWorkspace(string root, TimeProvider clock)
{
    // Do NOT add excludeDefaultComponents: it drops the default permissions (workspace reads get denied).
    public const string AgentMarkdown = """
        ---
        name: hotline
        description: Fast conversational assistant for the Hotline popup.
        tools:
          - view_file
        ---
        You are Hotline, a fast desktop assistant opened from the Windows Copilot key.
        Answer directly and concisely in Markdown. Do not plan, create tasks, browse the web, run commands or edit files.
        When the user lists attached images under ./attachments, use view_file to look at them.
        """;

    public string Root { get; } = root;

    private string AttachmentsDir => Path.Combine(Root, "attachments");

    public void Ensure()
    {
        var agentDir = Path.Combine(Root, ".agents", "agents");
        Directory.CreateDirectory(agentDir);
        Directory.CreateDirectory(AttachmentsDir);
        var agentFile = Path.Combine(agentDir, "hotline.md");
        if (!File.Exists(agentFile) || File.ReadAllText(agentFile) != AgentMarkdown)
            File.WriteAllText(agentFile, AgentMarkdown);
    }

    public IReadOnlyList<string> SaveImages(string messageId, IReadOnlyList<Attachment> images)
    {
        if (images.Count == 0) return [];
        var safeId = Sanitize(messageId);
        var dir = Path.Combine(AttachmentsDir, safeId);
        Directory.CreateDirectory(dir);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (var image in images)
        {
            var name = Sanitize(Path.GetFileName(image.Name.Replace('\\', '/').Split('/')[^1]));
            if (name.Length == 0) name = "image.png";
            var unique = name;
            for (var i = 2; !used.Add(unique); i++)
                unique = $"{Path.GetFileNameWithoutExtension(name)}-{i}{Path.GetExtension(name)}";
            File.WriteAllBytes(Path.Combine(dir, unique), image.Data);
            paths.Add($"attachments/{safeId}/{unique}");
        }
        return paths;
    }

    public int PruneAttachments(TimeSpan olderThan)
    {
        if (!Directory.Exists(AttachmentsDir)) return 0;
        var cutoff = clock.GetUtcNow().UtcDateTime - olderThan;
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(AttachmentsDir))
        {
            if (Directory.GetLastWriteTimeUtc(dir) >= cutoff) continue;
            Directory.Delete(dir, recursive: true);
            removed++;
        }
        return removed;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) || c == ':' ? '_' : c).ToArray()).Trim('.', ' ');
        return cleaned.Replace("..", "_");
    }
}
```

`src/Hotline.Core/Backends/Agy/AgyBackend.cs`:
```csharp
using System.Runtime.CompilerServices;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Agy;

/// <summary>
/// Chat through the user's installed Antigravity CLI, kept running as one persistent stream-json
/// session per conversation (agy holds the context). If the session is lost (cancel, crash, new chat,
/// backend switch) the next turn starts a fresh process and replays the conversation as a transcript.
/// </summary>
public sealed class AgyBackend(BackendProfile profile, Func<string?> locateExe, AgyWorkspace workspace, ILineProcessFactory processes, FileLog log)
    : IChatBackend
{
    private ILineProcess? _process;
    private int _knownCount;
    private string? _knownFirstId;

    public string Id => profile.Id;
    public string DisplayName => profile.Name;
    public BackendCapabilities Capabilities { get; } = new(Images: true, TextFiles: true);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(IReadOnlyList<ChatMessage> conversation, [EnumeratorCancellation] CancellationToken ct)
    {
        if (conversation.Count == 0 || conversation[^1].Role != ChatRole.User)
            throw new ArgumentException("The conversation must end with a user message.", nameof(conversation));
        var exe = locateExe() ?? throw new BackendException(BackendErrorKind.NotInstalled,
            "The Antigravity CLI (agy) isn't installed. Install it from antigravity.google/cli, then try again.");

        var user = conversation[^1];
        var prior = conversation.Take(conversation.Count - 1).ToList();
        var fresh = false;
        if (_process is null || _process.HasExited || prior.Count != _knownCount || (prior.Count > 0 && prior[0].Id != _knownFirstId))
        {
            await StopAsync();
            workspace.Ensure();
            var args = AgyProtocol.BuildArgs(profile);
            _process = processes.Start(exe, args, workspace.Root);
            fresh = true;
            log.Info($"agy started: {exe} {string.Join(' ', args)}");
        }

        var images = workspace.SaveImages(user.Id, user.Attachments.Where(a => a.Kind == AttachmentKind.Image).ToList());
        var texts = user.Attachments.Where(a => a.Kind == AttachmentKind.Text).Select(a => (a.Name, a.AsText())).ToList();
        var prompt = AgyProtocol.ComposePrompt(user.Text, images, texts, fresh ? prior : Array.Empty<ChatMessage>());

        var process = _process;
        using var stopOnCancel = ct.Register(() => _ = StopAsync());
        await process.WriteLineAsync(AgyProtocol.UserLine(prompt), ct);

        var parser = new AgyTurnParser();
        while (!parser.Completed)
        {
            var line = await process.ReadLineAsync(ct);
            if (line is null)
            {
                ct.ThrowIfCancellationRequested();
                var tail = process.StandardErrorTail;
                await StopAsync();
                throw new BackendException(BackendErrorKind.Failed, "agy stopped unexpectedly." + (tail.Length > 0 ? " " + tail : ""));
            }
            foreach (var delta in parser.Feed(line)) yield return delta;
        }

        if (parser.Error is { } error)
        {
            log.Error($"agy turn failed: {error}");
            throw AgyErrors.Map(error);
        }
        _knownCount = conversation.Count + 1; // + the assistant reply the controller is about to append
        _knownFirstId = conversation[0].Id;
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private async Task StopAsync()
    {
        var p = Interlocked.Exchange(ref _process, null);
        if (p is not null) await p.DisposeAsync();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`. `Writes_utf8_without_bom_and_reads_lines` takes about 1 s because it starts PowerShell.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): persistent agy backend with workspace, attachments and context replay" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Backend factory and cache (Core)

**Files:**
- Create: `src/Hotline.Core/Backends/BackendCatalog.cs`
- Test: `tests/Hotline.Core.Tests/BackendCatalogTests.cs`

**Interfaces:**
- Consumes: Tasks 3–5.
- Produces:
  - `sealed record BackendDeps(ILineProcessFactory Processes, AgyWorkspace AgyWorkspace, FileLog Log, Func<string, bool> FileExists, string? LocalAppData, string? PathEnv)`
  - `static IChatBackend? BackendFactory.Create(BackendProfile p, BackendDeps deps)` and `static bool BackendFactory.IsAvailable(BackendType t)`
  - `sealed class BackendCache(Func<IReadOnlyList<BackendProfile>> profiles, Func<BackendProfile, IChatBackend?> create)` with `IChatBackend? Get(string id)` and `ValueTask DisposeAllAsync()`

- [ ] **Step 1: Write the failing test**

`tests/Hotline.Core.Tests/BackendCatalogTests.cs`:
```csharp
using Hotline.Core.Backends;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class BackendCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private BackendDeps Deps() => new(new FakeLineProcessFactory(), new AgyWorkspace(_dir, new ManualTimeProvider()),
        new FileLog(Path.Combine(_dir, "h.log")), _ => false, null, null);

    [Fact]
    public void Agy_profile_creates_agy_backend()
    {
        var b = BackendFactory.Create(new BackendProfile { Id = "agy", Type = BackendType.Antigravity, Name = "Gemini (Antigravity)" }, Deps());
        Assert.IsType<AgyBackend>(b);
        Assert.Equal("Gemini (Antigravity)", b!.DisplayName);
    }

    [Theory]
    [InlineData(BackendType.Gemini)]
    [InlineData(BackendType.OpenAiCompatible)]
    [InlineData(BackendType.ClaudeCode)]
    public void Plan3_backends_are_not_available_yet(BackendType type)
    {
        Assert.Null(BackendFactory.Create(new BackendProfile { Id = "x", Type = type }, Deps()));
        Assert.False(BackendFactory.IsAvailable(type));
    }

    [Fact]
    public async Task Cache_creates_once_per_id_and_disposes_all()
    {
        var created = 0;
        var profiles = new List<BackendProfile> { new() { Id = "a", Type = BackendType.Antigravity } };
        var fake = new FakeBackend();
        var cache = new BackendCache(() => profiles, _ => { created++; return fake; });

        Assert.Same(fake, cache.Get("a"));
        Assert.Same(fake, cache.Get("a"));
        Assert.Null(cache.Get("missing"));
        Assert.Equal(1, created);
        await cache.DisposeAllAsync();
        Assert.Null(cache.Get("missing"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with CS0246 `BackendDeps`, `BackendFactory`, `BackendCache`.

- [ ] **Step 3: Write minimal implementation**

`src/Hotline.Core/Backends/BackendCatalog.cs`:
```csharp
using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Processes;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

public sealed record BackendDeps(
    ILineProcessFactory Processes, AgyWorkspace AgyWorkspace, FileLog Log,
    Func<string, bool> FileExists, string? LocalAppData, string? PathEnv);

public static class BackendFactory
{
    public static bool IsAvailable(BackendType type) => type == BackendType.Antigravity;

    /// <summary>Creates the backend for a profile, or null if that backend type isn't implemented yet (Plan 3).</summary>
    public static IChatBackend? Create(BackendProfile p, BackendDeps deps) => p.Type switch
    {
        BackendType.Antigravity => new AgyBackend(p,
            () => AgyLocator.Find(p.CliPath, deps.FileExists, deps.LocalAppData, deps.PathEnv),
            deps.AgyWorkspace, deps.Processes, deps.Log),
        _ => null,
    };
}

/// <summary>One live backend instance per profile id (backends like agy hold a running process).</summary>
public sealed class BackendCache(Func<IReadOnlyList<BackendProfile>> profiles, Func<BackendProfile, IChatBackend?> create)
{
    private readonly Dictionary<string, IChatBackend?> _instances = [];

    public IChatBackend? Get(string id)
    {
        if (_instances.TryGetValue(id, out var existing)) return existing;
        var profile = profiles().FirstOrDefault(p => p.Id == id);
        if (profile is null) return null;
        return _instances[id] = create(profile);
    }

    public async ValueTask DisposeAllAsync()
    {
        foreach (var b in _instances.Values)
            if (b is not null) await b.DisposeAsync();
        _instances.Clear();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): backend factory and per-profile backend cache" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Chat web view (HTML/CSS/JS) with reducer tests

**Files:**
- Create: `src/Hotline.App/Web/index.html`, `src/Hotline.App/Web/chat.css`, `src/Hotline.App/Web/chat-core.js`, `src/Hotline.App/Web/chat.js`
- Create (downloaded): `src/Hotline.App/Web/vendor/markdown-it.min.js`, `src/Hotline.App/Web/vendor/highlight.min.js`, `src/Hotline.App/Web/vendor/github-dark.min.css`, `src/Hotline.App/Web/vendor/LICENSES.md`
- Test: `tests/web/chat-core.test.mjs`
- Modify: `src/Hotline.App/Hotline.App.csproj` (copy `Web\**`), `.github/workflows/ci.yml` (node test step)

**Interfaces:**
- Produces the **bridge protocol** consumed by Task 8:
  - **host → web:**
    - `{type:'user', id, text, attachments:[{name, kind}]}`
    - `{type:'assistantStart', id, backend}`, `{type:'delta', id, text, replace}`
    - `{type:'done'|'cancelled', id}`, `{type:'error', id, code, message}`
    - `{type:'reset'}`, `{type:'theme', css}`, `{type:'backends', items:[{id, name, available}], selected}`
    - `{type:'attachmentAdded', id, name, kind, thumb}`, `{type:'attachmentRemoved', id}`, `{type:'attachmentsCleared'}`
    - `{type:'toast', message}`, `{type:'focus'}`
  - **web → host:**
    - `{type:'ready'}`, `{type:'send', text}`, `{type:'retry'}`, `{type:'cancel'}`, `{type:'newChat'}`
    - `{type:'pickFiles'}`, `{type:'captureWindow'}`, `{type:'captureScreen'}`
    - `{type:'pasteImage'|'dropFile', name, mime, base64}`, `{type:'removeAttachment', id}`
    - `{type:'selectBackend', id}`, `{type:'height', value}`, `{type:'escape'}`, `{type:'openLink', url}`
- `chat-core.js` exports `initialState()` and `reduce(state, msg)` (pure).

- [ ] **Step 1: Write the failing reducer tests**

`tests/web/chat-core.test.mjs`:
```js
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { initialState, reduce } from '../../src/Hotline.App/Web/chat-core.js';

const run = (...msgs) => msgs.reduce(reduce, initialState());

test('user + streamed assistant message', () => {
  const s = run(
    { type: 'user', id: 'u1', text: 'hi', attachments: [] },
    { type: 'assistantStart', id: 'a1', backend: 'Gemini (Antigravity)' },
    { type: 'delta', id: 'a1', text: 'Hel', replace: false },
    { type: 'delta', id: 'a1', text: 'lo', replace: false },
    { type: 'done', id: 'a1' });
  assert.equal(s.messages.length, 2);
  assert.deepEqual(s.messages[1], { id: 'a1', role: 'assistant', text: 'Hello', status: 'done', backend: 'Gemini (Antigravity)' });
  assert.equal(s.busy, false);
});

test('replace delta overwrites text', () => {
  const s = run({ type: 'assistantStart', id: 'a', backend: 'x' },
    { type: 'delta', id: 'a', text: 'draft', replace: false },
    { type: 'delta', id: 'a', text: 'final', replace: true });
  assert.equal(s.messages[0].text, 'final');
  assert.equal(s.busy, true);
});

test('error marks message and clears busy', () => {
  const s = run({ type: 'assistantStart', id: 'a', backend: 'x' },
    { type: 'error', id: 'a', code: 'NotInstalled', message: 'agy missing' });
  assert.equal(s.messages[0].status, 'error');
  assert.deepEqual(s.messages[0].error, { code: 'NotInstalled', message: 'agy missing' });
  assert.equal(s.busy, false);
});

test('error for an answer that never started still shows', () => {
  const s = run({ type: 'error', id: 'a', code: 'NotConfigured', message: 'nope' });
  assert.equal(s.messages[0].role, 'assistant');
  assert.equal(s.messages[0].status, 'error');
});

test('attachments add/remove/clear', () => {
  let s = run({ type: 'attachmentAdded', id: '1', name: 'a.png', kind: 'Image', thumb: 'data:x' },
    { type: 'attachmentAdded', id: '2', name: 'b.md', kind: 'Text', thumb: null });
  assert.deepEqual(s.attachments.map(a => a.id), ['1', '2']);
  s = reduce(s, { type: 'attachmentRemoved', id: '1' });
  assert.deepEqual(s.attachments.map(a => a.id), ['2']);
  s = reduce(s, { type: 'attachmentsCleared' });
  assert.equal(s.attachments.length, 0);
});

test('reset clears messages and attachments but keeps backends', () => {
  let s = run({ type: 'backends', items: [{ id: 'agy', name: 'G', available: true }], selected: 'agy' },
    { type: 'user', id: 'u', text: 'x', attachments: [] },
    { type: 'attachmentAdded', id: '1', name: 'a.png', kind: 'Image', thumb: null });
  s = reduce(s, { type: 'reset' });
  assert.equal(s.messages.length, 0);
  assert.equal(s.attachments.length, 0);
  assert.equal(s.selectedBackend, 'agy');
});

test('unknown messages leave state unchanged', () => {
  const s0 = initialState();
  assert.equal(reduce(s0, { type: 'mystery' }), s0);
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `node --test tests/web`
Expected: FAIL with `Cannot find module '...chat-core.js'` (ERR_MODULE_NOT_FOUND).

- [ ] **Step 3: Vendor the libraries**

```powershell
$v = 'src/Hotline.App/Web/vendor'; New-Item -ItemType Directory -Force $v | Out-Null
Invoke-WebRequest https://cdn.jsdelivr.net/npm/markdown-it@14.1.0/dist/markdown-it.min.js -OutFile "$v/markdown-it.min.js"
Invoke-WebRequest https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/highlight.min.js -OutFile "$v/highlight.min.js"
Invoke-WebRequest https://cdnjs.cloudflare.com/ajax/libs/highlight.js/11.11.1/styles/github-dark.min.css -OutFile "$v/github-dark.min.css"
(Get-ChildItem $v).Name
```
Expected: the three files are listed (about 123 KB, 127 KB and 1.3 KB).

`src/Hotline.App/Web/vendor/LICENSES.md`:
```markdown
# Vendored libraries
- markdown-it 14.1.0 — MIT — https://github.com/markdown-it/markdown-it
- highlight.js 11.11.1 (+ github-dark theme) — BSD-3-Clause — https://github.com/highlightjs/highlight.js
```

- [ ] **Step 4: Write the web view**

`src/Hotline.App/Web/chat-core.js`:
```js
// Pure chat state reducer (tested with `node --test tests/web`). No DOM access here.
export function initialState() {
  return { messages: [], attachments: [], backends: [], selectedBackend: null, busy: false };
}

function updateMessage(state, id, fn, createIfMissing) {
  let found = false;
  const messages = state.messages.map(m => (m.id === id ? ((found = true), fn(m)) : m));
  if (!found && createIfMissing) messages.push(fn(createIfMissing));
  return messages;
}

export function reduce(state, msg) {
  switch (msg.type) {
    case 'user':
      return { ...state, messages: [...state.messages, { id: msg.id, role: 'user', text: msg.text, attachments: msg.attachments ?? [] }] };
    case 'assistantStart':
      return { ...state, busy: true,
        messages: [...state.messages, { id: msg.id, role: 'assistant', text: '', status: 'streaming', backend: msg.backend }] };
    case 'delta':
      return { ...state, messages: updateMessage(state, msg.id, m => ({ ...m, text: msg.replace ? msg.text : m.text + msg.text })) };
    case 'done':
    case 'cancelled':
      return { ...state, busy: false, messages: updateMessage(state, msg.id, m => ({ ...m, status: msg.type })) };
    case 'error': {
      const placeholder = { id: msg.id, role: 'assistant', text: '', backend: null };
      const { status: _ignored, ...rest } = placeholder;
      return { ...state, busy: false,
        messages: updateMessage(state, msg.id, m => ({ ...m, status: 'error', error: { code: msg.code, message: msg.message } }), rest) };
    }
    case 'reset':
      return { ...state, messages: [], attachments: [], busy: false };
    case 'backends':
      return { ...state, backends: msg.items, selectedBackend: msg.selected };
    case 'attachmentAdded':
      return { ...state, attachments: [...state.attachments, { id: msg.id, name: msg.name, kind: msg.kind, thumb: msg.thumb ?? null }] };
    case 'attachmentRemoved':
      return { ...state, attachments: state.attachments.filter(a => a.id !== msg.id) };
    case 'attachmentsCleared':
      return { ...state, attachments: [] };
    default:
      return state;
  }
}
```

`src/Hotline.App/Web/index.html`:
```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta http-equiv="Content-Security-Policy"
        content="default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'">
  <link rel="stylesheet" href="vendor/github-dark.min.css">
  <link rel="stylesheet" href="chat.css">
  <style id="theme"></style>
  <script src="vendor/markdown-it.min.js"></script>
  <script src="vendor/highlight.min.js"></script>
  <script type="module" src="chat.js"></script>
</head>
<body>
  <main id="app">
    <section id="messages" aria-live="polite"></section>
    <div id="toast" role="status" hidden></div>
    <section id="composer">
      <div id="chips"></div>
      <div id="row">
        <div class="menu-wrap">
          <button id="plus" class="icon" title="Add files or capture" aria-haspopup="menu">+</button>
          <div id="menu" role="menu" hidden>
            <button role="menuitem" data-cmd="pickFiles">📎 Attach files…</button>
            <button role="menuitem" data-cmd="captureWindow">🪟 Capture window</button>
            <button role="menuitem" data-cmd="captureScreen">🖥️ Capture screen</button>
          </div>
        </div>
        <textarea id="input" rows="1" placeholder="📞 Hotline" spellcheck="true"></textarea>
        <button id="backend" class="pill" title="Switch AI"></button>
        <button id="send" class="icon" title="Send (Enter)">↑</button>
      </div>
    </section>
  </main>
</body>
</html>
```

`src/Hotline.App/Web/chat.css`:
```css
/* All colors/sizes come from --hl-* tokens injected by the host (Core ThemeTokens). Background stays transparent for acrylic. */
:root { color-scheme: light dark; }
* { box-sizing: border-box; }
html, body { margin: 0; height: 100%; background: transparent; overflow: hidden;
  font-family: var(--hl-font); font-size: var(--hl-font-size); color: var(--hl-text); }
#app { display: flex; flex-direction: column; height: 100vh; padding: 10px 12px 12px; }
#messages { flex: 1 1 auto; overflow-y: auto; display: none; flex-direction: column; gap: 10px; padding: 4px 2px 10px; }
#app.has-messages #messages { display: flex; }
.msg { max-width: 100%; line-height: 1.5; overflow-wrap: anywhere; }
.msg.user { align-self: flex-end; max-width: 85%; background: var(--hl-user-bubble); padding: 8px 12px; border-radius: var(--hl-radius); white-space: pre-wrap; }
.msg.assistant { align-self: stretch; }
.msg.assistant .meta { color: var(--hl-muted); font-size: 0.8em; margin-bottom: 2px; }
.msg.assistant.streaming .body::after { content: "▍"; color: var(--hl-accent); animation: blink 1s steps(2) infinite; }
.msg .body > :first-child { margin-top: 0; } .msg .body > :last-child { margin-bottom: 0; }
.msg pre { background: var(--hl-code-bg); padding: 10px; border-radius: var(--hl-radius); overflow-x: auto; position: relative; }
.msg code { font-family: 'Cascadia Code', Consolas, monospace; font-size: 0.92em; }
.msg :not(pre) > code { background: var(--hl-code-bg); padding: 1px 4px; border-radius: 4px; }
.msg pre .copy { position: absolute; top: 6px; right: 6px; font-size: 0.75em; opacity: 0.7; }
.msg .error { color: #ff8a80; } .msg .error button { margin-left: 8px; }
.msg .att { color: var(--hl-muted); font-size: 0.85em; }
a { color: var(--hl-accent); }
#toast { position: absolute; left: 12px; right: 12px; bottom: 64px; padding: 8px 12px; background: var(--hl-surface-strong);
  border: 1px solid var(--hl-border); border-radius: var(--hl-radius); }
#composer { flex: 0 0 auto; }
#chips { display: flex; flex-wrap: wrap; gap: 6px; margin-bottom: 6px; } #chips:empty { display: none; }
.chip { display: flex; align-items: center; gap: 6px; padding: 3px 6px; background: var(--hl-surface-strong);
  border: 1px solid var(--hl-border); border-radius: var(--hl-radius); font-size: 0.85em; max-width: 220px; }
.chip img { width: 28px; height: 28px; object-fit: cover; border-radius: 4px; }
.chip span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
#row { display: flex; align-items: flex-end; gap: 6px; background: var(--hl-surface); border: 1px solid var(--hl-border);
  border-radius: calc(var(--hl-radius) + 4px); padding: 6px; }
#row:focus-within { border-color: var(--hl-accent); }
#input { flex: 1; resize: none; border: 0; outline: 0; background: transparent; color: inherit; font: inherit;
  line-height: 1.45; max-height: 9em; padding: 5px 4px; }
button { font: inherit; color: inherit; background: transparent; border: 0; cursor: pointer; border-radius: var(--hl-radius); }
button:hover { background: var(--hl-surface-strong); }
.icon { width: 32px; height: 32px; font-size: 1.15em; flex: 0 0 auto; }
#send { background: var(--hl-accent); color: white; } #send.stop { background: var(--hl-surface-strong); color: inherit; }
.pill { font-size: 0.8em; padding: 6px 10px; color: var(--hl-muted); border: 1px solid var(--hl-border); white-space: nowrap; }
.menu-wrap { position: relative; }
#menu { position: absolute; bottom: 40px; left: 0; display: flex; flex-direction: column; min-width: 190px; padding: 4px;
  background: var(--hl-surface-strong); border: 1px solid var(--hl-border); border-radius: var(--hl-radius);
  backdrop-filter: blur(20px); z-index: 10; }
#menu[hidden] { display: none; }
#menu button { text-align: left; padding: 7px 10px; }
body.dragging #row { border-style: dashed; border-color: var(--hl-accent); }
@keyframes blink { 50% { opacity: 0; } }
```

`src/Hotline.App/Web/chat.js`:
```js
import { initialState, reduce } from './chat-core.js';

const host = window.chrome?.webview;
const post = msg => host?.postMessage(msg);
const $ = id => document.getElementById(id);
const els = { app: $('app'), messages: $('messages'), chips: $('chips'), input: $('input'), send: $('send'),
  plus: $('plus'), menu: $('menu'), backend: $('backend'), toast: $('toast'), theme: $('theme') };

const md = window.markdownit({ html: false, linkify: true, breaks: false,
  highlight: (code, lang) => {
    try { return lang && hljs.getLanguage(lang) ? hljs.highlight(code, { language: lang }).value : hljs.highlightAuto(code).value; }
    catch { return ''; }
  } });

let state = initialState();
const rendered = new Map(); // message id -> element

function dispatch(msg) {
  const prev = state;
  state = reduce(state, msg);
  if (state === prev) return;
  render(msg);
}

function escapeHtml(s) { return s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c])); }

function renderMessage(m) {
  let el = rendered.get(m.id);
  if (!el) { el = document.createElement('div'); rendered.set(m.id, el); els.messages.appendChild(el); }
  el.className = `msg ${m.role} ${m.status ?? ''}`;
  if (m.role === 'user') {
    const atts = (m.attachments ?? []).map(a => `📎 ${escapeHtml(a.name)}`).join('  ');
    el.innerHTML = escapeHtml(m.text) + (atts ? `<div class="att">${atts}</div>` : '');
    return;
  }
  const meta = m.backend ? `<div class="meta">${escapeHtml(m.backend)}</div>` : '';
  const err = m.status === 'error'
    ? `<div class="error">⚠ ${escapeHtml(m.error.message)}<button data-retry>Retry</button></div>` : '';
  el.innerHTML = `${meta}<div class="body">${md.render(m.text || '')}</div>${err}`;
  el.querySelectorAll('pre').forEach(pre => {
    const b = document.createElement('button'); b.className = 'copy'; b.textContent = 'Copy';
    b.onclick = () => navigator.clipboard.writeText(pre.innerText.replace(/Copy$/, ''));
    pre.appendChild(b);
  });
}

function render(msg) {
  const nearBottom = els.messages.scrollHeight - els.messages.scrollTop - els.messages.clientHeight < 40;
  if (msg.type === 'reset') { rendered.clear(); els.messages.innerHTML = ''; }
  for (const m of state.messages) if (!rendered.has(m.id) || m.id === msg.id) renderMessage(m);
  els.app.classList.toggle('has-messages', state.messages.length > 0);
  els.chips.innerHTML = state.attachments.map(a =>
    `<div class="chip" data-id="${a.id}">${a.thumb ? `<img src="${a.thumb}" alt="">` : '📄'}<span>${escapeHtml(a.name)}</span><button title="Remove" data-remove="${a.id}">×</button></div>`).join('');
  const sel = state.backends.find(b => b.id === state.selectedBackend);
  els.backend.textContent = sel ? sel.name : '';
  els.backend.hidden = state.backends.length === 0;
  els.send.classList.toggle('stop', state.busy);
  els.send.textContent = state.busy ? '■' : '↑';
  els.send.title = state.busy ? 'Stop' : 'Send (Enter)';
  if (nearBottom) els.messages.scrollTop = els.messages.scrollHeight;
  reportHeight();
}

let lastHeight = 0;
function reportHeight() {
  requestAnimationFrame(() => {
    const composer = $('composer').offsetHeight;
    const content = state.messages.length ? els.messages.scrollHeight + 14 : 0;
    const h = Math.ceil(content + composer + 22);
    if (Math.abs(h - lastHeight) > 1) { lastHeight = h; post({ type: 'height', value: h }); }
  });
}

function showToast(text) {
  els.toast.textContent = text; els.toast.hidden = false;
  clearTimeout(showToast.t); showToast.t = setTimeout(() => (els.toast.hidden = true), 4000);
}

function autoGrow() { els.input.style.height = 'auto'; els.input.style.height = els.input.scrollHeight + 'px'; reportHeight(); }

function send() {
  if (state.busy) { post({ type: 'cancel' }); return; }
  const text = els.input.value.trim();
  if (!text && state.attachments.length === 0) return;
  post({ type: 'send', text });
  els.input.value = ''; autoGrow();
}

function readFile(file, type) {
  const reader = new FileReader();
  reader.onload = () => post({ type, name: file.name || 'pasted.png', mime: file.type, base64: String(reader.result).split(',')[1] ?? '' });
  reader.readAsDataURL(file);
}

// --- events -------------------------------------------------------------------------------
els.input.addEventListener('input', autoGrow);
els.input.addEventListener('keydown', e => {
  if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); send(); }
});
els.send.addEventListener('click', send);
els.plus.addEventListener('click', () => { els.menu.hidden = !els.menu.hidden; });
els.menu.addEventListener('click', e => {
  const cmd = e.target.closest('[data-cmd]')?.dataset.cmd;
  if (cmd) { els.menu.hidden = true; post({ type: cmd }); }
});
els.chips.addEventListener('click', e => { const id = e.target.dataset.remove; if (id) post({ type: 'removeAttachment', id }); });
els.messages.addEventListener('click', e => {
  if (e.target.matches('[data-retry]')) { post({ type: 'retry' }); return; }
  const a = e.target.closest('a[href]');
  if (a) { e.preventDefault(); post({ type: 'openLink', url: a.href }); }
});
els.backend.addEventListener('click', () => {
  const avail = state.backends.filter(b => b.available);
  if (avail.length < 2) { showToast(state.backends.length > 1 ? 'More backends arrive in a later update.' : 'Only one AI is set up.'); return; }
  const i = avail.findIndex(b => b.id === state.selectedBackend);
  post({ type: 'selectBackend', id: avail[(i + 1) % avail.length].id });
});
document.addEventListener('keydown', e => {
  if (e.key === 'Escape') { e.preventDefault(); if (!els.menu.hidden) els.menu.hidden = true; else post({ type: 'escape' }); }
  else if (e.key.toLowerCase() === 'n' && e.ctrlKey) { e.preventDefault(); post({ type: 'newChat' }); }
});
document.addEventListener('paste', e => {
  const files = [...(e.clipboardData?.files ?? [])];
  if (files.length) { e.preventDefault(); files.forEach(f => readFile(f, 'pasteImage')); }
});
document.addEventListener('dragover', e => { e.preventDefault(); document.body.classList.add('dragging'); });
document.addEventListener('dragleave', e => { if (!e.relatedTarget) document.body.classList.remove('dragging'); });
document.addEventListener('drop', e => {
  e.preventDefault(); document.body.classList.remove('dragging');
  [...(e.dataTransfer?.files ?? [])].forEach(f => readFile(f, 'dropFile'));
});
new ResizeObserver(reportHeight).observe(els.messages);
new ResizeObserver(reportHeight).observe($('composer'));

host?.addEventListener('message', e => {
  const msg = e.data;
  if (msg.type === 'theme') { els.theme.textContent = msg.css; return; }
  if (msg.type === 'toast') { showToast(msg.message); return; }
  if (msg.type === 'focus') { els.input.focus(); return; }
  dispatch(msg);
});
post({ type: 'ready' });
els.input.focus();
```

Add to `src/Hotline.App/Hotline.App.csproj` inside the existing `<ItemGroup>` with Assets/Public:
```xml
    <Content Include="Web\**" CopyToOutputDirectory="PreserveNewest" />
```

Add to `.github/workflows/ci.yml` after the "Unit tests" step:
```yaml
      - uses: actions/setup-node@v4
        with:
          node-version: '24'

      - name: Web tests
        run: node --test tests/web
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `node --test tests/web`
Expected: `# pass 7` and `# fail 0`.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(web): chat view with markdown, composer, + menu, paste/drop and bridge protocol" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: WebView2 host, popup growth and wiring (App) — first end-to-end chat

**Files:**
- Create: `src/Hotline.App/Chat/ChatHost.cs`, `src/Hotline.App/Interop/ChildProcessJob.cs`
- Modify: `src/Hotline.App/PopupWindow.xaml`, `src/Hotline.App/PopupWindow.xaml.cs`, `src/Hotline.App/ActivationRouter.cs`, `src/Hotline.App/App.xaml.cs`, `src/Hotline.App/Interop/Native.cs`

**Interfaces:**
- Consumes: everything above.
- Produces:
  - `PopupWindow`:
    - `WebView2 Web`, `IDisposable Modal()`, `Task<T> WithHiddenAsync<T>(Func<Task<T>> work)`, `void SetContentHeight(double cssPx)`
    - events `Shown`, `NewChatRequested`, `CaptureRequested(bool window)`
    - constructor `(WindowSettings, int maxHeightDip, PopupToggleGuard, FileLog, bool showDebugStatus)`
  - `ChatHost(PopupWindow, ChatController, AttachmentTray, HotlineSettings, SettingsStore, FileLog, Func<IReadOnlyList<BackendProfile>>)` with `Task InitializeAsync(string dataDir)` and `void NewChat()`
  - Methods filled in by Task 9: `Task PickFilesAsync()`, `Task CaptureAsync(bool window)`, `Task AddBytesAsync(string name, string? mime, byte[] data)`

- [ ] **Step 1: Popup XAML hosts WebView2**

Replace `src/Hotline.App/PopupWindow.xaml` with:
```xml
<Window
    x:Class="Hotline.App.PopupWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Hotline">
    <Grid x:Name="Root">
        <WebView2 x:Name="Web" />
        <!-- Debug-only status line (diagnostics.verboseLogging or Debug build). -->
        <TextBlock x:Name="StatusText" Margin="14,2,14,0" VerticalAlignment="Top" Opacity="0.6" IsHitTestVisible="False"
                   Style="{StaticResource CaptionTextBlockStyle}" Visibility="Collapsed" />
    </Grid>
</Window>
```

- [ ] **Step 2: PopupWindow: growth, modal, events**

In `src/Hotline.App/PopupWindow.xaml.cs` make these exact changes:
1. Constructor signature → `public PopupWindow(WindowSettings settings, int maxHeightDip, PopupToggleGuard guard, FileLog log, bool showDebugStatus)`. Store `_maxHeightDip = maxHeightDip;`. Add fields `private readonly int _maxHeightDip; private int _modal; private RectI _bar; private RectI _work; private double _scale = 1; private int _contentPx;`. After `InitializeComponent();` add `Web.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;`.
2. Delete the `Root_KeyDown` method and `ResetConversation()` (Esc now comes from the web view).
3. In `ShowPopup()`, replace `PromptBox.Focus(FocusState.Programmatic);` with:
   ```csharp
        Web.Focus(FocusState.Programmatic);
        Shown?.Invoke();
   ```
4. In `OnActivated`, change the condition to `if (e.WindowActivationState == WindowActivationState.Deactivated && _settings.HideOnBlur && _modal == 0)`.
5. At the end of `PlaceOnActiveMonitor()`, replace `AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));` with:
   ```csharp
        (_bar, _work, _scale) = (r, new RectI(wa.X, wa.Y, wa.Width, wa.Height), scale);
        ApplyHeight();
   ```
6. Add these members:
```csharp
    public event Action? Shown;
    public event Action? NewChatRequested;
    public event Action<bool>? CaptureRequested;

    public void RequestNewChat() => NewChatRequested?.Invoke();
    public void RequestCapture(bool window) => CaptureRequested?.Invoke(window);

    /// <summary>Content height reported by the chat view (CSS px); the popup grows upward from the bar.</summary>
    public void SetContentHeight(double cssPx)
    {
        _contentPx = (int)Math.Ceiling(cssPx * _scale);
        if (AppWindow.IsVisible) ApplyHeight();
    }

    private void ApplyHeight()
    {
        if (_bar.Width == 0) return;
        var r = PopupGeometry.GrowUp(_bar, _contentPx, (int)Math.Round(_maxHeightDip * _scale), _work);
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
    }

    /// <summary>While disposed-not-yet, focus loss (file dialogs, captures) does not hide the popup.</summary>
    public IDisposable Modal()
    {
        _modal++;
        return new Releaser(() => _modal--);
    }

    /// <summary>Hides the popup briefly (e.g. to capture what is underneath), then brings it back.</summary>
    public async Task<T> WithHiddenAsync<T>(Func<Task<T>> work)
    {
        using var _ = Modal();
        AppWindow.Hide();
        await Task.Delay(220); // let DWM repaint without the popup
        try { return await work(); }
        finally
        {
            Activate();
            Native.SetForegroundWindow(Hwnd);
            Web.Focus(FocusState.Programmatic);
        }
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
```

- [ ] **Step 3: Router uses popup events**

In `src/Hotline.App/ActivationRouter.cs`, replace the body of `Execute` with:
```csharp
        switch (action)
        {
            case KeyAction.None:
                break;
            case KeyAction.TogglePopup:
                popup.Toggle();
                break;
            case KeyAction.NewChat:
                popup.RequestNewChat();
                popup.ShowPopup();
                break;
            case KeyAction.CaptureWindow:
                popup.ShowPopup();
                popup.RequestCapture(window: true);
                break;
            default:
                // ShowPopup; RegionSelect arrives in a later plan and just opens the popup for now.
                popup.ShowPopup();
                break;
        }
```

- [ ] **Step 4: Child-process job and native imports**

Append to `Native` in `src/Hotline.App/Interop/Native.cs`:
```csharp
    public const uint SRCCOPY = 0x00CC0020, CAPTUREBLT = 0x40000000;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    public const int JobObjectExtendedLimitInformation = 9;

    [DllImport("user32.dll")] public static extern nint GetDC(nint hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(nint hWnd, nint hdc);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hWnd);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleDC(nint hdc);
    [DllImport("gdi32.dll")] public static extern nint CreateCompatibleBitmap(nint hdc, int w, int h);
    [DllImport("gdi32.dll")] public static extern nint SelectObject(nint hdc, nint obj);
    [DllImport("gdi32.dll")] public static extern bool BitBlt(nint dst, int x, int y, int w, int h, nint src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] public static extern int GetDIBits(nint hdc, nint bmp, uint start, uint lines, byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(nint hdc);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hWnd, int attr, out RECT value, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint CreateJobObject(nint attrs, string? name);
    [DllImport("kernel32.dll")] public static extern bool SetInformationJobObject(nint job, int cls, ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION info, int size);
    [DllImport("kernel32.dll")] public static extern bool AssignProcessToJobObject(nint job, nint process);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public int biSize; public int biWidth; public int biHeight; public short biPlanes; public short biBitCount;
        public int biCompression; public int biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
        public int biClrUsed; public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit; public long PerJobUserTimeLimit; public uint LimitFlags;
        public nuint MinimumWorkingSetSize; public nuint MaximumWorkingSetSize; public uint ActiveProcessLimit;
        public nuint Affinity; public uint PriorityClass; public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation; public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }
```

`src/Hotline.App/Interop/ChildProcessJob.cs`:
```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using Hotline.Core.Diagnostics;

namespace Hotline.App.Interop;

/// <summary>Job object with kill-on-close: child processes (agy) die with Hotline, even on a crash.</summary>
internal sealed class ChildProcessJob
{
    private readonly nint _job;
    private readonly FileLog _log;

    public ChildProcessJob(FileLog log)
    {
        _log = log;
        _job = Native.CreateJobObject(0, null);
        var info = new Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = Native.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        if (_job == 0 || !Native.SetInformationJobObject(_job, Native.JobObjectExtendedLimitInformation, ref info,
                Marshal.SizeOf<Native.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>()))
            log.Error($"child process job setup failed (error {Marshal.GetLastWin32Error()}); agy may outlive a crash");
    }

    public void Add(Process process)
    {
        if (_job != 0 && !Native.AssignProcessToJobObject(_job, process.Handle))
            _log.Error($"could not add process {process.Id} to job");
    }
}
```

- [ ] **Step 5: ChatHost (bridge; attachments and capture are filled in by Task 9)**

`src/Hotline.App/Chat/ChatHost.cs`:
```csharp
using System.Text.Json;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Theming;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Hotline.App.Chat;

/// <summary>Bridges the WebView2 chat view and the ChatController. All members run on the UI thread.</summary>
internal sealed partial class ChatHost(
    PopupWindow popup, ChatController chat, AttachmentTray tray, HotlineSettings settings, SettingsStore store, FileLog log,
    Func<IReadOnlyList<BackendProfile>> profiles)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string Origin = "https://hotline.app/";
    private bool _ready;

    public async Task InitializeAsync(string dataDir)
    {
        var env = await CoreWebView2Environment.CreateWithOptionsAsync(null, Path.Combine(dataDir, "WebView2"), new CoreWebView2EnvironmentOptions());
        await popup.Web.EnsureCoreWebView2Async(env);
        var core = popup.Web.CoreWebView2;
        core.SetVirtualHostNameToFolderMapping("hotline.app", Path.Combine(AppContext.BaseDirectory, "Web"),
            CoreWebView2HostResourceAccessKind.Allow);
        var devtools = App.IsDebugBuild || settings.Diagnostics.VerboseLogging;
        core.Settings.AreDevToolsEnabled = devtools;
        core.Settings.AreDefaultContextMenusEnabled = devtools;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.WebMessageReceived += (_, e) => Guard("web message", () => OnWebMessage(e.WebMessageAsJson));
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenLink(e.Uri); };
        core.NavigationStarting += (_, e) =>
        {
            if (e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
            e.Cancel = true;
            OpenLink(e.Uri);
        };
        popup.Web.Source = new Uri(Origin + "index.html");

        chat.Event += e => Guard("chat event", () => OnChatEvent(e));
        popup.Shown += () => Post(new { type = "focus" });
        popup.NewChatRequested += NewChat;
        popup.CaptureRequested += window => Run("capture", () => CaptureAsync(window));
    }

    public void NewChat()
    {
        chat.NewChat();
        tray.TakeAll();
        Post(new { type = "attachmentsCleared" });
    }

    private void OnWebMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var m = doc.RootElement;
        string Str(string name) => m.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
        var type = Str("type");
        log.Debug($"web -> host: {type}");
        switch (type)
        {
            case "ready":
                _ready = true;
                log.Info("chat view ready");
                PostTheme();
                PostBackends();
                break;
            case "send": Run("send", () => SendAsync(Str("text"))); break;
            case "retry": Run("retry", chat.RetryAsync); break;
            case "cancel": chat.Cancel(); break;
            case "newChat": NewChat(); break;
            case "pickFiles": Run("pick files", PickFilesAsync); break;
            case "captureWindow": Run("capture window", () => CaptureAsync(window: true)); break;
            case "captureScreen": Run("capture screen", () => CaptureAsync(window: false)); break;
            case "pasteImage" or "dropFile":
                Run("add file", () => AddBytesAsync(Str("name"), Str("mime"), Convert.FromBase64String(Str("base64"))));
                break;
            case "removeAttachment":
                if (tray.Remove(Str("id"))) Post(new { type = "attachmentRemoved", id = Str("id") });
                break;
            case "selectBackend": SelectBackend(Str("id")); break;
            case "height": popup.SetContentHeight(m.GetProperty("value").GetDouble()); break;
            case "escape": popup.HidePopup(); break;
            case "openLink": OpenLink(Str("url")); break;
        }
    }

    private async Task SendAsync(string text)
    {
        if (!chat.CanAccept(tray.Items, out var reason))
        {
            Toast(reason!);
            return;
        }
        var attachments = tray.TakeAll();
        Post(new { type = "attachmentsCleared" });
        log.Info($"chat send via {chat.BackendId}: {text.Length} chars, {attachments.Count} attachment(s)");
        await chat.SendAsync(text, attachments);
    }

    private void OnChatEvent(ChatEvent e)
    {
        switch (e)
        {
            case UserMessageAdded u:
                Post(new { type = "user", id = u.Message.Id, text = u.Message.Text,
                    attachments = u.Message.Attachments.Select(a => new { name = a.Name, kind = a.Kind.ToString() }) });
                break;
            case AssistantStarted s: Post(new { type = "assistantStart", id = s.Id, backend = s.BackendName }); break;
            case AssistantDelta d: Post(new { type = "delta", id = d.Id, text = d.Text, replace = d.Replace }); break;
            case AssistantCompleted c: log.Info("chat answer completed"); Post(new { type = "done", id = c.Id }); break;
            case AssistantCancelled c: Post(new { type = "cancelled", id = c.Id }); break;
            case AssistantFailed f:
                log.Error($"chat answer failed: {f.Kind}: {f.Message}");
                Post(new { type = "error", id = f.Id, code = f.Kind.ToString(), message = f.Message });
                break;
            case ConversationReset: Post(new { type = "reset" }); break;
        }
    }

    private void SelectBackend(string id)
    {
        if (profiles().All(p => p.Id != id)) return;
        chat.BackendId = id;
        settings.Chat.DefaultBackend = id;
        try { store.Save(settings); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("saving backend choice failed", ex); }
        PostBackends();
    }

    private void PostTheme()
    {
        var dark = settings.Window.Theme switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
        Post(new { type = "theme", css = (dark ? ThemeTokens.Dark : ThemeTokens.Light).ToCss() });
    }

    private void PostBackends() => Post(new
    {
        type = "backends",
        items = profiles().Select(p => new { id = p.Id, name = p.Name, available = BackendFactory.IsAvailable(p.Type) }),
        selected = chat.BackendId,
    });

    private void Toast(string message) => Post(new { type = "toast", message });

    private void Post(object message)
    {
        if (!_ready || popup.Web.CoreWebView2 is null) return;
        popup.Web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Json));
    }

    private void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == "https" || uri.Scheme == "http"))
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { log.Error($"{what} failed", ex); }
    }

    private void Run(string what, Func<Task> work) => _ = RunAsync(what, work);

    private async Task RunAsync(string what, Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex)
        {
            log.Error($"{what} failed", ex);
            Toast($"Couldn't {what}: {ex.Message}");
        }
    }

    // Filled in by Task 9 (attachments & capture). Until then they report "coming soon".
    private partial Task PickFilesAsync();
    private partial Task CaptureAsync(bool window);
    private partial Task AddBytesAsync(string name, string? mime, byte[] data);
}
```
Create `src/Hotline.App/Chat/ChatHost.Attachments.cs` with temporary bodies (replaced in Task 9):
```csharp
namespace Hotline.App.Chat;

internal sealed partial class ChatHost
{
    private partial Task PickFilesAsync() { Toast("Attaching files arrives in the next step."); return Task.CompletedTask; }
    private partial Task CaptureAsync(bool window) { Toast("Capture arrives in the next step."); return Task.CompletedTask; }
    private partial Task AddBytesAsync(string name, string? mime, byte[] data) { Toast("Attachments arrive in the next step."); return Task.CompletedTask; }
}
```

- [ ] **Step 6: Wire it up in App**

In `src/Hotline.App/App.xaml.cs`:
1. Add usings: `using Hotline.App.Chat; using Hotline.Core.Backends; using Hotline.Core.Backends.Agy; using Hotline.Core.Chat; using Hotline.Core.Processes;`.
2. Add fields: `private ChatHost? _chatHost; private BackendCache? _backends;`.
3. Change the popup construction to pass the max height:
   `_popup = new PopupWindow(settings.Window, settings.Chat.MaxHeight, new PopupToggleGuard(TimeProvider.System, TimeSpan.FromMilliseconds(300)), _log, showDebugStatus: _log.Verbose);`
4. Immediately after the `_router = new ActivationRouter(...)` statement, add:
```csharp
        var job = new ChildProcessJob(_log);
        var agyWorkspace = new AgyWorkspace(Path.Combine(dataDir, "agy-workspace"), TimeProvider.System);
        try { agyWorkspace.PruneAttachments(TimeSpan.FromDays(1)); } catch (IOException ex) { _log.Error("attachment prune failed", ex); }
        var deps = new BackendDeps(new SystemLineProcessFactory(job.Add), agyWorkspace, _log, File.Exists,
            Environment.GetEnvironmentVariable("LOCALAPPDATA"), Environment.GetEnvironmentVariable("PATH"));
        _backends = new BackendCache(() => settings.Chat.Backends, p => BackendFactory.Create(p, deps));
        HistoryStore? history = null;
        if (settings.Chat.SaveHistory)
        {
            history = new HistoryStore(Path.Combine(dataDir, "history"), TimeProvider.System);
            try { history.Prune(settings.Chat.HistoryRetentionDays); } catch (IOException ex) { _log.Error("history prune failed", ex); }
        }
        var chat = new ChatController(_backends.Get, history, TimeProvider.System, _log) { BackendId = settings.Chat.DefaultBackend };
        _chatHost = new ChatHost(_popup, chat, new AttachmentTray(new AttachmentLimits()), settings, store, _log, () => settings.Chat.Backends);
        _ = InitChatAsync(dataDir);
```
5. Add the method:
```csharp
    private async Task InitChatAsync(string dataDir)
    {
        try { await _chatHost!.InitializeAsync(dataDir); }
        catch (Exception ex) { _log?.Error("chat view failed to initialize (is the WebView2 runtime installed?)", ex); }
    }
```
6. In the tray `onQuit` lambda, before `Exit();`, add `_backends?.DisposeAllAsync().AsTask().Wait(TimeSpan.FromSeconds(2));`. The job object kills agy regardless; this just shuts it down gracefully.

- [ ] **Step 7: Build, install, verify the view and a real agy chat**

Run:
```powershell
dotnet build src/Hotline.App/Hotline.App.csproj -c Debug -p:Platform=x64
powershell -File tests\smoke\smoke.ps1 -Install
```
Expected: `Build succeeded.` and `Smoke test passed.` (the Plan 1 checks still pass with the new popup content).

Then verify by hand (or have the executor drive it with `WScript.Shell.SendKeys`):
1. Press the Copilot key: the compact translucent bar appears with "📞 Hotline". The log has `chat view ready`.
2. Type `Reply with exactly: pong` and press Enter. The popup grows upward, the answer `pong` streams in under "Gemini (Antigravity)", and the log has `chat answer completed`.
3. Ask `What did I just ask you to reply with?`. The answer references "pong", so context is kept in the persistent agy session.
4. Press Ctrl+N: the conversation clears and the popup shrinks back to the bar.
5. Run `Get-Process agy`: exactly one agy process while chatting. After tray → Quit there is none.
6. **MSIX check:**
   - **Risk:** agy runs as a child of a packaged app.
   - **If agy works:** nothing to do.
   - **If it fails** with sign-in or state errors that a terminal `agy` doesn't show, writes under `%LOCALAPPDATA%` are being virtualized. Fix it by adding `xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"` to `IgnorableNamespaces`, `<desktop6:FileSystemWriteVirtualization>disabled</desktop6:FileSystemWriteVirtualization>` inside `<Properties>`, and `<rescap:Capability Name="unvirtualizedResources" />`. Then rebuild and record the outcome in the ledger.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "feat(app): WebView2 chat view hosted in the popup, upward growth, agy chat end-to-end" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Attachments and capture (App)

**Files:**
- Create: `src/Hotline.App/Capture/ImageProcessor.cs`, `src/Hotline.App/Capture/ScreenCapture.cs`
- Modify: `src/Hotline.App/Chat/ChatHost.Attachments.cs` (real implementations)

**Interfaces:**
- Consumes: `AttachmentFactory`, `AttachmentTray`, `ImageMath`, `PopupWindow.Modal/WithHiddenAsync/PreviousForeground`, `Native` GDI/DWM.
- Produces:
  - `ImageProcessor.NormalizeAsync(byte[] image, int maxPx) → Task<byte[]>` (PNG)
  - `ImageProcessor.EncodeBgraPngAsync(byte[] bgra, int w, int h, int maxPx) → Task<byte[]>`
  - `ImageProcessor.ThumbnailDataUrlAsync(byte[] png, int px = 96) → Task<string>`
  - `ScreenCapture.TryGetWindowRect(nint hwnd, out RectI)`, `ScreenCapture.MonitorRect(nint hwnd) → RectI`
  - `ScreenCapture.GrabBgra(RectI) → byte[]`

- [ ] **Step 1: Image processing**

`src/Hotline.App/Capture/ImageProcessor.cs`:
```csharp
using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.Core.Windowing;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Hotline.App.Capture;

/// <summary>Decode/scale/encode images with Windows.Graphics.Imaging (no extra dependencies). Output is always PNG.</summary>
internal static class ImageProcessor
{
    public static async Task<byte[]> NormalizeAsync(byte[] image, int maxPx)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(image.AsBuffer());
        input.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(input);
        var (w, h) = ImageMath.FitWithin((int)decoder.OrientedPixelWidth, (int)decoder.OrientedPixelHeight, maxPx);
        var transform = new BitmapTransform { ScaledWidth = (uint)w, ScaledHeight = (uint)h, InterpolationMode = BitmapInterpolationMode.Fant };
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        return await EncodeAsync(bitmap, w, h);
    }

    public static async Task<byte[]> EncodeBgraPngAsync(byte[] bgra, int width, int height, int maxPx)
    {
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(bgra.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
        var (w, h) = ImageMath.FitWithin(width, height, maxPx);
        return await EncodeAsync(bitmap, w, h);
    }

    public static async Task<string> ThumbnailDataUrlAsync(byte[] png, int px = 96)
        => "data:image/png;base64," + Convert.ToBase64String(await NormalizeAsync(png, px));

    private static async Task<byte[]> EncodeAsync(SoftwareBitmap bitmap, int outWidth, int outHeight)
    {
        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(bitmap);
        if (outWidth != bitmap.PixelWidth || outHeight != bitmap.PixelHeight)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)outWidth;
            encoder.BitmapTransform.ScaledHeight = (uint)outHeight;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }
        await encoder.FlushAsync();
        var bytes = new byte[output.Size];
        using var reader = new DataReader(output.GetInputStreamAt(0));
        await reader.LoadAsync((uint)output.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
```

- [ ] **Step 2: GDI screen capture**

`src/Hotline.App/Capture/ScreenCapture.cs`:
```csharp
using System.Runtime.InteropServices;
using Hotline.App.Interop;
using Hotline.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Windowing;

namespace Hotline.App.Capture;

/// <summary>
/// Copies what is on screen (after the popup has hidden itself) via GDI. Captures the visible pixels of a
/// window's rectangle, so overlapping windows appear too, like a snipping tool. Physical pixels (PerMonitorV2).
/// </summary>
internal static class ScreenCapture
{
    public static bool TryGetWindowRect(nint hwnd, out RectI rect)
    {
        rect = default;
        if (hwnd == 0 || !Native.IsWindow(hwnd) || Native.IsIconic(hwnd)) return false;
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out var r, Marshal.SizeOf<Native.RECT>()) != 0) return false;
        rect = new RectI(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
        return rect.Width > 0 && rect.Height > 0;
    }

    public static RectI MonitorRect(nint hwnd)
    {
        var area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd), DisplayAreaFallback.Primary) ?? DisplayArea.Primary;
        var b = area.OuterBounds;
        return new RectI(b.X, b.Y, b.Width, b.Height);
    }

    /// <summary>Top-down BGRA pixels of the given screen rectangle.</summary>
    public static byte[] GrabBgra(RectI r)
    {
        var screen = Native.GetDC(0);
        var mem = Native.CreateCompatibleDC(screen);
        var bmp = Native.CreateCompatibleBitmap(screen, r.Width, r.Height);
        var old = Native.SelectObject(mem, bmp);
        try
        {
            if (!Native.BitBlt(mem, 0, 0, r.Width, r.Height, screen, r.X, r.Y, Native.SRCCOPY | Native.CAPTUREBLT))
                throw new InvalidOperationException($"BitBlt failed ({Marshal.GetLastWin32Error()})");
            var header = new Native.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFOHEADER>(), biWidth = r.Width, biHeight = -r.Height, // negative = top-down
                biPlanes = 1, biBitCount = 32, biCompression = 0,
            };
            var pixels = new byte[r.Width * r.Height * 4];
            Native.SelectObject(mem, old); // a bitmap must not be selected into a DC for GetDIBits
            if (Native.GetDIBits(mem, bmp, 0, (uint)r.Height, pixels, ref header, 0) == 0)
                throw new InvalidOperationException("GetDIBits failed");
            return pixels;
        }
        finally
        {
            Native.SelectObject(mem, old);
            Native.DeleteObject(bmp);
            Native.DeleteDC(mem);
            Native.ReleaseDC(0, screen);
        }
    }
}
```

- [ ] **Step 3: Real attachment and capture flows**

Replace `src/Hotline.App/Chat/ChatHost.Attachments.cs` with:
```csharp
using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.App.Capture;
using Hotline.Core.Chat;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Hotline.App.Chat;

internal sealed partial class ChatHost
{
    private static readonly AttachmentLimits Limits = new();

    private partial async Task PickFilesAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, popup.Hwnd);
        IReadOnlyList<StorageFile> files;
        using (popup.Modal())
            files = await picker.PickMultipleFilesAsync();
        foreach (var file in files)
        {
            var buffer = await FileIO.ReadBufferAsync(file);
            await AddBytesAsync(file.Name, file.ContentType, buffer.ToArray());
        }
        popup.ShowPopup();
    }

    private partial async Task CaptureAsync(bool window)
    {
        var target = popup.PreviousForeground;
        var png = await popup.WithHiddenAsync(async () =>
        {
            var rect = window && ScreenCapture.TryGetWindowRect(target, out var w) ? w : ScreenCapture.MonitorRect(target);
            var bgra = ScreenCapture.GrabBgra(rect);
            return await ImageProcessor.EncodeBgraPngAsync(bgra, rect.Width, rect.Height, settings.Chat.MaxImagePixels);
        });
        var name = window ? $"window-{DateTime.Now:HHmmss}.png" : $"screen-{DateTime.Now:HHmmss}.png";
        await AddAttachmentAsync(AttachmentFactory.FromBytes(name, "image/png", png, Limits));
    }

    private partial async Task AddBytesAsync(string name, string? mime, byte[] data)
    {
        Attachment attachment;
        try
        {
            attachment = AttachmentFactory.FromBytes(name, string.IsNullOrEmpty(mime) ? null : mime, data, Limits);
            if (attachment.Kind == AttachmentKind.Image)
            {
                var png = await ImageProcessor.NormalizeAsync(attachment.Data, settings.Chat.MaxImagePixels);
                attachment = attachment with { Data = png, MimeType = "image/png", Name = Path.ChangeExtension(attachment.Name, ".png") };
            }
        }
        catch (AttachmentRejectedException ex) { Toast(ex.Message); return; }
        catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException) { Toast($"{name} couldn't be read as an image."); return; }
        await AddAttachmentAsync(attachment);
    }

    private async Task AddAttachmentAsync(Attachment attachment)
    {
        try { tray.Add(attachment); }
        catch (AttachmentRejectedException ex) { Toast(ex.Message); return; }
        var thumb = attachment.Kind == AttachmentKind.Image ? await ImageProcessor.ThumbnailDataUrlAsync(attachment.Data) : null;
        Post(new { type = "attachmentAdded", id = attachment.Id, name = attachment.Name, kind = attachment.Kind.ToString(), thumb });
        log.Info($"attachment added: {attachment.Kind} {attachment.Data.Length} bytes");
    }
}
```
- [ ] **Step 4: Build and install**

Run:
```powershell
dotnet build src/Hotline.App/Hotline.App.csproj -c Debug -p:Platform=x64
powershell -File scripts\install.ps1
```
Expected: `Build succeeded.`, then `Installed pmarc14.Hotline_...`.

- [ ] **Step 5: Verify every attachment path**

With the popup open (Copilot key), check:
1. + → "Attach files…": the file dialog opens **above** the popup, and the popup does not hide while the dialog is open. Pick a `.png` and a `.md`: two chips appear, with a thumbnail for the image and 📄 for the text file.
2. Ask "What's in these files?": agy describes both. The log shows `attachment added` twice, and `<LocalState>\agy-workspace\attachments\<id>\` holds the PNG.
3. Copy a screenshot (Win+Shift+S), then press Ctrl+V in the input: an image chip appears.
4. Drag a file from Explorer onto the popup: a chip appears while the bar shows a dashed border.
5. + → "Capture window" while a browser was the previous window: the popup vanishes briefly and returns with a `window-HHmmss.png` chip; asking "what page is this?" answers correctly. "Capture screen" gives a full-monitor chip.
6. Hold the Copilot key (default Hold = ShowPopup) and confirm nothing breaks. Then set `"activation": { "hold": "captureWindow" }` in settings, restart, and hold the key over a window: the popup opens with that window already attached.
7. Paste or drop a 30 MB image or a `.zip`: a toast explains the rejection, and existing chips are untouched.
8. Remove a chip with ×; it disappears and isn't sent.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(app): file picker, paste, drag-drop and window/screen capture attachments" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 10: Smoke test, docs and wrap-up

**Files:**
- Modify: `tests/smoke/smoke.ps1`, `README.md`, `docs/superpowers/notes/2026-10-01-resume-here.md` (mark Plan 2 done)

**Interfaces:**
- Consumes: the installed app.
- Produces: smoke checks for the chat view, plus an optional live agy check (`-WithAgy`).

- [ ] **Step 1: Smoke test additions**

In `tests/smoke/smoke.ps1`:
1. Change the `param` line to `param([switch]$Install, [switch]$WithAgy)`.
2. Insert this block before the `# 5. Fallback hotkey` section:
```powershell
    # 4b. Chat view loads in the popup
    $n = Get-LogCount
    Restart-Hotline
    for ($i = 0; $i -lt 20 -and -not (Get-NewLog $n | Select-String -SimpleMatch 'chat view ready'); $i++) { Start-Sleep -Milliseconds 500 }
    Expect-Log 'chat view loads' $n 'chat view ready'

    # 4c. Optional: a real agy round trip (needs agy installed and signed in)
    if ($WithAgy) {
        $n = Get-LogCount
        Start-Process 'hotline://key?state=Down'; Start-Sleep 2
        $shell = New-Object -ComObject WScript.Shell
        $shell.SendKeys('Reply with exactly: pong{ENTER}')
        for ($i = 0; $i -lt 120 -and -not (Get-NewLog $n | Select-String -Pattern 'chat answer (completed|failed)'); $i++) { Start-Sleep -Milliseconds 500 }
        Expect-Log 'agy answers' $n 'chat answer completed'
        Check 'one agy process' (@(Get-Process agy -ErrorAction SilentlyContinue).Count -eq 1)
    }
```
3. Add to the final `Check 'tray Quit exits'` area:
```powershell
    Start-Sleep 1
    Check 'agy exits with Hotline' (-not (Get-Process agy -ErrorAction SilentlyContinue))
```

Run: `powershell -File tests\smoke\smoke.ps1 -Install -WithAgy`
Expected: every line `PASS`, ending in `Smoke test passed.`

- [ ] **Step 2: README**

Add under the Settings table in `README.md`:
```markdown
| `chat.defaultBackend` | `agy` | Which AI answers (`agy` = Gemini via your Antigravity CLI sign-in) |
| `chat.backends[].model` | (agy default) | e.g. `gemini-3.8-flash-low` for faster answers |
| `chat.maxHeight` | `560` | How tall the popup may grow while chatting |
| `chat.maxImagePixels` | `2048` | Attached/captured images are scaled to this longest edge |
| `chat.saveHistory` / `chat.historyRetentionDays` | `true` / `30` | Conversation logs in LocalState\history (text only) |

### Chatting

Press the Copilot key, type, Enter. **+** attaches files or captures the window you were in / the whole screen;
you can also paste (Ctrl+V) or drag files in. Ctrl+N starts a new chat, Esc hides.
The Antigravity backend needs the [Antigravity CLI](https://antigravity.google/cli) installed and signed in (run `agy` once).
```

- [ ] **Step 3: Mark progress in the resume note**

Append to `docs/superpowers/notes/2026-10-01-resume-here.md`:
```markdown

## Update 2026-10-02
Plan 2 (`docs/superpowers/plans/2026-10-02-hotline-plan2-chat-agy.md`) executed: chat view, attachments/capture,
agy backend. Next: Plan 3 = Gemini API (when the user has an AI Studio key) → OpenAI-compatible → Claude Code.
```

- [ ] **Step 4: Full test run and commit**

Run:
```powershell
dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj
node --test tests/web
```
Expected: `failed: 0` and `# fail 0`.

```powershell
git add -A
git commit -m "test,docs: chat smoke checks, README chat settings, resume note" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
