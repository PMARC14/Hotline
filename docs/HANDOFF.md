# Hotline — handoff for the next session

Read this first, then `CLAUDE.md` (working rules; local, git-ignored) and, if present, `docs/local/HANDOFF-PRIVATE.md` (machine-specific
notes; git-ignored). The design spec is `docs/superpowers/specs/2026-09-30-hotline-design.md`; plans are in
`docs/superpowers/plans/`.

## What Hotline is

A Windows 11 app that takes over the Copilot key: a native WinUI 3 popup (acrylic, no WebView) that chats with the AI
the user picks — Claude Code CLI, OpenAI Codex CLI, Antigravity CLI (agy), Anthropic / Gemini / OpenAI-compatible APIs, or local
OpenAI-style servers. Everything is configured in files under `%USERPROFILE%\.hotline` and applies live.

## State (2026-10-16)

- **Released:** GitHub **v0.2.24** (2026-10-15), **v0.2.25** (2026-10-16; the history hotfix and the two
  history features below). GitHub releases are normal releases signed with Hotline's long-lived self-signed
  certificate (`CN=pmarc14 Hotline Dev`, valid to 2036-10-12; repo secrets `HOTLINE_SIGNING_PFX_BASE64` /
  `_PASSWORD`; the owner keeps `certs/release/hotline-release.pfx` + password, git-ignored, and a backup in
  `Documents\Hotline-release-certificate\`): users trust `Hotline.cer` once, then `.appinstaller` updates work.
- **Microsoft Store** **Hotline AI** (Store ID `9PMW5CR6G3BC`, also `winget install 9PMW5CR6G3BC`): uploads are by
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
- **Open PR #29** (`continue-in-targets`, ships as **0.4**): Continue in to web pages, Open WebUI (a real chat) and apps,
  from a shared list or per connection; built from `docs/superpowers/plans/2026-10-15-continue-in-targets.md`, reviewed,
  998 tests. Waits for 0.3 (the Codex feedback branch); bump `version.txt` to `0.4` when it merges. Hand-test list:
  1. llama.cpp web UI (default target of a Local connection): opens with the conversation as the first message.
  2. Open WebUI (if run): Settings › Chat › Continue in › Add › Open WebUI, address + API key, **Test** shows the model
     count; continuing creates a real Open WebUI chat with every message.
  3. LM Studio (if installed): opens with the whole chat on the clipboard.
  4. ChatGPT / Claude ready-made entries: the site opens with the chat in its message box.
  5. Per connection: *None* on one connection and *Own list* on another; each chat shows only its own targets.
  6. A very long chat with a web target: the notice that only the last message went and the chat is on the clipboard.
  7. A wrong Open WebUI key or address: a clear "Couldn't open…" notice.
  A Gemini API connection has no default target (Gemini's site takes no message in its link); add one to test it.
- Tests: ~998 Core unit tests; `tests/smoke/smoke.ps1 -Install` (the owner sometimes asks to skip it); the
  off-screen self-test `hotline://selftest` builds every settings page.

## Next work

1. **0.3** — Codex feedback (branch `codex-feedback`), then panel placement. **0.4** — PR #29 (Continue in).
2. **0.5 on the owner's desktop** (it already has 26H2 and several monitors; the laptop dev PC doesn't):
   - **Windows 11 26H2 follow-ups** (PR #9, draft, branch `windows-26h2`, kept up to date with `main`): agent
     connectors (`odr.exe`), App Actions, the Copilot key, re-run TESTING.md, new APIs behind `PlatformSupport` checks.
   - **Region capture on multi-monitor / mixed DPI.**
   Set the desktop up first: clone, dev certificate (README), `scripts/install.sh`.
3. **Brainstorm** anything else wanted before 1.0, then decide on it. (The Codex live check was done by the owner on
   2026-10-16; its feedback is 0.3.)
4. **Version 1.0** when: hand tests done, multi-monitor capture OK, 26H2 merged, a Store update certified with all
   languages, and settings files staying compatible from then on.
5. Known: the first off-screen layout after a fresh start logs a `LayoutCycleException` (kept running; older too).
   No native-speaker reviews or RTL planned. Paid signing (winget-pkgs) only if Hotline gets popular.
6. Owner preference: anything configurable should be in settings.json even when the Settings window doesn't show it.

## Versions (toward 1.0)

- `version.txt` holds major.minor (now `0.2`). Every code push to `main` publishes `v<major.minor>.<patch>`, the patch
  being the next number in that line from the existing tags (`scripts/next-version.sh`, used by release.yml): bumping
  to `0.3` starts at **v0.3.0**, then v0.3.1, … (until 2026-10-16 the patch
  was the GitHub run number, which is why 0.2 reached .25). The Store package is the same version plus `.0`.
- **The Store only needs each submission's version, compared as a whole (left to right), to be higher than the last**,
  and the fourth part to be `0`: `0.3.0.0` follows `0.2.25.0` fine.
- **The plan:**
  - **0.3** — the Codex-test feedback (thinking indicator, Codex models, connections, folders, reset) and the movable,
    pinnable panel.
  - **0.4** — Continue in (PR #29): the last big feature before 1.0.
  - **0.5** — when the Windows 11 26H2 work (#9) and the multi-monitor / mixed-DPI region capture fix land: feature
    complete for 1.0; after that only fixes, polish and a Store update certified with all 17 languages.
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
  - `Tools/` MCP (`McpToolHost`, `McpConfig`, `McpProcess` + `StdioMcpSession`: process start, stderr throttle). `Windowing/` geometry, `ToolbarLayout`. `Processes/` line processes.
- `src/Hotline.App` (WinUI 3, packaged MSIX, full trust):
  - `App.xaml.cs` composition root; `PopupWindow` (sizing, eased growth, focus, off-screen mode); `ActivationRouter`.
  - `Chat/ChatPresenter*.cs` (single selectable `RichTextBlock` transcript, attachments, recent chats, self-test,
    demo), `MarkdownRenderer`, `ProviderBar`, `CliRunner`, `PasswordVaultSecretStore`.
  - `Settings/` settings window (pages generated from `SettingsSchema`, connections, prompts, agy permissions).
  - `Interop/` tray, Copilot fast path, hotkey, job object, Win32, `SelectionReader` (UIA + clipboard fallback).
  - `Chat/` one job per class: `ChatPresenter` (coordinator, send flow), `Composer`, `AttachmentPanel`,
    `AttachmentVisuals`, `TranscriptView`, `NoticeArea`, `VoiceInput` + `WindowsSpeech`, `RecentChatsMenu`,
    `PanelSelfTest`, `FirstRunNote`, `ConnectionChooser` (first run), `SupportNote`, `AnswerNotifier`, `ContinueIn`,
    `PanelTheme`, `UiTasks`, `OcrReader`. `SelfTestLinks` (hotline://selftest),
    `Interop/CopilotKeyHook` (Right Ctrl mode).

## Hard-won facts (don't relearn these)

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
