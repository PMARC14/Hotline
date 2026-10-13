# Hotline — handoff for the next session

Read this first, then `CLAUDE.md` (working rules; local, git-ignored) and, if present, `docs/local/HANDOFF-PRIVATE.md` (machine-specific
notes; git-ignored). The design spec is `docs/superpowers/specs/2026-09-30-hotline-design.md`; plans are in
`docs/superpowers/plans/`.

## What Hotline is

A Windows 11 app that takes over the Copilot key: a native WinUI 3 popup (acrylic, no WebView) that chats with the AI
the user picks — Claude Code CLI, OpenAI Codex CLI, Antigravity CLI (agy), Anthropic / Gemini / OpenAI-compatible APIs, or local
OpenAI-style servers. Everything is configured in files under `%USERPROFILE%\.hotline` and applies live.

## State (2026-10-12)

- **Released:** the Microsoft Store listing **Hotline AI** is certified (first submission 0.2.4.0). The next Store
  package is built by hand (`scripts/build-msix.sh --store …`, docs/RELEASING.md) until automatic submission is set up
  (`.github/workflows/store.yml`, secrets and `STORE_PRODUCT_ID`). GitHub releases stay self-signed test builds.
- **Merged since the Store submission** (PRs #10–#21, #14):
  - ARM64 builds; x64 + ARM64 `.msixbundle` (#10). Store listing text in 17 languages (`store/listings/`).
  - 17 interface languages (#4, #16; Plan 8; model-drafted, every language spot-checked by Gemini; no RTL yet).
  - Donations: GitHub Sponsors / Ko-fi buttons in About and the README; a one-time "Enjoying Hotline?" note
    (`general.supportReminder`) (#12, #17).
  - Windows notification when an answer finishes in the background (`chat.notifyWhenDone`, `hotline://answer`) (#18).
  - First run: no connection chosen (`chat.defaultBackend` empty) and a "Choose how Hotline connects" card; no CLI
    runs until one is picked; Copilot key guide (docs/copilot-key.md) and a button to the Windows page (#19, #20).
  - **Plan 9 (#21):** chats with a CLI live in the CLI's own history, one conversation per chat, resumed after a
    restart and from Recent chats; `keepCliSessions` (default on) / `keepInHotline` (default off, Hotline then keeps
    `history\<id>.meta.json` only); chat-only folders `…\agy|claude|codex\Hotline`; agy project "Hotline";
    Recent chats › **Continue this chat in ▸** a terminal. Settings schema v11.
  - **OpenAI Codex CLI** connection (#14), on the same session design. Merged without a live Codex test (the owner
    doesn't use Codex): the first person with a Codex sign-in should run its checklist in the PR #14 description.
- Tests: 863 Core unit tests; `tests/smoke/smoke.ps1 -Install` 22/22 (the "agy exits with Hotline" check fails if any
  other `agy` is running, e.g. an agy review).
- Local scripts: `scripts/install.sh` builds, installs and starts in the tray; `scripts/build-msix.sh` (its cleanup
  no longer ends the script when there are no old bundles).

## Next work

1. **Store update:** upload the package from the last build (docs/RELEASING.md › Microsoft Store), with the
   translated listings from `store/listings/`. Then set up automatic submission (store.yml).
2. **Windows 11 26H2 follow-ups** (PR #9, draft) once 26H2 reaches the dev PC (it was rolling out from 2026-09-29).
3. **Codex:** a live check with a Codex sign-in (PR #14 checklist).
4. **Continue in a web page** (llama.cpp / a routing page): to be designed (Plan 9 left it open).
5. Native-speaker review of the translations; right-to-left languages.
6. Still open from earlier: region capture on multi-monitor / mixed DPI; `odr.exe` (Windows agent connectors) needs
   build 26220.7262+.

The personal branch `personal/gemini-web` (a WebView2 Gemini window, never released) is kept up to date by merging
`main` into it (no force-push).

UI follow-ups the user deferred (only fix if they become big problems): provider name shrinks very small in the bar;
full-fidelity SVG (Windows' renderer drops text); code blocks wrap instead of scrolling; panel-growth smoothness;
caret visibility on all themes.

Self-test links for headless checks: `hotline://selftest` (+ `?tools` starts the mcp.json servers, `?selection` reads
the foreground app's selection via UIA and logs only its length, `?voice` checks dictation setup without the mic).

**Languages:** interface text is in `Hotline.Core/Text/Localization/Strings*.resx` (generated `Strings` class; schema
text is keys looked up at render time; XAML uses `{local:Loc Key=…}`); `general.language` picks one or follows
Windows. See docs/TRANSLATING.md.

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
