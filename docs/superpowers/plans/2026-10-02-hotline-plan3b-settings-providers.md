# Hotline Plan 3b — Connections, Provider/Model/Effort Pickers, Tool Mode, System Prompts, Settings Window

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the bottom bar provider, model and effort dropdowns plus a system-prompt menu. Add a full settings window for every option: AI connections (add, edit, duplicate, remove), tool mode, prompts, and folders. Settings and API keys apply live, and keys are stored in Windows Credential Locker.

**Architecture:**
- **Core:**
  - a connection-type catalog and connection editor
  - a `SettingsService` that normalizes, saves and raises Changed
  - a declarative `SettingsSchema` from which the settings pages are generated
  - model-list parsers with a cached `ModelCatalog`
  - an `ISecretStore` abstraction
  - a `PromptLibrary` of Markdown files in `~/.hotline/prompts`
  - agy support for system prompts and tool mode
- **App:**
  - **Bottom bar:** a `ProviderBar` for the pickers.
  - **Secrets:** stored with Windows `PasswordVault`.
  - **Settings window:** a native WinUI window (NavigationView with hand-built settings cards; no new NuGet packages).
  - **Live apply:** appearance changes take effect immediately.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1), xUnit v3. No new packages.

**Spec:** `docs/superpowers/specs/2026-09-30-hotline-design.md` → Plan 3 → 3b bullet (provider/model/effort controls, connections) and "Tool use + system prompts".

## Global Constraints

- **API keys:** never written to `settings.json`. They live in `PasswordVault` under resource `Hotline`, user name `connection:<id>:apiKey` (`SecretKeys.ApiKey(id)`).
- **Connection types:**
  - `Antigravity`, `ClaudeCode`, `Gemini`, `Anthropic`, `OpenAiCompatible`, `Local`.
  - Only `Antigravity` can chat in this plan (`BackendFactory.IsAvailable`). The others are configurable and their model lists work, but the picker marks them "coming soon" (Plan 4).
- **Effort levels:** only agy is verified (`low`, `medium`, `high`, `max`). All other types have none; the effort dropdown is disabled with a tooltip.
- **Tool mode** (per connection; CLI types only):
  - **`ChatOnly`** (default): the private agy workspace plus the generated `hotline` agent whose body is the system prompt.
  - **`Inherit`:**
    - agy's default agent (no `--agent`), with the working directory set to `WorkingDirectory` (default: the user profile);
    - `--add-dir <workspace>` so attachments stay readable;
    - the system prompt is prepended to the first message of each fresh session;
    - attachment paths are absolute.
  - Never `--dangerously-skip-permissions`.
- **Prompts:**
  - Stored as `%USERPROFILE%\.hotline\prompts\<name>.md`. Names match `^[\w\- ]+$`.
  - `default.md` is created on first run.
  - The active prompt is `profile.Prompt ?? chat.DefaultPrompt`.
- **Settings:** changed only through `SettingsService.Update`, which normalizes, saves and raises `Changed` on the caller's thread.
- **Tests:** `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj` must stay green.
- **Smoke:** `powershell -File tests\smoke\smoke.ps1 -Install` must pass at the end of Tasks 5 and 6.
- **Test hygiene:** after any manual check, leave Hotline in the tray with the panel and settings window closed. Don't type into the user's apps or use their clipboard.
- **Commits:** authored by pmarc14, ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **Removing the connection currently in use, or the last connection.** The last one can't be removed, the default moves to the first remaining connection, and the chat continues on it. Pinned by `ConnectionEditorTests.Remove_default_reassigns` and `Cannot_remove_last` (Task 1).
2. **An API key that's wrong, or a local endpoint that isn't running.** "Test connection" and the model dropdown show a clear message (key rejected or server unreachable) and the UI never hangs. Pinned by `ModelCatalogTests.Unauthorized_is_reported` and `Unreachable_server_is_reported` (Task 3).
3. **Hand-edited prompt files:** a missing, empty or unsafely named file (`../x`). This falls back to the default prompt without crashing. Pinned by `PromptLibraryTests` (Task 4).
4. **Changing model or effort while an answer is streaming** must not kill the answer. The pickers are disabled while busy (Task 5); this is a manual check.
5. **Inherit mode with a working folder that doesn't exist** falls back to the user profile. Pinned by `AgyBackendTests.Inherit_mode_uses_working_dir_and_prefixes_prompt` and `Inherit_missing_working_dir_falls_back_to_home` (Task 4).

---

### Task 1: Connection types and editor (Core)

**Files:**
- Modify: `src/Hotline.Core/Settings/HotlineSettings.cs`
- Create: `src/Hotline.Core/Backends/ConnectionTypes.cs`, `src/Hotline.Core/Backends/ConnectionEditor.cs`
- Test: `tests/Hotline.Core.Tests/ConnectionTypesTests.cs`, `tests/Hotline.Core.Tests/ConnectionEditorTests.cs`

**Interfaces:**
- Produces:
  - `enum BackendType { Antigravity, Gemini, OpenAiCompatible, ClaudeCode, Anthropic, Local }`
  - `enum ToolMode { ChatOnly, Inherit }`
  - `BackendProfile`: `ToolMode Tools`, `string? WorkingDirectory`, `string? Prompt`
  - `ChatSettings.DefaultPrompt = "default"`
  - `[Flags] enum ConnectionField { None, Endpoint, ApiKey, CliPath, Agent, ExtraArgs, Tools }`
  - `sealed record ConnectionTypeInfo(BackendType Type, string DisplayName, string DefaultName, ConnectionField Fields, string? DefaultEndpoint, IReadOnlyList<string> EffortLevels, bool ApiKeyOptional, string Description)` with `bool Has(ConnectionField f)`
  - `static ConnectionTypes.All`, `ConnectionTypes.Of(BackendType)`
  - `static ConnectionEditor.Add(ChatSettings, BackendType) → BackendProfile`, `Duplicate(ChatSettings, string id) → BackendProfile`, `Remove(ChatSettings, string id) → bool`, `SetDefault(ChatSettings, string id)`

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/ConnectionTypesTests.cs`:
```csharp
using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ConnectionTypesTests
{
    [Fact]
    public void Every_backend_type_has_exactly_one_entry()
        => Assert.Equal(Enum.GetValues<BackendType>().Order(), ConnectionTypes.All.Select(t => t.Type).Order());

    [Fact]
    public void Only_agy_has_verified_effort_levels()
    {
        Assert.Equal(["low", "medium", "high", "max"], ConnectionTypes.Of(BackendType.Antigravity).EffortLevels);
        Assert.All(ConnectionTypes.All.Where(t => t.Type != BackendType.Antigravity), t => Assert.Empty(t.EffortLevels));
    }

    [Fact]
    public void Api_types_need_endpoint_and_key_cli_types_need_cli_path_and_tools()
    {
        foreach (var t in new[] { BackendType.Gemini, BackendType.Anthropic, BackendType.OpenAiCompatible, BackendType.Local })
        {
            var info = ConnectionTypes.Of(t);
            Assert.True(info.Has(ConnectionField.Endpoint) && info.Has(ConnectionField.ApiKey));
            Assert.False(string.IsNullOrEmpty(info.DefaultEndpoint));
        }
        foreach (var t in new[] { BackendType.Antigravity, BackendType.ClaudeCode })
            Assert.True(ConnectionTypes.Of(t).Has(ConnectionField.CliPath) && ConnectionTypes.Of(t).Has(ConnectionField.Tools));
        Assert.True(ConnectionTypes.Of(BackendType.Local).ApiKeyOptional);
    }
}
```

`tests/Hotline.Core.Tests/ConnectionEditorTests.cs`:
```csharp
using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ConnectionEditorTests
{
    private static ChatSettings Chat() => new(); // contains the default agy connection

    [Fact]
    public void Add_uses_type_defaults_and_unique_ids_and_names()
    {
        var chat = Chat();
        var a = ConnectionEditor.Add(chat, BackendType.Local);
        var b = ConnectionEditor.Add(chat, BackendType.Local);
        Assert.Equal(("local-model", "Local model", "http://127.0.0.1:8080/v1"), (a.Id, a.Name, a.Endpoint));
        Assert.Equal(("local-model-2", "Local model 2"), (b.Id, b.Name));
        Assert.Equal(3, chat.Backends.Count);
    }

    [Fact]
    public void Add_agy_sets_agent_and_avoids_existing_name()
    {
        var p = ConnectionEditor.Add(Chat(), BackendType.Antigravity);
        Assert.Equal("hotline", p.Agent);
        Assert.Equal("Gemini (Antigravity) 2", p.Name);
    }

    [Fact]
    public void Duplicate_copies_settings_after_original()
    {
        var chat = Chat();
        chat.Backends[0].Model = "gemini-3.8-flash-low";
        chat.Backends[0].Tools = ToolMode.Inherit;
        var copy = ConnectionEditor.Duplicate(chat, "agy");
        Assert.Equal(1, chat.Backends.IndexOf(copy));
        Assert.Equal(("Gemini (Antigravity) (copy)", "gemini-3.8-flash-low", ToolMode.Inherit), (copy.Name, copy.Model, copy.Tools));
        Assert.NotEqual("agy", copy.Id);
    }

    [Fact]
    public void Cannot_remove_last()
    {
        var chat = Chat();
        Assert.False(ConnectionEditor.Remove(chat, "agy"));
        Assert.Single(chat.Backends);
    }

    [Fact]
    public void Remove_default_reassigns()
    {
        var chat = Chat();
        var local = ConnectionEditor.Add(chat, BackendType.Local);
        Assert.True(ConnectionEditor.Remove(chat, "agy"));
        Assert.Equal(local.Id, chat.DefaultBackend);
    }

    [Fact]
    public void Set_default_requires_existing_id()
    {
        var chat = Chat();
        var local = ConnectionEditor.Add(chat, BackendType.Local);
        ConnectionEditor.SetDefault(chat, local.Id);
        Assert.Equal(local.Id, chat.DefaultBackend);
        Assert.Throws<KeyNotFoundException>(() => ConnectionEditor.SetDefault(chat, "nope"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with `CS0246: ... 'ConnectionEditor'` / `ConnectionTypes`.

- [ ] **Step 3: Implement**

In `src/Hotline.Core/Settings/HotlineSettings.cs`:
- Replace `public enum BackendType { Antigravity, Gemini, OpenAiCompatible, ClaudeCode }` with:
```csharp
public enum BackendType { Antigravity, Gemini, OpenAiCompatible, ClaudeCode, Anthropic, Local }

/// <summary>ChatOnly: answers only (attachments readable). Inherit: the CLI's own tools and permission rules.</summary>
public enum ToolMode { ChatOnly, Inherit }
```
- In `BackendProfile`, after the `ExtraArgs` property, add:
```csharp
    /// <summary>Tool use for CLI connections (agy, Claude Code).</summary>
    public ToolMode Tools { get; set; } = ToolMode.ChatOnly;
    /// <summary>Folder the CLI works in when Tools = Inherit. Null or missing = your user folder.</summary>
    public string? WorkingDirectory { get; set; }
    /// <summary>System prompt name (file ~/.hotline/prompts/&lt;name&gt;.md). Null = chat.defaultPrompt.</summary>
    public string? Prompt { get; set; }
```
- In `ChatSettings`, after `DefaultBackend`, add:
```csharp
    /// <summary>System prompt used by connections that don't pick their own.</summary>
    public string DefaultPrompt { get; set; } = "default";
```

`src/Hotline.Core/Backends/ConnectionTypes.cs`:
```csharp
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

[Flags]
public enum ConnectionField { None = 0, Endpoint = 1, ApiKey = 2, CliPath = 4, Agent = 8, ExtraArgs = 16, Tools = 32 }

public sealed record ConnectionTypeInfo(
    BackendType Type, string DisplayName, string DefaultName, ConnectionField Fields, string? DefaultEndpoint,
    IReadOnlyList<string> EffortLevels, bool ApiKeyOptional, string Description)
{
    public bool Has(ConnectionField field) => (Fields & field) == field;
}

/// <summary>What each kind of AI connection needs. Effort levels are listed only where verified (agy).</summary>
public static class ConnectionTypes
{
    private const ConnectionField Cli = ConnectionField.CliPath | ConnectionField.ExtraArgs | ConnectionField.Tools;
    private const ConnectionField Api = ConnectionField.Endpoint | ConnectionField.ApiKey;

    public static IReadOnlyList<ConnectionTypeInfo> All { get; } =
    [
        new(BackendType.Antigravity, "Antigravity CLI (agy)", "Gemini (Antigravity)", Cli | ConnectionField.Agent, null,
            ["low", "medium", "high", "max"], false, "Your installed agy and its Google sign-in."),
        new(BackendType.ClaudeCode, "Claude Code CLI", "Claude (Claude Code)", Cli, null,
            [], false, "Your installed claude and its sign-in."),
        new(BackendType.Gemini, "Gemini API", "Gemini API", Api, "https://generativelanguage.googleapis.com/v1beta",
            [], false, "A Google AI Studio API key."),
        new(BackendType.Anthropic, "Anthropic API", "Claude API", Api, "https://api.anthropic.com/v1",
            [], false, "An Anthropic Console API key."),
        new(BackendType.OpenAiCompatible, "OpenAI-compatible API", "OpenAI-compatible", Api, "https://api.openai.com/v1",
            [], false, "OpenAI, OpenRouter, Groq and other OpenAI-style APIs."),
        new(BackendType.Local, "Local endpoint", "Local model", Api, "http://127.0.0.1:8080/v1",
            [], true, "llama.cpp server, LM Studio and other local OpenAI-style servers."),
    ];

    public static ConnectionTypeInfo Of(BackendType type) => All.First(t => t.Type == type);
}
```

`src/Hotline.Core/Backends/ConnectionEditor.cs`:
```csharp
using System.Text;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

/// <summary>Add / duplicate / remove / set-default operations on the configured connections.</summary>
public static class ConnectionEditor
{
    public static BackendProfile Add(ChatSettings chat, BackendType type)
    {
        var info = ConnectionTypes.Of(type);
        var profile = new BackendProfile
        {
            Id = UniqueId(chat, Slug(info.DefaultName)),
            Type = type,
            Name = UniqueName(chat, info.DefaultName),
            Endpoint = info.DefaultEndpoint,
            Agent = type == BackendType.Antigravity ? AgyProtocol.DefaultAgent : null,
        };
        chat.Backends.Add(profile);
        return profile;
    }

    public static BackendProfile Duplicate(ChatSettings chat, string id)
    {
        var source = Find(chat, id);
        var copy = new BackendProfile
        {
            Id = UniqueId(chat, source.Id), Type = source.Type, Name = UniqueName(chat, source.Name + " (copy)"),
            Model = source.Model, Effort = source.Effort, Endpoint = source.Endpoint, CliPath = source.CliPath,
            Agent = source.Agent, ExtraArgs = source.ExtraArgs, Tools = source.Tools,
            WorkingDirectory = source.WorkingDirectory, Prompt = source.Prompt,
        };
        chat.Backends.Insert(chat.Backends.IndexOf(source) + 1, copy);
        return copy;
    }

    /// <summary>Removes a connection; the last one can't be removed. The default moves to the first remaining one.</summary>
    public static bool Remove(ChatSettings chat, string id)
    {
        if (chat.Backends.Count <= 1) return false;
        if (chat.Backends.RemoveAll(b => b.Id == id) == 0) return false;
        if (chat.DefaultBackend == id) chat.DefaultBackend = chat.Backends[0].Id;
        return true;
    }

    public static void SetDefault(ChatSettings chat, string id) => chat.DefaultBackend = Find(chat, id).Id;

    private static BackendProfile Find(ChatSettings chat, string id) =>
        chat.Backends.FirstOrDefault(b => b.Id == id) ?? throw new KeyNotFoundException($"No connection '{id}'.");

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        var slug = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "connection" : slug;
    }

    private static string UniqueId(ChatSettings chat, string baseId)
    {
        var id = baseId;
        for (var i = 2; chat.Backends.Any(b => b.Id == id); i++) id = $"{baseId}-{i}";
        return id;
    }

    private static string UniqueName(ChatSettings chat, string baseName)
    {
        var name = baseName;
        for (var i = 2; chat.Backends.Any(b => b.Name == name); i++) name = $"{baseName} {i}";
        return name;
    }
}
```

- [ ] **Step 4: Run tests, commit**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`.
```powershell
git add -A
git commit -m "feat(core): connection types catalog and connection editor; tool mode and prompt per connection" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: SettingsService and SettingsSchema (Core)

**Files:**
- Modify: `src/Hotline.Core/Settings/SettingsStore.cs` (`Normalize` becomes `public static`)
- Create: `src/Hotline.Core/Settings/SettingsService.cs`, `src/Hotline.Core/Settings/SettingsSchema.cs`
- Test: `tests/Hotline.Core.Tests/SettingsServiceTests.cs`, `tests/Hotline.Core.Tests/SettingsSchemaTests.cs`

**Interfaces:**
- Produces:
  - `sealed class SettingsService(SettingsStore store, HotlineSettings settings, FileLog log)` with `HotlineSettings Current`, `event Action? Changed`, `void Update(Action<HotlineSettings> change)`
  - `enum SettingsPage { General, Appearance, Window, Chat, Advanced }`
  - setting descriptors:
    - `abstract record SettingItem(SettingsPage Page, string Header, string? Description, bool RequiresRestart)`
    - `ToggleItem(Get, Set)`, `NumberItem(Min, Max, Step, Unit, Get, Set)`
    - `ChoiceItem(Options, Get, Set)` with `ChoiceOption(Value, Label)`
    - `TextItem(Placeholder, Get, Set)`
  - `static IReadOnlyList<SettingItem> SettingsSchema.Items`

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/SettingsServiceTests.cs`:
```csharp
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private SettingsService New(out SettingsStore store)
    {
        store = new SettingsStore(_dir);
        return new SettingsService(store, store.Load(), new FileLog(Path.Combine(_dir, "h.log")));
    }

    [Fact]
    public void Update_saves_normalizes_and_raises_changed_once()
    {
        var service = New(out var store);
        var raised = 0;
        service.Changed += () => raised++;
        service.Update(s => s.Window.FontSize = 99);
        Assert.Equal(1, raised);
        Assert.Equal(32, service.Current.Window.FontSize);
        Assert.Equal(32, store.Load().Window.FontSize);
    }

    [Fact]
    public void Current_is_the_same_instance_other_components_hold()
    {
        var service = New(out _);
        var window = service.Current.Window;
        service.Update(s => s.Window.HideOnBlur = false);
        Assert.False(window.HideOnBlur);
    }
}
```

`tests/Hotline.Core.Tests/SettingsSchemaTests.cs`:
```csharp
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class SettingsSchemaTests
{
    [Fact]
    public void Every_page_has_items_and_headers_are_unique_per_page()
    {
        foreach (var page in Enum.GetValues<SettingsPage>())
        {
            var items = SettingsSchema.Items.Where(i => i.Page == page).ToList();
            Assert.NotEmpty(items);
            Assert.Equal(items.Count, items.Select(i => i.Header).Distinct().Count());
        }
    }

    [Fact]
    public void Toggles_round_trip()
    {
        foreach (var t in SettingsSchema.Items.OfType<ToggleItem>())
        {
            var s = new HotlineSettings();
            var flipped = !t.Get(s);
            t.Set(s, flipped);
            Assert.Equal(flipped, t.Get(SettingsStore.Normalize(s)));
        }
    }

    [Fact]
    public void Number_ranges_survive_normalize()
    {
        foreach (var n in SettingsSchema.Items.OfType<NumberItem>())
        {
            var s = new HotlineSettings();
            n.Set(s, n.Max);
            Assert.Equal(n.Max, n.Get(SettingsStore.Normalize(s)), 3);
            var low = new HotlineSettings();
            n.Set(low, n.Min);
            Assert.InRange(n.Get(SettingsStore.Normalize(low)), n.Min, n.Max);
        }
    }

    [Fact]
    public void Choices_round_trip_and_defaults_are_listed()
    {
        foreach (var c in SettingsSchema.Items.OfType<ChoiceItem>())
        {
            Assert.Contains(c.Options, o => o.Value == c.Get(new HotlineSettings()));
            foreach (var option in c.Options)
            {
                var s = new HotlineSettings();
                c.Set(s, option.Value);
                Assert.Equal(option.Value, c.Get(SettingsStore.Normalize(s)));
            }
        }
    }

    [Fact]
    public void Text_items_round_trip_and_blank_becomes_null()
    {
        foreach (var t in SettingsSchema.Items.OfType<TextItem>())
        {
            var s = new HotlineSettings();
            t.Set(s, "Ctrl+Alt+H");
            Assert.Equal("Ctrl+Alt+H", t.Get(s));
            t.Set(s, "  ");
            Assert.Null(t.Get(s));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with `CS0246: ... 'SettingsService'`.

- [ ] **Step 3: Implement**

In `src/Hotline.Core/Settings/SettingsStore.cs`, change `private static HotlineSettings Normalize(HotlineSettings s)` to `public static HotlineSettings Normalize(HotlineSettings s)`.

`src/Hotline.Core/Settings/SettingsService.cs`:
```csharp
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Settings;

/// <summary>
/// The single way to change settings at runtime: mutate, normalize, save, notify. Components keep references to
/// the same settings objects, so most changes apply live; Changed lets the UI re-apply appearance.
/// </summary>
public sealed class SettingsService(SettingsStore store, HotlineSettings settings, FileLog log)
{
    public HotlineSettings Current { get; } = settings;

    public event Action? Changed;

    public void Update(Action<HotlineSettings> change)
    {
        change(Current);
        SettingsStore.Normalize(Current);
        try { store.Save(Current); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.Error("saving settings failed", ex); }
        Changed?.Invoke();
    }
}
```

`src/Hotline.Core/Settings/SettingsSchema.cs`:
```csharp
using Hotline.Core.Activation;

namespace Hotline.Core.Settings;

public enum SettingsPage { General, Appearance, Window, Chat, Advanced }

public abstract record SettingItem(SettingsPage Page, string Header, string? Description, bool RequiresRestart);

public sealed record ToggleItem(SettingsPage Page, string Header, string? Description, Func<HotlineSettings, bool> Get,
    Action<HotlineSettings, bool> Set, bool RequiresRestart = false) : SettingItem(Page, Header, Description, RequiresRestart);

public sealed record NumberItem(SettingsPage Page, string Header, string? Description, double Min, double Max, double Step, string? Unit,
    Func<HotlineSettings, double> Get, Action<HotlineSettings, double> Set, bool RequiresRestart = false)
    : SettingItem(Page, Header, Description, RequiresRestart);

public sealed record ChoiceOption(string Value, string Label);

public sealed record ChoiceItem(SettingsPage Page, string Header, string? Description, IReadOnlyList<ChoiceOption> Options,
    Func<HotlineSettings, string> Get, Action<HotlineSettings, string> Set, bool RequiresRestart = false)
    : SettingItem(Page, Header, Description, RequiresRestart);

public sealed record TextItem(SettingsPage Page, string Header, string? Description, string? Placeholder,
    Func<HotlineSettings, string?> Get, Action<HotlineSettings, string?> Set, bool RequiresRestart = false)
    : SettingItem(Page, Header, Description, RequiresRestart);

/// <summary>Every user-facing setting, described once; the settings window is generated from this list.</summary>
public static class SettingsSchema
{
    private static ChoiceItem Choice<T>(SettingsPage page, string header, string? description, (T Value, string Label)[] options,
        Func<HotlineSettings, T> get, Action<HotlineSettings, T> set, bool restart = false) where T : struct, Enum
        => new(page, header, description, options.Select(o => new ChoiceOption(o.Value.ToString(), o.Label)).ToList(),
            s => get(s).ToString(), (s, v) => set(s, Enum.Parse<T>(v)), restart);

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static readonly (KeyAction, string)[] KeyActions =
    [
        (KeyAction.TogglePopup, "Open / close Hotline"), (KeyAction.ShowPopup, "Open Hotline"), (KeyAction.NewChat, "Start a new chat"),
        (KeyAction.CaptureWindow, "Capture the current window"), (KeyAction.None, "Do nothing"),
    ];

    public static IReadOnlyList<SettingItem> Items { get; } =
    [
        Choice(SettingsPage.General, "Short press of the Copilot key", null, KeyActions, s => s.Activation.Tap, (s, v) => s.Activation.Tap = v),
        Choice(SettingsPage.General, "Long press of the Copilot key", null, KeyActions, s => s.Activation.Hold, (s, v) => s.Activation.Hold = v),
        new TextItem(SettingsPage.General, "Extra hotkey", "Also opens Hotline, e.g. Ctrl+Alt+H. Leave empty for none.", "Ctrl+Alt+H",
            s => s.Activation.FallbackHotkey, (s, v) => s.Activation.FallbackHotkey = Blank(v), RequiresRestart: true),
        new ToggleItem(SettingsPage.General, "Hide when I click elsewhere", "Pin the panel (📌) to keep it open temporarily.",
            s => s.Window.HideOnBlur, (s, v) => s.Window.HideOnBlur = v),
        new ToggleItem(SettingsPage.General, "Keep on top of other windows", null, s => s.Window.AlwaysOnTop, (s, v) => s.Window.AlwaysOnTop = v),

        Choice(SettingsPage.Appearance, "Theme", null, [(ThemeChoice.System, "Use Windows setting"), (ThemeChoice.Light, "Light"), (ThemeChoice.Dark, "Dark")],
            s => s.Window.Theme, (s, v) => s.Window.Theme = v),
        Choice(SettingsPage.Appearance, "Background", "Acrylic is translucent; Solid is opaque.",
            [(BackdropKind.Acrylic, "Acrylic"), (BackdropKind.AcrylicThin, "Acrylic (thin)"), (BackdropKind.Mica, "Mica"), (BackdropKind.Solid, "Solid")],
            s => s.Window.Backdrop, (s, v) => s.Window.Backdrop = v),
        new NumberItem(SettingsPage.Appearance, "Acrylic tint", "0 = clear, 1 = strongly tinted.", 0, 1, 0.05, null,
            s => s.Window.TintOpacity, (s, v) => s.Window.TintOpacity = v),
        new NumberItem(SettingsPage.Appearance, "Acrylic luminosity", "Lower is more see-through.", 0, 1, 0.05, null,
            s => s.Window.LuminosityOpacity, (s, v) => s.Window.LuminosityOpacity = v),
        new NumberItem(SettingsPage.Appearance, "Text size", null, 10, 32, 1, "px", s => s.Window.FontSize, (s, v) => s.Window.FontSize = (int)v),
        new TextItem(SettingsPage.Appearance, "Font", "Any installed font, e.g. Cascadia Code. Empty = Segoe UI Variable.", "Segoe UI Variable",
            s => s.Window.FontFamily, (s, v) => s.Window.FontFamily = Blank(v)),
        Choice(SettingsPage.Appearance, "Scrollbar", null, [(ScrollbarStyle.Auto, "Show when scrolling"), (ScrollbarStyle.Visible, "Always"), (ScrollbarStyle.Hidden, "Never")],
            s => s.Window.Scrollbar, (s, v) => s.Window.Scrollbar = v),

        new NumberItem(SettingsPage.Window, "Width", "Share of the screen width.", 20, 90, 1, "%", s => s.Window.WidthPercent, (s, v) => s.Window.WidthPercent = v),
        new NumberItem(SettingsPage.Window, "Minimum width", null, 320, 4000, 10, "DIP", s => s.Window.MinWidth, (s, v) => s.Window.MinWidth = (int)v),
        new NumberItem(SettingsPage.Window, "Maximum width", null, 320, 4000, 10, "DIP", s => s.Window.MaxWidth, (s, v) => s.Window.MaxWidth = (int)v),
        new NumberItem(SettingsPage.Window, "Starting height", "The empty input bar; it grows upward as you type and chat.", 120, 4000, 10, "DIP",
            s => s.Window.Height, (s, v) => s.Window.Height = (int)v),
        new NumberItem(SettingsPage.Window, "Maximum height", "How much of the screen it may grow to.", 30, 95, 1, "%",
            s => s.Window.MaxHeightPercent, (s, v) => s.Window.MaxHeightPercent = v),
        new NumberItem(SettingsPage.Window, "Vertical position", "0 = top, 0.5 = centre, 1 = bottom.", 0, 1, 0.05, null,
            s => s.Window.VerticalPosition, (s, v) => s.Window.VerticalPosition = v),
        Choice(SettingsPage.Window, "Growth", null, [(GrowMode.Grow, "Fit the conversation"), (GrowMode.Full, "Jump to maximum height")],
            s => s.Chat.GrowMode, (s, v) => s.Chat.GrowMode = v),

        new ToggleItem(SettingsPage.Chat, "Save chat history", "Text only, in your .hotline folder.", s => s.Chat.SaveHistory, (s, v) => s.Chat.SaveHistory = v, RequiresRestart: true),
        new NumberItem(SettingsPage.Chat, "Keep history for", null, 1, 3650, 1, "days", s => s.Chat.HistoryRetentionDays,
            (s, v) => s.Chat.HistoryRetentionDays = (int)v, RequiresRestart: true),
        new NumberItem(SettingsPage.Chat, "Image size limit", "Attached and captured images are scaled to this longest edge.", 256, 8192, 128, "px",
            s => s.Chat.MaxImagePixels, (s, v) => s.Chat.MaxImagePixels = (int)v),

        new ToggleItem(SettingsPage.Advanced, "Detailed logging", "Writes extra diagnostics to the log.", s => s.Diagnostics.VerboseLogging,
            (s, v) => s.Diagnostics.VerboseLogging = v),
    ];
}
```

- [ ] **Step 4: Run tests, commit**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`.
```powershell
git add -A
git commit -m "feat(core): settings service (live apply) and declarative settings schema" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Model lists, secrets, backend invalidation (Core)

**Files:**
- Create: `src/Hotline.Core/Backends/ModelCatalog.cs`, `src/Hotline.Core/Backends/Secrets.cs`
- Modify: `src/Hotline.Core/Backends/BackendCatalog.cs` (`BackendCache.InvalidateAsync`)
- Test: `tests/Hotline.Core.Tests/ModelListParsersTests.cs`, `tests/Hotline.Core.Tests/ModelCatalogTests.cs`, `tests/Hotline.Core.Tests/BackendCatalogTests.cs` (add one test)

**Interfaces:**
- Produces:
  - `sealed record ModelInfo(string Id, string Label)`
  - `sealed class ModelListException(string message) : Exception`
  - `static ModelListParsers.Agy(string)`, `.OpenAi(string)`, `.Gemini(string)`, `.Anthropic(string)`, `.ClaudeCodeAliases`
  - `sealed class ModelCatalog(HttpClient http, ISecretStore secrets, Func<BackendProfile, CancellationToken, Task<string>> runAgyModels, TimeProvider clock)` with `Task<IReadOnlyList<ModelInfo>> GetAsync(BackendProfile p, bool refresh, CancellationToken ct)`
  - `interface ISecretStore { string? Get(string key); void Set(string key, string? value); }`, `sealed class InMemorySecretStore : ISecretStore`, `static SecretKeys.ApiKey(string connectionId)`
  - `ValueTask BackendCache.InvalidateAsync(string id)`

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/ModelListParsersTests.cs`:
```csharp
using Hotline.Core.Backends;

namespace Hotline.Core.Tests;

public class ModelListParsersTests
{
    [Fact]
    public void Agy_lines_are_id_tab_label()
        => Assert.Equal(
            [new ModelInfo("gemini-3.8-flash-high", "Gemini 3.8 Flash (High)"), new ModelInfo("claude-sonnet-4-6", "Claude Sonnet 4.6 (Thinking)")],
            ModelListParsers.Agy("Fetching available models...\ngemini-3.8-flash-high\tGemini 3.8 Flash (High)\r\nclaude-sonnet-4-6\tClaude Sonnet 4.6 (Thinking)\n\n"));

    [Fact]
    public void OpenAi_data_ids_sorted()
        => Assert.Equal(["a-model", "b-model"],
            ModelListParsers.OpenAi("""{"object":"list","data":[{"id":"b-model"},{"id":"a-model"}]}""").Select(m => m.Id));

    [Fact]
    public void Gemini_keeps_generate_content_models_without_prefix()
    {
        var models = ModelListParsers.Gemini("""
            {"models":[
              {"name":"models/gemini-3.8-flash","displayName":"Gemini 3.8 Flash","supportedGenerationMethods":["generateContent","countTokens"]},
              {"name":"models/text-embedding-005","displayName":"Embedding","supportedGenerationMethods":["embedContent"]}]}
            """);
        Assert.Equal([new ModelInfo("gemini-3.8-flash", "Gemini 3.8 Flash")], models);
    }

    [Fact]
    public void Anthropic_ids_with_display_names()
        => Assert.Equal([new ModelInfo("claude-x", "Claude X")],
            ModelListParsers.Anthropic("""{"data":[{"id":"claude-x","display_name":"Claude X","type":"model"}],"has_more":false}"""));

    [Fact]
    public void Malformed_json_is_a_model_list_error()
        => Assert.Throws<ModelListException>(() => ModelListParsers.OpenAi("<html>not json</html>"));
}
```

`tests/Hotline.Core.Tests/ModelCatalogTests.cs`:
```csharp
using System.Net;
using Hotline.Core.Backends;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class ModelCatalogTests
{
    private sealed class FakeHttp(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(body) };

    private static (ModelCatalog, FakeHttp, InMemorySecretStore, ManualTimeProvider) New(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var http = new FakeHttp(respond);
        var secrets = new InMemorySecretStore();
        var clock = new ManualTimeProvider();
        return (new ModelCatalog(new HttpClient(http), secrets, (_, _) => Task.FromResult("m1\tModel One\n"), clock), http, secrets, clock);
    }

    [Fact]
    public async Task OpenAi_compatible_sends_bearer_key_to_endpoint_models()
    {
        var (catalog, http, secrets, _) = New(_ => Json("""{"data":[{"id":"gpt-x"}]}"""));
        var p = new BackendProfile { Id = "oa", Type = BackendType.OpenAiCompatible, Endpoint = "https://api.example.com/v1/" };
        secrets.Set(SecretKeys.ApiKey("oa"), "sk-test");
        var models = await catalog.GetAsync(p, refresh: false, default);
        Assert.Equal("gpt-x", Assert.Single(models).Id);
        Assert.Equal("https://api.example.com/v1/models", http.Requests[0].RequestUri!.ToString());
        Assert.Equal("Bearer sk-test", http.Requests[0].Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Gemini_uses_goog_header_and_default_endpoint()
    {
        var (catalog, http, secrets, _) = New(_ => Json("""{"models":[]}"""));
        secrets.Set(SecretKeys.ApiKey("g"), "AIza");
        await catalog.GetAsync(new BackendProfile { Id = "g", Type = BackendType.Gemini }, false, default);
        Assert.StartsWith("https://generativelanguage.googleapis.com/v1beta/models", http.Requests[0].RequestUri!.ToString());
        Assert.Equal("AIza", http.Requests[0].Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task Anthropic_sends_key_and_version()
    {
        var (catalog, http, secrets, _) = New(_ => Json("""{"data":[]}"""));
        secrets.Set(SecretKeys.ApiKey("a"), "sk-ant");
        await catalog.GetAsync(new BackendProfile { Id = "a", Type = BackendType.Anthropic }, false, default);
        Assert.Equal("sk-ant", http.Requests[0].Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", http.Requests[0].Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task Missing_required_key_is_reported_without_calling()
    {
        var (catalog, http, _, _) = New(_ => Json("{}"));
        var ex = await Assert.ThrowsAsync<ModelListException>(() => catalog.GetAsync(new BackendProfile { Id = "g", Type = BackendType.Gemini }, false, default));
        Assert.Contains("API key", ex.Message);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task Local_endpoint_works_without_key()
    {
        var (catalog, http, _, _) = New(_ => Json("""{"data":[{"id":"qwen"}]}"""));
        var models = await catalog.GetAsync(new BackendProfile { Id = "l", Type = BackendType.Local }, false, default);
        Assert.Equal("qwen", Assert.Single(models).Id);
        Assert.Null(http.Requests[0].Headers.Authorization);
    }

    [Fact]
    public async Task Unauthorized_is_reported()
    {
        var (catalog, _, secrets, _) = New(_ => Json("""{"error":"bad key"}""", HttpStatusCode.Unauthorized));
        secrets.Set(SecretKeys.ApiKey("oa"), "bad");
        var ex = await Assert.ThrowsAsync<ModelListException>(() =>
            catalog.GetAsync(new BackendProfile { Id = "oa", Type = BackendType.OpenAiCompatible }, false, default));
        Assert.Contains("rejected", ex.Message);
    }

    [Fact]
    public async Task Unreachable_server_is_reported()
    {
        var (catalog, _, _, _) = New(_ => throw new HttpRequestException("connection refused"));
        var ex = await Assert.ThrowsAsync<ModelListException>(() =>
            catalog.GetAsync(new BackendProfile { Id = "l", Type = BackendType.Local }, false, default));
        Assert.Contains("reach", ex.Message);
    }

    [Fact]
    public async Task Results_are_cached_until_refresh_or_ttl()
    {
        var (catalog, http, _, clock) = New(_ => Json("""{"data":[{"id":"x"}]}"""));
        var p = new BackendProfile { Id = "l", Type = BackendType.Local };
        await catalog.GetAsync(p, false, default);
        await catalog.GetAsync(p, false, default);
        Assert.Single(http.Requests);
        await catalog.GetAsync(p, refresh: true, default);
        Assert.Equal(2, http.Requests.Count);
        clock.Advance(TimeSpan.FromMinutes(11));
        await catalog.GetAsync(p, false, default);
        Assert.Equal(3, http.Requests.Count);
    }

    [Fact]
    public async Task Agy_uses_cli_runner_and_claude_code_uses_aliases()
    {
        var (catalog, _, _, _) = New(_ => Json("{}"));
        Assert.Equal("m1", Assert.Single(await catalog.GetAsync(new BackendProfile { Id = "agy", Type = BackendType.Antigravity }, false, default)).Id);
        Assert.Contains(await catalog.GetAsync(new BackendProfile { Id = "cc", Type = BackendType.ClaudeCode }, false, default), m => m.Id == "sonnet");
    }
}
```

Add to `tests/Hotline.Core.Tests/BackendCatalogTests.cs` (inside the class):
```csharp
    [Fact]
    public async Task Invalidate_disposes_and_recreates_on_next_get()
    {
        var created = 0;
        var profiles = new List<BackendProfile> { new() { Id = "a", Type = BackendType.Antigravity } };
        var cache = new BackendCache(() => profiles, _ => { created++; return new FakeBackend(); });
        cache.Get("a");
        await cache.InvalidateAsync("a");
        cache.Get("a");
        Assert.Equal(2, created);
        await cache.InvalidateAsync("missing"); // no-op
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with `CS0246: ... 'ModelInfo'` / `InMemorySecretStore`.

- [ ] **Step 3: Implement**

`src/Hotline.Core/Backends/Secrets.cs`:
```csharp
namespace Hotline.Core.Backends;

/// <summary>Where API keys live (the App uses Windows Credential Locker; never settings.json).</summary>
public interface ISecretStore
{
    string? Get(string key);
    /// <summary>Stores the value; null or empty deletes it.</summary>
    void Set(string key, string? value);
}

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = [];
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) _values.Remove(key);
        else _values[key] = value;
    }
}

public static class SecretKeys
{
    public static string ApiKey(string connectionId) => $"connection:{connectionId}:apiKey";
}
```

`src/Hotline.Core/Backends/ModelCatalog.cs`:
```csharp
using System.Text.Json;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

public sealed record ModelInfo(string Id, string Label);

public sealed class ModelListException(string message) : Exception(message);

public static class ModelListParsers
{
    public static IReadOnlyList<ModelInfo> ClaudeCodeAliases { get; } =
        [new("sonnet", "Sonnet (latest)"), new("opus", "Opus (latest)"), new("haiku", "Haiku (latest)")];

    /// <summary>`agy models` prints "id\tLabel" per line (plus a "Fetching…" banner).</summary>
    public static IReadOnlyList<ModelInfo> Agy(string output) =>
        output.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Contains('\t'))
            .Select(l => l.Split('\t', 2)).Select(p => new ModelInfo(p[0].Trim(), p[1].Trim())).ToList();

    public static IReadOnlyList<ModelInfo> OpenAi(string json) => Parse(json, root =>
        root.GetProperty("data").EnumerateArray().Select(m => m.GetProperty("id").GetString()!)
            .Order(StringComparer.Ordinal).Select(id => new ModelInfo(id, id)).ToList());

    public static IReadOnlyList<ModelInfo> Gemini(string json) => Parse(json, root =>
        root.TryGetProperty("models", out var models)
            ? models.EnumerateArray()
                .Where(m => m.TryGetProperty("supportedGenerationMethods", out var g) && g.EnumerateArray().Any(x => x.GetString() == "generateContent"))
                .Select(m =>
                {
                    var id = m.GetProperty("name").GetString()!.Replace("models/", "", StringComparison.Ordinal);
                    return new ModelInfo(id, m.TryGetProperty("displayName", out var d) ? d.GetString() ?? id : id);
                }).ToList()
            : []);

    public static IReadOnlyList<ModelInfo> Anthropic(string json) => Parse(json, root =>
        root.GetProperty("data").EnumerateArray().Select(m =>
        {
            var id = m.GetProperty("id").GetString()!;
            return new ModelInfo(id, m.TryGetProperty("display_name", out var d) ? d.GetString() ?? id : id);
        }).ToList());

    private static IReadOnlyList<ModelInfo> Parse(string json, Func<JsonElement, IReadOnlyList<ModelInfo>> read)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return read(doc.RootElement);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new ModelListException("The server's model list wasn't in the expected format.");
        }
    }
}

/// <summary>Lists the models a connection offers, cached for 10 minutes per connection.</summary>
public sealed class ModelCatalog(HttpClient http, ISecretStore secrets, Func<BackendProfile, CancellationToken, Task<string>> runAgyModels, TimeProvider clock)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);
    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<ModelInfo> Models)> _cache = [];

    public async Task<IReadOnlyList<ModelInfo>> GetAsync(BackendProfile p, bool refresh, CancellationToken ct)
    {
        var key = $"{p.Type}|{p.Id}|{p.Endpoint}|{p.CliPath}";
        if (!refresh && _cache.TryGetValue(key, out var hit) && clock.GetUtcNow() - hit.At < Ttl) return hit.Models;
        var models = await FetchAsync(p, ct);
        _cache[key] = (clock.GetUtcNow(), models);
        return models;
    }

    private async Task<IReadOnlyList<ModelInfo>> FetchAsync(BackendProfile p, CancellationToken ct)
    {
        switch (p.Type)
        {
            case BackendType.Antigravity:
                return ModelListParsers.Agy(await runAgyModels(p, ct));
            case BackendType.ClaudeCode:
                return ModelListParsers.ClaudeCodeAliases;
            case BackendType.Gemini:
                return ModelListParsers.Gemini(await GetAsync(p, "models?pageSize=1000", (r, k) => r.Headers.Add("x-goog-api-key", k), ct));
            case BackendType.Anthropic:
                return ModelListParsers.Anthropic(await GetAsync(p, "models?limit=100", (r, k) =>
                {
                    r.Headers.Add("x-api-key", k);
                    r.Headers.Add("anthropic-version", "2023-06-01");
                }, ct));
            default: // OpenAiCompatible, Local
                return ModelListParsers.OpenAi(await GetAsync(p, "models", (r, k) =>
                    r.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", k), ct));
        }
    }

    private async Task<string> GetAsync(BackendProfile p, string path, Action<HttpRequestMessage, string> addKey, CancellationToken ct)
    {
        var info = ConnectionTypes.Of(p.Type);
        var key = secrets.Get(SecretKeys.ApiKey(p.Id));
        if (string.IsNullOrEmpty(key) && !info.ApiKeyOptional)
            throw new ModelListException($"Add an API key for {p.Name} first.");
        var endpoint = (string.IsNullOrWhiteSpace(p.Endpoint) ? info.DefaultEndpoint : p.Endpoint)!.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/{path}");
        if (!string.IsNullOrEmpty(key)) addKey(request, key);
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
                throw new ModelListException($"{p.Name} rejected the API key.");
            if (!response.IsSuccessStatusCode)
                throw new ModelListException($"{p.Name} answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ModelListException($"Can't reach {endpoint}: {ex.Message}");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new ModelListException($"{endpoint} didn't answer in time.");
        }
    }
}
```

In `src/Hotline.Core/Backends/BackendCatalog.cs`, add to `BackendCache` (before `DisposeAllAsync`):
```csharp
    /// <summary>Drops the live instance for a connection (after its settings change); the next Get recreates it.</summary>
    public async ValueTask InvalidateAsync(string id)
    {
        if (!_instances.Remove(id, out var backend) || backend is null) return;
        await backend.DisposeAsync();
    }
```

- [ ] **Step 4: Run tests, commit**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`.
```powershell
git add -A
git commit -m "feat(core): model lists per provider with cache, secret store abstraction, backend invalidation" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Prompt library and agy tool mode (Core)

**Files:**
- Create: `src/Hotline.Core/Chat/PromptLibrary.cs`
- Modify: `src/Hotline.Core/Backends/Agy/AgyWorkspace.cs`, `src/Hotline.Core/Backends/Agy/AgyProtocol.cs`, `src/Hotline.Core/Backends/Agy/AgyBackend.cs`, `src/Hotline.Core/Backends/BackendCatalog.cs`
- Test: `tests/Hotline.Core.Tests/PromptLibraryTests.cs`; update `AgyWorkspaceTests.cs`, `AgyBackendTests.cs`, `AgyProtocolTests.cs`, `BackendCatalogTests.cs`

**Interfaces:**
- Produces:
  - `sealed class PromptLibrary(string directory)` with:
    - `const DefaultName = "default"`, `const DefaultText`
    - `string Directory`, `void EnsureDefault()`, `IReadOnlyList<string> List()`, `string Read(string? name)`
    - `string PathFor(string name)`, `string Create(string? baseName = null)`, `bool Delete(string name)`
    - `static bool IsValidName(string name)`
  - `AgyWorkspace.Ensure(string systemPrompt)`, `static string AgyWorkspace.AgentMarkdownFor(string systemPrompt)`
  - `AgyProtocol.BuildArgs(BackendProfile p, string? addDir = null)`
  - `AgyProtocol.ComposePrompt(text, imagePaths, textFiles, priorContext, string? instructions = null)`
  - `AgyBackend(BackendProfile, Func<string?>, AgyWorkspace, ILineProcessFactory, FileLog, Func<BackendProfile, string> systemPrompt, string homeDirectory)`
  - `BackendDeps` gains `Func<BackendProfile, string> SystemPrompt, string HomeDirectory`

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/PromptLibraryTests.cs`:
```csharp
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public sealed class PromptLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"), "prompts");
    public void Dispose() { var root = Path.GetDirectoryName(_dir)!; if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    [Fact]
    public void Ensure_default_creates_file_once_without_overwriting_edits()
    {
        var lib = new PromptLibrary(_dir);
        lib.EnsureDefault();
        Assert.Equal(PromptLibrary.DefaultText, lib.Read("default"));
        File.WriteAllText(lib.PathFor("default"), "my edits");
        lib.EnsureDefault();
        Assert.Equal("my edits", lib.Read("default"));
    }

    [Fact]
    public void List_puts_default_first_then_alphabetical()
    {
        var lib = new PromptLibrary(_dir);
        lib.EnsureDefault();
        File.WriteAllText(lib.PathFor("zeta"), "z");
        File.WriteAllText(lib.PathFor("alpha"), "a");
        Assert.Equal(["default", "alpha", "zeta"], lib.List());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("../evil")]
    public void Missing_or_unsafe_names_fall_back_to_default(string? name)
    {
        var lib = new PromptLibrary(_dir);
        lib.EnsureDefault();
        Assert.Equal(PromptLibrary.DefaultText, lib.Read(name));
    }

    [Fact]
    public void Empty_file_falls_back_to_default_text()
    {
        var lib = new PromptLibrary(_dir);
        lib.EnsureDefault();
        File.WriteAllText(lib.PathFor("blank"), "   ");
        Assert.Equal(PromptLibrary.DefaultText, lib.Read("blank"));
    }

    [Fact]
    public void Create_copies_default_with_unique_name_and_delete_protects_default()
    {
        var lib = new PromptLibrary(_dir);
        lib.EnsureDefault();
        var a = lib.Create();
        var b = lib.Create();
        Assert.Equal(("prompt", "prompt-2"), (a, b));
        Assert.Equal(PromptLibrary.DefaultText, lib.Read("prompt"));
        Assert.True(lib.Delete("prompt"));
        Assert.False(lib.Delete("default"));
        Assert.DoesNotContain("prompt", lib.List());
    }

    [Theory]
    [InlineData("work notes", true)]
    [InlineData("code-review_2", true)]
    [InlineData("../x", false)]
    [InlineData("a:b", false)]
    [InlineData("", false)]
    public void Name_validation(string name, bool ok) => Assert.Equal(ok, PromptLibrary.IsValidName(name));
}
```

In `tests/Hotline.Core.Tests/AgyWorkspaceTests.cs`, replace the body of `Agent_file_keeps_default_components` with:
```csharp
        new AgyWorkspace(_dir, _clock).Ensure("Be a pirate.");
        var md = File.ReadAllText(Path.Combine(_dir, ".agents", "agents", "hotline.md"));
        Assert.StartsWith("---", md);
        Assert.Contains("name: hotline", md);
        Assert.Contains("Be a pirate.", md);
        Assert.DoesNotContain("excludeDefaultComponents", md);
```

In `tests/Hotline.Core.Tests/AgyProtocolTests.cs`, add (inside the class):
```csharp
    [Fact]
    public void Inherit_mode_uses_default_agent_and_adds_workspace_dir()
    {
        var args = AgyProtocol.BuildArgs(new BackendProfile { Id = "agy", Agent = "hotline", Tools = ToolMode.Inherit }, addDir: @"C:\ws");
        Assert.DoesNotContain("--agent", args);
        Assert.Equal(@"C:\ws", args[args.ToList().IndexOf("--add-dir") + 1]);
    }

    [Fact]
    public void Instructions_are_prepended_once()
    {
        var p = AgyProtocol.ComposePrompt("hi", [], [], [], instructions: "Be brief.");
        Assert.StartsWith("Instructions for this conversation:", p);
        Assert.Contains("Be brief.", p);
        Assert.EndsWith("hi", p);
    }
```

In `tests/Hotline.Core.Tests/AgyBackendTests.cs`:
- Change the `New` helper to:
```csharp
    private AgyBackend New(string? exe = @"C:\agy.exe", BackendProfile? profile = null, string? home = null) =>
        new(profile ?? new BackendProfile { Id = "agy", Name = "Gemini (Antigravity)", Agent = "hotline" }, () => exe,
            new AgyWorkspace(Path.Combine(_dir, "ws"), new ManualTimeProvider()), _factory, new FileLog(Path.Combine(_dir, "h.log")),
            _ => "SYSTEM PROMPT", home ?? Path.Combine(_dir, "home"));
```
- Add:
```csharp
    [Fact]
    public async Task Chat_only_writes_prompt_into_agent_file()
    {
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        await Collect(New().StreamAsync([U("1", "hi")], default));
        Assert.Contains("SYSTEM PROMPT", File.ReadAllText(Path.Combine(_dir, "ws", ".agents", "agents", "hotline.md")));
    }

    [Fact]
    public async Task Inherit_mode_uses_working_dir_and_prefixes_prompt()
    {
        var work = Directory.CreateDirectory(Path.Combine(_dir, "project")).FullName;
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var img = new Attachment("a1", "shot.png", AttachmentKind.Image, "image/png", [1, 2]);
        var b = New(profile: new BackendProfile { Id = "agy", Name = "G", Tools = ToolMode.Inherit, WorkingDirectory = work });
        await Collect(b.StreamAsync([U("m1", "look", img)], default));

        var (_, args, cwd, proc) = _factory.Started[0];
        Assert.Equal(work, cwd);
        Assert.DoesNotContain("--agent", args);
        var prompt = PromptOf(proc.Written[0]);
        Assert.StartsWith("Instructions for this conversation:", prompt);
        Assert.Contains(Path.Combine(_dir, "ws", "attachments", "m1", "shot.png"), prompt); // absolute path
    }

    [Fact]
    public async Task Inherit_missing_working_dir_falls_back_to_home()
    {
        var home = Directory.CreateDirectory(Path.Combine(_dir, "home")).FullName;
        _factory.Create = () => new FakeLineProcess { Respond = _ => Answer("ok") };
        var b = New(profile: new BackendProfile { Id = "agy", Name = "G", Tools = ToolMode.Inherit, WorkingDirectory = @"Z:\does\not\exist" }, home: home);
        await Collect(b.StreamAsync([U("1", "hi")], default));
        Assert.Equal(home, _factory.Started[0].Cwd);
    }
```

In `tests/Hotline.Core.Tests/BackendCatalogTests.cs`, change `Deps()` to:
```csharp
    private BackendDeps Deps() => new(new FakeLineProcessFactory(), new AgyWorkspace(_dir, new ManualTimeProvider()),
        new FileLog(Path.Combine(_dir, "h.log")), _ => false, null, null, _ => "prompt", _dir);
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with `CS0246: ... 'PromptLibrary'` and argument-count errors for `AgyBackend` and `BackendDeps`.

- [ ] **Step 3: Implement**

`src/Hotline.Core/Chat/PromptLibrary.cs`:
```csharp
using System.Text.RegularExpressions;

namespace Hotline.Core.Chat;

/// <summary>System prompts as editable Markdown files: %USERPROFILE%\.hotline\prompts\&lt;name&gt;.md.</summary>
public sealed partial class PromptLibrary(string directory)
{
    public const string DefaultName = "default";
    public const string DefaultText =
        "You are Hotline, a fast desktop assistant opened from the Windows Copilot key. Answer directly and concisely in Markdown.";

    public string Directory => directory;

    public static bool IsValidName(string name) => SafeName().IsMatch(name);

    public string PathFor(string name) => Path.Combine(directory, name + ".md");

    public void EnsureDefault()
    {
        System.IO.Directory.CreateDirectory(directory);
        if (!File.Exists(PathFor(DefaultName))) File.WriteAllText(PathFor(DefaultName), DefaultText);
    }

    public IReadOnlyList<string> List()
    {
        if (!System.IO.Directory.Exists(directory)) return [DefaultName];
        var names = System.IO.Directory.EnumerateFiles(directory, "*.md").Select(Path.GetFileNameWithoutExtension)
            .OfType<string>().Where(IsValidName).Where(n => n != DefaultName).Order(StringComparer.OrdinalIgnoreCase).ToList();
        names.Insert(0, DefaultName);
        return names;
    }

    /// <summary>The prompt text; missing, unsafe or empty prompts fall back to default.md, then to the built-in text.</summary>
    public string Read(string? name)
    {
        foreach (var candidate in new[] { name, DefaultName })
        {
            if (candidate is null || !IsValidName(candidate)) continue;
            try
            {
                var path = PathFor(candidate);
                if (File.Exists(path) && File.ReadAllText(path) is { } text && !string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return DefaultText;
    }

    /// <summary>Creates a new prompt (a copy of the default text) with a unique name; returns the name.</summary>
    public string Create(string? baseName = null)
    {
        System.IO.Directory.CreateDirectory(directory);
        var stem = baseName is not null && IsValidName(baseName) ? baseName : "prompt";
        var name = stem;
        for (var i = 2; File.Exists(PathFor(name)); i++) name = $"{stem}-{i}";
        File.WriteAllText(PathFor(name), Read(DefaultName));
        return name;
    }

    public bool Delete(string name)
    {
        if (name == DefaultName || !IsValidName(name) || !File.Exists(PathFor(name))) return false;
        File.Delete(PathFor(name));
        return true;
    }

    [GeneratedRegex(@"^[\w\- ]+$")]
    private static partial Regex SafeName();
}
```

`src/Hotline.Core/Backends/Agy/AgyWorkspace.cs`: replace the `AgentMarkdown` constant and `Ensure()` method with:
```csharp
    // Do NOT add excludeDefaultComponents: it drops the default permissions (workspace reads get denied).
    public static string AgentMarkdownFor(string systemPrompt) => $"""
        ---
        name: hotline
        description: Fast conversational assistant for the Hotline popup.
        tools:
          - view_file
        ---
        {systemPrompt.Trim()}

        When the user lists attached images under ./attachments, use view_file to look at them.
        Do not run commands, browse the web or edit files in this mode.
        """;

    public void Ensure(string systemPrompt)
    {
        var agentDir = Path.Combine(Root, ".agents", "agents");
        Directory.CreateDirectory(agentDir);
        Directory.CreateDirectory(AttachmentsDir);
        var agentFile = Path.Combine(agentDir, "hotline.md");
        var markdown = AgentMarkdownFor(systemPrompt);
        if (!File.Exists(agentFile) || File.ReadAllText(agentFile) != markdown)
            File.WriteAllText(agentFile, markdown);
    }
```

`src/Hotline.Core/Backends/Agy/AgyProtocol.cs`:
- Change the `ComposePrompt` signature to add `string? instructions = null` as the last parameter. At the start of its body, after `var sb = new StringBuilder();`, add:
```csharp
        if (!string.IsNullOrWhiteSpace(instructions))
            sb.AppendLine("Instructions for this conversation:").AppendLine(instructions.Trim()).AppendLine();
```
- Replace `BuildArgs` with:
```csharp
    public static IReadOnlyList<string> BuildArgs(BackendProfile p, string? addDir = null)
    {
        var args = new List<string> { "--input-format", "stream-json", "--output-format", "stream-json", "-p=" };
        if (p.Tools == ToolMode.ChatOnly)
            args.AddRange(["--agent", string.IsNullOrWhiteSpace(p.Agent) ? DefaultAgent : p.Agent]);
        if (!string.IsNullOrWhiteSpace(addDir)) args.AddRange(["--add-dir", addDir]);
        if (!string.IsNullOrWhiteSpace(p.Model)) args.AddRange(["--model", p.Model]);
        if (p.Effort?.Trim().ToLowerInvariant() is "low" or "medium" or "high" or "max")
            args.AddRange(["--effort", p.Effort.Trim().ToLowerInvariant()]);
        if (!string.IsNullOrWhiteSpace(p.ExtraArgs))
            args.AddRange(p.ExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(a => !a.Equals("--dangerously-skip-permissions", StringComparison.OrdinalIgnoreCase)));
        return args;
    }
```

`src/Hotline.Core/Backends/Agy/AgyBackend.cs`:
- Constructor → `AgyBackend(BackendProfile profile, Func<string?> locateExe, AgyWorkspace workspace, ILineProcessFactory processes, FileLog log, Func<BackendProfile, string> systemPrompt, string homeDirectory)`.
- Replace the block that starts the process:
```csharp
            await StopAsync();
            workspace.Ensure();
            var args = AgyProtocol.BuildArgs(profile);
            _process = processes.Start(exe, args, workspace.Root);
```
with:
```csharp
            await StopAsync();
            workspace.Ensure(systemPrompt(profile));
            var inherit = profile.Tools == ToolMode.Inherit;
            var cwd = inherit ? WorkingDirectory() : workspace.Root;
            var args = AgyProtocol.BuildArgs(profile, inherit ? workspace.Root : null);
            _process = processes.Start(exe, args, cwd);
```
- Replace the line `var prompt = AgyProtocol.ComposePrompt(user.Text, images, texts, fresh ? prior : Array.Empty<ChatMessage>());` with:
```csharp
        var inheritMode = profile.Tools == ToolMode.Inherit;
        if (inheritMode) images = images.Select(p => Path.Combine(workspace.Root, p.Replace('/', Path.DirectorySeparatorChar))).ToList();
        var prompt = AgyProtocol.ComposePrompt(user.Text, images, texts, fresh ? prior : Array.Empty<ChatMessage>(),
            fresh && inheritMode ? systemPrompt(profile) : null);
```
- Add the method:
```csharp
    private string WorkingDirectory() =>
        !string.IsNullOrWhiteSpace(profile.WorkingDirectory) && Directory.Exists(profile.WorkingDirectory) ? profile.WorkingDirectory : homeDirectory;
```
(`images` must be a `var` of type `IReadOnlyList<string>`; if the compiler rejects reassignment, declare it as `IReadOnlyList<string> images = workspace.SaveImages(...)`.)

`src/Hotline.Core/Backends/BackendCatalog.cs`: change `BackendDeps` to
```csharp
public sealed record BackendDeps(
    ILineProcessFactory Processes, AgyWorkspace AgyWorkspace, FileLog Log,
    Func<string, bool> FileExists, string? LocalAppData, string? PathEnv,
    Func<BackendProfile, string> SystemPrompt, string HomeDirectory);
```
and the factory's agy line to pass `deps.Log, deps.SystemPrompt, deps.HomeDirectory`.

- [ ] **Step 4: Run tests, commit**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`. The App build will be fixed in Task 5 (it constructs `BackendDeps`).
```powershell
git add -A
git commit -m "feat(core): prompt library; agy system prompts and inherit tool mode" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Bottom-bar pickers, prompt menu, secrets, live apply (App)

**Files:**
- Create: `src/Hotline.App/Chat/ProviderBar.cs`, `src/Hotline.App/Chat/PasswordVaultSecretStore.cs`, `src/Hotline.App/Chat/CliRunner.cs`
- Modify: `src/Hotline.App/PopupWindow.xaml`, `src/Hotline.App/PopupWindow.xaml.cs`, `src/Hotline.App/Chat/ChatPresenter.cs`, `src/Hotline.App/App.xaml.cs`

**Interfaces:**
- Consumes: Tasks 1–4.
- Produces:
  - `PopupWindow`: named elements `ProviderBox`, `ModelBox`, `EffortBox`, `PromptButton`, `PromptMenu`; `GrowMode` property; `void ApplyAppearance()`
  - `ChatPresenter`: `event Action<bool>? BusyChanged`, `internal void Notice(string, InfoBarSeverity)`, `void ApplyAppearance()`, `event Action? SettingsRequested`
  - `ProviderBar(PopupWindow, SettingsService, ChatController, BackendCache, ModelCatalog, PromptLibrary, Action<string, InfoBarSeverity> notify, FileLog)` with `Initialize()`, `Refresh()`, `SetEnabled(bool)`
  - `static CliRunner.RunAsync(string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)` and `static CliRunner.OpenInEditor(string path)`, `CliRunner.OpenFolder(string path)`

- [ ] **Step 1: Toolbar XAML**

In `src/Hotline.App/PopupWindow.xaml`, replace the whole `<Grid x:Name="Toolbar" …>…</Grid>` element with:
```xml
        <!-- Bottom toolbar: pin, capture, who answers (provider / model / effort), prompt, new chat, settings. -->
        <Grid x:Name="Toolbar" x:FieldModifier="internal" Grid.Row="3" ColumnSpacing="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <ToggleButton x:Name="PinButton" x:FieldModifier="internal" Width="36" Height="32" Padding="0" BorderThickness="0"
                          Background="Transparent" FontFamily="Segoe Fluent Icons" FontSize="15" Content="&#xE718;"
                          ToolTipService.ToolTip="Pin open (stays open when you click elsewhere, so you can drag files in)" />
            <Button x:Name="CaptureWindowButton" x:FieldModifier="internal" Grid.Column="1" Style="{StaticResource ToolbarButton}"
                    Content="&#xE7C4;" ToolTipService.ToolTip="Capture the window you were in" />
            <StackPanel Grid.Column="3" Orientation="Horizontal" Spacing="4" VerticalAlignment="Center">
                <ComboBox x:Name="ProviderBox" x:FieldModifier="internal" Width="140" Height="30" FontSize="12" Padding="8,2"
                          ToolTipService.ToolTip="Who answers" />
                <ComboBox x:Name="ModelBox" x:FieldModifier="internal" Width="150" Height="30" FontSize="12" Padding="8,2"
                          IsEditable="True" ToolTipService.ToolTip="Model (type a name or pick one)" />
                <ComboBox x:Name="EffortBox" x:FieldModifier="internal" Width="92" Height="30" FontSize="12" Padding="8,2"
                          ToolTipService.ToolTip="Reasoning effort" />
            </StackPanel>
            <Button x:Name="PromptButton" x:FieldModifier="internal" Grid.Column="4" Style="{StaticResource ToolbarButton}"
                    Content="&#xE8A5;" ToolTipService.ToolTip="System prompt">
                <Button.Flyout>
                    <MenuFlyout x:Name="PromptMenu" x:FieldModifier="internal" Placement="TopEdgeAlignedRight" />
                </Button.Flyout>
            </Button>
            <Button x:Name="NewChatButton" x:FieldModifier="internal" Grid.Column="5" Style="{StaticResource ToolbarButton}"
                    Content="&#xE8BD;" ToolTipService.ToolTip="New chat (Ctrl+N)" />
            <Button x:Name="SettingsButton" x:FieldModifier="internal" Grid.Column="6" Style="{StaticResource ToolbarButton}"
                    Content="&#xE713;" ToolTipService.ToolTip="Settings" />
        </Grid>
```
"Capture screen" is still available in the + menu; it moved off the toolbar to make room.

- [ ] **Step 2: PopupWindow live appearance**

In `src/Hotline.App/PopupWindow.xaml.cs`:
1. Replace `private readonly GrowMode _growMode;` with `public GrowMode GrowMode { get; set; }`. In the constructor, replace `_growMode = growMode;` with `GrowMode = growMode;`. In `ApplyHeight`, replace `_growMode` with `GrowMode`.
2. Move the theme/scrollbar/always-on-top setup into a new public method, called at the end of the constructor:
```csharp
    /// <summary>Re-applies backdrop, theme, always-on-top and scrollbar from the (live) settings object.</summary>
    public void ApplyAppearance()
    {
        SystemBackdrop = Backdrops.Create(_settings);
        if (AppWindow.Presenter is OverlappedPresenter op) op.IsAlwaysOnTop = _settings.AlwaysOnTop;
        Root.RequestedTheme = _settings.Theme switch
        {
            ThemeChoice.Light => ElementTheme.Light,
            ThemeChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (_settings.Backdrop != BackdropKind.Solid) Root.Background = null;
        MessagesScroll.VerticalScrollBarVisibility = _settings.Scrollbar switch
        {
            ScrollbarStyle.Visible => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Visible,
            ScrollbarStyle.Hidden => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden,
            _ => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
        };
        if (AppWindow.IsVisible) PlaceOnActiveMonitor();
    }
```
Delete the constructor's own `SystemBackdrop = Backdrops.Create(settings);`, the `presenter.IsAlwaysOnTop = …` line, the `Root.RequestedTheme = …` block and the `MessagesScroll.VerticalScrollBarVisibility = …` block. Add `ApplyAppearance();` just before `Activated += OnActivated;`.

- [ ] **Step 3: Presenter hooks**

In `src/Hotline.App/Chat/ChatPresenter.cs`:
1. Add the events `public event Action<bool>? BusyChanged;` and `public event Action? SettingsRequested;`.
2. In `SetBusy`, add `BusyChanged?.Invoke(busy);` at the end.
3. Change `private void Notice(` to `internal void Notice(`.
4. Extract the token/style lines from `Initialize` (computing `dark`, `_tokens`, `_style`, the Solid background and the input font) into:
```csharp
    /// <summary>Recomputes tokens from the live settings and re-renders answers (font, colours, solid background).</summary>
    public void ApplyAppearance()
    {
        var dark = settings.Window.Theme switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
        _tokens = ThemeTokens.For(dark, settings.Window);
        _style = new RenderStyle(_tokens.FontSizePx, new FontFamily(_tokens.Font), Brush(_tokens.Muted), Brush(_tokens.CodeBackground),
            Brush(_tokens.Accent), _tokens.RadiusPx, OpenLink);
        if (settings.Window.Backdrop == BackdropKind.Solid) popup.Root.Background = Brush(_tokens.SolidBackground);
        popup.Input.FontSize = _tokens.FontSizePx;
        popup.Input.FontFamily = _style.Font;
        foreach (var view in _assistants.Values)
        {
            view.Body.Blocks.Clear();
            view.ParagraphCounts.Clear();
            view.Blocks = [];
            view.Body.FontSize = _tokens.FontSizePx;
            view.Body.FontFamily = _style.Font;
            view.Dirty = true;
        }
        if (_assistants.Count > 0) RenderDirty();
    }
```
   Call `ApplyAppearance();` at the start of `Initialize` in their place.
5. Delete the `popup.CaptureScreenButton.Click += …` line, the `UpdateBackendLabel` method and its call.
6. Replace `popup.SettingsButton.Click += (_, _) => OpenSettings();` with `popup.SettingsButton.Click += (_, _) => { if (SettingsRequested is null) OpenSettings(); else SettingsRequested(); };`.

- [ ] **Step 4: Secret store and CLI helpers**

`src/Hotline.App/Chat/PasswordVaultSecretStore.cs`:
```csharp
using Hotline.Core.Backends;
using Hotline.Core.Diagnostics;
using Windows.Security.Credentials;

namespace Hotline.App.Chat;

/// <summary>API keys in Windows Credential Locker (per user, encrypted), never in settings.json.</summary>
internal sealed class PasswordVaultSecretStore(FileLog log) : ISecretStore
{
    private const string Resource = "Hotline";
    private readonly PasswordVault _vault = new();

    public string? Get(string key)
    {
        try
        {
            var credential = _vault.Retrieve(Resource, key);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception) { return null; } // not found
    }

    public void Set(string key, string? value)
    {
        try { _vault.Remove(_vault.Retrieve(Resource, key)); } catch (Exception) { /* nothing stored */ }
        if (string.IsNullOrEmpty(value)) return;
        try { _vault.Add(new PasswordCredential(Resource, key, value)); }
        catch (Exception ex) { log.Error("saving API key failed", ex); throw; }
    }
}
```

`src/Hotline.App/Chat/CliRunner.cs`:
```csharp
using System.Diagnostics;
using System.Text;

namespace Hotline.App.Chat;

internal static class CliRunner
{
    /// <summary>Runs a command-line tool and returns its standard output (killed after <paramref name="timeout"/>).</summary>
    public static async Task<string> RunAsync(string exe, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {exe}");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
        _ = process.StandardError.ReadToEndAsync(cts.Token);
        try { await process.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
        return await stdout;
    }

    /// <summary>Opens a file in its associated editor (Notepad if .md has no association).</summary>
    public static void OpenInEditor(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true }); }
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }
}
```

- [ ] **Step 5: ProviderBar**

`src/Hotline.App/Chat/ProviderBar.cs`:
```csharp
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Chat;

/// <summary>Bottom-bar pickers: provider (connection), model and effort, plus the system-prompt menu.</summary>
internal sealed class ProviderBar(
    PopupWindow popup, SettingsService settings, ChatController chat, BackendCache backends, ModelCatalog models,
    PromptLibrary prompts, Action<string, InfoBarSeverity> notify, FileLog log)
{
    private const string DefaultLabel = "Default";
    private bool _updating;
    private bool _enabled = true;

    private BackendProfile? Current => settings.Current.Chat.Backends.FirstOrDefault(b => b.Id == chat.BackendId);

    public void Initialize()
    {
        popup.ProviderBox.SelectionChanged += (_, _) => { if (!_updating) OnProviderChanged(); };
        popup.ModelBox.SelectionChanged += (_, _) => { if (!_updating && popup.ModelBox.SelectedItem is string m) _ = SetModelAsync(m); };
        popup.ModelBox.TextSubmitted += (_, e) => { if (!_updating) _ = SetModelAsync(e.Text); };
        popup.ModelBox.DropDownOpened += (_, _) => _ = LoadModelsAsync();
        popup.EffortBox.SelectionChanged += (_, _) => { if (!_updating && popup.EffortBox.SelectedItem is string e) _ = SetEffortAsync(e); };
        popup.PromptMenu.Opening += (_, _) => BuildPromptMenu();
        Refresh();
    }

    /// <summary>Disabled while an answer streams: changing model/effort restarts the backend.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        popup.ProviderBox.IsEnabled = popup.ModelBox.IsEnabled = enabled;
        popup.EffortBox.IsEnabled = enabled && popup.EffortBox.Items.Count > 1;
    }

    public void Refresh()
    {
        _updating = true;
        try
        {
            popup.ProviderBox.Items.Clear();
            foreach (var p in settings.Current.Chat.Backends)
            {
                var available = BackendFactory.IsAvailable(p.Type);
                popup.ProviderBox.Items.Add(new ComboBoxItem { Content = available ? p.Name : $"{p.Name} (coming soon)", Tag = p.Id, IsEnabled = available });
            }
            popup.ProviderBox.SelectedItem = popup.ProviderBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == chat.BackendId);

            var current = Current;
            popup.ModelBox.Items.Clear();
            popup.ModelBox.Items.Add(DefaultLabel);
            if (!string.IsNullOrWhiteSpace(current?.Model)) popup.ModelBox.Items.Add(current.Model);
            popup.ModelBox.SelectedItem = string.IsNullOrWhiteSpace(current?.Model) ? DefaultLabel : current.Model;

            IReadOnlyList<string> levels = current is null ? Array.Empty<string>() : ConnectionTypes.Of(current.Type).EffortLevels;
            popup.EffortBox.Items.Clear();
            popup.EffortBox.Items.Add(DefaultLabel);
            foreach (var level in levels) popup.EffortBox.Items.Add(level);
            popup.EffortBox.SelectedItem = current?.Effort is { } e && levels.Contains(e) ? e : DefaultLabel;
            ToolTipService.SetToolTip(popup.EffortBox, levels.Count > 0 ? "Reasoning effort" : "This provider doesn't offer effort levels yet");
            SetEnabled(_enabled);
        }
        finally { _updating = false; }
    }

    private void OnProviderChanged()
    {
        if (popup.ProviderBox.SelectedItem is not ComboBoxItem { Tag: string id } || id == chat.BackendId) return;
        chat.BackendId = id;
        settings.Update(s => s.Chat.DefaultBackend = id); // Changed → Refresh
        log.Info($"provider switched to {id}");
    }

    private async Task SetModelAsync(string text)
    {
        var profile = Current;
        if (profile is null) return;
        var model = string.IsNullOrWhiteSpace(text) || text == DefaultLabel ? null : text.Trim();
        if (model == profile.Model) return;
        settings.Update(_ => profile.Model = model);
        await backends.InvalidateAsync(profile.Id);
        log.Info($"model for {profile.Id} = {model ?? "default"}");
    }

    private async Task SetEffortAsync(string level)
    {
        var profile = Current;
        if (profile is null) return;
        var effort = level == DefaultLabel ? null : level;
        if (effort == profile.Effort) return;
        settings.Update(_ => profile.Effort = effort);
        await backends.InvalidateAsync(profile.Id);
    }

    private async Task LoadModelsAsync()
    {
        var profile = Current;
        if (profile is null) return;
        try
        {
            var list = await models.GetAsync(profile, refresh: false, CancellationToken.None);
            _updating = true;
            var selected = popup.ModelBox.SelectedItem as string ?? DefaultLabel;
            popup.ModelBox.Items.Clear();
            popup.ModelBox.Items.Add(DefaultLabel);
            foreach (var m in list) popup.ModelBox.Items.Add(m.Id);
            if (selected != DefaultLabel && !list.Any(m => m.Id == selected)) popup.ModelBox.Items.Add(selected);
            popup.ModelBox.SelectedItem = selected;
        }
        catch (ModelListException ex) { notify(ex.Message, InfoBarSeverity.Warning); }
        catch (Exception ex) { log.Error("model list failed", ex); notify($"Couldn't list models: {ex.Message}", InfoBarSeverity.Warning); }
        finally { _updating = false; }
    }

    private void BuildPromptMenu()
    {
        popup.PromptMenu.Items.Clear();
        var profile = Current;
        var active = profile?.Prompt ?? settings.Current.Chat.DefaultPrompt;
        foreach (var name in prompts.List())
        {
            var item = new ToggleMenuFlyoutItem { Text = name, IsChecked = name == active };
            item.Click += async (_, _) =>
            {
                if (profile is null) return;
                settings.Update(_ => profile.Prompt = name == settings.Current.Chat.DefaultPrompt ? null : name);
                await backends.InvalidateAsync(profile.Id);
                notify($"System prompt: {name} (applies to the next message)", InfoBarSeverity.Informational);
            };
            popup.PromptMenu.Items.Add(item);
        }
        popup.PromptMenu.Items.Add(new MenuFlyoutSeparator());
        var edit = new MenuFlyoutItem { Text = $"Edit \"{active}\"…" };
        edit.Click += (_, _) => { popup.HidePopup(); CliRunner.OpenInEditor(prompts.PathFor(active)); };
        var create = new MenuFlyoutItem { Text = "New prompt…" };
        create.Click += (_, _) => { popup.HidePopup(); CliRunner.OpenInEditor(prompts.PathFor(prompts.Create())); };
        var folder = new MenuFlyoutItem { Text = "Open prompts folder" };
        folder.Click += (_, _) => { popup.HidePopup(); CliRunner.OpenFolder(prompts.Directory); };
        popup.PromptMenu.Items.Add(edit);
        popup.PromptMenu.Items.Add(create);
        popup.PromptMenu.Items.Add(folder);
    }
}
```

- [ ] **Step 6: App wiring**

In `src/Hotline.App/App.xaml.cs`:
1. Add fields `private ProviderBar? _providerBar; private SettingsService? _settingsService; private PromptLibrary? _prompts; private ModelCatalog? _models; private ISecretStore? _secrets;`.
2. Right after `var settings = store.Load();` (and the verbose/log lines), add:
```csharp
        _settingsService = new SettingsService(store, settings, _log);
        _prompts = new PromptLibrary(Path.Combine(dataDir, "prompts"));
        try { _prompts.EnsureDefault(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _log.Error("prompt folder setup failed", ex); }
        _secrets = new PasswordVaultSecretStore(_log);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
```
3. Change the `BackendDeps` construction to append `p => _prompts.Read(p.Prompt ?? settings.Chat.DefaultPrompt), home`.
4. After `_presenter.Initialize()` (inside or after its try), add:
```csharp
        _models = new ModelCatalog(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, _secrets, (p, ct) =>
        {
            var exe = AgyLocator.Find(p.CliPath, File.Exists, Environment.GetEnvironmentVariable("LOCALAPPDATA"), Environment.GetEnvironmentVariable("PATH"))
                      ?? throw new ModelListException("The Antigravity CLI (agy) isn't installed.");
            return CliRunner.RunAsync(exe, ["models"], TimeSpan.FromSeconds(30), ct);
        }, TimeProvider.System);
        _providerBar = new ProviderBar(_popup, _settingsService, chat, _backends, _models, _prompts, _presenter.Notice, _log);
        try { _providerBar.Initialize(); } catch (Exception ex) { _log.Error("provider bar failed to initialize", ex); }
        _presenter.BusyChanged += busy => _providerBar.SetEnabled(!busy);
        _settingsService.Changed += () =>
        {
            _log.Verbose = IsDebugBuild || settings.Diagnostics.VerboseLogging;
            _popup.GrowMode = settings.Chat.GrowMode;
            _popup.ApplyAppearance();
            _presenter.ApplyAppearance();
            _providerBar.Refresh();
        };
```
   (`chat` is the existing local `ChatController`.)

- [ ] **Step 7: Build, install, verify**

Run:
```powershell
dotnet build src/Hotline.App/Hotline.App.csproj -c Debug -p:Platform=x64
dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj
powershell -File tests\smoke\smoke.ps1 -Install
```
Expected: `Build succeeded.`, `failed: 0`, `Smoke test passed.`, and the panel left closed. Manual checks (the user, or the executor with permission):
1. The bottom bar shows "Gemini (Antigravity)", "Default", "Default".
2. Opening the model dropdown lists agy's models.
3. Pick `gemini-3.8-flash-low` and effort `low`. The next message's log line `agy started: …` contains `--model gemini-3.8-flash-low --effort low`.
4. While an answer streams, the three pickers are disabled.
5. Prompt menu → New prompt opens a new `.md` in an editor. Selecting it, then asking "what are your instructions?", reflects the edited text.
6. Set `"tools": "inherit"` on the agy connection in settings.json (the settings window arrives in Task 6) and ask "list the files in this folder". agy answers from your user folder.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "feat(app): provider/model/effort pickers and prompt menu in the bottom bar; credential-locker API keys; live appearance" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Settings window (App)

**Files:**
- Create: `src/Hotline.App/Settings/SettingsWindow.xaml`, `src/Hotline.App/Settings/SettingsWindow.xaml.cs`, `src/Hotline.App/Settings/SettingsWindow.Connections.cs`, `src/Hotline.App/Settings/SettingsHost.cs`
- Modify: `src/Hotline.Core/Activation/ActivationPlanner.cs` (+ test), `src/Hotline.App/ActivationRouter.cs`, `src/Hotline.App/Interop/TrayIcon.cs`, `src/Hotline.App/App.xaml.cs`, `README.md`
- Test: `tests/Hotline.Core.Tests/ActivationPlannerTests.cs` (add one test)

**Interfaces:**
- Consumes: `SettingsService`, `SettingsSchema`, `ConnectionTypes`, `ConnectionEditor`, `ISecretStore`, `SecretKeys`, `ModelCatalog`, `PromptLibrary`, `BackendCache.InvalidateAsync`, `CliRunner`.
- Produces:
  - `ActivationPlan(KeyEvent? Key, bool ShowPopup, bool OpenSettings = false)`
  - `ActivationRouter.OpenSettingsRequested`
  - `SettingsHost(Func<SettingsWindow> create, FileLog log)` with `Show()`

- [ ] **Step 1: Failing planner test**

Add to `tests/Hotline.Core.Tests/ActivationPlannerTests.cs`:
```csharp
    [Fact]
    public void Settings_uri_opens_settings_without_popup()
        => Assert.Equal(new ActivationPlan(null, ShowPopup: false, OpenSettings: true),
            ActivationPlanner.Plan(new ActivationRequest(ActivationKind.Protocol, new Uri("hotline://settings"), false)));
```
Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS (`ActivationPlan` has no `OpenSettings` parameter).

- [ ] **Step 2: Planner and router**

In `src/Hotline.Core/Activation/ActivationPlanner.cs`:
- `public sealed record ActivationPlan(KeyEvent? Key, bool ShowPopup, bool OpenSettings = false);`
- In the protocol arm, before the `tray` check, add:
  `: string.Equals(r.Uri?.Host, "settings", StringComparison.OrdinalIgnoreCase) ? new ActivationPlan(null, false, OpenSettings: true)`

In `src/Hotline.App/ActivationRouter.cs`: add `public event Action? OpenSettingsRequested;`, and in `OnActivation` after computing `plan`:
```csharp
        if (plan.OpenSettings) { OpenSettingsRequested?.Invoke(); return; }
```
Run the tests again. Expected: `failed: 0`.

- [ ] **Step 3: Settings window XAML**

`src/Hotline.App/Settings/SettingsWindow.xaml`:
```xml
<Window
    x:Class="Hotline.App.Settings.SettingsWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Hotline settings">
    <Grid x:Name="Root" x:FieldModifier="internal">
        <Grid.Resources>
            <Style x:Key="CardStyle" TargetType="Border">
                <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
                <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
                <Setter Property="BorderThickness" Value="1" />
                <Setter Property="CornerRadius" Value="6" />
                <Setter Property="Padding" Value="16,12" />
            </Style>
        </Grid.Resources>
        <NavigationView x:Name="Nav" PaneDisplayMode="Left" IsBackButtonVisible="Collapsed" IsSettingsVisible="False"
                        IsPaneToggleButtonVisible="False" OpenPaneLength="210" SelectionChanged="Nav_SelectionChanged">
            <NavigationView.MenuItems>
                <NavigationViewItem Content="General" Tag="General"><NavigationViewItem.Icon><FontIcon Glyph="&#xE713;" /></NavigationViewItem.Icon></NavigationViewItem>
                <NavigationViewItem Content="AI connections" Tag="Connections"><NavigationViewItem.Icon><FontIcon Glyph="&#xE703;" /></NavigationViewItem.Icon></NavigationViewItem>
                <NavigationViewItem Content="Prompts" Tag="Prompts"><NavigationViewItem.Icon><FontIcon Glyph="&#xE8A5;" /></NavigationViewItem.Icon></NavigationViewItem>
                <NavigationViewItem Content="Appearance" Tag="Appearance"><NavigationViewItem.Icon><FontIcon Glyph="&#xE790;" /></NavigationViewItem.Icon></NavigationViewItem>
                <NavigationViewItem Content="Window" Tag="Window"><NavigationViewItem.Icon><FontIcon Glyph="&#xE737;" /></NavigationViewItem.Icon></NavigationViewItem>
                <NavigationViewItem Content="Chat and history" Tag="Chat"><NavigationViewItem.Icon><FontIcon Glyph="&#xE8F2;" /></NavigationViewItem.Icon></NavigationViewItem>
                <NavigationViewItem Content="Advanced" Tag="Advanced"><NavigationViewItem.Icon><FontIcon Glyph="&#xE9F5;" /></NavigationViewItem.Icon></NavigationViewItem>
            </NavigationView.MenuItems>
            <ScrollViewer Padding="28,20,28,28">
                <StackPanel x:Name="PageHost" x:FieldModifier="internal" Spacing="8" MaxWidth="860" HorizontalAlignment="Stretch" />
            </ScrollViewer>
        </NavigationView>
    </Grid>
</Window>
```

- [ ] **Step 4: Settings window code**

`src/Hotline.App/Settings/SettingsWindow.xaml.cs`:
```csharp
using Hotline.App.Chat;
using Hotline.App.Interop;
using Hotline.Core.Activation;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Hotline.App.Settings;

/// <summary>Native settings window. Pages are generated from <see cref="SettingsSchema"/>; connections and prompts are custom.</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly SettingsService _settings;
    private readonly ISecretStore _secrets;
    private readonly ModelCatalog _models;
    private readonly Func<string, ValueTask> _invalidate;
    private readonly PromptLibrary _prompts;
    private readonly string _settingsFile;
    private readonly string _logsDir;
    private readonly FileLog _log;

    internal SettingsWindow(SettingsService settings, ISecretStore secrets, ModelCatalog models, Func<string, ValueTask> invalidate,
        PromptLibrary prompts, string settingsFile, string logsDir, FileLog log)
    {
        (_settings, _secrets, _models, _invalidate, _prompts, _settingsFile, _logsDir, _log) =
            (settings, secrets, models, invalidate, prompts, settingsFile, logsDir, log);
        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon("Assets\\Hotline.ico");
        var dpi = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;
        AppWindow.Resize(new SizeInt32((int)(1000 * scale), (int)(740 * scale)));
        ApplyTheme();
        _settings.Changed += OnSettingsChanged;
        Closed += (_, _) => _settings.Changed -= OnSettingsChanged;
        Nav.SelectedItem = Nav.MenuItems[0];
    }

    private void OnSettingsChanged() => DispatcherQueue.TryEnqueue(ApplyTheme);

    private void ApplyTheme() => Root.RequestedTheme = _settings.Current.Window.Theme switch
    {
        ThemeChoice.Light => ElementTheme.Light,
        ThemeChoice.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        => ShowPage(args.SelectedItemContainer?.Tag as string ?? "General");

    private void ShowPage(string tag)
    {
        PageHost.Children.Clear();
        PageHost.Children.Add(new TextBlock
        {
            Text = Nav.MenuItems.OfType<NavigationViewItem>().First(i => (string)i.Tag == tag).Content as string,
            Style = (Style)Application.Current.Resources["TitleTextBlockStyle"], Margin = new Thickness(0, 0, 0, 12),
        });
        switch (tag)
        {
            case "Connections": BuildConnectionsPage(); break;
            case "Prompts": BuildPromptsPage(); break;
            default:
                var page = Enum.Parse<SettingsPage>(tag);
                foreach (var item in SettingsSchema.Items.Where(i => i.Page == page)) PageHost.Children.Add(BuildItem(item));
                if (page == SettingsPage.Advanced) BuildAdvancedExtras();
                break;
        }
    }

    // ---- generic setting cards -------------------------------------------------------------

    private Border Card(string header, string? description, UIElement? control)
    {
        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = header, TextWrapping = TextWrapping.Wrap });
        if (description is not null) text.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
        grid.Children.Add(text);
        if (control is FrameworkElement fe)
        {
            Grid.SetColumn(fe, 1);
            fe.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(fe);
        }
        return new Border { Child = grid, Style = (Style)Root.Resources["CardStyle"] };
    }

    private Border BuildItem(SettingItem item)
    {
        var description = item.RequiresRestart ? $"{item.Description} (Applies after restarting Hotline.)".TrimStart() : item.Description;
        var s = _settings.Current;
        switch (item)
        {
            case ToggleItem t:
            {
                var toggle = new ToggleSwitch { IsOn = t.Get(s), OnContent = "", OffContent = "", MinWidth = 0 };
                toggle.Toggled += (_, _) => _settings.Update(x => t.Set(x, toggle.IsOn));
                return Card(t.Header, description, toggle);
            }
            case NumberItem n:
            {
                var box = new NumberBox
                {
                    Minimum = n.Min, Maximum = n.Max, SmallChange = n.Step, LargeChange = n.Step * 10, Value = n.Get(s), Width = 150,
                    SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact, ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
                };
                box.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) _settings.Update(x => n.Set(x, e.NewValue)); };
                var label = n.Unit is null ? (UIElement)box : new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8,
                    Children = { box, new TextBlock { Text = n.Unit, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 } },
                };
                return Card(n.Header, description, label);
            }
            case ChoiceItem c:
            {
                var combo = new ComboBox { MinWidth = 200 };
                foreach (var o in c.Options) combo.Items.Add(new ComboBoxItem { Content = o.Label, Tag = o.Value });
                combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == c.Get(s));
                combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is ComboBoxItem { Tag: string v }) _settings.Update(x => c.Set(x, v)); };
                return Card(c.Header, description, combo);
            }
            case TextItem tx:
            {
                var box = new TextBox { Text = tx.Get(s) ?? "", PlaceholderText = tx.Placeholder ?? "", Width = 240 };
                box.LostFocus += (_, _) =>
                {
                    if (tx.Header == "Extra hotkey" && !string.IsNullOrWhiteSpace(box.Text) && !Hotkey.TryParse(box.Text, out _))
                    {
                        box.Header = "Not a valid hotkey (example: Ctrl+Alt+H)";
                        return;
                    }
                    box.Header = null;
                    if ((tx.Get(_settings.Current) ?? "") != box.Text.Trim()) _settings.Update(x => tx.Set(x, box.Text));
                };
                return Card(tx.Header, description, box);
            }
            default:
                return Card(item.Header, description, null);
        }
    }

    private void BuildAdvancedExtras()
    {
        Button Link(string text, Action action)
        {
            var b = new Button { Content = text };
            b.Click += (_, _) => action();
            return b;
        }
        var folder = Path.GetDirectoryName(_settingsFile)!;
        PageHost.Children.Add(Card("Settings file", _settingsFile, new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8,
            Children = { Link("Open folder", () => CliRunner.OpenFolder(folder)), Link("Edit settings.json", () => CliRunner.OpenInEditor(_settingsFile)) },
        }));
        PageHost.Children.Add(Card("Logs", _logsDir, Link("Open logs folder", () => CliRunner.OpenFolder(_logsDir))));
    }

    // ---- prompts page ----------------------------------------------------------------------

    private void BuildPromptsPage()
    {
        PageHost.Children.Add(new TextBlock
        {
            Text = "System prompts are Markdown files. The default prompt is used by every connection that doesn't pick its own.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 8),
        });
        var chat = _settings.Current.Chat;
        foreach (var name in _prompts.List())
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var edit = new Button { Content = "Edit" };
            edit.Click += (_, _) => CliRunner.OpenInEditor(_prompts.PathFor(name));
            buttons.Children.Add(edit);
            if (name != chat.DefaultPrompt)
            {
                var makeDefault = new Button { Content = "Make default" };
                makeDefault.Click += (_, _) => { _settings.Update(s => s.Chat.DefaultPrompt = name); ShowPage("Prompts"); };
                buttons.Children.Add(makeDefault);
            }
            if (name != PromptLibrary.DefaultName)
            {
                var delete = new Button { Content = "Delete" };
                delete.Click += (_, _) =>
                {
                    _prompts.Delete(name);
                    if (chat.DefaultPrompt == name) _settings.Update(s => s.Chat.DefaultPrompt = PromptLibrary.DefaultName);
                    ShowPage("Prompts");
                };
                buttons.Children.Add(delete);
            }
            PageHost.Children.Add(Card(name, name == chat.DefaultPrompt ? "Default prompt" : null, buttons));
        }
        var create = new Button { Content = "New prompt", Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        create.Click += (_, _) => { CliRunner.OpenInEditor(_prompts.PathFor(_prompts.Create())); ShowPage("Prompts"); };
        var open = new Button { Content = "Open prompts folder" };
        open.Click += (_, _) => CliRunner.OpenFolder(_prompts.Directory);
        PageHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { create, open } });
    }
}
```

`src/Hotline.App/Settings/SettingsWindow.Connections.cs`:
```csharp
using Hotline.App.Chat;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace Hotline.App.Settings;

public sealed partial class SettingsWindow
{
    private string? _selectedConnection;

    private void BuildConnectionsPage()
    {
        var chat = _settings.Current.Chat;
        _selectedConnection ??= chat.DefaultBackend;
        if (chat.Backends.All(b => b.Id != _selectedConnection)) _selectedConnection = chat.DefaultBackend;

        var add = new DropDownButton { Content = "Add connection" };
        var addMenu = new MenuFlyout();
        foreach (var type in ConnectionTypes.All)
        {
            var item = new MenuFlyoutItem { Text = type.DisplayName + (BackendFactory.IsAvailable(type.Type) ? "" : "  (chat coming soon)") };
            item.Click += (_, _) => { _settings.Update(s => _selectedConnection = ConnectionEditor.Add(s.Chat, type.Type).Id); ShowPage("Connections"); };
            addMenu.Items.Add(item);
        }
        add.Flyout = addMenu;
        var duplicate = new Button { Content = "Duplicate" };
        duplicate.Click += (_, _) =>
        {
            var source = _selectedConnection!;
            string? copyId = null;
            _settings.Update(s => copyId = ConnectionEditor.Duplicate(s.Chat, source).Id);
            if (_secrets.Get(SecretKeys.ApiKey(source)) is { } key) _secrets.Set(SecretKeys.ApiKey(copyId!), key);
            _selectedConnection = copyId;
            ShowPage("Connections");
        };
        var remove = new Button { Content = "Remove", IsEnabled = chat.Backends.Count > 1 };
        remove.Click += async (_, _) =>
        {
            var id = _selectedConnection!;
            var dialog = new ContentDialog
            {
                XamlRoot = Root.XamlRoot, Title = "Remove this connection?", Content = chat.Backends.First(b => b.Id == id).Name,
                PrimaryButtonText = "Remove", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close,
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
            _settings.Update(s => ConnectionEditor.Remove(s.Chat, id));
            _secrets.Set(SecretKeys.ApiKey(id), null);
            await _invalidate(id);
            _selectedConnection = null;
            ShowPage("Connections");
        };
        var makeDefault = new Button { Content = "Use by default", IsEnabled = _selectedConnection != chat.DefaultBackend };
        makeDefault.Click += (_, _) => { _settings.Update(s => ConnectionEditor.SetDefault(s.Chat, _selectedConnection!)); ShowPage("Connections"); };
        PageHost.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, duplicate, remove, makeDefault } });

        var list = new ListView { SelectionMode = ListViewSelectionMode.Single, Margin = new Thickness(0, 8, 0, 8) };
        foreach (var p in chat.Backends)
        {
            var info = ConnectionTypes.Of(p.Type);
            var subtitle = info.DisplayName + (p.Id == chat.DefaultBackend ? " · default" : "") + (BackendFactory.IsAvailable(p.Type) ? "" : " · chat coming soon");
            list.Items.Add(new ListViewItem
            {
                Tag = p.Id,
                Content = new StackPanel { Spacing = 2, Children = { new TextBlock { Text = p.Name }, new TextBlock { Text = subtitle, FontSize = 12, Opacity = 0.7 } } },
            });
        }
        list.SelectedItem = list.Items.OfType<ListViewItem>().FirstOrDefault(i => (string)i.Tag == _selectedConnection);
        var editorHost = new StackPanel { Spacing = 8 };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is not ListViewItem { Tag: string id }) return;
            _selectedConnection = id;
            BuildEditor(editorHost, chat.Backends.First(b => b.Id == id));
        };
        PageHost.Children.Add(list);
        PageHost.Children.Add(editorHost);
        if (chat.Backends.FirstOrDefault(b => b.Id == _selectedConnection) is { } selected) BuildEditor(editorHost, selected);
    }

    private void BuildEditor(StackPanel host, BackendProfile p)
    {
        host.Children.Clear();
        var info = ConnectionTypes.Of(p.Type);
        host.Children.Add(new TextBlock { Text = info.Description, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) });

        async Task Save(Action<BackendProfile> change)
        {
            _settings.Update(_ => change(p));
            await _invalidate(p.Id);
        }

        TextBox Text(string? value, string placeholder, Action<string?> set)
        {
            var box = new TextBox { Text = value ?? "", PlaceholderText = placeholder, Width = 360 };
            box.LostFocus += async (_, _) =>
            {
                var v = string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
                if (v != (string.IsNullOrWhiteSpace(value) ? null : value)) { value = v; await Save(_ => set(v)); }
            };
            return box;
        }

        host.Children.Add(Card("Name", null, Text(p.Name, info.DefaultName, v => p.Name = v ?? info.DefaultName)));
        if (info.Has(ConnectionField.Endpoint))
            host.Children.Add(Card("Endpoint", "Base URL of the API.", Text(p.Endpoint, info.DefaultEndpoint ?? "", v => p.Endpoint = v)));
        if (info.Has(ConnectionField.ApiKey))
        {
            var saved = _secrets.Get(SecretKeys.ApiKey(p.Id)) is not null;
            var keyBox = new PasswordBox
            {
                Width = 360,
                PlaceholderText = saved ? "Saved; type to replace" : info.ApiKeyOptional ? "Optional" : "Required",
            };
            keyBox.LostFocus += async (_, _) =>
            {
                if (keyBox.Password.Length == 0) return;
                try { _secrets.Set(SecretKeys.ApiKey(p.Id), keyBox.Password.Trim()); }
                catch (Exception) { keyBox.Header = "Couldn't save the key"; return; }
                keyBox.Password = "";
                keyBox.PlaceholderText = "Saved; type to replace";
                await _invalidate(p.Id);
            };
            host.Children.Add(Card("API key", "Stored in Windows Credential Locker, not in settings.json.", keyBox));
        }
        if (info.Has(ConnectionField.CliPath))
            host.Children.Add(Card("Program path", "Leave empty to find it automatically.", Text(p.CliPath, "Auto-detect", v => p.CliPath = v)));
        if (info.Has(ConnectionField.Agent))
            host.Children.Add(Card("agy agent", "Used in Chat-only mode.", Text(p.Agent, "hotline", v => p.Agent = v)));
        if (info.Has(ConnectionField.ExtraArgs))
            host.Children.Add(Card("Extra arguments", "Passed to the program as-is (space separated).", Text(p.ExtraArgs, "", v => p.ExtraArgs = v)));

        if (info.Has(ConnectionField.Tools))
        {
            var mode = new ComboBox { MinWidth = 280 };
            mode.Items.Add(new ComboBoxItem { Content = "Chat only (can read your attachments)", Tag = ToolMode.ChatOnly });
            mode.Items.Add(new ComboBoxItem { Content = "Use the program's own tools and permissions", Tag = ToolMode.Inherit });
            mode.SelectedIndex = p.Tools == ToolMode.Inherit ? 1 : 0;
            var folder = Text(p.WorkingDirectory, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), v => p.WorkingDirectory = v);
            folder.IsEnabled = p.Tools == ToolMode.Inherit;
            var browse = new Button { Content = "Browse…", IsEnabled = folder.IsEnabled };
            browse.Click += async (_, _) =>
            {
                var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
                picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                if (await picker.PickSingleFolderAsync() is { } chosen) { folder.Text = chosen.Path; await Save(x => x.WorkingDirectory = chosen.Path); }
            };
            mode.SelectionChanged += async (_, _) =>
            {
                if (mode.SelectedItem is not ComboBoxItem { Tag: ToolMode m }) return;
                folder.IsEnabled = browse.IsEnabled = m == ToolMode.Inherit;
                await Save(x => x.Tools = m);
            };
            host.Children.Add(Card("Tool use", "Inherit lets the program run its tools (files, commands, web) under its own permission rules.", mode));
            host.Children.Add(Card("Working folder", "Where the program's tools act in Inherit mode.",
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { folder, browse } }));
        }

        var prompt = new ComboBox { MinWidth = 220 };
        prompt.Items.Add(new ComboBoxItem { Content = $"Default ({_settings.Current.Chat.DefaultPrompt})", Tag = "" });
        foreach (var name in _prompts.List()) prompt.Items.Add(new ComboBoxItem { Content = name, Tag = name });
        prompt.SelectedItem = prompt.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (p.Prompt ?? "")) ?? prompt.Items[0];
        prompt.SelectionChanged += async (_, _) =>
        {
            if (prompt.SelectedItem is ComboBoxItem { Tag: string name }) await Save(x => x.Prompt = name.Length == 0 ? null : name);
        };
        host.Children.Add(Card("System prompt", null, prompt));

        var model = new ComboBox { IsEditable = true, MinWidth = 260 };
        model.Items.Add("Default");
        if (!string.IsNullOrWhiteSpace(p.Model)) model.Items.Add(p.Model);
        model.SelectedItem = string.IsNullOrWhiteSpace(p.Model) ? "Default" : p.Model;
        var status = new InfoBar { IsClosable = true };
        async Task LoadModels(bool refresh)
        {
            try
            {
                var list = await _models.GetAsync(p, refresh, CancellationToken.None);
                var current = model.SelectedItem as string ?? "Default";
                model.Items.Clear();
                model.Items.Add("Default");
                foreach (var m in list) model.Items.Add(m.Id);
                if (current != "Default" && !list.Any(m => m.Id == current)) model.Items.Add(current);
                model.SelectedItem = current;
                status.Severity = InfoBarSeverity.Success;
                status.Message = $"Connected: {list.Count} model(s) available.";
            }
            catch (ModelListException ex) { status.Severity = InfoBarSeverity.Error; status.Message = ex.Message; }
            catch (Exception ex) { _log.Error("model list failed", ex); status.Severity = InfoBarSeverity.Error; status.Message = ex.Message; }
            status.IsOpen = true;
        }
        model.DropDownOpened += async (_, _) => await LoadModels(false);
        model.SelectionChanged += async (_, _) =>
        {
            if (model.SelectedItem is string m) await Save(x => x.Model = m == "Default" ? null : m);
        };
        model.TextSubmitted += async (_, e) => await Save(x => x.Model = string.IsNullOrWhiteSpace(e.Text) || e.Text == "Default" ? null : e.Text.Trim());
        host.Children.Add(Card("Default model", null, model));

        if (info.EffortLevels.Count > 0)
        {
            var effort = new ComboBox { MinWidth = 160 };
            effort.Items.Add("Default");
            foreach (var l in info.EffortLevels) effort.Items.Add(l);
            effort.SelectedItem = p.Effort is { } e && info.EffortLevels.Contains(e) ? e : "Default";
            effort.SelectionChanged += async (_, _) =>
            {
                if (effort.SelectedItem is string v) await Save(x => x.Effort = v == "Default" ? null : v);
            };
            host.Children.Add(Card("Default effort", null, effort));
        }

        var test = new Button { Content = "Test connection" };
        test.Click += async (_, _) => await LoadModels(true);
        host.Children.Add(test);
        host.Children.Add(status);
    }
}
```

`src/Hotline.App/Settings/SettingsHost.cs`:
```csharp
using Hotline.Core.Diagnostics;

namespace Hotline.App.Settings;

/// <summary>Opens the settings window (one at a time) and brings it to the front.</summary>
internal sealed class SettingsHost(Func<SettingsWindow> create, FileLog log)
{
    private SettingsWindow? _window;

    public void Show()
    {
        if (_window is null)
        {
            _window = create();
            _window.Closed += (_, _) => _window = null;
            log.Info("settings window opened");
        }
        _window.Activate();
    }
}
```

- [ ] **Step 5: Wire it up**

In `src/Hotline.App/Interop/TrayIcon.cs`, change the menu text `"Edit settings file"` to `"Settings…"`.

In `src/Hotline.App/App.xaml.cs`:
1. Add `using Hotline.App.Settings;` and the field `private SettingsHost? _settingsHost;`.
2. After the provider bar setup, add:
```csharp
        _settingsHost = new SettingsHost(() => new SettingsWindow(_settingsService, _secrets, _models, id => _backends.InvalidateAsync(id),
            _prompts, store.FilePath, Path.Combine(dataDir, "logs"), _log), _log);
        _presenter.SettingsRequested += () => { _popup.HidePopup(); _settingsHost.Show(); };
        _router.OpenSettingsRequested += () => _settingsHost.Show();
```
3. Replace the tray `onOpenSettings` lambda body with `_settingsHost!.Show()`.

- [ ] **Step 6: README**

Add after the Settings table in `README.md`:
```markdown
### Settings window and AI connections

Open it with ⚙ in the panel, tray → **Settings…**, or `hotline://settings`. Changes apply immediately (a few say
"applies after restart"). **AI connections** lets you add, duplicate and remove connections: Antigravity (agy), Claude
Code, Gemini API, Anthropic API, OpenAI-compatible APIs (OpenRouter and others) and local endpoints (llama.cpp, LM Studio).
API keys are stored in Windows Credential Locker. Only Antigravity can chat today; the others can already be configured
and tested. **Tool use**: "Chat only" (default) or "Use the program's own tools" (the CLI's tools and permission rules
in a working folder you choose). **Prompts**: system prompts are Markdown files in `%USERPROFILE%\.hotline\prompts`;
switch them from the 📄 button in the panel or per connection.
```

- [ ] **Step 7: Build, test, verify, commit**

Run:
```powershell
dotnet build src/Hotline.App/Hotline.App.csproj -c Debug -p:Platform=x64
dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj
powershell -File tests\smoke\smoke.ps1 -Install
```
Expected: `Build succeeded.`, `failed: 0`, `Smoke test passed.`

Manual checks (the user, or the executor with permission; close the settings window afterwards):
1. ⚙ opens "Hotline settings" with seven pages.
2. Changing Theme or Text size updates the panel immediately.
3. AI connections → Add connection → Local endpoint creates "Local model". "Test connection" with no server running shows "Can't reach http://127.0.0.1:8080/v1…".
4. Removing a connection asks for confirmation. The last connection can't be removed.
5. Prompts → New prompt opens an editor. "Make default" marks it as default.
6. Advanced → Open folder shows `%USERPROFILE%\.hotline`.

```powershell
git add -A
git commit -m "feat(app): settings window with generated pages, AI connections, prompts and folders; hotline://settings" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
