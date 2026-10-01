# Hotline Plan 1 — Copilot Key Registration + App Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Pressing the Copilot key (or Win+C, or an optional fallback hotkey) toggles a pre-warmed, centered Hotline popup from a tray-resident, single-instance, signed MSIX app that appears in Windows Settings' Copilot key picker.

**Architecture:** A pure `Hotline.Core` library (net10.0, unit-tested) owns all decisions: URI/fast-path parsing, de-duplication, key→action mapping, hotkey parsing, settings, geometry, and logging. A thin `Hotline.App` (WinUI 3, packaged) wires Windows inputs (protocol activation, Copilot fast-path window message, `RegisterHotKey`, tray) into Core and shows or hides the popup. The popup content is a placeholder in this plan; Plan 2 replaces it with the WebView2 chat.

**Tech Stack:** .NET SDK 10.0.401, C#, WinUI 3 (Microsoft.WindowsAppSDK 2.5.1, self-contained), Microsoft.Windows.SDK.BuildTools 10.0.28000.2705, xUnit v3 (xunit.v3 4.0.1, xunit.runner.visualstudio 4.0.0, Microsoft.NET.Test.Sdk 18.10.1), hand-written `DllImport` interop, Windows PowerShell 5.1 scripts.

**Spec:** `docs/superpowers/specs/2026-09-30-hotline-design.md` (this plan = milestones 0–1)

## Global Constraints

- Repo root `PMARC14/hotline`; protocol scheme `hotline`; Settings picker display name `Hotline`.
- License Apache-2.0. No CLA.
- Package identity name `pmarc14.Hotline`; publisher `CN=pmarc14 Hotline Dev` (must equal the signing cert subject exactly).
- Minimum OS 10.0.22621.0 (the Copilot key provider API starts at build 22621); MaxVersionTested 10.0.26200.0.
- Runtime dependencies: **only** Microsoft.WindowsAppSDK (plus the build-only Microsoft.Windows.SDK.BuildTools). No tray library, no CsWin32, no MVVM toolkit.
- No Ollama anywhere. Local models are out of scope for this plan.
- Copilot key URIs: `hotline://key?state=Tap`, `hotline://key?state=Down`, `hotline://key?state=Up`; fast-path `MessageWParam` 0 = Tap, 1 = Down, 2 = Up; fast-path window message `WM_APP + 1` (0x8001).
- The app is single-instance: a second launch redirects its activation to the running instance and exits.
- Settings live in `settings.json` in the package LocalFolder; logs in `logs\hotline.log` there.
- Commits are authored by the global git identity (pmarc14 / 16502495+PMARC14@users.noreply.github.com) and end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **The same key press arriving twice** (fast-path message *and* protocol activation for one physical press) should toggle the popup once, not show then immediately hide it. This is pinned by the `KeyEventDeduper` tests in Task 2.
2. **Clicking the tray icon or pressing the key while the popup is open** causes a focus loss that hides the popup, and the toggle then re-shows it. Expected: the popup closes and stays closed. This is pinned by the `PopupToggleGuard` tests in Task 2.
3. **A hand-edited `settings.json` that is malformed, has an unknown enum value, or null sections** should still let the app start with defaults, keeping the bad file as `settings.json.bad`. Pinned by the `SettingsStore` tests in Task 4.
4. **A fallback hotkey that is invalid ("Alt+", "Banana") or already taken by another app** should be logged and ignored, and the app keeps running. Pinned by the `Hotkey` tests in Task 3 and the registration-failure path in Task 8.
5. **A popup on a secondary or negative-coordinate monitor, at 150% scaling, or larger than the work area** should be centered on the monitor of the window you were in and fit inside it. Pinned by the `PopupGeometry` tests in Task 5.

---

## File Structure

```
hotline/
  .gitignore, .gitattributes, global.json, LICENSE, README.md, Hotline.slnx
  src/Hotline.Core/
    Hotline.Core.csproj
    Activation/KeyEvent.cs          KeyEvent, KeySource, KeyAction enums
    Activation/ActivationParser.cs  URI + fast-path wParam → KeyEvent
    Activation/KeyEventDeduper.cs   drops duplicate events inside a time window
    Activation/KeyActionResolver.cs KeyEvent + settings → KeyAction
    Activation/PopupToggleGuard.cs  "was it just hidden by focus loss?" rule
    Activation/Hotkey.cs            "Ctrl+Alt+H" ⇄ (modifiers, virtual key)
    Settings/HotlineSettings.cs     settings model + defaults
    Settings/SettingsStore.cs       load/save/normalize/backup
    Windowing/PopupGeometry.cs      RectI + centering math
    Diagnostics/FileLog.cs          tiny rotating file logger
  tests/Hotline.Core.Tests/
    Hotline.Core.Tests.csproj, ManualTimeProvider.cs, one *Tests.cs per Core file
  src/Hotline.App/
    Hotline.App.csproj, Package.appxmanifest, app.manifest
    Program.cs                      custom Main: single-instance redirect
    App.xaml, App.xaml.cs           composition root
    ActivationRouter.cs             activations/keys → popup actions
    PopupWindow.xaml(.cs)           centered acrylic popup (placeholder content)
    Interop/Native.cs               all DllImports + structs
    Interop/WindowMessageHook.cs    window subclass dispatching messages
    Interop/CopilotFastPath.cs      property-store registration + WM handler
    Interop/HotkeyRegistration.cs   RegisterHotKey + WM_HOTKEY handler
    Interop/TrayIcon.cs             Shell_NotifyIcon + context menu
    Assets/*.png, Assets/Hotline.ico, Public/README.txt
  scripts/make-assets.ps1, dev-cert.ps1, build-msix.ps1, install.ps1
  docs/superpowers/notes/2026-09-30-copilot-key-spike.md   (Task 9 results)
```

---

### Task 1: Repo scaffold + ActivationParser

**Files:**
- Create: `.gitignore`, `.gitattributes`, `global.json`, `LICENSE`, `README.md`, `Hotline.slnx`
- Create: `src/Hotline.Core/Hotline.Core.csproj`, `src/Hotline.Core/Activation/KeyEvent.cs`, `src/Hotline.Core/Activation/ActivationParser.cs`
- Create: `tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`, `tests/Hotline.Core.Tests/ActivationParserTests.cs`

**Interfaces:**
- Produces: `enum KeyEvent { Tap, HoldStart, HoldStop }`, `enum KeySource { Protocol, FastPath, Hotkey }`, `enum KeyAction { None, TogglePopup, ShowPopup, NewChat, CaptureWindow, RegionSelect }` (namespace `Hotline.Core.Activation`); `static KeyEvent? ActivationParser.ParseUri(Uri? uri)`; `static KeyEvent? ActivationParser.ParseFastPath(nuint wParam)`; `const string ActivationParser.Scheme = "hotline"`.

- [ ] **Step 1: Create root files**

`.gitignore`:
```
bin/
obj/
artifacts/
certs/
AppPackages/
BundleArtifacts/
*.user
.vs/
TestResults/
```

`.gitattributes`:
```
* text=auto eol=lf
*.ps1 text eol=crlf
*.png binary
*.ico binary
```

`global.json`:
```json
{ "sdk": { "version": "10.0.401", "rollForward": "latestFeature" } }
```

`LICENSE`: download the canonical Apache-2.0 text:
```powershell
Invoke-WebRequest https://www.apache.org/licenses/LICENSE-2.0.txt -OutFile LICENSE
```

`README.md`:
```markdown
# Hotline

Replace the Windows Copilot key with a fast popup that talks to the AI you choose:
Claude (via your installed Claude Code), Gemini (API or Antigravity CLI), any
OpenAI-compatible endpoint, or local llama.cpp models.

Status: early development. Licensed under Apache-2.0.
```

- [ ] **Step 2: Create projects and solution**

`src/Hotline.Core/Hotline.Core.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

`tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageReference Include="xunit.v3" Version="4.0.1" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Hotline.Core\Hotline.Core.csproj" />
  </ItemGroup>
</Project>
```

Run:
```powershell
dotnet new sln -n Hotline --format slnx
dotnet sln Hotline.slnx add src/Hotline.Core/Hotline.Core.csproj tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj
```
Expected: `Hotline.slnx` created; two "Project ... added" lines.

- [ ] **Step 3: Write the failing tests**

`tests/Hotline.Core.Tests/ActivationParserTests.cs`:
```csharp
using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class ActivationParserTests
{
    [Theory]
    [InlineData("hotline://key?state=Tap", KeyEvent.Tap)]
    [InlineData("hotline://key?state=Down", KeyEvent.HoldStart)]
    [InlineData("hotline://key?state=Up", KeyEvent.HoldStop)]
    [InlineData("HOTLINE://key?STATE=tap", KeyEvent.Tap)]
    [InlineData("hotline://key?foo=1&state=Up", KeyEvent.HoldStop)]
    public void ParseUri_maps_known_states(string uri, KeyEvent expected)
        => Assert.Equal(expected, ActivationParser.ParseUri(new Uri(uri)));

    [Theory]
    [InlineData("hotline://key")]
    [InlineData("hotline://key?state=Wiggle")]
    [InlineData("hotline://key?state=")]
    [InlineData("https://key?state=Tap")]
    public void ParseUri_returns_null_for_unknown_input(string uri)
        => Assert.Null(ActivationParser.ParseUri(new Uri(uri)));

    [Fact]
    public void ParseUri_returns_null_for_null()
        => Assert.Null(ActivationParser.ParseUri(null));

    [Theory]
    [InlineData(0u, KeyEvent.Tap)]
    [InlineData(1u, KeyEvent.HoldStart)]
    [InlineData(2u, KeyEvent.HoldStop)]
    public void ParseFastPath_maps_manifest_wparams(uint wParam, KeyEvent expected)
        => Assert.Equal(expected, ActivationParser.ParseFastPath(wParam));

    [Fact]
    public void ParseFastPath_returns_null_for_unknown_wparam()
        => Assert.Null(ActivationParser.ParseFastPath(7));
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test Hotline.slnx`
Expected: build FAILS with `error CS0246: The type or namespace name 'KeyEvent' could not be found` (or CS0103 for `ActivationParser`).

- [ ] **Step 5: Write minimal implementation**

`src/Hotline.Core/Activation/KeyEvent.cs`:
```csharp
namespace Hotline.Core.Activation;

/// <summary>A Copilot-key (or hotkey) state change.</summary>
public enum KeyEvent { Tap, HoldStart, HoldStop }

/// <summary>Which channel delivered the key event (for logging and de-duplication diagnostics).</summary>
public enum KeySource { Protocol, FastPath, Hotkey }

/// <summary>What the app should do in response to a key event. Configurable per event in settings.</summary>
public enum KeyAction { None, TogglePopup, ShowPopup, NewChat, CaptureWindow, RegionSelect }
```

`src/Hotline.Core/Activation/ActivationParser.cs`:
```csharp
namespace Hotline.Core.Activation;

public static class ActivationParser
{
    public const string Scheme = "hotline";

    /// <summary>Parses hotline://key?state=Tap|Down|Up (case-insensitive). Returns null if not a key URI.</summary>
    public static KeyEvent? ParseUri(Uri? uri)
    {
        if (uri is null || !string.Equals(uri.Scheme, Scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        return GetQueryValue(uri.Query, "state")?.ToLowerInvariant() switch
        {
            "tap" => KeyEvent.Tap,
            "down" => KeyEvent.HoldStart,
            "up" => KeyEvent.HoldStop,
            _ => null,
        };
    }

    /// <summary>Maps the MessageWParam values declared in Package.appxmanifest (0/1/2).</summary>
    public static KeyEvent? ParseFastPath(nuint wParam) => (ulong)wParam switch
    {
        0UL => KeyEvent.Tap,
        1UL => KeyEvent.HoldStart,
        2UL => KeyEvent.HoldStop,
        _ => null,
    };

    private static string? GetQueryValue(string query, string name)
    {
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && string.Equals(Uri.UnescapeDataString(kv[0]), name, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(kv[1]);
        }
        return null;
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test Hotline.slnx`
Expected: `Passed!  - Failed: 0, Passed: 14` (5 + 4 + 1 + 3 + 1).

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "feat(core): scaffold solution and Copilot key activation parser" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: De-duplication, action resolution, toggle guard

**Files:**
- Create: `src/Hotline.Core/Activation/KeyEventDeduper.cs`, `src/Hotline.Core/Activation/KeyActionResolver.cs`, `src/Hotline.Core/Activation/PopupToggleGuard.cs`, `src/Hotline.Core/Settings/HotlineSettings.cs` (ActivationSettings only in this task; Task 4 adds the rest)
- Test: `tests/Hotline.Core.Tests/ManualTimeProvider.cs`, `tests/Hotline.Core.Tests/KeyEventDeduperTests.cs`, `tests/Hotline.Core.Tests/KeyActionResolverTests.cs`, `tests/Hotline.Core.Tests/PopupToggleGuardTests.cs`

**Interfaces:**
- Consumes: `KeyEvent`, `KeyAction` (Task 1).
- Produces: `sealed class KeyEventDeduper(TimeProvider clock, TimeSpan window)` with `bool ShouldHandle(KeyEvent e)`; `static KeyAction KeyActionResolver.Resolve(KeyEvent e, ActivationSettings s)`; `sealed class PopupToggleGuard(TimeProvider clock, TimeSpan grace)` with `void NoteHidden()` and `bool ShouldShowOnToggle(bool isVisible)`; `sealed class ActivationSettings { KeyAction Tap = TogglePopup; KeyAction Hold = ShowPopup; string? FallbackHotkey = null; }` in namespace `Hotline.Core.Settings`.

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/ManualTimeProvider.cs`:
```csharp
namespace Hotline.Core.Tests;

/// <summary>Deterministic clock for time-window tests.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now += by;
}
```

`tests/Hotline.Core.Tests/KeyEventDeduperTests.cs`:
```csharp
using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class KeyEventDeduperTests
{
    private readonly ManualTimeProvider _clock = new();
    private KeyEventDeduper New() => new(_clock, TimeSpan.FromMilliseconds(150));

    [Fact]
    public void First_event_is_handled()
        => Assert.True(New().ShouldHandle(KeyEvent.Tap));

    [Fact]
    public void Same_event_within_window_is_dropped()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap);
        _clock.Advance(TimeSpan.FromMilliseconds(40));
        Assert.False(d.ShouldHandle(KeyEvent.Tap));
    }

    [Fact]
    public void Same_event_after_window_is_handled()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap);
        _clock.Advance(TimeSpan.FromMilliseconds(151));
        Assert.True(d.ShouldHandle(KeyEvent.Tap));
    }

    [Fact]
    public void Different_event_within_window_is_handled()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.HoldStart);
        _clock.Advance(TimeSpan.FromMilliseconds(10));
        Assert.True(d.ShouldHandle(KeyEvent.HoldStop));
    }

    [Fact]
    public void Dropped_duplicate_does_not_extend_the_window()
    {
        var d = New();
        d.ShouldHandle(KeyEvent.Tap);                       // t=0
        _clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(d.ShouldHandle(KeyEvent.Tap));         // t=100 dropped
        _clock.Advance(TimeSpan.FromMilliseconds(60));
        Assert.True(d.ShouldHandle(KeyEvent.Tap));          // t=160 > 150 from the handled one
    }
}
```

`tests/Hotline.Core.Tests/KeyActionResolverTests.cs`:
```csharp
using Hotline.Core.Activation;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public class KeyActionResolverTests
{
    [Fact]
    public void Defaults_tap_toggles_and_hold_shows()
    {
        var s = new ActivationSettings();
        Assert.Equal(KeyAction.TogglePopup, KeyActionResolver.Resolve(KeyEvent.Tap, s));
        Assert.Equal(KeyAction.ShowPopup, KeyActionResolver.Resolve(KeyEvent.HoldStart, s));
    }

    [Fact]
    public void Hold_release_does_nothing_in_v1()
        => Assert.Equal(KeyAction.None, KeyActionResolver.Resolve(KeyEvent.HoldStop, new ActivationSettings()));

    [Fact]
    public void Custom_mapping_is_respected()
    {
        var s = new ActivationSettings { Tap = KeyAction.NewChat, Hold = KeyAction.CaptureWindow };
        Assert.Equal(KeyAction.NewChat, KeyActionResolver.Resolve(KeyEvent.Tap, s));
        Assert.Equal(KeyAction.CaptureWindow, KeyActionResolver.Resolve(KeyEvent.HoldStart, s));
    }
}
```

`tests/Hotline.Core.Tests/PopupToggleGuardTests.cs`:
```csharp
using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class PopupToggleGuardTests
{
    private readonly ManualTimeProvider _clock = new();
    private PopupToggleGuard New() => new(_clock, TimeSpan.FromMilliseconds(300));

    [Fact]
    public void Hidden_popup_is_shown_on_toggle()
        => Assert.True(New().ShouldShowOnToggle(isVisible: false));

    [Fact]
    public void Visible_popup_is_hidden_on_toggle()
        => Assert.False(New().ShouldShowOnToggle(isVisible: true));

    [Fact]
    public void Popup_hidden_by_focus_loss_just_now_stays_hidden()
    {
        var g = New();
        g.NoteHidden();                                    // click on tray stole focus → popup hid itself
        _clock.Advance(TimeSpan.FromMilliseconds(50));
        Assert.False(g.ShouldShowOnToggle(isVisible: false));
    }

    [Fact]
    public void Popup_hidden_long_ago_is_shown()
    {
        var g = New();
        g.NoteHidden();
        _clock.Advance(TimeSpan.FromMilliseconds(301));
        Assert.True(g.ShouldShowOnToggle(isVisible: false));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Hotline.slnx`
Expected: build FAILS with CS0246 for `KeyEventDeduper`, `ActivationSettings`, `PopupToggleGuard`.

- [ ] **Step 3: Write minimal implementation**

`src/Hotline.Core/Activation/KeyEventDeduper.cs`:
```csharp
namespace Hotline.Core.Activation;

/// <summary>
/// One physical key press can arrive via both the fast-path window message and protocol
/// activation. Drops an event identical to the last handled one within <paramref name="window"/>.
/// </summary>
public sealed class KeyEventDeduper(TimeProvider clock, TimeSpan window)
{
    private KeyEvent? _last;
    private DateTimeOffset _lastAt;

    public bool ShouldHandle(KeyEvent e)
    {
        var now = clock.GetUtcNow();
        if (_last == e && now - _lastAt < window)
            return false;
        _last = e;
        _lastAt = now;
        return true;
    }
}
```

`src/Hotline.Core/Activation/KeyActionResolver.cs`:
```csharp
using Hotline.Core.Settings;

namespace Hotline.Core.Activation;

public static class KeyActionResolver
{
    public static KeyAction Resolve(KeyEvent e, ActivationSettings s) => e switch
    {
        KeyEvent.Tap => s.Tap,
        KeyEvent.HoldStart => s.Hold,
        _ => KeyAction.None, // HoldStop is reserved for push-to-talk (v2)
    };
}
```

`src/Hotline.Core/Activation/PopupToggleGuard.cs`:
```csharp
namespace Hotline.Core.Activation;

/// <summary>
/// Clicking the tray icon (or the shell taking focus on a key press) deactivates the popup,
/// which hides it before the toggle request arrives. Treat "hidden within <paramref name="grace"/>"
/// as "was visible" so the toggle closes it instead of re-opening it.
/// </summary>
public sealed class PopupToggleGuard(TimeProvider clock, TimeSpan grace)
{
    private DateTimeOffset _hiddenAt = DateTimeOffset.MinValue;

    public void NoteHidden() => _hiddenAt = clock.GetUtcNow();

    public bool ShouldShowOnToggle(bool isVisible)
        => !isVisible && clock.GetUtcNow() - _hiddenAt > grace;
}
```

`src/Hotline.Core/Settings/HotlineSettings.cs`:
```csharp
using Hotline.Core.Activation;

namespace Hotline.Core.Settings;

public sealed class ActivationSettings
{
    public KeyAction Tap { get; set; } = KeyAction.TogglePopup;
    public KeyAction Hold { get; set; } = KeyAction.ShowPopup;
    /// <summary>Optional extra hotkey, e.g. "Ctrl+Alt+H". Null or empty = off.</summary>
    public string? FallbackHotkey { get; set; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Hotline.slnx`
Expected: `Passed!  - Failed: 0, Passed: 26`.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): key de-dup, action resolver, popup toggle guard" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Hotkey parsing

**Files:**
- Create: `src/Hotline.Core/Activation/Hotkey.cs`
- Test: `tests/Hotline.Core.Tests/HotkeyTests.cs`

**Interfaces:**
- Produces: `[Flags] enum HotkeyModifiers : uint { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }` (values equal Win32 MOD_*); `readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)` with `static bool TryParse(string? text, out Hotkey hotkey)` and `override string ToString()` producing canonical `Ctrl+Alt+Shift+Win+Key`.

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/HotkeyTests.cs`:
```csharp
using Hotline.Core.Activation;

namespace Hotline.Core.Tests;

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+H", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x48u)]
    [InlineData("alt + space", HotkeyModifiers.Alt, 0x20u)]
    [InlineData("Win+Shift+F23", HotkeyModifiers.Win | HotkeyModifiers.Shift, 0x86u)]
    [InlineData("Control+1", HotkeyModifiers.Control, 0x31u)]
    [InlineData("F13", HotkeyModifiers.None, 0x7Cu)]
    public void Parses_valid_hotkeys(string text, HotkeyModifiers mods, uint vk)
    {
        Assert.True(Hotkey.TryParse(text, out var hk));
        Assert.Equal(new Hotkey(mods, vk), hk);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Alt+")]
    [InlineData("Banana")]
    [InlineData("Ctrl+Alt")]          // no key
    [InlineData("Ctrl+A+B")]          // two keys
    [InlineData("H")]                 // ordinary key without modifier would hijack typing
    [InlineData("F25")]
    public void Rejects_invalid_hotkeys(string? text)
        => Assert.False(Hotkey.TryParse(text, out _));

    [Theory]
    [InlineData("alt+ctrl+h", "Ctrl+Alt+H")]
    [InlineData("shift+win+f23", "Shift+Win+F23")]
    [InlineData("Alt+Space", "Alt+Space")]
    public void ToString_is_canonical_and_round_trips(string input, string expected)
    {
        Assert.True(Hotkey.TryParse(input, out var hk));
        Assert.Equal(expected, hk.ToString());
        Assert.True(Hotkey.TryParse(hk.ToString(), out var again));
        Assert.Equal(hk, again);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Hotline.slnx`
Expected: build FAILS with CS0246 `Hotkey` / `HotkeyModifiers`.

- [ ] **Step 3: Write minimal implementation**

`src/Hotline.Core/Activation/Hotkey.cs`:
```csharp
namespace Hotline.Core.Activation;

/// <summary>Values match Win32 MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN.</summary>
[Flags]
public enum HotkeyModifiers : uint { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private const uint VkF1 = 0x70, VkF13 = 0x7C, VkF24 = 0x87;

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var mods = HotkeyModifiers.None;
        uint? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim().ToLowerInvariant();
            switch (part)
            {
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "ctrl" or "control": mods |= HotkeyModifiers.Control; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win" or "windows": mods |= HotkeyModifiers.Win; continue;
            }
            if (key is not null)
                return false;
            key = KeyFromName(part);
            if (key is null)
                return false;
        }

        if (key is null)
            return false;
        // A bare key (no modifier) would swallow normal typing; only F13–F24 are allowed alone.
        if (mods == HotkeyModifiers.None && key is < VkF13 or > VkF24)
            return false;

        hotkey = new Hotkey(mods, key.Value);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(NameFromKey(VirtualKey));
        return string.Join('+', parts);
    }

    private static uint? KeyFromName(string n)
    {
        if (n.Length == 1 && n[0] is >= 'a' and <= 'z') return char.ToUpperInvariant(n[0]);
        if (n.Length == 1 && n[0] is >= '0' and <= '9') return n[0];
        if (n.Length > 1 && n[0] == 'f' && int.TryParse(n.AsSpan(1), out var f) && f is >= 1 and <= 24)
            return VkF1 + (uint)(f - 1);
        return n switch
        {
            "space" => 0x20u,
            "enter" => 0x0Du,
            "tab" => 0x09u,
            "esc" or "escape" => 0x1Bu,
            _ => null,
        };
    }

    private static string NameFromKey(uint vk) => vk switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)vk).ToString(),
        >= VkF1 and <= VkF24 => $"F{vk - VkF1 + 1}",
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Esc",
        _ => $"0x{vk:X2}",
    };
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Hotline.slnx`
Expected: `Passed!  - Failed: 0, Passed: 43`.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): fallback hotkey parsing" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Settings model + SettingsStore

**Files:**
- Modify: `src/Hotline.Core/Settings/HotlineSettings.cs` (add root + window settings)
- Create: `src/Hotline.Core/Settings/SettingsStore.cs`
- Test: `tests/Hotline.Core.Tests/SettingsStoreTests.cs`

**Interfaces:**
- Consumes: `ActivationSettings` (Task 2), `KeyAction` (Task 1).
- Produces: `sealed class HotlineSettings { const int CurrentSchemaVersion = 1; int SchemaVersion; ActivationSettings Activation; WindowSettings Window; }`; `enum PopupLayout { QuickView, CommandBar, SidePanel }`; `enum ThemeChoice { System, Light, Dark }`; `sealed class WindowSettings { PopupLayout Layout = QuickView; int Width = 640; int Height = 520; bool HideOnBlur = true; bool AlwaysOnTop = true; ThemeChoice Theme = System; }`; `sealed class SettingsStore(string directory)` with `string FilePath`, `HotlineSettings Load()`, `void Save(HotlineSettings s)`.

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/SettingsStoreTests.cs`:
```csharp
using Hotline.Core.Activation;
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private SettingsStore New() => new(_dir);
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Missing_file_returns_defaults_and_writes_file()
    {
        var store = New();
        var s = store.Load();
        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
        Assert.Equal(640, s.Window.Width);
        Assert.True(File.Exists(store.FilePath));
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = New();
        var s = store.Load();
        s.Activation.Hold = KeyAction.RegionSelect;
        s.Activation.FallbackHotkey = "Ctrl+Alt+H";
        s.Window.Layout = PopupLayout.SidePanel;
        store.Save(s);

        var loaded = New().Load();
        Assert.Equal(KeyAction.RegionSelect, loaded.Activation.Hold);
        Assert.Equal("Ctrl+Alt+H", loaded.Activation.FallbackHotkey);
        Assert.Equal(PopupLayout.SidePanel, loaded.Window.Layout);
    }

    [Fact]
    public void File_uses_readable_enum_names()
    {
        var store = New();
        store.Load();
        Assert.Contains("\"togglePopup\"", File.ReadAllText(store.FilePath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Missing_properties_keep_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), """{ "window": { "width": 800 } }""");
        var s = New().Load();
        Assert.Equal(800, s.Window.Width);
        Assert.Equal(520, s.Window.Height);
        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
    }

    [Fact]
    public void Null_sections_are_replaced_with_defaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), """{ "activation": null, "window": null }""");
        var s = New().Load();
        Assert.NotNull(s.Activation);
        Assert.NotNull(s.Window);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "activation": { "tap": "Teleport" } }""")]
    [InlineData("null")]
    public void Corrupt_file_is_backed_up_and_defaults_used(string content)
    {
        Directory.CreateDirectory(_dir);
        var path = Path.Combine(_dir, SettingsStore.FileName);
        File.WriteAllText(path, content);

        var s = New().Load();

        Assert.Equal(KeyAction.TogglePopup, s.Activation.Tap);
        Assert.Equal(content, File.ReadAllText(path + ".bad"));
    }

    [Theory]
    [InlineData(10, 320)]
    [InlineData(99999, 4000)]
    public void Window_size_is_clamped(int width, int expected)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), $$"""{ "window": { "width": {{width}} } }""");
        Assert.Equal(expected, New().Load().Window.Width);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Hotline.slnx`
Expected: build FAILS with CS0246 `SettingsStore`, `PopupLayout`.

- [ ] **Step 3: Write minimal implementation**

Replace `src/Hotline.Core/Settings/HotlineSettings.cs` with:
```csharp
using Hotline.Core.Activation;

namespace Hotline.Core.Settings;

public sealed class HotlineSettings
{
    public const int CurrentSchemaVersion = 1;
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public ActivationSettings Activation { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
}

public sealed class ActivationSettings
{
    public KeyAction Tap { get; set; } = KeyAction.TogglePopup;
    public KeyAction Hold { get; set; } = KeyAction.ShowPopup;
    /// <summary>Optional extra hotkey, e.g. "Ctrl+Alt+H". Null or empty = off.</summary>
    public string? FallbackHotkey { get; set; }
}

public enum PopupLayout { QuickView, CommandBar, SidePanel }

public enum ThemeChoice { System, Light, Dark }

public sealed class WindowSettings
{
    public PopupLayout Layout { get; set; } = PopupLayout.QuickView;
    /// <summary>Size in device-independent pixels (scaled by monitor DPI).</summary>
    public int Width { get; set; } = 640;
    public int Height { get; set; } = 520;
    public bool HideOnBlur { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = true;
    public ThemeChoice Theme { get; set; } = ThemeChoice.System;
}
```

`src/Hotline.Core/Settings/SettingsStore.cs`:
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hotline.Core.Settings;

/// <summary>Loads/saves settings.json. Never throws on bad content: backs it up to .bad and uses defaults.</summary>
public sealed class SettingsStore(string directory)
{
    public const string FileName = "settings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string FilePath => Path.Combine(directory, FileName);

    public HotlineSettings Load()
    {
        if (!File.Exists(FilePath))
            return SaveDefaults();

        try
        {
            var s = JsonSerializer.Deserialize<HotlineSettings>(File.ReadAllText(FilePath), Options)
                    ?? throw new JsonException("settings.json contained null");
            return Normalize(s);
        }
        catch (JsonException)
        {
            File.Copy(FilePath, FilePath + ".bad", overwrite: true);
            return SaveDefaults();
        }
    }

    public void Save(HotlineSettings s)
    {
        Directory.CreateDirectory(directory);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Options));
        File.Move(tmp, FilePath, overwrite: true);
    }

    private HotlineSettings SaveDefaults()
    {
        var d = new HotlineSettings();
        Save(d);
        return d;
    }

    private static HotlineSettings Normalize(HotlineSettings s)
    {
        s.Activation ??= new ActivationSettings();
        s.Window ??= new WindowSettings();
        s.Window.Width = Math.Clamp(s.Window.Width, 320, 4000);
        s.Window.Height = Math.Clamp(s.Window.Height, 200, 4000);
        s.SchemaVersion = HotlineSettings.CurrentSchemaVersion;
        return s;
    }
}
```

Note: `??=` on a non-nullable property can raise nullable warning CS8600-family under `TreatWarningsAsErrors`; if the build fails on that line, wrap the two assignments in `#pragma warning disable CS8601`/`restore` and keep the logic.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Hotline.slnx`
Expected: `Passed!  - Failed: 0, Passed: 53`.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): settings model and resilient settings store" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: PopupGeometry + FileLog

**Files:**
- Create: `src/Hotline.Core/Windowing/PopupGeometry.cs`, `src/Hotline.Core/Diagnostics/FileLog.cs`
- Test: `tests/Hotline.Core.Tests/PopupGeometryTests.cs`, `tests/Hotline.Core.Tests/FileLogTests.cs`

**Interfaces:**
- Produces: `readonly record struct RectI(int X, int Y, int Width, int Height)`; `static RectI PopupGeometry.CenterIn(RectI workArea, int width, int height, double scale)` (namespace `Hotline.Core.Windowing`); `sealed class FileLog(string path, long maxBytes = 1_000_000)` with `void Info(string message)` and `void Error(string message, Exception? ex = null)` (namespace `Hotline.Core.Diagnostics`).

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/PopupGeometryTests.cs`:
```csharp
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class PopupGeometryTests
{
    [Fact]
    public void Centers_in_primary_work_area()
        => Assert.Equal(new RectI(640, 260, 640, 520),
            PopupGeometry.CenterIn(new RectI(0, 0, 1920, 1040), 640, 520, 1.0));

    [Fact]
    public void Applies_dpi_scale()
        => Assert.Equal(new RectI(1440, 330, 960, 780),
            PopupGeometry.CenterIn(new RectI(0, 0, 3840, 1440), 640, 520, 1.5));

    [Fact]
    public void Handles_monitor_left_of_primary_with_negative_coordinates()
        => Assert.Equal(new RectI(-1280, 280, 640, 520),
            PopupGeometry.CenterIn(new RectI(-1920, 0, 1920, 1080), 640, 520, 1.0));

    [Fact]
    public void Clamps_to_work_area_when_too_big()
        => Assert.Equal(new RectI(100, 50, 800, 600),
            PopupGeometry.CenterIn(new RectI(100, 50, 800, 600), 2000, 2000, 1.0));
}
```

`tests/Hotline.Core.Tests/FileLogTests.cs`:
```csharp
using Hotline.Core.Diagnostics;

namespace Hotline.Core.Tests;

public sealed class FileLogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Writes_level_and_message_creating_directories()
    {
        var path = Path.Combine(_dir, "logs", "hotline.log");
        new FileLog(path).Info("key Tap via FastPath");
        Assert.Contains("INFO key Tap via FastPath", File.ReadAllText(path));
    }

    [Fact]
    public void Error_includes_exception()
    {
        var path = Path.Combine(_dir, "hotline.log");
        new FileLog(path).Error("boom", new InvalidOperationException("bad state"));
        var text = File.ReadAllText(path);
        Assert.Contains("ERROR boom", text);
        Assert.Contains("bad state", text);
    }

    [Fact]
    public void Rotates_when_over_max_size()
    {
        var path = Path.Combine(_dir, "hotline.log");
        var log = new FileLog(path, maxBytes: 200);
        for (var i = 0; i < 20; i++) log.Info($"line {i} padding padding padding");
        Assert.True(File.Exists(path + ".1"));
        Assert.True(new FileInfo(path).Length < 400);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Hotline.slnx`
Expected: build FAILS with CS0246 `RectI`, `FileLog`.

- [ ] **Step 3: Write minimal implementation**

`src/Hotline.Core/Windowing/PopupGeometry.cs`:
```csharp
namespace Hotline.Core.Windowing;

/// <summary>Rectangle in physical pixels (virtual-screen coordinates; may be negative).</summary>
public readonly record struct RectI(int X, int Y, int Width, int Height);

public static class PopupGeometry
{
    /// <summary>Centers a DIP-sized popup in a monitor work area, scaled by that monitor's DPI and clamped to fit.</summary>
    public static RectI CenterIn(RectI workArea, int width, int height, double scale)
    {
        var w = Math.Min((int)Math.Round(width * scale), workArea.Width);
        var h = Math.Min((int)Math.Round(height * scale), workArea.Height);
        return new RectI(workArea.X + (workArea.Width - w) / 2, workArea.Y + (workArea.Height - h) / 2, w, h);
    }
}
```

`src/Hotline.Core/Diagnostics/FileLog.cs`:
```csharp
namespace Hotline.Core.Diagnostics;

/// <summary>Minimal thread-safe append logger with one-file rotation. Never throws.</summary>
public sealed class FileLog(string path, long maxBytes = 1_000_000)
{
    private readonly Lock _gate = new();

    public void Info(string message) => Write("INFO", message);

    public void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}: {ex}");

    private void Write(string level, string message)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > maxBytes)
                    File.Move(path, path + ".1", overwrite: true);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging must never take the app down.
            }
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Hotline.slnx`
Expected: `Passed!  - Failed: 0, Passed: 60`.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m "feat(core): popup centering geometry and file logger" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: WinUI app shell (popup, single instance, router), which builds

**Files:**
- Create: `scripts/make-assets.ps1`, `src/Hotline.App/Assets/*` (generated), `src/Hotline.App/Public/README.txt`
- Create: `src/Hotline.App/Hotline.App.csproj`, `src/Hotline.App/app.manifest`, `src/Hotline.App/Package.appxmanifest`
- Create: `src/Hotline.App/Program.cs`, `src/Hotline.App/App.xaml`, `src/Hotline.App/App.xaml.cs`, `src/Hotline.App/ActivationRouter.cs`, `src/Hotline.App/PopupWindow.xaml`, `src/Hotline.App/PopupWindow.xaml.cs`, `src/Hotline.App/Interop/Native.cs`

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces: `PopupWindow(WindowSettings settings, PopupToggleGuard guard)` with `nint Hwnd`, `nint PreviousForeground`, `void ShowPopup()`, `void HidePopup()`, `void Toggle()`, `void SetStatus(string text)`, `void ResetConversation()`; `ActivationRouter(PopupWindow popup, ActivationSettings settings, KeyEventDeduper deduper, FileLog log)` with `void OnActivation(AppActivationArguments args, bool isFirstLaunch)`, `void OnKey(KeyEvent e, KeySource source)`, `void TogglePopup()`; static class `Native` (members listed in Step 4).

This task's deliverable is verified by a successful build plus a manual unpackaged smoke run. Packaging comes in Task 7.

- [ ] **Step 1: Generate assets**

`scripts/make-assets.ps1`:
```powershell
# Generates placeholder logo PNGs and a PNG-embedded .ico for the tray. Run with Windows PowerShell 5.1.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'src\Hotline.App\Assets'
New-Item -ItemType Directory -Force $out | Out-Null

function New-Logo([int]$size, [string]$path) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.TextRenderingHint = 'AntiAliasGridFit'
    $g.Clear([System.Drawing.Color]::Transparent)
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 91, 75, 245))
    $r = [int]($size * 0.2)
    $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
    $gp.AddArc(0, 0, $r, $r, 180, 90); $gp.AddArc($size - $r - 1, 0, $r, $r, 270, 90)
    $gp.AddArc($size - $r - 1, $size - $r - 1, $r, $r, 0, 90); $gp.AddArc(0, $size - $r - 1, $r, $r, 90, 90)
    $gp.CloseFigure(); $g.FillPath($brush, $gp)
    $font = New-Object System.Drawing.Font 'Segoe UI', ([float]($size * 0.55)), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat; $fmt.Alignment = 'Center'; $fmt.LineAlignment = 'Center'
    $g.DrawString('H', $font, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF 0, 0, $size, $size), $fmt)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
}

New-Logo 44  (Join-Path $out 'Square44x44Logo.png')
New-Logo 150 (Join-Path $out 'Square150x150Logo.png')
New-Logo 50  (Join-Path $out 'StoreLogo.png')
New-Logo 32  (Join-Path $out 'tray32.png')

# ICO container holding one 32x32 PNG (supported by LoadImage on Vista+).
$png = [IO.File]::ReadAllBytes((Join-Path $out 'tray32.png'))
$ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
$bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1)            # ICONDIR
$bw.Write([byte]32); $bw.Write([byte]32); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$png.Length); $bw.Write([UInt32]22)
$bw.Write($png); $bw.Flush()
[IO.File]::WriteAllBytes((Join-Path $out 'Hotline.ico'), $ms.ToArray())
Remove-Item (Join-Path $out 'tray32.png')
Write-Host "Assets written to $out"
```

Run: `powershell -NoProfile -File scripts/make-assets.ps1`
Expected: `Assets written to ...\src\Hotline.App\Assets`, and the folder contains `Square44x44Logo.png`, `Square150x150Logo.png`, `StoreLogo.png`, `Hotline.ico`.

`src/Hotline.App/Public/README.txt`:
```
Hotline public folder (required by the Copilot key provider extension's PublicFolder attribute).
```

- [ ] **Step 2: Project, app manifest, package manifest**

`src/Hotline.App/Hotline.App.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.22621.0</TargetPlatformMinVersion>
    <RootNamespace>Hotline.App</RootNamespace>
    <AssemblyName>Hotline</AssemblyName>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <ApplicationIcon>Assets\Hotline.ico</ApplicationIcon>
    <Platforms>x64</Platforms>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <UseWinUI>true</UseWinUI>
    <EnableMsixTooling>true</EnableMsixTooling>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <SelfContained>true</SelfContained>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <DefineConstants>$(DefineConstants);DISABLE_XAML_GENERATED_MAIN</DefineConstants>
    <AppxBundle>Never</AppxBundle>
    <GenerateAppInstallerFile>false</GenerateAppInstallerFile>
    <AppxPackageSigningTimestampDigestAlgorithm>SHA256</AppxPackageSigningTimestampDigestAlgorithm>
  </PropertyGroup>
  <ItemGroup>
    <Content Include="Assets\**" />
    <Content Include="Public\**" />
    <Manifest Include="$(ApplicationManifest)" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="2.5.1" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.28000.2705" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Hotline.Core\Hotline.Core.csproj" />
  </ItemGroup>
</Project>
```

`src/Hotline.App/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Hotline.app"/>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

`src/Hotline.App/Package.appxmanifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3"
  xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap uap3 desktop rescap">

  <Identity Name="pmarc14.Hotline" Publisher="CN=pmarc14 Hotline Dev" Version="0.1.0.0" />

  <Properties>
    <DisplayName>Hotline</DisplayName>
    <PublisherDisplayName>pmarc14</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.22621.0" MaxVersionTested="10.0.26200.0" />
  </Dependencies>

  <Resources>
    <Resource Language="x-generate" />
  </Resources>

  <Applications>
    <Application Id="App" Executable="$targetnametoken$.exe" EntryPoint="$targetentrypoint$">
      <uap:VisualElements DisplayName="Hotline" Description="Your AI on the Copilot key"
        BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png" />
      <Extensions>
        <uap:Extension Category="windows.protocol">
          <uap:Protocol Name="hotline">
            <uap:DisplayName>Hotline</uap:DisplayName>
          </uap:Protocol>
        </uap:Extension>
        <uap3:Extension Category="windows.appExtension">
          <uap3:AppExtension Name="com.microsoft.windows.copilotkeyprovider" Id="Hotline"
            DisplayName="Hotline" Description="Open the Hotline AI popup" PublicFolder="Public">
            <uap3:Properties>
              <SingleTap MessageWParam="0">hotline://key?state=Tap</SingleTap>
              <PressAndHoldStart MessageWParam="1">hotline://key?state=Down</PressAndHoldStart>
              <PressAndHoldStop MessageWParam="2">hotline://key?state=Up</PressAndHoldStop>
            </uap3:Properties>
          </uap3:AppExtension>
        </uap3:Extension>
        <desktop:Extension Category="windows.startupTask">
          <desktop:StartupTask TaskId="HotlineStartup" Enabled="true" DisplayName="Hotline" />
        </desktop:Extension>
      </Extensions>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
  </Capabilities>
</Package>
```

- [ ] **Step 3: Single-instance entry point**

`src/Hotline.App/Program.cs`:
```csharp
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Hotline.App;

public static class Program
{
    private const string InstanceKey = "hotline-main";

    [STAThread]
    private static int Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        var main = AppInstance.FindOrRegisterForKey(InstanceKey);
        if (!main.IsCurrent)
        {
            // Second launch (e.g. hotline://key?state=Tap while running): hand off and exit.
            // Run on a worker thread so the STA isn't blocked inside the COM call.
            Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait();
            return 0;
        }

        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App(activation);
        });
        return 0;
    }
}
```

- [ ] **Step 4: Native interop (complete for this plan)**

`src/Hotline.App/Interop/Native.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Hotline.App.Interop;

/// <summary>All Win32 interop used by Hotline, hand-written to avoid generator dependencies.</summary>
internal static class Native
{
    public const uint WM_COMMAND = 0x0111, WM_HOTKEY = 0x0312, WM_NULL = 0x0000;
    public const uint WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205, WM_CONTEXTMENU = 0x007B;
    public const uint WM_APP = 0x8000;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int MDT_EFFECTIVE_DPI = 0;
    public const int NIM_ADD = 0, NIM_DELETE = 2, NIM_SETVERSION = 4;
    public const int NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;
    public const int NOTIFYICON_VERSION_4 = 4;
    public const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;
    public const uint TPM_RIGHTBUTTON = 0x2, TPM_BOTTOMALIGN = 0x20;
    public const uint IMAGE_ICON = 1, LR_LOADFROMFILE = 0x10;
    public const ushort VT_UINT = 23;

    public delegate nint SubclassProc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData);

    [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(nint hWnd, SubclassProc proc, nuint id, nuint refData);
    [DllImport("comctl32.dll")] public static extern nint DefSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hWnd, uint msg, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hWnd, uint flags);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint hMonitor, int type, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool AppendMenu(nint hMenu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] public static extern bool TrackPopupMenuEx(nint hMenu, uint flags, int x, int y, nint hWnd, nint tpm);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(nint hMenu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint LoadImage(nint hInst, string name, uint type, int cx, int cy, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);
    [DllImport("shell32.dll")] public static extern int SHGetPropertyStoreForWindow(nint hWnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATA
    {
        public int cbSize;
        public nint hWnd;
        public int uID;
        public int uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY { public Guid fmtid; public uint pid; }

    /// <summary>Only the VT_UINT case is used; size matches x64 PROPVARIANT (24 bytes).</summary>
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    public struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint uintVal;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }
}
```

- [ ] **Step 5: Popup window**

`src/Hotline.App/PopupWindow.xaml`:
```xml
<Window
    x:Class="Hotline.App.PopupWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Hotline">
    <Grid x:Name="Root" Padding="20" RowSpacing="12" KeyDown="Root_KeyDown">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>
        <TextBlock Text="Hotline" Style="{StaticResource SubtitleTextBlockStyle}" />
        <TextBlock x:Name="StatusText" Grid.Row="1" Opacity="0.7" TextWrapping="Wrap" />
        <TextBox x:Name="PromptBox" Grid.Row="2" PlaceholderText="Ask anything" />
    </Grid>
</Window>
```

`src/Hotline.App/PopupWindow.xaml.cs`:
```csharp
using Hotline.App.Interop;
using Hotline.Core.Activation;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;

namespace Hotline.App;

/// <summary>Centered acrylic popup. Created hidden at startup so showing it is instant.</summary>
public sealed partial class PopupWindow : Window
{
    private readonly WindowSettings _settings;
    private readonly PopupToggleGuard _guard;

    public nint Hwnd { get; }
    /// <summary>The window the user was in before the popup appeared (target for monitor choice and, later, capture).</summary>
    public nint PreviousForeground { get; private set; }

    public PopupWindow(WindowSettings settings, PopupToggleGuard guard)
    {
        _settings = settings;
        _guard = guard;
        InitializeComponent();
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        SystemBackdrop = new DesktopAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = settings.AlwaysOnTop;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon("Assets\\Hotline.ico");
        AppWindow.Closing += (_, e) => { e.Cancel = true; HidePopup(); };

        Root.RequestedTheme = settings.Theme switch
        {
            ThemeChoice.Light => ElementTheme.Light,
            ThemeChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        Activated += OnActivated;
    }

    public bool IsShown => AppWindow.IsVisible;

    public void ShowPopup()
    {
        var fg = Native.GetForegroundWindow();
        if (fg != Hwnd && fg != 0)
            PreviousForeground = fg;

        PlaceOnActiveMonitor();
        Activate();
        Native.SetForegroundWindow(Hwnd);
        PromptBox.Focus(FocusState.Programmatic);
    }

    public void HidePopup()
    {
        if (!AppWindow.IsVisible) return;
        AppWindow.Hide();
        _guard.NoteHidden();
    }

    public void Toggle()
    {
        if (_guard.ShouldShowOnToggle(AppWindow.IsVisible)) ShowPopup();
        else HidePopup();
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public void ResetConversation() => PromptBox.Text = string.Empty;

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated && _settings.HideOnBlur)
            HidePopup();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            HidePopup();
            e.Handled = true;
        }
    }

    private void PlaceOnActiveMonitor()
    {
        var anchor = PreviousForeground != 0 ? PreviousForeground : Hwnd;
        var area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(anchor), DisplayAreaFallback.Primary);
        var wa = area.WorkArea;

        var monitor = Native.MonitorFromWindow(anchor, Native.MONITOR_DEFAULTTONEAREST);
        var scale = Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        var r = PopupGeometry.CenterIn(new RectI(wa.X, wa.Y, wa.Width, wa.Height), _settings.Width, _settings.Height, scale);
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
    }
}
```

- [ ] **Step 6: Router and App**

`src/Hotline.App/ActivationRouter.cs`:
```csharp
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace Hotline.App;

/// <summary>Turns activations and key events into popup actions. All calls must be on the UI thread.</summary>
public sealed class ActivationRouter(PopupWindow popup, ActivationSettings settings, KeyEventDeduper deduper, FileLog log)
{
    public void OnActivation(AppActivationArguments args, bool isFirstLaunch)
    {
        log.Info($"activation kind={args.Kind} first={isFirstLaunch}");
        switch (args.Kind)
        {
            case ExtendedActivationKind.Protocol:
                var uri = ((IProtocolActivatedEventArgs)args.Data).Uri;
                if (ActivationParser.ParseUri(uri) is { } e) OnKey(e, KeySource.Protocol);
                else popup.ShowPopup();
                break;
            case ExtendedActivationKind.StartupTask when isFirstLaunch:
                break; // signed in: stay quietly in the tray
            default:
                popup.ShowPopup();
                break;
        }
    }

    public void OnKey(KeyEvent e, KeySource source)
    {
        if (!deduper.ShouldHandle(e))
        {
            log.Info($"key {e} via {source} (duplicate, ignored)");
            return;
        }
        var action = KeyActionResolver.Resolve(e, settings);
        log.Info($"key {e} via {source} -> {action}");
        popup.SetStatus($"Last key: {e} via {source} → {action}");
        Execute(action);
    }

    public void TogglePopup() => popup.Toggle();

    private void Execute(KeyAction action)
    {
        switch (action)
        {
            case KeyAction.None:
                break;
            case KeyAction.TogglePopup:
                popup.Toggle();
                break;
            case KeyAction.NewChat:
                popup.ResetConversation();
                popup.ShowPopup();
                break;
            default:
                // ShowPopup, plus CaptureWindow/RegionSelect which arrive in Plan 4 (context); until then they just open the popup.
                popup.ShowPopup();
                break;
        }
    }
}
```

`src/Hotline.App/App.xaml`:
```xml
<Application
    x:Class="Hotline.App.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

`src/Hotline.App/App.xaml.cs` (Task 8 adds the hook, fast path, hotkey and tray lines):
```csharp
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Hotline.App;

public partial class App : Application
{
    private readonly AppActivationArguments _initialActivation;
    private FileLog? _log;
    private PopupWindow? _popup;
    private ActivationRouter? _router;

    public App(AppActivationArguments initialActivation)
    {
        _initialActivation = initialActivation;
        InitializeComponent();
        UnhandledException += (_, e) => _log?.Error("unhandled exception", e.Exception);
    }

    internal static string DataDirectory
    {
        get
        {
            try { return Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch (Exception) // no package identity: running unpackaged (dev smoke run)
            { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hotline"); }
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dataDir = DataDirectory;
        _log = new FileLog(Path.Combine(dataDir, "logs", "hotline.log"));
        var store = new SettingsStore(dataDir);
        var settings = store.Load();
        _log.Info($"starting; settings at {store.FilePath}");

        _popup = new PopupWindow(settings.Window, new PopupToggleGuard(TimeProvider.System, TimeSpan.FromMilliseconds(300)));
        _router = new ActivationRouter(_popup, settings.Activation,
            new KeyEventDeduper(TimeProvider.System, TimeSpan.FromMilliseconds(150)), _log);

        AppInstance.GetCurrent().Activated += (_, a) =>
            _popup.DispatcherQueue.TryEnqueue(() => _router.OnActivation(a, isFirstLaunch: false));

        _router.OnActivation(_initialActivation, isFirstLaunch: true);
    }
}
```

- [ ] **Step 7: Add to solution and build**

Run:
```powershell
dotnet sln Hotline.slnx add src/Hotline.App/Hotline.App.csproj
dotnet build src/Hotline.App/Hotline.App.csproj -c Debug -p:Platform=x64
```
Expected: `Build succeeded.` with 0 errors. If the build reports XAML compiler errors mentioning missing Windows SDK tools, confirm `Microsoft.Windows.SDK.BuildTools` restored (`dotnet restore`) and rebuild. The SDK BuildTools package supplies makepri/makeappx, so no Visual Studio install is needed.

- [ ] **Step 8: Unpackaged smoke run**

Run:
```powershell
dotnet run --project src/Hotline.App/Hotline.App.csproj -p:Platform=x64 -p:WindowsPackageType=None
```
Expected: a centered acrylic popup titled "Hotline" with an "Ask anything" box appears. Esc hides it. `%LOCALAPPDATA%\Hotline\logs\hotline.log` contains `activation kind=Launch first=True`. Close it with `Stop-Process -Name Hotline`; there is no tray icon to quit from until Task 8.

- [ ] **Step 9: Run Core tests and commit**

Run: `dotnet test tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `Passed!  - Failed: 0, Passed: 60`.

```powershell
git add -A
git commit -m "feat(app): WinUI shell with centered popup, single instance and activation router" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Signing, MSIX build, install; verify protocol activation

**Files:**
- Create: `scripts/dev-cert.ps1`, `scripts/build-msix.ps1`, `scripts/install.ps1`
- Modify: `README.md` (append "Build & install (dev)" section)

**Interfaces:**
- Consumes: `Hotline.App.csproj`, `Package.appxmanifest` (Task 6).
- Produces: `certs/hotline-dev.pfx` (password in certs/hotline-dev.password, gitignored), an installed package `pmarc14.Hotline`, and `scripts/build-msix.ps1`, which prints the `.msix` path as its last output line.

- [ ] **Step 1: Dev certificate script**

`scripts/dev-cert.ps1`:
```powershell
# Creates a self-signed code-signing cert matching the manifest Publisher and trusts it
# for MSIX sideloading (LocalMachine\TrustedPeople needs one UAC prompt). Dev only.
$ErrorActionPreference = 'Stop'
$subject = 'CN=pmarc14 Hotline Dev'
$root = Split-Path $PSScriptRoot -Parent
$certDir = Join-Path $root 'certs'
New-Item -ItemType Directory -Force $certDir | Out-Null
$pfx = Join-Path $certDir 'hotline-dev.pfx'
$cer = Join-Path $certDir 'hotline-dev.cer'

$cert = New-SelfSignedCertificate -Type Custom -Subject $subject -KeyUsage DigitalSignature `
    -FriendlyName 'Hotline Dev Signing' -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(3)
$pw = ConvertTo-SecureString $password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $pw | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

Start-Process powershell -Verb RunAs -Wait -ArgumentList @(
    '-NoProfile', '-Command',
    "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null")
Write-Host "Created and trusted $subject ($($cert.Thumbprint))"
```

- [ ] **Step 2: Build script**

`scripts/build-msix.ps1`:
```powershell
# Builds and signs the Hotline MSIX. Last output line = path to the .msix.
param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$pfx = Join-Path $root 'certs\hotline-dev.pfx'
if (-not (Test-Path $pfx)) { throw 'No dev cert. Run scripts\dev-cert.ps1 first.' }
$out = Join-Path $root 'artifacts\'

dotnet publish (Join-Path $root 'src\Hotline.App\Hotline.App.csproj') -c $Configuration -r win-x64 `
    -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=true `
    -p:PackageCertificateKeyFile="$pfx" -p:PackageCertificatePassword="$CertificatePassword" `
    -p:AppxPackageDir="$out" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Get-ChildItem $out -Recurse -Filter *.msix | Sort-Object LastWriteTime | Select-Object -Last 1 -ExpandProperty FullName
```

- [ ] **Step 3: Install script**

`scripts/install.ps1`:
```powershell
# Builds, installs (or updates) and launches Hotline. Run with Windows PowerShell 5.1.
$ErrorActionPreference = 'Stop'
$msix = & (Join-Path $PSScriptRoot 'build-msix.ps1') | Select-Object -Last 1
Get-Process Hotline -ErrorAction SilentlyContinue | Stop-Process -Force
Add-AppxPackage -Path $msix -ForceApplicationShutdown -ForceUpdateFromAnyVersion
$pfn = (Get-AppxPackage pmarc14.Hotline).PackageFamilyName
Write-Host "Installed $pfn from $msix"
Start-Process "shell:AppsFolder\$pfn!App"
```

- [ ] **Step 4: Create cert, build, install**

Run:
```powershell
powershell -NoProfile -File scripts/dev-cert.ps1
powershell -NoProfile -File scripts/install.ps1
```
Expected: one UAC prompt for the cert import, then `Created and trusted CN=pmarc14 Hotline Dev (...)`, then `Installed pmarc14.Hotline_<hash> from ...\artifacts\...\Hotline.App_0.1.0.0_x64.msix` (exact file name may differ), and the popup appears centered.

If `dotnet publish` doesn't produce an `.msix`, check the publish output for the `_GenerateAppxPackage` target. A known fallback is building instead of publishing: replace `dotnet publish` with `dotnet build` and the same `-p:` switches in `build-msix.ps1`, then rerun.

- [ ] **Step 5: Verify protocol activation and single instance**

Run each line and observe:
```powershell
Start-Process 'hotline://key?state=Tap'    # popup toggles (hides if shown, shows if hidden)
Start-Process 'hotline://key?state=Tap'    # toggles back
Start-Process 'hotline://key?state=Down'   # popup shows; status reads "Last key: HoldStart via Protocol → ShowPopup"
(Get-Process Hotline).Count                 # exactly 1 after a few seconds (redirected launches exit)
Get-Content "$env:LOCALAPPDATA\Packages\$((Get-AppxPackage pmarc14.Hotline).PackageFamilyName)\LocalState\logs\hotline.log" -Tail 10
```
Expected: the behaviour above, `Count` = 1, and log lines `activation kind=Protocol first=False` followed by `key Tap via Protocol -> TogglePopup`.

Note: `Start-Process` focuses a shell window first, so a Tap while the popup is visible may hide it via focus loss before the toggle arrives. The toggle guard keeps it hidden, which is correct.

- [ ] **Step 6: README section and commit**

Append to `README.md`:
```markdown
## Build & install (dev)

Requires .NET SDK 10.0.401+ on Windows 11 (22H2 or later).

    powershell -File scripts\dev-cert.ps1   # once: self-signed cert, trusted for sideloading (UAC)
    powershell -File scripts\install.ps1    # build, sign, install, launch

Then: Settings → Personalization → Text input → Customize Copilot key on keyboard → Custom → Hotline.
```

```powershell
git add -A
git commit -m "build: dev signing, MSIX build and install scripts" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Copilot fast path, fallback hotkey, tray icon

**Files:**
- Create: `src/Hotline.App/Interop/WindowMessageHook.cs`, `src/Hotline.App/Interop/CopilotFastPath.cs`, `src/Hotline.App/Interop/HotkeyRegistration.cs`, `src/Hotline.App/Interop/TrayIcon.cs`
- Modify: `src/Hotline.App/App.xaml.cs` (wire them in `OnLaunched`)

**Interfaces:**
- Consumes: `Native` (Task 6), `ActivationParser.ParseFastPath`, `Hotkey.TryParse`, `FileLog`, `ActivationRouter`, `PopupWindow.Hwnd`.
- Produces: `WindowMessageHook(nint hwnd)` with `nint Hwnd` and `void On(uint msg, Func<nint, nint, bool> handler)`; `static bool CopilotFastPath.Register(WindowMessageHook hook, Action<KeyEvent> onKey, FileLog log)`; `static bool HotkeyRegistration.TryRegister(WindowMessageHook hook, string? text, Action onPressed, FileLog log)`; `TrayIcon(WindowMessageHook hook, string iconPath, Action onToggle, Action onOpenSettings, Action onRestart, Action onQuit)` with `void Dispose()`.

- [ ] **Step 1: Window message hook**

`src/Hotline.App/Interop/WindowMessageHook.cs`:
```csharp
namespace Hotline.App.Interop;

/// <summary>Subclasses a window once and dispatches selected messages to handlers (true = handled).</summary>
internal sealed class WindowMessageHook
{
    private readonly Native.SubclassProc _proc; // field keeps the delegate alive for native code
    private readonly Dictionary<uint, Func<nint, nint, bool>> _handlers = [];

    public WindowMessageHook(nint hwnd)
    {
        Hwnd = hwnd;
        _proc = Proc;
        if (!Native.SetWindowSubclass(hwnd, _proc, 1, 0))
            throw new InvalidOperationException("SetWindowSubclass failed");
    }

    public nint Hwnd { get; }

    public void On(uint msg, Func<nint, nint, bool> handler) => _handlers[msg] = handler;

    private nint Proc(nint hWnd, uint msg, nint wParam, nint lParam, nuint id, nuint refData)
        => _handlers.TryGetValue(msg, out var handler) && handler(wParam, lParam)
            ? 0
            : Native.DefSubclassProc(hWnd, msg, wParam, lParam);
}
```

- [ ] **Step 2: Copilot fast path**

`src/Hotline.App/Interop/CopilotFastPath.cs`:
```csharp
using System.Runtime.InteropServices;
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;

namespace Hotline.App.Interop;

/// <summary>
/// Registers the window for Copilot-key "fast path" invocation: while Hotline is running, the shell
/// sends WM_COPILOT with the manifest's MessageWParam instead of a (slower) protocol launch.
/// See learn.microsoft.com/windows/apps/develop/windows-integration/copilot-key-state.
/// </summary>
internal static class CopilotFastPath
{
    public const uint WM_COPILOT = Native.WM_APP + 1;
    private static readonly Guid FastPathFmtId = new("38652BCA-4329-4E74-86F9-39CF29345EEA");

    public static bool Register(WindowMessageHook hook, Action<KeyEvent> onKey, FileLog log)
    {
        try
        {
            var iid = typeof(Native.IPropertyStore).GUID;
            Marshal.ThrowExceptionForHR(Native.SHGetPropertyStoreForWindow(hook.Hwnd, ref iid, out var store));
            var key = new Native.PROPERTYKEY { fmtid = FastPathFmtId, pid = 2 };
            var value = new Native.PROPVARIANT { vt = Native.VT_UINT, uintVal = WM_COPILOT };
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
            Marshal.ThrowExceptionForHR(store.Commit());

            hook.On(WM_COPILOT, (wParam, _) =>
            {
                if (ActivationParser.ParseFastPath((nuint)wParam) is { } e) onKey(e);
                else log.Info($"fast path: unknown wParam {wParam}");
                return true;
            });
            log.Info("Copilot key fast path registered");
            return true;
        }
        catch (Exception ex)
        {
            log.Error("Copilot key fast path registration failed (protocol activation still works)", ex);
            return false;
        }
    }
}
```

- [ ] **Step 3: Fallback hotkey**

`src/Hotline.App/Interop/HotkeyRegistration.cs`:
```csharp
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;

namespace Hotline.App.Interop;

internal static class HotkeyRegistration
{
    private const int HotkeyId = 0x484C; // "HL"

    /// <summary>Registers the optional fallback hotkey. Invalid or already-taken hotkeys are logged and skipped.</summary>
    public static bool TryRegister(WindowMessageHook hook, string? text, Action onPressed, FileLog log)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!Hotkey.TryParse(text, out var hk))
        {
            log.Error($"fallback hotkey '{text}' is not valid (example: Ctrl+Alt+H); ignored");
            return false;
        }
        if (!Native.RegisterHotKey(hook.Hwnd, HotkeyId, (uint)hk.Modifiers | Native.MOD_NOREPEAT, hk.VirtualKey))
        {
            log.Error($"fallback hotkey {hk} is already used by another app; ignored");
            return false;
        }
        hook.On(Native.WM_HOTKEY, (wParam, _) =>
        {
            if (wParam != HotkeyId) return false;
            onPressed();
            return true;
        });
        log.Info($"fallback hotkey {hk} registered");
        return true;
    }
}
```

- [ ] **Step 4: Tray icon**

`src/Hotline.App/Interop/TrayIcon.cs`:
```csharp
using System.Runtime.InteropServices;

namespace Hotline.App.Interop;

/// <summary>Native notification-area icon: left click toggles the popup, right click opens a menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private const uint WM_TRAY = Native.WM_APP + 2;
    private const uint CmdOpen = 1, CmdSettings = 2, CmdRestart = 3, CmdQuit = 4;

    private readonly WindowMessageHook _hook;
    private readonly Dictionary<uint, Action> _commands;
    private Native.NOTIFYICONDATA _data;

    public TrayIcon(WindowMessageHook hook, string iconPath, Action onToggle, Action onOpenSettings, Action onRestart, Action onQuit)
    {
        _hook = hook;
        _commands = new() { [CmdOpen] = onToggle, [CmdSettings] = onOpenSettings, [CmdRestart] = onRestart, [CmdQuit] = onQuit };
        _data = new Native.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Native.NOTIFYICONDATA>(),
            hWnd = hook.Hwnd,
            uID = 1,
            uFlags = Native.NIF_MESSAGE | Native.NIF_ICON | Native.NIF_TIP,
            uCallbackMessage = WM_TRAY,
            hIcon = Native.LoadImage(0, iconPath, Native.IMAGE_ICON, 0, 0, Native.LR_LOADFROMFILE),
            szTip = "Hotline",
            szInfo = string.Empty,
            szInfoTitle = string.Empty,
            uVersion = Native.NOTIFYICON_VERSION_4,
        };
        Add();

        hook.On(WM_TRAY, (_, lParam) =>
        {
            switch ((uint)(lParam & 0xFFFF)) // NOTIFYICON_VERSION_4: LOWORD(lParam) = event
            {
                case Native.WM_LBUTTONUP: onToggle(); break;
                case Native.WM_RBUTTONUP or Native.WM_CONTEXTMENU: ShowMenu(); break;
            }
            return true;
        });
        hook.On(Native.WM_COMMAND, (wParam, _) =>
        {
            if (!_commands.TryGetValue((uint)(wParam & 0xFFFF), out var action)) return false;
            action();
            return true;
        });
        // Explorer restarts drop tray icons; re-add when the taskbar comes back.
        hook.On(Native.RegisterWindowMessage("TaskbarCreated"), (_, _) => { Add(); return false; });
    }

    private void Add()
    {
        Native.Shell_NotifyIcon(Native.NIM_ADD, ref _data);
        Native.Shell_NotifyIcon(Native.NIM_SETVERSION, ref _data);
    }

    private void ShowMenu()
    {
        var menu = Native.CreatePopupMenu();
        Native.AppendMenu(menu, Native.MF_STRING, CmdOpen, "Open Hotline");
        Native.AppendMenu(menu, Native.MF_STRING, CmdSettings, "Edit settings file");
        Native.AppendMenu(menu, Native.MF_STRING, CmdRestart, "Restart (apply settings)");
        Native.AppendMenu(menu, Native.MF_SEPARATOR, 0, null);
        Native.AppendMenu(menu, Native.MF_STRING, CmdQuit, "Quit");
        Native.GetCursorPos(out var pt);
        Native.SetForegroundWindow(_hook.Hwnd); // required so the menu closes when clicking elsewhere
        Native.TrackPopupMenuEx(menu, Native.TPM_RIGHTBUTTON | Native.TPM_BOTTOMALIGN, pt.X, pt.Y, _hook.Hwnd, 0);
        Native.PostMessage(_hook.Hwnd, Native.WM_NULL, 0, 0);
        Native.DestroyMenu(menu);
    }

    public void Dispose() => Native.Shell_NotifyIcon(Native.NIM_DELETE, ref _data);
}
```

Note: `SetForegroundWindow(_hook.Hwnd)` before the menu activates the hidden popup's HWND. If that makes the popup itself appear, change `ShowMenu` to call `Native.SetForegroundWindow` only when the popup is visible, and verify the menu still dismisses on an outside click.

- [ ] **Step 5: Wire into App**

In `src/Hotline.App/App.xaml.cs`, add `using Hotline.App.Interop;` and `using System.Diagnostics;`, add the field `private TrayIcon? _tray;`, and insert these lines in `OnLaunched` immediately **before** `_router.OnActivation(_initialActivation, isFirstLaunch: true);`:
```csharp
        var hook = new WindowMessageHook(_popup.Hwnd);
        CopilotFastPath.Register(hook, e => _router.OnKey(e, KeySource.FastPath), _log);
        HotkeyRegistration.TryRegister(hook, settings.Activation.FallbackHotkey,
            () => _router.OnKey(KeyEvent.Tap, KeySource.Hotkey), _log);
        _tray = new TrayIcon(hook, Path.Combine(AppContext.BaseDirectory, "Assets", "Hotline.ico"),
            onToggle: _router.TogglePopup,
            onOpenSettings: () => Process.Start(new ProcessStartInfo(store.FilePath) { UseShellExecute = true }),
            onRestart: () => { _tray?.Dispose(); AppInstance.Restart(string.Empty); },
            onQuit: () => { _tray?.Dispose(); Exit(); });
```

- [ ] **Step 6: Build, reinstall, verify**

Run:
```powershell
dotnet build src/Hotline.App/Hotline.App.csproj -c Debug -p:Platform=x64
powershell -NoProfile -File scripts/install.ps1
```
Expected: build succeeds and the app installs and launches. Then verify by hand:
1. A purple "H" tray icon is present. Left-click toggles the popup; clicking it while the popup is open closes it (toggle guard).
2. Right-click shows the menu. "Edit settings file" opens `settings.json`. Set `"fallbackHotkey": "Ctrl+Alt+H"`, save, then choose "Restart (apply settings)". Ctrl+Alt+H now toggles the popup.
3. Set `"fallbackHotkey": "Banana"` and restart: the app runs, and the log shows `fallback hotkey 'Banana' is not valid`.
4. The log contains `Copilot key fast path registered`.
5. "Quit" removes the tray icon, and `Get-Process Hotline` returns nothing.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m "feat(app): Copilot key fast path, fallback hotkey and native tray icon" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Milestone-0 verification on real hardware (the spike's answer)

**Files:**
- Create: `docs/superpowers/notes/2026-09-30-copilot-key-spike.md`

**Interfaces:**
- Consumes: the installed app from Task 8.
- Produces: a written record of what works. Plans 2+ rely on its conclusions (fast path vs protocol, hold behaviour, whether a self-signed package is accepted).

This task needs the human at the keyboard; the executor walks them through it and records the results.

- [ ] **Step 1: Picker registration**

Open Settings → Personalization → Text input → "Customize Copilot key on keyboard" → Custom, and look for **Hotline** in the list. Select it.
Then run:
```powershell
Get-ItemProperty HKCU:\Software\Microsoft\Windows\Shell\BrandedKey | Select-Object BrandedKeyChoiceType, AppAumid
```
Expected: `BrandedKeyChoiceType = App`, `AppAumid = pmarc14.Hotline_<hash>!App`.
If Hotline is **not** listed: record that, keep the fallback hotkey (Ctrl+Alt+H) as the primary trigger, and note the policy fallback (`SetCopilotHardwareKey` policy) as the follow-up.

- [ ] **Step 2: Key behaviour while running**

With Hotline running and the popup hidden: tap the Copilot key; tap again; press and hold for 1 s, then release; press Win+C.
Then run:
```powershell
Get-Content "$env:LOCALAPPDATA\Packages\$((Get-AppxPackage pmarc14.Hotline).PackageFamilyName)\LocalState\logs\hotline.log" -Tail 20
```
Record for each press which source fired (`via FastPath` or `via Protocol`), whether duplicates appeared (`duplicate, ignored`), and how quickly the popup appeared (target: no visible delay).

- [ ] **Step 3: Key behaviour when not running**

Quit from the tray, then tap the Copilot key.
Expected: Hotline cold-starts via protocol and the popup shows. Record the cold-start time (stopwatch estimate).

- [ ] **Step 4: Sign-in start**

Sign out and back in (or reboot).
Expected: the tray icon is present, no popup appears, and the log shows `activation kind=StartupTask first=True`.

- [ ] **Step 5: Write the notes and commit**

`docs/superpowers/notes/2026-09-30-copilot-key-spike.md`, filled in with the observed values. Template:
```markdown
# Copilot key spike — results (2026-09-30)

- Self-signed package listed in Settings picker: YES / NO
- BrandedKey after selection: <BrandedKeyChoiceType>, <AppAumid>
- Tap while running: source = FastPath / Protocol / both (deduped); latency feel = instant / noticeable
- Hold (Down/Up) while running: Down source = …, Up source = …
- Win+C behaves like the key: YES / NO
- Cold start via key: works YES / NO, ~<n> s
- Startup task launches to tray: YES / NO
- Decisions for Plan 2: <e.g. keep fast path as primary; hold reserved for capture>
```

```powershell
git add -A
git commit -m "docs: Copilot key spike results" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## Later plans (not in this document)

Plan 2: WebView2 chat + OpenAI-compatible backend + history. Plan 3: Gemini/Anthropic APIs + Claude Code/agy CLIs. Plan 4: context (window capture, region select, attachments). Plan 5: settings UI, layouts, double-tap, release. Plan 6 (low priority): llama.cpp local server manager. Each one is written after the previous plan lands, informed by the Task 9 notes.
