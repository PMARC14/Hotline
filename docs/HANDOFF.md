# Hotline handoff

Start here. Hotline is a Windows 11 app that takes over the Copilot key: a native WinUI 3 popup that chats with Claude Code,
Codex, agy (Antigravity), Anthropic, Gemini, OpenAI-compatible or local servers, configured by files in `%USERPROFILE%\.hotline`.
**State:** 0.7 (S1 schema freeze) is merged to `main` (PR #66); the owner hand-checks it on a new PC, then promotes it and
uploads the Store bundle. Next work: the refactor plan, then the Road to 1.0.

Then read `CLAUDE.md` (working rules) and, if present, `docs/local/HANDOFF-PRIVATE.md` (machine notes). Docs:
[README](../README.md) (users, Copilot key, translations), [configuration.md](configuration.md) (every file and setting, file
rules), [TESTING.md](TESTING.md) (tests and hand checks), [RELEASING.md](RELEASING.md) (versions, signing, Store).

**Contents:** [State](#state) | [Finish 0.7](#finish-07) | [Road to 1.0](#road-to-10) | [Refactor plan](#refactor-plan) |
[Architecture](#architecture) | [Rules](#rules) | [Hard-won facts](#hard-won-facts) | [Deferred](#deferred) | [Glossary](#glossary)

## State

| Item | Value |
|---|---|
| Stable release | v0.6.4 (GitHub). Pushes to `main` publish prereleases; **Actions › Promote** makes one stable |
| Branch | `main` at 0.7 (`version.txt` 0.7, 3,140 Core tests green); `personal/gemini-web` is the owner's own build, kept merged with `main`, never merged into it |
| Store | **Hotline AI** `9PMW5CR6G3BC`; last upload 0.3.0.0; next upload 0.7.0.0 (bundle built locally, carried by hand) |

S1 replaced every config format with a versioned one, one writer (`SafeFile`), backup sets, `state.json`, toolbar v3 and a
one-time upgrade. Reviews on 2026-11-22: Sol (U1, Core), Argon (App), Astra and Neon (whole codebase); every confirmed finding is
fixed with a test.

## Finish 0.7

1. **Hand tests on the new PC:** install the newest prerelease from GitHub (or `scripts/install.sh`), then run
   [TESTING.md](TESTING.md) › "Not yet checked by hand". Ask before `smoke.ps1` (keystrokes) or screen captures.
2. **Promote** that prerelease (**Actions › Promote** with its tag) once the hand tests pass.
3. **Store upload** of 0.7.0.0: rebuild if code changed (`scripts/build-msix.sh --store --arch all --version 0.7.0.0` with the
   identity values in `store.yml`), paste the listings from `store/listing.md` and `store/listings/`, reuse the images in
   `artifacts/store/<lang>/` (git-ignored; `fr/screenshot-4.png` still shows the old clipped label, kept for now).

## Road to 1.0

1.0 = the items below done and one certified Store update in all 17 languages. Formats are settled from 1.0: a compatible change
is free, a breaking one needs a version bump and a conversion. Agreed order (2026-11-22):

1. Refactor plan steps 1–3 below.
2. **E1** security leftovers (see Deferred), multi-monitor and mixed-DPI capture and bar behaviour, **P1** notices in one style and
   panel memory with long chats (measure first).
3. **F1** chat basics (edit and resend, regenerate, history search, pin, export, context meter), **D2** support bundle, first-run
   flow, accessibility pass, **I** local-model presets.
4. Release: Store update with all languages, signing fix from E1, then 1.0.

Moved to 1.1 (additive, free under the format rules): **N** customization (profiles, sync, themes), **K** read-aloud,
**L** `hotline ask` CLI, **J** web search and MCP gallery, **H** PDF and Office text. Windows agent connectors (`odr.exe mcp list`,
needs build 26220.7262+ with Experimental agentic features on) ship as preview if they can't be tested.

## Refactor plan

From the Neon design review, agreed 2026-11-22: do steps 1-3 before new 1.0 features. Each step keeps the build and tests green.

| # | Change | Cost | When |
|---|---|---|---|
| 1 | `Files/JsonFile` (BOM, one case-insensitive `ReadVersion`, merge wrapper); `StateStore` rebuilt on a typed `StateDocument` + `JsonMerge` + `IFileSystem`, one instance injected everywhere (drop the static lock map); spot math into `PanelSpotPolicy` | M | before 1.0 |
| 2 | Split `PopupWindow` (953 lines): Core `MonitorChoice`, `PopupBlurPolicy`, `PopupSessionState`; App `Windowing/PopupPlacement`; delete dead `PopupGeometry.ChatWidthPx`/`RelativeWidthPx`. Also move `OnPanelKey`, `ConnectionLifecycle`, `ModelSelection` (no localized "Default" sentinel), attachment intake and the copy-last-answer rule to Core | L | before 1.0 |
| 3 | One Core `ToolbarSession` (load, fingerprint, edit, shortcuts, reset, picture staging through `SafeFile`); remove `ChatSettings.Toolbar`, `ToolbarShortcutSession`, `CustomIcons` `.prev` and `PendingPictures` | M | before 1.0 |
| 4 | Split `ToolbarModel` into model, reader, `JsonObject` writer; move `ChatShortcuts` to `Windowing/PanelShortcuts` | M | after 1.0 |
| 5 | `SettingsWindow` partials into page classes with a `SettingsPageContext`; split `ToolbarRenderer` (styler, picture loader); shared CLI turn and API tool-round helpers; typed warning codes | M | after 1.0 |

Keep: config vs state separation, embedded defaults as the single source, pure toolbar planning in Core, launch-bound grants and
secret scrubbing, the single-`RichTextBlock` transcript.

## Architecture

- `src/Hotline.Core` (net10.0, no UI, unit-tested): `Activation/` (key events, `hotline://` planner, App Actions), `Backends/`
  (`ConnectionProfile`, catalog, CLI backends in `Agy/`, `ClaudeCode/`, `Codex/`; APIs in `Api/` on `ApiCommon`), `Chat/`
  (`ChatController`, `HistoryStore`, prompts, quick actions, memory), `Files/` (`IFileSystem`, `SafeFile`, `BackupSet`, `JsonMerge`),
  `Settings/` (`HotlineSettings`, `SettingsStore`, `LenientReader`, `SettingsService`, `SettingsSchema`, reset, package),
  `State/` (`StateStore`), `Tools/` (MCP host, rules, grants, secrets, `OdrDiscovery`), `Upgrade/` (`PreFreezeUpgrade`,
  `StarterFiles`), `Windowing/` (`PanelWidth`, placement, spot policy, toolbar model, plan, layout, editor), `Defaults/*.json`.
- `src/Hotline.App` (WinUI 3, packaged MSIX): `App*.cs` composition root (upgrade → starter files → load → watchers),
  `PopupWindow`, `ActivationRouter`, `ToolbarRenderer`, `Chat/` (`ChatPresenter` and one class per job), `Settings/` (pages from
  `SettingsSchema`, toolbar editor), `Interop/` (tray, Copilot fast path, hotkey, job object, selection reader).
- `schemas/` JSON Schemas (shipped in the package), `tests/fixtures/<family>/v<N>/` and `tests/fixtures/pre-freeze/`;
  `Defaults/*.json`, schemas and fixtures change together.

## Rules

- File formats, backups and the compatibility promise: [configuration.md › File rules](configuration.md). Every config write goes
  through `SafeFile.Replace` with a `BackupSet`; loading never writes; unknown keys survive; a newer file is never written.
- Owner preferences: anything configurable is a file setting even without UI; acrylic, compact, native UI; no Ollama; no
  native-speaker translation review, no RTL; paid signing only if Hotline gets popular.
- Process: branch from `origin/main`, TDD in Core, build, smoke where the UI changed, adversarial review by another vendor,
  **verify every finding against the code** (about a third are wrong), fix with a test. UI changes are checked by eye
  (`hotline://selftest` PNGs in `~/.hotline/logs/selftest/`, or a DPI-aware capture), never by window rectangles alone.
- After testing on this PC: restore changed config files byte for byte, close every panel and window, restart Hotline
  (`taskkill /IM Hotline.exe /F`, then `hotline://tray`).
- New features get a Settings › Guide section (`Settings/Guide.cs` + `Guide_*` strings, translated).

## Hard-won facts

**UI**
- A window resize closes an open WinUI flyout: wrap flyouts in `PopupWindow.HoldResize()`; keep focus styling layout-neutral.
- A `LayoutCycleException` stops WinUI drawing that window for good. Never change sizes inside a size event; queue after layout,
  set only on change, measure from a container you don't size. Trace with `DebugSettings.LayoutCycleTracingLevel = High`.
- The panel's window rectangle includes a frame (`FramePx`); `hotline://grips?show=1|0` shows the Alt grips.
- `TextHighlighter` on a `RichTextBlock` crashes natively when drawn. The TextBox caret takes the box background's colour and opacity.
  A `UIElement` can't have two parents. `ListView.ItemClick` with directly added items reports the item's content.
- Hidden self-test Settings uses NavigationView's default 320 DIP pane: measure overhead, not absolute widths.
- A `ContentDialog` needs `XamlRoot` (`ShowDialogAsync` sets it).

**Windows and the key**
- 26H2 gives focus to Explorer's `CopilotKeyPressFocusFrame` ~0.1 s before Hotline's toggle; an open panel closes when that frame
  takes focus. After the key is released the frame hands focus to a window of Windows' choosing: not the user moving. The key needs package identity (MSIX); cold start arrives as `ProtocolForResults`; tap and hold arrive only through
  the `WM_APP+1` fast path.
- Moving a window onto a monitor of another scale sends `WM_DPICHANGED` with the window scaled by the DPI ratio; WinUI applies
  it. When placement already used the new scale, answer with the planned rect (`PopupWindow.OnDpiChanged`) or it is 1.5x off.
- The foreground change `AppWindow.Hide()` causes is delivered to the WinEvent hook while `Hide` runs: hide under
  `ForegroundWindowTracker.Suppress()` and give focus back explicitly.
- The desktop (`Progman`/`WorkerW`) spans every monitor: a click there says where the user is only through the cursor.
- Clipboard restore needs a real owner window (`ClipboardOwner`). Never send Ctrl+C unless UI Automation reports no text pattern.
- Credential Locker names ignore case: never delete an "old" entry that differs from the new one only by case.
- Probing a `\\server\share` path makes Windows connect and send the user's hash: check `SafeDirectory` first.
- PowerShell 5.1 reads `0x80000003` as negative: pass `[uint32]2147483651`. OpenSSL on Windows writes `\r\n`: strip `\r` from
  password files. MCP SDK `StreamClientTransport(serverInput, serverOutput)`: the first stream is the server's stdin.
- Child processes: only `CreateNoWindow = true` avoids a console; never wrap in `cmd /c`; kill whole trees (job object).

**The CLIs**
- Histories: Claude Code `~/.claude/projects/<folder>/<session>.jsonl`; Codex `~/.codex/sessions/YYYY/MM/DD/rollout-*.jsonl`;
  agy `~/.gemini/antigravity-cli/conversations/<id>.db` (SQLite `steps`: 14 user, 15 answer). No agy command exports a chat.
- agy: effort is part of the model id; headless mode denies anything not in `permissions.allow` and aborts silently on a denied
  tool; `--add-dir` folders are searched for custom agents.
- Claude Code: `--bare` never reads the OAuth login; chat-only isolation is `--tools "" --setting-sources "" --strict-mcp-config`.

## Deferred

- **Security:** a plain `hotline://key` web link can read the selection; Markdig takes ~15 s on adversarial nesting; MCP
  `readOnlyHint` is trusted from the server; unsigned inner packages in the signed bundle.
- **Mixed-DPI drags:** a drop across monitors may keep the old width; a live move between stacked monitors of different DPI can
  jitter; `FramePx` is read before the panel moves to its monitor; keyboard Move and Resize stay on the current monitor.
- **Processes:** a CLI child joins the kill-on-close job after `Process.Start`; anything it spawns first can outlive a crash.
- **Windows AI:** code to the stable `LanguageModel` surface, never a model name; hidden unless probed supported.

## Glossary

- **S1:** the schema freeze (0.7). **Core / App:** `Hotline.Core` (UI-free) / `Hotline.App` (WinUI).
- **Backup set:** one `backups\<stamp>-<reason>\` folder per operation. **Grant:** an "Always allow" click in `state.json`.
- **Scaled pixel (DIP):** a pixel after Windows scaling. **agy:** Antigravity CLI. **MSIX:** Windows app package.
