# Hotline — handoff for the next session

Read this first, then `CLAUDE.md` (working rules; local, git-ignored) and, if present, `docs/local/HANDOFF-PRIVATE.md` (machine-specific
notes; git-ignored). The design spec is `docs/superpowers/specs/2026-09-30-hotline-design.md`; plans are in
`docs/superpowers/plans/`.

## What Hotline is

A Windows 11 app that takes over the Copilot key: a native WinUI 3 popup (acrylic, no WebView) that chats with the AI
the user picks — Claude Code CLI, OpenAI Codex CLI, Antigravity CLI (agy), Anthropic / Gemini / OpenAI-compatible APIs, or local
OpenAI-style servers. Everything is configured in files under `%USERPROFILE%\.hotline` and applies live.

## State (2026-10-17)

- **Released:** GitHub **v0.3.0** (2026-10-17: PRs #32 and #33, below), before it v0.2.25 and v0.2.24. GitHub releases are normal releases signed with Hotline's long-lived self-signed
  certificate (`CN=pmarc14 Hotline Dev`, valid to 2036-10-12; repo secrets `HOTLINE_SIGNING_PFX_BASE64` /
  `_PASSWORD`; the owner keeps `certs/release/hotline-release.pfx` + password, git-ignored, and a backup in
  `Documents\Hotline-release-certificate\`): users trust `Hotline.cer` once, then `.appinstaller` updates work.
- **Microsoft Store** **Hotline AI** (Store ID `9PMW5CR6G3BC`, also `winget install 9PMW5CR6G3BC`): the last bundle
  built is **0.3.0.0** (2026-10-17, in Downloads). **No more Store bundles until just short of 1.0** (owner,
  2026-10-17); GitHub releases continue on every push to `main`. Uploads are by
  hand (no Partner Center automation, by the owner's choice): `scripts/build-msix.sh --store --arch all --version
  X.Y.Z.0 …` (docs/RELEASING.md), then upload with the 17 listings in `store/listings/`.
- **In v0.2.24** (#26, #27): accent colour, High contrast theme, Windows contrast/transparency followed, code blocks
  scroll sideways, the transcript follows answers until you scroll, the panel expands to half after the first
  message and then with the text, the history list (🕘 / Ctrl+↑ / `/resume`), a key binder for the extra hotkey,
  quick-action defaults/restore/rename/translated descriptions.
- **In v0.2.25** (branch `hotfix-history-list`):
  - Fix: the history list opened from Ctrl+↑ or `/resume` closed at once (a window resize closes a flyout: focus
    leaving the message box changed its outline thickness, which resized the panel). The outline now changes colour
    only, and `PopupWindow.HoldResize()` (taken only by the list) defers resizes while it's open.
  - Fix: clicking or pressing Enter on a chat in the list did nothing (`ItemClick` reports a directly added
    `ListViewItem`'s *content*); the same bug in the `/` suggestion list.
  - `/resume` + space opens the list and the search continues there (`chat.resumeWhileTyping`).
  - Reopened CLI chats show their earlier messages from the CLI's own history (`CliTranscripts`;
    `chat.cliTranscripts`). `chat.historyListSize` sets how many chats the list reads. All three are settings.json only.
- **In v0.3.0** (PR #32 `codex-feedback` + PR #33 `panel-placement`, merged 2026-10-17):
  - Thinking indicator (the cursor spins while no text comes; `chat.thinkingIndicator`, `chat.thinkingDelayMs`).
  - Codex lists its models (`CodexModels`: `codex app-server` `model/list`, else `~/.codex/models_cache.json`, else a
    curated list).
  - No built-in agy connection (new installs ask; every connection can be removed; schema v12 keeps it for old files).
  - CLI chats run in the home folder; scratch files in `~/.hotline/scratch` (agy's agent via `--add-dir`).
  - Continue in: the chat's own CLI on top, the others under More…; a Continue in toolbar button.
  - Settings › Advanced › Reset settings / Reset everything (backed up), hidden defaults copy in `~/.hotline/.defaults`.
  - The panel: `window.anchor` (monitor, x, y), `width`, `chatHeight`; the chat grows up or down from a fixed message
    bar (`growDirection`, auto = up unless no room above); **Alt grips** move and resize it (a resize fixes the height
    for that opening; it can go back down to the bar alone); the grip's menu saves the spot as default; a moved spot
    is kept only while pinned (forgotten on unpin, clicking into another app, restart). Schema v14.
  - Button rows: toolbar.json `top` (above the message box) and `items` (below), each with `left` and `right`; the owner's layout and
    `acrylicThin` are the defaults.
  - Open/close sounds (off by default): Windows sound events or .wav files in `~/.hotline/sounds`, preloaded media
    players. The generated `hotline.wav` (`src/Hotline.App/Assets/Sounds/hotline.wav`) ships as the default sound;
    sounds stay off by default (owner decision, 2026-10-17).
  - Settings › Guide: every feature in 13 sections, 17 languages (page names checked per language by Gemini Flash).
  - Fixes: the layout cycle that stopped the panel drawing for good; the window frame now counted in the panel's
    height (the bottom padding and move grip were cut off); no empty band beside an empty panel; the newest text
    stays in view; the panel never left off-screen by the self-test; 26H2's Copilot-key focus frame (below).
  - **Not yet hand-tested by the owner:** real Alt-drags across monitors with mixed DPI (on the desktop).
- **In 0.4** (PR #9 `windows-26h2`, not merged yet; `version.txt` says `0.4`):
  - **Button rows v2.** `toolbar.json` has four groups (`top` / `items`, each with `left` / `right`), a `style` (size compact |
    normal | wide, outline, filled), per-item `overrides`, and custom `buttons` that run a quick action (by name) or insert or
    send a text. Everything read goes through `ToolbarModel.Resolve` (Core: never throws, per-field fallback, clamps);
    everything edited through `ToolbarEditor` (Core); `ToolbarFile` loads and saves (a v1 file is migrated and the old one kept
    as `toolbar.json.old`; a v2 file with warnings, or a version above 2, is never rewritten; an unreadable file is left alone
    and the defaults run). `ToolbarPlan` with `ToolbarLayout.Fit` / `FitRow` decide what shows and what goes into the ⋯ menu;
    `ToolbarRenderer` (App) builds the rows. Settings › Window › Button rows is a four-corner editor: drag and drop, arrow
    buttons, Alt / Ctrl + arrow, a "Not shown" strip, a look flyout per item and a custom button dialog
    (`ToolbarQuadrantEditor*.cs`; AutomationIds are `bar-*` and `quadrant-*`).
  - Schema v14: Hotline's own Right Ctrl mode is gone (Windows' Copilot-key remap replaces it, see `docs/copilot-key.md`). The
    upgrade archives `settings.json` as `settings.json.old` (next free `.old2` …), leaves connection files untouched, and moves
    `minWidth` 600 → 680 when it was exactly 600. Default panel width 680. The generated `hotline.wav` ships as the default
    sound; sounds stay off by default.
  - Reset settings also restores `toolbar.json`; a hidden `.defaults/toolbar.json` is kept like the settings copy.
  - 26H2: agent connectors are still a Windows preview. This PC (26300.9711) has the Windows `Mcp` registry key but no
    `odr.exe`, so This PC says "preview, not delivered" and no longer "needs a build". Phi Silica / the Windows AI APIs and
    `odr.exe` discovery were researched and **deferred, maybe never** (owner, 2026-10-17: Hotline's on-device route is a
    local API connection). No new 26H2 API was worth adopting.
  - **Later in 0.4 (after the first PR text):**
    - Each button row has a **centre group** between left and right (six areas; `center` in `toolbar.json` is optional; the centre
      drops into the ⋯ menu before the left group does).
    - **Sizes:** `compact` = ½, `normal` = 1×, `wide` = 2× of one base (`ToolbarPlan.BaseWidth/BaseHeight/BaseFont`, now 40 × 36,
      icon 16; compact never under 20 × 20). Custom buttons never exceed 80 × 80 (icon 22). The bar's size choice shows
      **Custom** by itself while some button has its own size. `outline` is drawn in the accent colour (or `outlineColor`).
    - **Custom buttons can use a picture** (`iconFile`, a PNG ≤ 256 KB, 16-256 px, stored as `icons/<id>.png`; `CustomIcons` validates
      the header only; the renderer decodes the checked bytes and falls back to the glyph).
    - **Panel notice** when `toolbar.json` can't be read or parts were ignored (`ConfigProblems`, once per distinct set).
    - First-run card always lists Claude Code, Codex and Antigravity (missing ones dimmed, with a hint line).
    - Quick actions page: two-line previews (full text in a tooltip), clearer intro and button names.
    - **Security pass** (Argon + Flash multi-model audit, findings verified against the code, fixes with tests): UNC/device paths refused
      for images and stored working directories (`SafeDirectory`, `AppActionRequest.IsSafeLocalImagePath`; an image comes only from
      Windows' own App Actions inputs); history ids validated (`HistoryIds`); response-size caps and clear redirect errors for the
      API clients; chat-only isolation flags always applied and tool pre-approval flags dropped from user launch flags; CLI output
      capped; `ShellOpen` opens only Hotline's own files; MCP `.exe` servers start directly and `cmd.exe` is refused for unsafe
      characters; SVG `style` allow-listed, depth/size caps for SVG and markdown (Markdig throws past ~128 levels: caught);
      `mcp.json` duplicate keys and backups (`BackupFiles.Prune`, newest ten); release workflow passes expressions through `env:`;
      logs are cleaned (`LogText`: line breaks indented, secrets redacted, capped, link queries never logged).
      **Deferred on purpose:** a plain `hotline://key` link from a web page can still read the selection (the real key path can't be told
      apart without risking it); Markdig can take ~15 s on adversarial nested input (a UI freeze, not a crash); MCP `readOnlyHint` is
      trusted from the server (recommendation: only for servers marked trusted); unsigned inner packages in a signed bundle (Azure flow).
  - **Not hand-tested yet (owner):** mouse drags (inside and between corners, to "Not shown", across monitors and DPI), the ⋯
    menu opening Prompt / Continue in and surviving a `toolbar.json` change, outline / filled / sizes in light, dark and
    contrast themes, 24 custom buttons at 320 DIP, and that the Copilot key still arrives as before after the 26H2 upgrade
    with the "Customize Copilot key" choice of Hotline surviving it (TESTING.md has the list).
- **Open PR #29** (`continue-in-targets`, ships as **0.5**): Continue in to web pages, Open WebUI (a real chat) and apps,
  from a shared list or per connection; built from `docs/superpowers/plans/2026-10-15-continue-in-targets.md`, reviewed,
  998 tests. Waits for 0.4 (26H2); bump `version.txt` when it merges. Hand-test list:
  1. llama.cpp web UI (default target of a Local connection): opens with the conversation as the first message.
  2. Open WebUI (if run): Settings › Chat › Continue in › Add › Open WebUI, address + API key, **Test** shows the model
     count; continuing creates a real Open WebUI chat with every message.
  3. LM Studio (if installed): opens with the whole chat on the clipboard.
  4. ChatGPT / Claude ready-made entries: the site opens with the chat in its message box.
  5. Per connection: *None* on one connection and *Own list* on another; each chat shows only its own targets.
  6. A very long chat with a web target: the notice that only the last message went and the chat is on the clipboard.
  7. A wrong Open WebUI key or address: a clear "Couldn't open…" notice.
  A Gemini API connection has no default target (Gemini's site takes no message in its link); add one to test it.
- Tests: ~1043 Core unit tests; `tests/smoke/smoke.ps1 -Install` (the owner sometimes asks to skip it); the
  off-screen self-test `hotline://selftest` builds every settings page.

## Next work

1. **Next: merge PR #9 (0.4)** once the owner has hand-tested the list above.
2. Then PR #29 (Continue in: web pages, Open WebUI, apps; merge `main` into it first: on 2026-10-17 that conflicted in
   20 files — the 17 `Strings*.resx` (both sides added strings: keep both), `App.xaml.cs`, `SettingsWindow.xaml.cs`
   and `docs/HANDOFF.md`, so it was left for that chat) and region capture on multi-monitor / mixed DPI plus the panel's hand test
   (on the owner's desktop: clone, dev certificate (README), `scripts/install.sh`). Version numbers when ordered.
   - **Left from the whole-codebase review (2026-10-17, Argon, 48 findings; the real Critical/Important ones are fixed
     in PR "after 0.3")** — check these on the desktop / when touching the code:
     - Mixed-DPI drags: a drop across monitors may keep the old monitor's width in pixels; a live move between a
       top/bottom pair of different DPIs can jitter; a tall auto-direction panel dragged to the top edge may not flip
       to growing down; `FramePx` is read before the panel moves to its monitor (one show may be a few px short).
     - The window frame on the first open: `_barDip` is 0 until the first layout, so a grow-down panel's bar line
       can be set from the fallback height.
     - Settings: a `settings.json` without `schemaVersion` is treated as current (skips migrations); connection ids are
       case-sensitive in a few places (`defaultBackend`); the Language box is blank for `"en-us"` (case).
     - Minor: scheme-less endpoints throw a raw error; MCP `cwd`/`displayName` of the wrong JSON type drop the file's
       other servers; `MCP approval keys differ by
       case; SVG `style` with CSS escapes; code-fence parity in `MarkdownModel`; sounds don't reload a replaced .wav;
       the fallback hotkey isn't re-registered on a settings change; transcript spinner not stopped on Clear.
3. **Brainstorm** anything else wanted before 1.0, then decide on it. (The Codex live check was done by the owner on
   2026-10-16; its feedback is 0.3.)
4. **Version 1.0** when: hand tests done, multi-monitor capture OK, 26H2 merged, a Store update certified with all
   languages, and settings files staying compatible from then on.
5. No native-speaker reviews or RTL planned. Paid signing (winget-pkgs) only if Hotline gets popular.
6. Owner preference: anything configurable should be in settings.json even when the Settings window doesn't show it.

## Versions (toward 1.0)

- `version.txt` holds major.minor (now `0.4`). Every code push to `main` publishes `v<major.minor>.<patch>`, the patch
  being the next number in that line from the existing tags (`scripts/next-version.sh`, used by release.yml): bumping
  to `0.3` starts at **v0.3.0**, then v0.3.1, … (until 2026-10-16 the patch
  was the GitHub run number, which is why 0.2 reached .25). The Store package is the same version plus `.0`.
- **The Store only needs each submission's version, compared as a whole (left to right), to be higher than the last**,
  and the fourth part to be `0`: `0.3.0.0` follows `0.2.25.0` fine.
- **The plan:**
  - **0.3** (released 2026-10-17) — the Codex-test feedback and the movable, resizable panel.
  - **0.4** — button rows v2, 26H2 wording, Right Ctrl mode dropped, width 680 (#9; built, awaiting the owner's hand test).
  - **0.5** — everything else left: Continue in (PR #29, now with a ready-made Gemini entry), the multi-monitor /
    mixed-DPI region capture fix, the panel's desktop hand test, the shipped sound. Feature complete for 1.0; after
    that only fixes, polish and one Store update (the first since 0.3.0.0) certified with all 17 languages.
  - **1.0** — when that Store update is through and nothing that breaks existing settings files is pending. From 1.0
    on, settings files must keep working across updates (migrations, never breaking changes).

## Architecture map

- `src/Hotline.Core` (net10.0, no UI, unit-tested):
  - `Activation/` key events, URI parsing, dedupe, planner (`hotline://key|tray|settings|demo|selftest`).
  - `Backends/` `ConnectionTypes`, `ConnectionEditor`, `ModelCatalog`/`ModelFamilies`, `Secrets`, `BackendCatalog`
    (factory + cache), `CliHandoff` (Continue in), `Agy/` (stream-json CLI, workspace, permissions, `AgyProjects`),
    `ClaudeCode/` (stream-json CLI), `Codex/` (`codex exec --json`, one process per turn); the three CLIs resume a chat's
    own conversation (`--conversation` / `--resume` / `exec resume`),
    `Api/` (`ApiCommon` SSE/errors/https rule, `OpenAiBackend`, `GeminiBackend`, `AnthropicBackend`).
  - `Chat/` `ChatController` (turns, cancel, retry, resume; records each chat's `CliSession` and hands it back to an
    `ICliSessionBackend`), `HistoryStore` (JSONL transcripts + `<id>.meta.json` records, `Recent`), `PromptLibrary`,
    `QuickActions`, `OcrPlan`, `VoiceText`, attachments (`SelectionAttachment`).
  - `Settings/` `HotlineSettings` (schema v11), `SettingsStore` (settings.json + `connections/*.json`, migrations,
    side-effect-free `TryRead`), `SettingsService` (live Update/Reload with merge-by-id), `SettingsSchema`.
  - `Text/` `MarkdownModel` (Markdig → block model; math, SVG, task lists), `LatexText`, `SvgSanitizer`.
  - `Tools/` MCP (`McpToolHost`, `McpConfig`, `McpProcess` + `StdioMcpSession`: process start, stderr throttle). `Windowing/` geometry (`PopupGeometry` GrowUp/GrowDown, `PanelPlacement`: anchor, direction, clamp, resize, rescale), `ToolbarLayout`, `ToolbarModel`/`ToolbarFile`/`ToolbarEditor` (toolbar.json v2; `ToolbarEditorView` feeds the Settings quadrant editor), `Sounds` (what may play). `Processes/` line processes.
- `src/Hotline.App` (WinUI 3, packaged MSIX, full trust):
  - `App.xaml.cs` composition root; `PopupWindow` (sizing, eased growth, focus, off-screen mode); `ActivationRouter`.
  - `Chat/ChatPresenter*.cs` (single selectable `RichTextBlock` transcript, attachments, recent chats, self-test,
    demo), `MarkdownRenderer`, `ProviderBar`, `CliRunner`, `PasswordVaultSecretStore`.
  - `Settings/` settings window (pages generated from `SettingsSchema`, connections, prompts, agy permissions).
  - `Interop/` tray, Copilot fast path, hotkey, job object, Win32, `SelectionReader` (UIA + clipboard fallback).
  - `Chat/` one job per class: `ChatPresenter` (coordinator, send flow), `Composer`, `AttachmentPanel`,
    `AttachmentVisuals`, `TranscriptView`, `NoticeArea`, `VoiceInput` + `WindowsSpeech`, `RecentChatsMenu`,
    `PanelSelfTest`, `FirstRunNote`, `ConnectionChooser` (first run), `SupportNote`, `AnswerNotifier`, `ContinueIn`,
    `PanelTheme`, `UiTasks`, `OcrReader`, `PanelLayout` (rows for up/down), `PanelGrips` (Alt move/resize), `Interop/SoundPlayer`. `SelfTestLinks` (hotline://selftest).

## Hard-won facts (don't relearn these)

- **Tool quirks (2026-10-22):** a shell heredoc given to the Bash tool collapses backslashes (`\n` becomes a real newline, `\b` a
  backspace): write C#, resx or script text that contains backslashes with the Write/Edit tools, or build the character with
  `chr(92)`. `rm -f "$P"/*` is refused unless written `"${P:?}"/*`. PowerShell 5.1: `@($null)` is a one-item array.
- **agy as a reviewer:** give it a prompt that lists absolute file paths and says "use only view_file on these"; it reads whole
  folders that way (no 32k command-line limit, nothing pasted, no Defender false positive on hook code). Headless agy can't write
  files: `write_file(<path>/**)` allow rules did not match and a blanket rule was refused as self-modification; ask the owner.

- **A window resize closes an open WinUI flyout** (including `ShouldConstrainToRootBounds=false` ones). The panel
  sizes itself to its content, so anything that changes layout while a flyout is open (even a 0.5 px border change on
  focus) closes it. Use `PopupWindow.HoldResize()` around flyouts; keep focus styling layout-neutral.
- **`ListView.ItemClick` with `ListViewItem`s added directly** reports the item's *Content*, not the `ListViewItem`:
  put the `Tag` on the content too and match `FrameworkElement { Tag: … }`. `SelectedItem` is the `ListViewItem`.
- **CLI histories:** Claude Code `~/.claude/projects/<folder>/<session>.jsonl` (type user/assistant, `message.content`
  string or blocks; skip `isMeta`, `isSidechain`, `tool_result`); Codex `~/.codex/sessions/YYYY/MM/DD/rollout-*-<thread>.jsonl`
  (`response_item` › `message`; skip `<environment_context>`-style turns); agy
  `~/.gemini/antigravity-cli/conversations/<id>.db`, SQLite table `steps`: type 14 = user (protobuf field 19›2),
  15 = answer (20›1), 101 = system notice. No agy command exports a conversation.
- **OpenSSL on Windows writes `\r\n`:** strip `\r` too when making password files (`tr -d '\r\n'`), or Windows and
  signtool see a different password than OpenSSL ("The specified PFX password is not correct").
- **agy model quotas:** Opus/Sonnet via agy can run out for the week; Gemini 4 Argon (`gemini-4-argon-high`) is the
  deep-review fallback, Gemini 3.8 Flash the everyday one.
- **A `LayoutCycleException` stops WinUI drawing that window for good** (it keeps running and toggling, but every
  open is blank; RenderTargetBitmap/OCR then never complete). The "known first-start layout cycle" of earlier versions
  was this: the pickers sized themselves from their own row's width inside its SizeChanged, shrinking 2 px per pass
  when the buttons didn't fit (the self-test's 350-DIP off-screen window at 200 %). Rules: never change sizes inside a
  size event; queue the work after layout (DispatcherQueue, coalesced) and set values only when they change; measure
  from a container whose size doesn't depend on what you set. `diagnostics`: WinUI's
  `DebugSettings.LayoutCycleTracingLevel = High` writes the cycle to OutputDebugString (catch it with a DBWIN listener;
  see the 2026-10-17 session) and names the element.
- **Windows 11 26H2 gives focus to Explorer's `CopilotKeyPressFocusFrame` on each Copilot key press**, ~0.1 s before
  Hotline's toggle arrives. A panel still showing when the toggle hid it (pinned, or kept because Hotline's own
  Settings window took focus) made Explorer play the Windows warning sound, and the key's keystrokes reached a focused
  pin button and unpinned it. Now: any open panel closes when that frame takes focus (the toggle that follows counts
  as that close, within the 300-ms guard), bar buttons never keep keyboard focus, the frame is a shell surface, and the
  log names who takes focus on each deactivation (`deactivated: focus went to …`).
- **The panel's window rectangle includes a frame** (`AppWindow.Size` − `ClientSize`: 13 px tall at 200 %). Sizing the
  window to the content height cut the bottom padding off, and with it the move grip (fixed 2026-10-17: `FramePx`).
  The open check logs `panel content cut off` if it ever happens again. `hotline://grips?show=1|0` shows the Alt grips
  without a keystroke (screenshots, tests).
- **Before 0.3, a self-test that hung left the panel parked at (-32000, -32000)**, invisible on every later open. A
  real open now always ends off-screen mode. When "nothing appears", check the window rect first (`EnumWindows`; a
  non-DPI-aware PowerShell reports logical pixels: -16000 at 200 %).
- **agy custom agents are found in the workspace, including `--add-dir` folders**: `agy --add-dir <dir> --agent
  hotline` works from any folder (verified 2026-10-16). An unknown `--project <name>` is silently ignored (no project
  is made); `--new-project` names the project after the folder agy runs in.
- **PowerShell 5.1 reads `0x80000003` as a negative Int32**: pass `[uint32]2147483651` to `SetThreadExecutionState`
  (a keep-awake loop that silently never worked). Power requests (`PowerCreateRequest`/`PowerSetRequest`) hold
  Modern Standby off more reliably.
- **Python edit scripts from bash heredocs:** a non-raw Python string turns `\r`, `\a`, `\n` in Windows paths into control
  characters; use raw strings or `chr(92)`.
- Antigravity reviews: the agy MCP tool can return nothing and can't pick a model. What works: `agy --model
  <claude-opus-5-5-high | gemini-3.8-flash-high> -p "<prompt>"` with the code pasted INTO the prompt and "do not use any
  tools" (headless agy aborts silently when it tries a shell command). Windows caps the command line at ~32k chars, so
  split the review into chunks (a script did 5 parts in parallel: tool host, Anthropic, OpenAI+Gemini, app wiring,
  capture).
- **WinUI `TextHighlighter` on a `RichTextBlock` crashes natively when drawn** (AV in Microsoft.UI.Xaml.dll). Removed;
  don't reintroduce. Reproduced only when actually rendered → `hotline://selftest` renders off-screen for that reason.
- **The TextBox caret takes the inverse colour *and the opacity* of the box background** (WinUI #6498/#8207/#9005):
  the composer is a solid surface on purpose.
- **A `UIElement` can't have two parents** — that's what broke the settings window once; the self-test now builds every
  settings page.
- **agy:** effort is part of the model id (`gemini-3.8-flash-high`; `max` is invalid for Flash); headless mode denies
  anything not in `permissions.allow`; the agy MCP tool returns empty output when its tools are denied (give it
  file-only tasks, e.g. a diff written to `.superpowers/review/`).
- **Claude Code:** `--bare` never reads the OAuth login — chat-only isolation uses `--tools "" --setting-sources ""
  --strict-mcp-config`; `--no-session-persistence` keeps its resume list clean.
- **Copilot key:** needs package identity (hence MSIX; see docs/RELEASING.md); cold start arrives as
  `ProtocolForResults`; the fast path is a `WM_APP+1` message to the registered window.
- **Windows on-device agent registry:** connectors run only via `odr.exe` (build 26220.7262+).
- **Scripting edits from bash heredocs mangles `\n`** in C# strings — write Python edit scripts to a file with raw
  strings, or use the Edit tool.
- **Clipboard:** `OpenClipboard(NULL)` + `EmptyClipboard` leaves the clipboard ownerless and every `SetClipboardData`
  then fails — restoring needs a real owner window (`ClipboardOwner`: message-only window on its own pumping thread,
  so other apps' clipboard messages never wait on the UI thread). Never send Ctrl+C unless UIA confirmed "no text
  pattern": an empty selection copies a whole line (editors) or sends SIGINT (terminals).
- **agy reviews:** prompts over ~32k chars fail with "Argument list too long"; start the prompt with "answer from the
  text below only, do not call any tool" or Gemini sometimes tries a tool and headless mode aborts; Opus via agy hits
  a short per-user quota after a few big reviews (wait ~5 min and rerun).
- **Defender vs. agy reviews:** pasting keyboard-hook / input-injection code (SetWindowsHookEx, SendInput) into an
  agy command line got the call blocked as `Trojan:Win32/ClickFix` (a false positive on the command line). Review
  such code without putting it on a command line.
- **MCP SDK 2.2.0:** `StreamClientTransport(serverInput, serverOutput)` — the first stream is the one written to
  (the server's stdin).

## Process that worked

Branch from GitHub `main` per feature → TDD in Core → build → `smoke.ps1 -Install` → commit + push → adversarial review
(Antigravity via the agy MCP tool with a diff file, or a Sonnet subagent) → fix confirmed findings → PR/merge.

**UI changes are verified by eye, not by window rectangles** (2026-10-17: a rect check passed while nothing was drawn):
- `hotline://selftest` fails loudly on a layout cycle, too few frames or an OCR timeout, renders narrow/wide/both
  directions/both button-row layouts, and saves PNGs to `~/.hotline/logs/selftest/`.
- Real screenshots: a DPI-aware `CopyFromScreen` of the panel (open with `hotline://key?state=Tap` or `hotline://demo`,
  then close it); Settings pages via UI Automation (select the nav item, capture, close the window).
- Put both in one folder and run `agy --model gemini-4-argon-high` there with a prompt describing each state; it reads
  the images with view_file. Text checks (translations, page names in the guide) go to `gemini-3.8-flash-high`.
- **Clean up after testing:** restore any settings.json/toolbar.json you changed (back them up first), close every
  panel/window you opened, and finally restart Hotline (`taskkill /IM Hotline.exe /F`, then `hotline://tray`) so
  nothing a test did stays in the owner's running instance.
- New features get a section in Settings › Guide (Core `Settings/Guide.cs` + `Guide_*` strings in all languages).
