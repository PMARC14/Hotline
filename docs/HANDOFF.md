# Hotline — handoff for the next session

Read this first, then `CLAUDE.md` (working rules; local, git-ignored) and, if present, `docs/local/HANDOFF-PRIVATE.md`
(machine-specific notes; git-ignored). File formats and every setting: [configuration.md](configuration.md) (the single
reference; this file does not repeat it). Hand tests: [TESTING.md](TESTING.md). Design spec:
`docs/superpowers/specs/2026-09-30-hotline-design.md`; plans in `docs/superpowers/plans/`.

## What Hotline is

A Windows 11 app that takes over the Copilot key: a native WinUI 3 popup (acrylic, no WebView) that chats with the AI the
user picks: Claude Code CLI, OpenAI Codex CLI, Antigravity CLI (agy), Anthropic / Gemini / OpenAI-compatible APIs, or local
OpenAI-style servers. Everything is configured in files under `%USERPROFILE%\.hotline` and applies live.

## State (2026-10-22)

- **Released:** GitHub **v0.3.0** (2026-10-17), before it v0.2.25 and v0.2.24. Releases are signed with Hotline's long-lived
  self-signed certificate (`CN=pmarc14 Hotline Dev`, valid to 2036-10-12; repo secrets `HOTLINE_SIGNING_PFX_BASE64` /
  `_PASSWORD`; the owner keeps `certs/release/hotline-release.pfx` + password, git-ignored, and a backup in
  `Documents\Hotline-release-certificate\`): users trust `Hotline.cer` once, then `.appinstaller` updates work.
- **Microsoft Store** **Hotline AI** (`9PMW5CR6G3BC`): last bundle **0.3.0.0**. **No more Store bundles until just short of
  1.0** (owner, 2026-10-17); GitHub releases continue on every push to `main`. Uploads are by hand (owner's choice):
  `scripts/build-msix.sh --store --arch all --version X.Y.Z.0 …` ([RELEASING.md](RELEASING.md)), then the 17 listings in
  `store/listings/`.
- **0.3 in short:** history list (`HoldResize`), CLI chats resumable and shown from the CLI's own history
  (`CliTranscripts`), Codex model list (`CodexModels`), no built-in agy connection, CLIs run in the home folder (scratch in
  `~/.hotline/scratch`), Continue in, Reset settings / everything (hidden copy in `.defaults`), movable and resizable panel
  (`window.anchor`, Alt grips, `PanelPlacement`), open/close sounds, thinking indicator, Settings › Guide in 17 languages.
- **0.4 = PR #9 `windows-26h2` (not merged; `version.txt` says `0.4`).** Each item below is implemented:
  - **Button rows v2** (`toolbar.json` version 2; format in [configuration.md](configuration.md)). Two rows (`top`, `items`),
    each with left / center / right groups. `style` compact / normal / wide = ½ / 1× / 2× of a 40 × 36 **cell** that includes its
    own 1 DIP inset (`ToolbarPlan.CellGap` 0, `CellInset` 1, `Inner(cell)`); outline colour; per-item overrides; up to 24 custom
    buttons, optionally with a PNG `iconFile` (`CustomIcons`: 16-256 px, <= 256 KB, copied to `icons\<id>.png`). Pickers (effort,
    model, provider) are three independent items with `span` 1-8 cells (floors 56 / 84 / 72 outer width; they shrink, then hide in
    the order effort, provider, model). The right group overflows from its left end (F4). The `more` item and the `menu` key make a
    user-filled "..." box; the row's automatic overflow merges into it (`ToolbarPlan.Stays`, `alwaysOverflow`,
    `ToolbarEditor.FillMenuFromOverflow`; editor ids `quadrant-menu`, `bar-menu-<id>`, `bar-fill-menu`).
  - Read through `ToolbarModel.Resolve` (never throws; per-field fallback; warnings go through `ConfigProblems` to a panel
    notice), edited through `ToolbarEditor`/`ToolbarEditorView` (Core), loaded and saved by `ToolbarFile`, laid out by
    `ToolbarPlan`/`ToolbarLayout`, drawn by `ToolbarRenderer` (App). The Settings editor (`ToolbarQuadrantEditor*.cs`) has
    drag, arrows, hide, span and the custom-button dialog. A v1 file is **not** converted any more (defaults + warning;
    `ToolbarMigration` is gone). Outline colour defaults to the panel theme's accent (Hotline purple unless Windows matching is on).
  - **Reference layouts** in `ToolbarProfiles`, drawn by self-test steps P01..P33 (PNGs in `~/.hotline/logs/selftest/`).
  - **Defaults:** the owner's layout is `ToolbarModel.Defaults`; `widthPercent` 25, `maxHeightPercent` 60, panel padding 10,
    `minWidth` 460 = `ToolbarPlan.WidthForButtons(11)` (11 cells + 2 × 10); new `window.chatWidthPercent` (0 = no growth) and
    `window.chatWidth`; a hand-set `window.width` overrides growth. Schema v14.
  - **Settings files are read field by field** (`LenientReader`; `SettingsStore.SettingsProblems` / `SettingsError`; notice tag
    `settings-problems`; Advanced-page InfoBar). `Deserialize<HotlineSettings>` is gone from the read path. Old settings.json
    versions are no longer migrated or archived (no `settings.json.old`; `FileArchive` remains for the Settings save buttons;
    `.bak` copies remain; the inline `chat.backends` split into connection files is not version-gated). A syntax error gives a `.bad` copy.
  - **Export/import:** Settings › Backup; `SettingsPackage` (zip, manifest format 1; categories Settings / Toolbar / Mcp / Sounds /
    Connections / Actions / Prompts; limits 400 entries, 8 MB per entry, 64 MB total; zip-slip rules; import never deletes and
    backs replaced files up to `backups\import-<time>`; API keys never exported).
  - Hotline's own Right Ctrl mode is gone (Windows' Copilot-key remap replaces it, [copilot-key.md](copilot-key.md)).
  - 26H2: agent connectors are still a Windows preview (This PC says "preview, not delivered" here: the `Mcp` registry key but
    no `odr.exe`). Phi Silica / Windows AI APIs / `odr.exe` discovery: **deferred, maybe never** (Hotline's on-device route is a
    local API connection).
  - **Security pass** (multi-model audit, verified, fixed with tests): UNC/device paths refused (`SafeDirectory`,
    `AppActionRequest.IsSafeLocalImagePath`), history ids validated (`HistoryIds`), response-size caps, chat-only isolation
    flags always applied and tool pre-approval flags dropped from launch flags, CLI output capped, `ShellOpen` limited to
    Hotline's own files, MCP `.exe` started directly and `cmd.exe` refused for unsafe characters, SVG `style` allow-listed with
    depth/size caps, `mcp.json` duplicate keys and `BackupFiles.Prune` (newest ten), `env:` in the release workflow, `LogText`
    (secrets redacted, capped, link queries never logged).
  - **Review fixes X1-X4 (merged; `git log --grep "Review"`):** `App.xaml.cs` and `SettingsWindow` split into partial files, big
    Core classes split one concern per file (`ApiCommon`, `McpConfig`, `McpProcess`, `BoundedLineReader`, `CliTranscripts` readers),
    SSE final event at EOF, odd JSON shapes in Gemini/OpenAI streams, Claude Code start-up pipe errors, locked files in history
    prune and memory append, non-string `mcp.json` fields, prompt `Default` name case, clipboard memory, Continue in failures,
    nesting guard and owned ids on settings reload, the xhigh/ultra effort mapping.
  - **Tests:** ~2545 Core tests, `tests/smoke/smoke.ps1 -Install` (the owner sometimes skips it), `hotline://selftest` (builds every
    settings page). The test suite was consolidated once (~10%: most reviewer-proposed deletions were rejected); [TESTING.md](TESTING.md)
    has the hand-test list. The coverage tooling is not in the repo.
  - **Not hand-tested yet (owner):** mouse drags (inside and between groups, to "Not shown", across monitors and DPI), the "..." menu
    opening Prompt / Continue in and surviving a `toolbar.json` change, outline / filled / sizes in light, dark and contrast themes,
    24 custom buttons at 320 DIP, Alt-drags across mixed-DPI monitors, and the Copilot key still arriving after the 26H2 upgrade.
- **PR #29 `continue-in-targets` / `personal/gemini-web` (ships as 0.5):** Continue in to web pages, Open WebUI (a real chat) and
  apps, from a shared list or per connection (plan `2026-10-15-continue-in-targets.md`). Merge `main` into it first: it conflicts
  in the 17 `Strings*.resx` (keep both sides), `App.xaml.cs`, `SettingsWindow.xaml.cs` and this file. Hand tests:
  1. llama.cpp web UI (a Local connection's default target) opens with the conversation as the first message.
  2. Open WebUI: Settings › Chat › Continue in › Add › Open WebUI, address + key, **Test** shows the model count; continuing creates
     a real chat with every message. A wrong key or address gives a clear "Couldn't open…" notice.
  3. LM Studio: opens with the whole chat on the clipboard. ChatGPT / Claude entries: the site opens with the chat in its box.
  4. *None* on one connection and *Own list* on another: each chat shows only its own targets.
  5. A very long chat with a web target: the notice that only the last message went and the chat is on the clipboard.
  A Gemini API connection has no default target (Gemini's site takes no message in its link); add one to test it.

### Deferred (not bugs to rediscover; from the reviews)

- **Design notes (Argon/Flash):** `ThisPc` is static, `ActivationRouter` is coupled to `PopupWindow`, `App` is a monolith.
- **Small:** tray icon HICON leak; ComboBox `TextSubmitted` `e.Handled`; `ReportHeight` runs in SizeChanged; `CopyImage` on the UI
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
  isn't re-registered on a settings change; the transcript spinner isn't stopped on Clear.

## Next work

1. **Merge PR #9 (0.4)** once the owner has hand-tested the list above.
2. **0.5 (owner's scope):** Continue in (PR #29, above); region capture on multi-monitor / mixed DPI; multi-monitor behaviour of the
   bar; moving the bar and how it acts when pinned; the desktop hand test (clone, dev certificate per README,
   `scripts/install.sh`); the shipped sound. Bump `version.txt` to 0.5 when it merges.
3. **Before 1.0: schema freeze (decide with the owner; not started).** Findings of the read-only config design review (Opus):
   - unknown keys are dropped on save (carry them: `JsonExtensionData` / unknown nodes);
   - written-out defaults + rewrite-on-load versus sparse files;
   - one versioning rule (`version` vs `schemaVersion`; a `$schema` file);
   - a connection file's name should be its id (the id is stored twice);
   - `mcp.json` `env` secrets versus the Credential Locker rule;
   - app state (`.first-run-shown`, `supportReminder`) mixed into config;
   - one backup policy in `backups\` (there are ~7 schemes today);
   - renames while still allowed: `window.height` → `minHeight`, `backends` → `connections`, toolbar `items` → `bottom`, key names that carry units;
   - docs drift.
4. **1.0** when: hand tests done, multi-monitor capture OK, 26H2 merged, a Store update certified with all languages, and settings
   files staying compatible from then on. No native-speaker reviews or RTL planned. Paid signing (winget-pkgs) only if Hotline
   gets popular.
5. Owner preference: anything configurable should be in settings.json even when the Settings window doesn't show it.

## Versions (toward 1.0)

- `version.txt` holds major.minor (now `0.4`). Every code push to `main` publishes `v<major.minor>.<patch>`, the patch being the next
  number in that line from the existing tags (`scripts/next-version.sh`, used by release.yml); bumping to `0.5` starts at
  **v0.5.0**. The Store package is the same version plus `.0`; the Store only needs each submission, compared left to right, to be
  higher than the last (`0.3.0.0` follows `0.2.25.0`).
- **0.3** released 2026-10-17. **0.4** button rows v2, 26H2 wording, Right Ctrl mode dropped, settings read leniently, backup (#9).
  **0.5** everything left above; feature complete for 1.0, then only fixes, polish and one Store update certified with all 17
  languages. **1.0** when that update is through and nothing that breaks existing settings files is pending; from then on settings
  files must keep working across updates (migrations, never breaking changes).

## Architecture map

- `src/Hotline.Core` (net10.0, no UI, unit-tested):
  - `Activation/` key events, URI parsing, dedupe, planner (`hotline://key|tray|settings|demo|selftest`), `AppActionRequest`.
  - `Backends/` `ConnectionTypes`, `ConnectionEditor`, `ModelCatalog`/`ModelFamilies`, `Secrets`, `BackendCatalog` (factory + cache),
    `CliHandoff` (Continue in), `CliTranscripts.*` (reads each CLI's own history), `Agy/`, `ClaudeCode/`, `Codex/` (`codex exec
    --json`, one process per turn); the three CLIs resume a chat's own conversation (`--conversation` / `--resume` / `exec resume`);
    `Api/` (`ApiCommon` SSE/errors/https rule, `OpenAiBackend`, `GeminiBackend`, `AnthropicBackend`).
  - `Chat/` `ChatController` (turns, cancel, retry, resume; records each chat's `CliSession`), `HistoryStore` (JSONL +
    `<id>.meta.json`), `PromptLibrary`, `QuickActions`, `MemoryStore`, `OcrPlan`, `VoiceText`, attachments.
  - `Settings/` `HotlineSettings` (schema v14), `SettingsStore` (settings.json + `connections/*.json`, `LenientReader`, side-effect-free
    `TryRead`), `SettingsService` (live Update/Reload, merge-by-id), `SettingsSchema` (pages), `SettingsReset`, `SettingsPackage*`
    (export/import), `BackupFiles`, `FileArchive`, `Guide`.
  - `Text/` `MarkdownModel` (Markdig → blocks; math, SVG, task lists), `LatexText`, `SvgSanitizer`, `Localization/`.
  - `Tools/` MCP (`McpToolHost`, `McpConfig`, `McpProcess` + `StdioMcpSession`), `OdrDiscovery`. `Diagnostics/` `FileLog`, `LogText`,
    `ConfigProblems`. `Processes/` line processes, `ShellOpen`. `Platform/` `SafeDirectory`, `CommandResolver`.
  - `Windowing/` `PopupGeometry` (GrowUp/GrowDown), `PanelPlacement` (anchor, direction, clamp, resize, rescale), the toolbar files
    (`ToolbarModel`, `ToolbarFile`, `ToolbarEditor`, `ToolbarEditorView`, `ToolbarPlan`, `ToolbarLayout`, `ToolbarProfiles`,
    `CustomIcons`), `Sounds`.
- `src/Hotline.App` (WinUI 3, packaged MSIX, full trust):
  - `App.xaml.cs` (+ `App.*.cs` partials) composition root; `PopupWindow` (sizing, eased growth, focus, off-screen mode, `HoldResize`);
    `ActivationRouter`; `ToolbarRenderer`; `SelfTestLinks` (hotline://selftest).
  - `Chat/` one job per class: `ChatPresenter` (coordinator, send flow), `Composer`, `AttachmentPanel`, `TranscriptView` (one
    selectable `RichTextBlock`), `MarkdownRenderer`, `NoticeArea`, `ProviderBar`, `VoiceInput`, `RecentChatsMenu`,
    `PanelSelfTest`, `FirstRunNote`, `ConnectionChooser`, `SupportNote`, `AnswerNotifier`, `ContinueIn`, `PanelTheme`, `PanelLayout`
    (rows for up/down), `PanelGrips` (Alt move/resize), `OcrReader`, `CliRunner`, `PasswordVaultSecretStore`.
  - `Settings/` window (pages generated from `SettingsSchema`, connections, prompts, agy permissions, `ToolbarQuadrantEditor*`, Backup).
  - `Interop/` tray, Copilot fast path, hotkey, job object, Win32, `SoundPlayer`, `SelectionReader` (UIA + clipboard fallback).

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
  path is a `WM_APP+1` message to the registered window.
- **Windows on-device agent registry:** connectors run only via `odr.exe` (build 26220.7262+).
- **Clipboard:** `OpenClipboard(NULL)` + `EmptyClipboard` leaves it ownerless and every `SetClipboardData` then fails; restoring needs a
  real owner window (`ClipboardOwner`: message-only window on its own pumping thread). Never send Ctrl+C unless UIA confirmed "no text
  pattern": an empty selection copies a whole line (editors) or sends SIGINT (terminals).
- **PowerShell 5.1 reads `0x80000003` as a negative Int32**: pass `[uint32]2147483651` to `SetThreadExecutionState`. Power requests
  (`PowerCreateRequest`/`PowerSetRequest`) hold Modern Standby off more reliably. `@($null)` is a one-item array.
- **OpenSSL on Windows writes `\r\n`:** strip `\r` too when making password files (`tr -d '\r\n'`), or signtool sees a different password.
- **MCP SDK 2.2.0:** `StreamClientTransport(serverInput, serverOutput)`: the first stream is the one written to (the server's stdin).

**The CLIs**
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
