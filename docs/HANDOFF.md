# Hotline — handoff for the next session

Read this first, then `CLAUDE.md` (working rules; local, git-ignored) and, if present, `docs/local/HANDOFF-PRIVATE.md` (machine-specific
notes; git-ignored). The design spec is `docs/superpowers/specs/2026-09-30-hotline-design.md`; plans are in
`docs/superpowers/plans/`.

## What Hotline is

A Windows 11 app that takes over the Copilot key: a native WinUI 3 popup (acrylic, no WebView) that chats with the AI
the user picks — Claude Code CLI, Antigravity CLI (agy), Anthropic / Gemini / OpenAI-compatible APIs, or local
OpenAI-style servers. Everything is configured in files under `%USERPROFILE%\.hotline` and applies live.

## State (2026-10-04, evening)

- `main`: Plans 1–5 merged (PR #1). This adds MCP tools for API connections with approvals, region capture,
  `toolbar.json`, release automation and three review rounds of hardening. See the PR #1 description for the full list.
- Branch **`plan6-public`**, PR #2: **Plan 6, the public-ready build**
  (`docs/superpowers/plans/2026-10-04-hotline-plan6-public-release.md`) — all seven tasks implemented, each reviewed
  by agy (Opus 5.5 high + Gemini 3.8 Flash high) with confirmed findings fixed:
  1. MCP servers start in Hotline's kill-on-close job (`Tools/McpProcess.cs`: same cmd /c wrapping and escaping as
     the SDK, `StreamClientTransport`); verified with a hard kill and a stdin-ignoring control server.
  2. Gemini/OpenAI effort pickers, 429/503/529 retries with status notes (`ChatDelta.Status` → `AssistantStatus`),
     Test connection says whether the chosen model exists.
  3. Quick actions (`actions\*.md`, `/` suggestions in the composer).
  4. Selected text → chip (`chat.attachSelection`, UI Automation; opt-in Ctrl+C with exact clipboard restore).
     Settings schema v8.
  5. OCR for text-only models (`chat.ocr`, connection `images`; Windows.Media.Ocr).
  6. Push-to-talk voice (`activation.hold = "voice"`, `chat.voiceAutoSend`; Windows speech recognition, microphone
     capability).
  7. README/configuration/THIRD-PARTY-NOTICES updated (Interop.UIAutomationClient, MIT).
- Release automation: every code merge to `main` publishes a release `v<version.txt>.<run number>`. These are
  self-signed pre-releases until the signing secrets exist (`docs/RELEASING.md`).
- Tests: 610 Core unit tests green; `tests/smoke/smoke.ps1 -Install` green (now also checks OCR on the rendered
  transcript and the `/` suggestion list; the "fallback hotkey" check is flaky when the user is typing during the run).
- Git history was rewritten on 2026-10-04: noreply author, no personal paths, no certificate password. The dev
  certificate password now lives in `certs/hotline-dev.password` (git-ignored); `build-msix.sh` reads it.

## Next work

User-tested 2026-10-07: everything in `docs/TESTING.md` works. Next: merge PR #2 (Plans 6 + 7), make the repo public,
then apply the `main` ruleset (`scriptspply-rulesets.ps1`; see docs/RELEASING.md).

**Future: OpenAI Codex support.** Add Codex as a connection the same way Claude Code and agy are: drive the user's
logged-in `codex` CLI (non-interactive `codex exec` with JSON output, session resume), plus the API path for
OpenAI keys if that's still missing. Check the current Codex CLI flags first; they change often.

Plan 7 (`docs/superpowers/plans/2026-10-05-hotline-plan7-structure-actions.md`) is done: memory, the ChatPresenter
split, SendRouter + VoiceSession in Core, App Actions, the Copilot key as Right Ctrl, privacy notes, `docs/TESTING.md`.
Release signing: the user won't pay for signing, so plan on the Microsoft Store (free, Microsoft signs; `winget install
-s msstore`). GitHub releases stay self-signed test builds; the winget-pkgs workflow stays off.

Self-test links for headless checks: `hotline://selftest` (+ `?tools` starts the mcp.json servers, `?selection` reads
the foreground app's selection via UIA and logs only its length, `?voice` checks dictation setup without the mic).

Still open: the region capture human check (multi-monitor / mixed DPI); `odr.exe` untested (needs build 26220.7262+).

UI follow-ups the user deferred (only fix if they become big problems): provider name shrinks very small in the bar;
full-fidelity SVG (Windows' renderer drops text); code blocks wrap instead of scrolling; panel-growth smoothness;
caret visibility on all themes.

## Architecture map

- `src/Hotline.Core` (net10.0, no UI, unit-tested):
  - `Activation/` key events, URI parsing, dedupe, planner (`hotline://key|tray|settings|demo|selftest`).
  - `Backends/` `ConnectionTypes`, `ConnectionEditor`, `ModelCatalog`/`ModelFamilies`, `Secrets`, `BackendCatalog`
    (factory + cache), `Agy/` (stream-json CLI, workspace, permissions), `ClaudeCode/` (stream-json CLI),
    `Api/` (`ApiCommon` SSE/errors/https rule, `OpenAiBackend`, `GeminiBackend`, `AnthropicBackend`).
  - `Chat/` `ChatController` (turns, cancel, retry, resume), `HistoryStore` (JSONL, `Recent`), `PromptLibrary`, `QuickActions`, `OcrPlan`, `VoiceText`, attachments (`SelectionAttachment`).
  - `Settings/` `HotlineSettings` (schema v7), `SettingsStore` (settings.json + `connections/*.json`, migrations,
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
    `PanelSelfTest`, `FirstRunNote`, `PanelTheme`, `UiTasks`, `OcrReader`. `SelfTestLinks` (hotline://selftest),
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
