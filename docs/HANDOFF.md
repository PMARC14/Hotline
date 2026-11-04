# Hotline — start here (handoff for the next development chat)

Read this first, then `CLAUDE.md` (working rules; local, git-ignored) and, if present, `docs/local/HANDOFF-PRIVATE.md`
(machine-specific notes; git-ignored). Everything else is linked from *Where things are documented*. The maintainer's job briefs and
review outputs are **outside the repo**, in `~/.claude/briefs/hotline/` (the 1.0 job briefs `1.0-*.md` and `1.0-plan-README.md` are there).

## What Hotline is

A Windows 11 app that takes over the Copilot key: a native WinUI 3 popup (acrylic, no WebView) that chats with the AI the
user picks: Claude Code CLI, OpenAI Codex CLI, Antigravity CLI (agy), Anthropic / Gemini / OpenAI-compatible APIs, or local
OpenAI-style servers. Everything is configured in files under `%USERPROFILE%\.hotline` and applies live.

## State (2026-11-04)

- **Released:** v0.6.0 (GitHub; `version.txt` says `0.6`). Every push to `main` that changes code publishes the next patch release, signed
  with Hotline's long-lived self-signed certificate (`CN=pmarc14 Hotline Dev`, valid to 2036-10-12; [RELEASING.md](RELEASING.md)).
- **Microsoft Store:** **Hotline AI** (`9PMW5CR6G3BC`). Last uploaded bundle 0.3.0.0. The 0.6.0.0 bundle (`HotlineAI_0.6.0.0_Store.msixbundle`)
  was built by hand (Actions › Store, submit off) and is **not yet uploaded by the owner**; the 0.6 listing text is in `store/listing.md`
  and `store/listings/` (translations are model-drafted). Otherwise no Store bundles until just short of 1.0 (owner, 2026-10-17).
- **Schema freeze (S1) is NOT done:** it is the first job of the road to 1.0.
- **Tests:** about 2,525 Core unit tests (`dotnet test`), `tests/smoke/smoke.ps1 -Install` (the owner sometimes skips it) and
  `hotline://selftest`; commands, options and the hand-test checklists: [TESTING.md](TESTING.md). Test cleanup not done: shared HTTP fake /
  stream collector / MCP fake helpers and Argon items 2, 3, 7, 9, 15, 17.
- **Reviews so far** (Astra, Argon, Opus, Sonnet, Flash; findings verified and fixed with a failing test first, rejected ones have tests):
  PR #36 (2026-11-01 deep review), #40/#41, #45/#46/#48 (0.6). Rejected items are listed in the PR texts.
- **Security pass** (multi-model audit, verified, fixed with tests): UNC/device paths refused (`SafeDirectory`,
  `AppActionRequest.IsSafeLocalImagePath`), history ids validated (`HistoryIds`), response-size caps, chat-only isolation flags
  always applied and tool pre-approval flags dropped, CLI output capped, `ShellOpen` limited to Hotline's own files, MCP `.exe`
  started directly and `cmd.exe` refused for unsafe characters, SVG `style` allow-listed, `LogText` (secrets redacted, link queries
  never logged). Findings still open are under *Deferred*.
- Hotline's own Right Ctrl mode is gone (Windows' Copilot-key remap replaces it, [copilot-key.md](copilot-key.md)).

### Where things are documented (this file does not repeat them)

- File formats and every setting, panel shortcuts (`toolbar.json` `shortcuts` map and `buttons.<id>.shortcut`), toolbar rows, width model,
  Continue in, Backup, safe file writes: [configuration.md](configuration.md).
- Hand tests and the automatic checks: [TESTING.md](TESTING.md). Releases, signing, Store, Azure: [RELEASING.md](RELEASING.md).
  Translations: [TRANSLATING.md](TRANSLATING.md). Copilot key setup: [copilot-key.md](copilot-key.md). Store text: `store/`. Privacy: `PRIVACY.md`.
- Code entry points: button rows `ToolbarModel.Resolve` / `ToolbarEditor` / `ToolbarPlan` / `ToolbarLayout` / `ToolbarRenderer` (reference
  layouts `ToolbarProfiles`, self-test steps P01..P47); width in real pixels `ScreenScale.Line`; `LenientReader`; "no backup, no write"
  `BackupFiles.CopyStamped`; `SettingsPackage`; Continue in `ContinueIn.Targets()` / `ContinueTargets.FillCommand` / `OpenWebUiChat`. Old
  design specs and plans: `git log -- docs/superpowers`.

## Open items

- **Hand tests still open** (all in [TESTING.md](TESTING.md), "0.6 checklist" first): shortcuts as the App uses them (focus routing,
  AltGr/IME/auto-repeat guards, tooltips and Narrator accelerator keys, capture box, More menu shortcut), multi-monitor / mixed DPI,
  dropdown hover / press look with outline or fill (**UNVERIFIED by eye**), window-close / rename / flyout / tray guards, Narrator names.
- **App-only review findings** (need a hand check or an App-side fix; the Core parts are tested):
  - Settings reload vs unsaved edits: open controls and file-watcher dispatch in `Settings/SettingsWindow*.cs` (`SettingsService.Reload` and
    failed-save handling in `Update` are Core-tested).
  - Horizontal Alt-resize: grips, height application and drag state in `Chat/PanelGrips.cs` (`PanelPlacement.Resize` is tested).
  - Filled-dropdown brushes: WinUI resource lookup and templates in `ToolbarRenderer.cs`.
  - "Move what doesn't fit" stale width: the App supplies the available width (`Settings/ToolbarQuadrantEditor.cs` →
    `ToolbarEditor.FillMenuFromOverflow(m, room, room)`, Core-tested with explicit widths).
  - Hold leak: `PopupWindow.HoldResize` users, menu event subscriptions and disposal (`ToolbarRenderer.cs`, `Chat/PanelGrips.cs`,
    `Chat/RecentChatsMenu.cs`).
  - Open WebUI keys stay in Credential Locker after both resets (`ContinueIn.ApiKeyName(url)`; documented in configuration.md; delete in
    the App's reset handler in `SettingsWindow.xaml.cs` if wanted; Credential Locker is not reachable from Core tests).
- `PanelPlacement.Rescale` keeps a pinned panel's size in scaled units across monitors of different scale (hand check).
- `personal/gemini-web` still needs `main` merged in.

## Next work

### Road to 1.0

Owner decisions: everything below is in 1.0; the file formats are frozen from 1.0 (migrations, never breaking changes); the one
exception is Windows agent connectors verification, which moves to 1.1 if they cannot be tested. Work in this order:

1. **S1 schema freeze** (first, merged before the rest): unknown keys survive saves; one `version` rule with migrations (no
   `schemaVersion` / `version` split); no rewrite on load; a connection file's name = its id; app state (`.first-run-shown`,
   `supportReminder`) in `state.json`, not in config; one backup policy in `backups\` (about 7 schemes today); 3 renames with aliases
   (`window.height` to `minHeight`, `backends` to `connections`, toolbar `items` to `bottom`); JSON schemas and frozen compatibility
   fixtures. Also decide `mcp.json` `env` secrets versus the Credential Locker rule. Notes: a toolbar edit in Settings drops unknown
   top-level keys and unknown nested fields (documented by the `Compat_*` tests in `SettingsPackageTests`); runtime shortcut conflict
   resolution must keep the requested value; Astra's recommendation: a version 3 boundary for shortcuts, one backup policy, unknown
   fields preserved.
2. After S1, in parallel (disjoint files): **E1** security leftovers (see *Deferred*); **F1** chat basics (edit/resend, regenerate,
   history search, pin, export, context meter); **D2** support bundle, first-run flow, accessibility.
3. Outlines, one job each (Core TDD + UI): **H** attachments (PDF/Office text, size caps, text only) and "ask about this window"
   context; **I** local models (Ollama, LM Studio presets and model lists); **J** tools (web search, MCP gallery, safe built-ins);
   **K** voice read-aloud; **L** `hotline ask` CLI and a shell entry; **N** customization (profiles, per-action hotkeys, quick-action
   variables, placement presets, theme gallery, sync folder).
4. 0.5 items still open: multi-monitor / mixed-DPI region capture and bar behaviour, moving the bar and how it acts when pinned, the
   shipped sound, and the owner's desktop hand tests ([TESTING.md](TESTING.md)).
5. Release: one certified Store update with all 17 languages (new strings arrive as English placeholders; [TRANSLATING.md](TRANSLATING.md)),
   the signing fix from E1, an accessibility pass (Narrator, Accessibility Insights), then 1.0. A fresh multi-model review after S1
   and the feature jobs is planned first. No native-speaker reviews or RTL planned; paid signing (winget-pkgs) only if Hotline gets popular.

**Agent connectors (Windows) - testing note.** Hotline discovers connectors through `odr.exe mcp list` (`OdrDiscovery`, shown as
`windows-...` servers) when the file exists; this PC (26H2, build 26300) has none. To test, use a spare machine or VM on a 25H2
build with KB5070316 or an Insider Dev build, turn on Settings > System > AI components > **Experimental agentic features**
(administrator, off by default), and run `odr mcp list`. Sources: <https://www.elevenforum.com/posts/674378/>,
<https://windowsreport.com/kb5070316-adds-native-mcp-smarter-app-updates-and-midi-2-0-tools-to-windows-11-25h2/>. If it cannot be
tested before 1.0, ship the code as is, label it preview, and verify in 1.1.

Owner preference: anything configurable should be in settings.json even when the Settings window doesn't show it.

### Versions

`version.txt` holds major.minor (now `0.6`). Every code push to `main` publishes `v<major.minor>.<patch>` (`scripts/next-version.sh`;
[RELEASING.md](RELEASING.md)); the Store package is the same version plus `.0` and each submission must be higher than the last.
**1.0** comes when the Road to 1.0 is done and a Store update with all 17 languages is certified.

## Architecture map

- `src/Hotline.Core` (net10.0, no UI, unit-tested): `Activation/` (key events, URI planner `hotline://key|tray|settings|demo|selftest`,
  `AppActionRequest`); `Backends/` (`ConnectionEditor`, `BackendCatalog`, `CliHandoff`, `CliTranscripts.*`, `Agy/`, `ClaudeCode/`,
  `Codex/` = `codex exec --json`, one process per turn, all three resume a chat's own conversation; `Api/` with `ApiCommon` SSE/errors/https
  rule); `Chat/` (`ChatController`, `HistoryStore` JSONL + `<id>.meta.json`, `PromptLibrary`, `QuickActions`, `MemoryStore`, `OcrPlan`);
  `Settings/` (`HotlineSettings` schema v15, `SettingsStore`, `LenientReader`, `SettingsService` live Update/Reload, `SettingsSchema`,
  `SettingsReset`, `SettingsPackage*`, `BackupFiles`, `Guide`); `Text/` (`MarkdownModel`, `SvgSanitizer`, `Localization/`); `Tools/` (MCP:
  `McpToolHost`, `McpConfig`, `McpProcess`; `OdrDiscovery`); `Diagnostics/`, `Processes/`, `Platform/`; `Windowing/` (`PopupGeometry`,
  `PanelPlacement`, the `Toolbar*` files, `Sounds`). MCP approvals: only failed "Always" saves are remembered; a file rule or a
  removed/disabled server clears that fallback.
- `src/Hotline.App` (WinUI 3, packaged MSIX, full trust): `App.xaml.cs` (+ partials) composition root; `PopupWindow` (sizing, eased
  growth, focus, off-screen mode, `HoldResize`); `ActivationRouter`; `ToolbarRenderer`; `SelfTestLinks`; `Chat/` one job per class
  (`ChatPresenter` coordinates, `Composer`, `AttachmentPanel`, `TranscriptView` = one selectable `RichTextBlock`, `PanelGrips`, `ContinueIn`,
  `PanelSelfTest`, ...); `Settings/` (pages generated from `SettingsSchema`, `ToolbarQuadrantEditor*`); `Interop/` (tray, Copilot fast
  path, hotkey, job object, `SelectionReader` UIA + clipboard fallback).

## Hard-won facts (don't relearn these)

**UI and layout**
- **A window resize closes an open WinUI flyout** (even `ShouldConstrainToRootBounds=false`). The panel sizes itself to its content,
  so anything that changes layout while a flyout is open (even a 0.5 px border change on focus) closes it. Use
  `PopupWindow.HoldResize()` around flyouts; keep focus styling layout-neutral.
- **A `LayoutCycleException` stops WinUI drawing that window for good** (it keeps running, but every open is blank; RenderTargetBitmap/OCR
  never complete). The old "first-start layout cycle" was the pickers sizing themselves from their own row's width inside its
  SizeChanged. Rules: never change sizes inside a size event; queue the work after layout (DispatcherQueue, coalesced) and set values
  only when they change; measure from a container whose size doesn't depend on what you set. To find one: `DebugSettings.
  LayoutCycleTracingLevel = High` writes it to OutputDebugString (DBWIN listener) and names the element.
- **The panel's window rectangle includes a frame** (`AppWindow.Size` − `ClientSize`: 13 px tall at 200 %); sizing to the content height
  cut off the bottom padding and the move grip (`FramePx`). The open check logs `panel content cut off`. `hotline://grips?show=1|0`
  shows the Alt grips without a keystroke.
- Before 0.3 a hung self-test left the panel at (-32000, -32000), invisible on every later open; a real open now always ends off-screen
  mode. When "nothing appears", check the window rect first (`EnumWindows`; a non-DPI-aware PowerShell reports logical pixels).
- **`ListView.ItemClick` with `ListViewItem`s added directly** reports the item's *Content*: put the `Tag` on the content too and match
  `FrameworkElement { Tag: … }`. `SelectedItem` is the `ListViewItem`.
- **WinUI `TextHighlighter` on a `RichTextBlock` crashes natively when drawn** (AV in Microsoft.UI.Xaml.dll); don't reintroduce. Only
  seen when rendered, which is why `hotline://selftest` renders off-screen.
- **The TextBox caret takes the inverse colour and opacity of the box background** (WinUI #6498/#8207/#9005): the composer is a solid surface.
- **A `UIElement` can't have two parents** (broke the settings window once; the self-test now builds every page).

**Windows and the key**
- **Windows 11 26H2 gives focus to Explorer's `CopilotKeyPressFocusFrame` on each key press**, ~0.1 s before Hotline's toggle arrives. A
  panel still showing then (pinned, or kept because Settings had focus) made Explorer play the warning sound, and the key's keystrokes
  reached a focused pin button. Now: any open panel closes when that frame takes focus (the toggle that follows counts as that close,
  within the 300-ms guard), bar buttons never keep keyboard focus, and the log names who takes focus (`deactivated: focus went to …`).
- **Copilot key:** needs package identity (hence MSIX; [RELEASING.md](RELEASING.md)); cold start arrives as `ProtocolForResults`; the fast
  path is a `WM_APP+1` message to the registered window. Tap and hold arrive only through the fast path (the protocol sends no
  duplicates); a hold's key-up arrives 10-45 ms after its key-down.
- **Windows on-device agent registry:** connectors run only via `odr.exe` (build 26220.7262+).
- **Clipboard:** `OpenClipboard(NULL)` + `EmptyClipboard` leaves it ownerless and every `SetClipboardData` then fails; restoring needs a
  real owner window (`ClipboardOwner`: message-only window on its own pumping thread). Never send Ctrl+C unless UIA confirmed "no text
  pattern": an empty selection copies a whole line (editors) or sends SIGINT (terminals).
- **PowerShell 5.1 reads `0x80000003` as a negative Int32**: pass `[uint32]2147483651` to `SetThreadExecutionState`. Power requests
  (`PowerCreateRequest`/`PowerSetRequest`) hold Modern Standby off more reliably. `@($null)` is a one-item array.
- **OpenSSL on Windows writes `\r\n`:** strip `\r` too when making password files (`tr -d '\r\n'`), or signtool sees a different password.
- **MCP SDK 2.2.0:** `StreamClientTransport(serverInput, serverOutput)`: the first stream is the one written to (the server's stdin).

**Child processes (no console flashing)**
- Only launches with `CreateNoWindow = true` avoid a visible console (`CliRunner`, `LineProcess`, `McpProcess` all do); `ContinueIn`
  opens a console on purpose (its app targets too). Never wrap a child in `cmd /c`; kill whole process trees (job object).

**The CLIs**
- **CLI sessions:** a connection detour clears the session when the transcript is whole; record-only chats still resume, without detour turns in that CLI's context.
- **CLI histories:** Claude Code `~/.claude/projects/<folder>/<session>.jsonl` (type user/assistant, `message.content` string or blocks;
  skip `isMeta`, `isSidechain`, `tool_result`); Codex `~/.codex/sessions/YYYY/MM/DD/rollout-*-<thread>.jsonl` (`response_item` ›
  `message`; skip `<environment_context>` turns); agy `~/.gemini/antigravity-cli/conversations/<id>.db`, SQLite table `steps`: type
  14 = user (protobuf field 19›2), 15 = answer (20›1), 101 = system notice. No agy command exports a conversation.
- **agy:** effort is part of the model id (`gemini-3.8-flash-high`; `max` is invalid for Flash); headless mode denies anything not in
  `permissions.allow`. Custom agents are found in the workspace including `--add-dir` folders (`agy --add-dir <dir> --agent hotline`
  works from any folder); an unknown `--project <name>` is silently ignored; `--new-project` names the project after the folder agy runs in.
- **Claude Code:** `--bare` never reads the OAuth login; chat-only isolation uses `--tools "" --setting-sources "" --strict-mcp-config`;
  `--no-session-persistence` keeps its resume list clean.

**Working with the tools (this machine)**
- The Agent tool's worktree isolation needs the cwd spelled `C:\Users\pmadd\Git\...` (capital G); otherwise create worktrees by hand
  with `git worktree add`. The auto-mode classifier blocks copying files out of `~/.hotline` into the repo (build fixtures from code
  defaults). Python run from bash cannot read `/tmp`. Translations: `agy --model gemini-3.8-flash-high -p "Do NOT use any tools ..."`
  in batches, validated for `{n}` placeholders and file names. Use the `hold` skill for long runs.
- A shell heredoc given to the Bash tool collapses backslashes (`\n` becomes a newline, `\b` a backspace), and a non-raw Python string
  turns `\r`, `\a`, `\n` in Windows paths into control characters: write C#, resx or script text that contains backslashes with the
  Write/Edit tools, or use raw strings / `chr(92)`. Python `/tmp` and bash `/tmp` are different folders here. `rm -f "$P"/*` is
  refused unless written `"${P:?}"/*`.
- **agy as a reviewer** (the agy MCP tool can return nothing and can't pick a model; headless agy can't write files): run `agy --model
  <claude-opus-5-5-high | gemini-3.8-flash-high | gemini-4-argon-high> -p "<prompt>"`. Say "do not use any tools" for pure text jobs
  (headless agy aborts silently when it tries a tool), or list absolute file paths and say "use only view_file on these" to review whole
  folders (no pasting). Command lines cap at ~32k chars, so don't paste big diffs (split into chunks). Pasting keyboard-hook /
  input-injection code (SetWindowsHookEx, SendInput) into a command line got blocked by Defender as `Trojan:Win32/ClickFix` (a false
  positive): review such code by file path. Opus/Sonnet via agy run out of quota (a few big reviews; the week); Flash is the everyday model.

## Deferred (not bugs to rediscover; from the reviews)

- **Windows AI (deferred, maybe never):** Phi Silica is replaced by "Aion Instruct" (Microsoft Learn, 2026-10-12), so code to the
  stable `LanguageModel` surface, never a model name, and hide it unless probed supported (no download without consent). Hotline's
  on-device route is the Local connection. Windows agent connectors are discovered with `odr.exe mcp list`.
- **Design notes (Argon/Flash):** `ThisPc` is static, `ActivationRouter` is coupled to `PopupWindow`, `App` is a monolith.
- **Small:** ComboBox `TextSubmitted` `e.Handled`; `ReportHeight` runs in SizeChanged; `CopyImage` on the UI
  thread; `PanelTheme.Style` read before `Update`; `PanelGrips` cursors; sounds not reloading a replaced .wav; Claude
  `Win32Exception` mapping (needs a string); Codex images beside the workspace; the Anthropic path bypasses `ApiCommon`; the
  tool round is written three times; `ToolsFor` type test.
- **Larger refactors:** `CliPromptFormat`; derive the CLI set from `ConnectionTypeInfo`; one JSON string helper;
  `BackendProfile.Clone`; required `Secrets` in `BackendFactory`; per-profile agy agent files; `AddSafe` flag parsing.
- **Security:** a plain `hotline://key` web link can read the selection (the real key path can't be told apart without risking it);
  Markdig takes ~15 s on adversarial nesting (a UI freeze, not a crash); MCP `readOnlyHint` is trusted from the server (only for
  servers marked trusted would be better); unsigned inner packages in the signed bundle (Azure flow).
- **From the 2026-10-17 whole-codebase review, check when touching the code:** mixed-DPI drags (a drop across monitors may keep the
  old width in pixels; a live move between a top/bottom pair of different DPIs can jitter; a tall auto-direction panel dragged to
  the top edge may not flip; `FramePx` is read before the panel moves to its monitor); `_barDip` is 0 until the first layout; connection
  ids are case-sensitive in a few places (`defaultBackend`); the Language box is blank for `"en-us"`; scheme-less endpoints throw a raw
  error; MCP approval keys differ by case; SVG `style` with CSS escapes; code-fence parity in `MarkdownModel`; the fallback hotkey
  isn't re-registered on a settings change.

## Process that worked

Branch from GitHub `main` per feature → TDD in Core → build → `smoke.ps1 -Install` → commit + push → adversarial review (agy, or a
Sonnet subagent) → fix confirmed findings → PR/merge. **Reviewers' claims are about one third wrong: verify each against the code.**
Parallel work: worktree agents, each on a branch from a given SHA, cherry-picked in order. A coverage check before deleting tests.

**UI changes are verified by eye, not by window rectangles** (a rect check once passed while nothing was drawn):
- `hotline://selftest` fails loudly on a layout cycle, too few frames or an OCR timeout, renders narrow/wide/both directions/the reference
  layouts, and saves PNGs to `~/.hotline/logs/selftest/`.
- Real screenshots: a DPI-aware `CopyFromScreen` of the panel (open with `hotline://key?state=Tap` or `hotline://demo`, then close it);
  Settings pages via UI Automation. Put them in one folder and have `agy --model gemini-4-argon-high` read them with view_file; text
  checks (translations) go to `gemini-3.8-flash-high`.
- **Clean up after testing:** restore any settings.json/toolbar.json you changed (back them up first), close every panel/window you
  opened, and restart Hotline (`taskkill /IM Hotline.exe /F`, then `hotline://tray`).
- New features get a section in Settings › Guide (`Settings/Guide.cs` + `Guide_*` strings in all languages).
