# Hotline — start here (handoff for the next development chat)

Read this first, then `CLAUDE.md` (working rules; local, git-ignored) and, if present, `docs/local/HANDOFF-PRIVATE.md`
(machine-specific notes; git-ignored). Everything else is linked from *Where things are documented*. The maintainer's job briefs and
review outputs are **outside the repo**, in `~/.claude/briefs/hotline/` (the 1.0 job briefs `1.0-*.md` and `1.0-plan-README.md` are there).

## What Hotline is

A Windows 11 app that takes over the Copilot key: a native WinUI 3 popup (acrylic, no WebView) that chats with the AI the
user picks: Claude Code CLI, OpenAI Codex CLI, Antigravity CLI (agy), Anthropic / Gemini / OpenAI-compatible APIs, or local
OpenAI-style servers. Everything is configured in files under `%USERPROFILE%\.hotline` and applies live.

## State (2026-11-12)

- **Released:** v0.6.2 on GitHub (`version.txt` says `0.6`). PR #60 (`fix/review-2026-11`, the review-fix wave below) is open and tested
  but **not merged**: the owner said to finish every finding from Fable, Astra, Sol and Sonnet first, so merging (which publishes
  v0.6.3 automatically) waits for that. Pushes to `main` publish the next patch release, signed with Hotline's self-signed certificate
  (`CN=pmarc14 Hotline Dev`, valid to 2036-10-12; [RELEASING.md](RELEASING.md)).
- **Microsoft Store:** **Hotline AI** (`9PMW5CR6G3BC`). Last uploaded bundle 0.3.0.0. A 0.6.3.0 bundle was built from the branch (Actions
  › Store, run 43164221390, artifact `hotline-store-0.6.3.0`, submit off) and is **not uploaded**; the owner uploads by hand and fills
  each language's listing from `store/listing.md` (the "additional languages" section is gone: one listing per package language).
  Rebuild it after the merge if the code changed. Otherwise no Store bundles until just short of 1.0 (owner, 2026-10-17).
- **Schema freeze (S1) is NOT done:** the first job of the road to 1.0; the plan and the owner's decisions (2026-11-12, reviewed with
  Fable) are in [simplification-plan.md](simplification-plan.md).
- **Tests:** 2,740 Core unit tests (`dotnet test`), `tests/smoke/smoke.ps1 -Install` and `hotline://selftest`; commands and the hand-test
  checklists: [TESTING.md](TESTING.md).
- **Reviews:** 2026-11-12 whole-codebase review by Fable 5.2 and Astra (bugs), Sol and Sonnet (design, testing, simplification, merged
  into [simplification-plan.md](simplification-plan.md)). Findings were confirmed by Sol, fixed in worktrees and combined in PR #60.
- **Review-fix wave in PR #60** (all with tests; App-only parts need the hand tests under *Open items*): attachments and links
  (send-time snapshot, destination guard, protocol auto-send rules, image size bound); clipboard (deny list of terminals, remote
  and VM windows, no Ctrl+C when the process is unknown, late-copy restore); flyout holds, keyboard move and resize of the panel
  (Ctrl+Shift+F10), tray keyboard activation, Narrator announcements for key capture, AltGr refused as a shortcut; one durable writer
  for settings, toolbar, mcp and history, failed-migration keeps the inline list, automatic updates queue while `settings.json` is
  unreadable; import safety (execution review dialog, `diagnostics.testLinks` default off, protocol voice blocked); backends (cut-off
  API answers noted, command-line length check, one argument splitter); release and scripts (signing identity required on push,
  least-privilege CI, winget tag validated); Settings window clamped to the work area; embedded tables and code re-fit when the
  panel resizes; foreground-window tracker (fixes "Capture window" from the Copilot key; see below); display and DPI change recovery;
  Esc auto-repeat no longer hides the panel while stopping an answer; tap-mode voice stops on blur and after 30 s of silence;
  Credential Locker errors logged (not shown as "no key"); non-recursive settings watchers; a newer `settings.json` or
  `toolbar.json` is never overwritten; narrow panel keeps the bottom row; test duplicates merged; new strings translated in 16 languages.
- **Security pass** (earlier, verified): UNC/device paths refused, history ids validated, response-size caps, chat-only isolation flags
  always applied, CLI output capped, `ShellOpen` limited, MCP `.exe` started directly, SVG `style` allow-listed, secrets redacted in logs.
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

- **Owner hand tests for PR #60** (on the desktop, not run by agents; [TESTING.md](TESTING.md) has the older checklists):
  Ctrl+Shift+F10 then Move or Resize with the arrow keys, Enter and Esc; **Capture window from the Copilot key** on a second monitor
  (expect `window-HHmmss.png` of the window you were in, no notice; minimize it first and expect the notice; the "selected text"
  chip from another app; panel opens on that window's monitor); Settings on a small or mixed-DPI screen (opens clamped, fully
  visible); the import confirmation dialog (Cancel is the default; a backup with programs shows the review); resizing the panel with
  long code or a wide table; unplug a monitor, change scale or the taskbar under an open pinned panel (display-change recovery,
  new in this wave: **never reproduced on real hardware**); hold Esc while an answer streams (stops, panel stays); tap the voice
  button and click another app (stops listening), stay silent 30 s (stops); tray icon via keyboard (Tab to the tray, Enter); a
  hand-edited newer `settings.json` (`"schemaVersion": 99`) is not overwritten and says so.
- **Findings from the 2026-11-12 reviews not fixed** (Fable and Astra bugs; the design ones are in [simplification-plan.md](simplification-plan.md)):
  - Done in PR #60: SHA-pinned actions, pinned and hash-checked `wingetcreate`, CI builds the MSIX, `build-msix.sh` signs by
    thumbprint (no password on a command line), timestamps and builds from a staged copy (F24). **Update-channel gating** is PR #63
    (stacked on #60, merge right after it): prereleases on push, Actions › Promote makes a release stable.
  - Toast text: owner chose a settings key, default title-only; part of S1. Test gaps: deferred to plan step 5 (Core decisions).
  - **`toolbar.json` lossless writer** and a version-aware save for every family: part of S1 (the 0.6.3 guard only refuses to overwrite).
  - **Test gaps:** `ClipboardCopy` and `SelectionReader` logic, `OnPanelKey` routing, attachment intake, flyout hold counting,
    packaging checks (the Core decisions in simplification-plan step 5 close most of them).
  - Smaller items from the Fable list not covered by any fix: check `docs/` and the Fable findings file
    (`~/AppData/Local/Temp/claude/.../scratchpad/fable-findings.md`, machine-local) if you need the full text; the reports of the Sol
    confirm phase are in `~/.agent-jobs/jobs/<id>/final.txt` (W1 979681f0, W2 14dac610, W3 c70e617d, W4 a2d6d989, W5 9943d62e), Sol
    design review 16df04d0, Sonnet review a920fbc2.
- **Fable 5.2 toolbar review T1-T8: fixed in PR #60** (tests first where Core holds them; T1 proven red then green by the self-test's
  drawn-width check). Hand-test: a shortcut save after editing toolbar.json by hand shows the "changed on disk" notice (T3); Settings
  refuses Stop = Ctrl+N with the conflict message (T5); with the model picker off the bar, Ctrl+M does nothing (T6, a change: hidden
  pickers no longer cycle).
- **Store images (2026-11-12): all 17 sets are done and reviewed.** The zh-Hans and zh-Hant panels were recaptured with the 0.6.3
  build; a hydrogen pass over all 102 images (plus my own look at the fixed ones) found: caption wrap and line height (fixed in
  `store/art/art.html`: French spaces before `: ; ! ?` are non-breaking, Devanagari lines get line-height 1.4), the slash scene
  showing leftover summary text (the summary demo's 8 s hold keeps running after its shot and streamed into the next scene; fixed:
  `scripts/make-store-media.sh` takes the summary last), and a stray "Ctrl+Shift+F10" tooltip on the message
  box (a real app bug from PR #60, fixed there: `KeyboardAcceleratorPlacementMode.Hidden`). The images are in
  `C:/Users/pmadd/Git/hotline/artifacts/store/<lang>/` (git-ignored); the panel captures are in `artifacts/store/panels/<lang>/`.
  Trap fixed: composing in another checkout silently made screenshots without the app panel when its `artifacts/store/panels/<lang>/`
  was missing; `scripts/lib/store-text.py` now stops with a message. The French sidebar clipping is fixed in PR #60 (the pane widens
  to its longest label: fr 238, uk 215, tr 216 DIP; the self-test checks all 17 languages). `fr/screenshot-4.png` still shows the old
  clipping: recapture with `bash scripts/make-store-panels.sh fr` and `bash scripts/make-store-localized.sh fr` before the next Store upload.
- **Interface strings:** all new keys are translated in 16 languages by model (Flash); no native review (see TRANSLATING.md).
- **Cleanup left on this PC:** agent worktrees and branches (`codex/*`, `agy/*`, under `Git/hotline.worktrees/review-fixes.worktrees`),
  the integration worktree `Git/hotline.worktrees/review-fixes`, and an empty locked folder `Git/hotline.worktrees/handoff-capture`.
- `PanelPlacement.Rescale` keeps a pinned panel's size in scaled units across monitors of different scale (hand check).
- **Fixed in PR #60, verify by hand: "Capture window" fell back to the whole screen.** The Copilot key moves focus to
  `CopilotKeyPressFocusFrame`, so the foreground handle was 0 at `ShowPopup`. Now `ForegroundWindowTracker` (`SetWinEventHook`,
  App) and `ForegroundWindowPolicy` (Core, tested) keep the last real window; `CaptureAsync` revalidates it. If it still fails, read
  the `toggle:` and `deactivated:` lines in `hotline.log`.
- **Store screenshots in every language** (owner request 2026-11-08; scripts written and the pieces tested, the full 17-language run
  not finished: the owner stopped it because the PC became slow and windows stuck). `scripts/make-store-panels.sh` sets
  `general.language` per language (settings.json backed up first and restored byte for byte, also on Ctrl+C or kill), restarts
  Hotline and runs `scripts/make-store-media.sh --panels-only`, which captures the real panel and Settings (AI connections page,
  `hotline://settings?page=Connections`) into `artifacts/store/panels/<lang>/`; `scripts/make-store-localized.sh` then composes
  `artifacts/store/<lang>/screenshot-1..4.png`, poster and box art with the captions from `store/listings/<lang>.md` and the
  tagline from `store/art/taglines.json` (the app's own `AppDescription` strings). The demo conversations are now interface text
  (`Strings.Demo_*`, translated in all 16 files), so the panels are fully localized. Logos have no text and are shared. The trailer
  is skipped (`store/video/storyboard.md` and `scripts/make-store-tour.sh` have the in-depth video ready; its caption pipeline was
  tested with `--dry-run`, the screen recording was not run).
  Lessons from the first runs (Argon review, verified): an edit to `scripts/lib/store-capture.sh` once dropped the exit trap, so
  every run left a full-screen Edge backdrop and a 70 MB temp folder behind, which made the PC stuck and slow. Now: the cleanup
  trap kills the backdrop by process id, the recorder, closes Settings and deletes the work folder (tested on exit and on kill);
  window handling is one small ctypes helper (`scripts/lib/win-helper.py`) instead of eight PowerShell `Add-Type` compiles per
  language (each starts `csc.exe`, which Defender scans); the backdrop is brought to the front by process (AttachThreadInput to
  the foreground thread, then checked), and the capture stops with a message if the screen is not the dark backdrop; panel and
  Settings pictures are cropped to the window's exact rectangle (DWM frame bounds), not to a diff against the backdrop; Hotline is
  stopped before settings.json is edited, and the run waits for the "interface language" log line instead of sleeping. Still open:
  Hotline has no quit link, so each language ends it with `taskkill /F` (a ghost tray icon until the mouse passes over it); a
  graceful quit would need a `hotline://quit` (not built). Before the long run: keep the PC awake (`hold`), plug in the power, and
  close other apps; run it when you will not use the PC for about 15 minutes. The panel's demo scene scrolls the answer back to the
  top after streaming (`PanelSelfTest.DemoAsync`), so the question and heading show.
- **Panel memory with a long chat** (owner request 2026-11-08; no change made yet). Measured on the 0.1.308 dev build with a big
  Antigravity answer open: Hotline.exe 311-322 MB working set / 209-218 MB private, handles flat at about 1,370, no growth while
  idle; the `agy.exe` child is a separate 275 MB. The WebView2 processes in the list belong to other apps. No baseline was taken, so
  the cost per message is unknown. Facts from the code: `MessagesPanel` is a plain `StackPanel` in one `ScrollViewer`
  (`PopupWindow.xaml`), the whole conversation is one selectable `RichTextBlock` (`Chat/TranscriptView.cs`) with tables as `Grid`s of
  `RichTextBlock` cells and code blocks as `NoWrap` boxes (`Chat/MarkdownRenderer.cs`), attachment images decode at display width
  (`AttachmentVisuals.cs`). Nothing is virtualized, so everything stays in the visual tree while it is scrolled off. The panel height
  is capped by `window.chatHeight` or `maxHeightPercent`, then it scrolls; it follows the bottom while streaming, with a jump-to-latest
  button.
  1. **Measure first** (read-only, no app changes): restart Hotline, note working set, private bytes and handles; open a new chat and
     note them; generate a very large answer (long text, many table rows, code, an image) and note them again; close and re-open the
     panel 20 times and compare handles. `dotnet-dump` is installed; `dotnet-gcdump` and `dotnet-counters` are not (dumps hold chat
     text and keys: write them to a scratch folder and delete them).
  2. **If the numbers justify it, release old messages**: past a size threshold, replace messages far above the viewport with a
     light "N older messages" stub that rebuilds from the stored text on scroll-up. Keep cross-message selection and copy, scroll
     position and the follow-bottom behaviour. `ItemsRepeater` virtualization is the larger option and loses one-drag selection
     across messages, so it is not the first choice.
- **Notices in one style, and they must survive resizing and several monitors** (owner request 2026-11-08). The target style is the
  in-panel `InfoBar` from `Chat/NoticeArea.cs` (info icon, text, close X; passing ones are gone after 10 s, at most three, tagged ones
  stay). Audit every message the user can see and move the odd ones into it:
  - Capture: `AttachmentPanel.CaptureAsync` shows a notice only when the target window is gone; when the previous foreground window,
    its monitor (`ScreenCapture.MonitorRect`, `DisplayArea.GetFromWindowId(..., Nearest)`) or the grab fails (`GrabBgra` throws
    "BitBlt failed"), the only message is the generic `Task_Failed` error from `Chat/UiTasks.cs`. Give the failures their own
    localized wording, such as "Couldn't pick the screen you were on" with the reason. `CaptureRegionAsync` has no notice at all:
    cancel (Esc) is silent and fine, but a failed grab or a monitor that went away should say so.
  - Other places: `TranscriptView.ShowError` (an error written inside the answer), `PopupWindow.SetStatus` (the status line; debug
    "Last key" text in `ActivationRouter`), the Windows toast in `Chat/AnswerNotifier.cs`, tray messages, Settings `InfoBar`s
    (`SettingsWindow.Backup.cs`, `Connections.cs`) and `ContentDialog` confirmations. Decide per case what is a notice and what must
    stay a dialog (destructive confirmations stay). Strings go through `Strings.resx` with the 16 translations
    ([TRANSLATING.md](TRANSLATING.md)).
  - Behaviour to check by hand and, where it can be Core-tested, test: the notice bar is inside the panel, so the panel must grow
    for it (and shrink back when it goes) without covering the box or the bar, hold its size when the owner has resized by hand
    (`_fixedPx`/`_fixedWidth` in `PopupWindow.xaml.cs`), wrap long text at narrow widths, and stay on the monitor the panel is on
    when the panel moves to another monitor or a different scale (`PanelPlacement.Rescale`); a notice raised while the panel is
    hidden or on the other monitor shows when it opens. Test with a capture failure on a second monitor at 100% and 200% scaling.

## Next work

### Road to 1.0

Owner decisions: everything below is in 1.0; the file formats are frozen from 1.0 (migrations, never breaking changes); the one
exception is Windows agent connectors verification, which moves to 1.1 if they cannot be tested. Work in this order:

1. **S1 schema freeze** (first, merged before the rest; the reviewed plan, owner decisions and order are in
   [simplification-plan.md](simplification-plan.md)): unknown keys survive saves; one `version` rule with migrations (no
   `schemaVersion` / `version` split); no rewrite on load; a connection file's name = its id; app state (`.first-run-shown`,
   `supportReminder`) in `state.json`, not in config; one backup policy in `backups\` (about 7 schemes today); 3 renames with aliases
   (`window.height` to `minHeight`, `backends` to `connections`, toolbar `items` to `bottom`); JSON schemas and frozen compatibility
   fixtures. Also decide `mcp.json` `env` secrets versus the Credential Locker rule. Notes: a toolbar edit in Settings drops unknown
   top-level keys and unknown nested fields (documented by the `Compat_*` tests in `SettingsPackageTests`); runtime shortcut conflict
   resolution must keep the requested value; Astra's recommendation: a version 3 boundary for shortcuts, one backup policy, unknown
   fields preserved.
2. After S1, in parallel (disjoint files): **E1** security leftovers (see *Deferred*); **F1** chat basics (edit/resend, regenerate,
   history search, pin, export, context meter); **D2** support bundle, first-run flow, accessibility; **P1** panel memory with long
   chats and notices in one style across resize and monitors (both in *Open items*).
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
