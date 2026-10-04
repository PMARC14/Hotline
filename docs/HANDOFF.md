# Hotline — handoff for the next session

Read this first, then `CLAUDE.md` (working rules) and, if present, `docs/local/HANDOFF-PRIVATE.md` (machine-specific
notes; git-ignored). The design spec is `docs/superpowers/specs/2026-09-30-hotline-design.md`; plans are in
`docs/superpowers/plans/`.

## What Hotline is

A Windows 11 app that takes over the Copilot key: a native WinUI 3 popup (acrylic, no WebView) that chats with the AI
the user picks — Claude Code CLI, Antigravity CLI (agy), Anthropic / Gemini / OpenAI-compatible APIs, or local
OpenAI-style servers. Everything is configured in files under `%USERPROFILE%\.hotline` and applies live.

## State (2026-10-04)

- `main`: Plans 1–5 merged (PR #1). This adds MCP tools for API connections with approvals, region capture,
  `toolbar.json`, release automation and three review rounds of hardening. See the PR #1 description for the full list.
- Branch **`plan6-public`**, draft PR #2: **Plan 6, the public-ready build** —
  `docs/superpowers/plans/2026-10-04-hotline-plan6-public-release.md`. Nothing is implemented yet; start at task 1.
- Release automation: every code merge to `main` publishes a release `v<version.txt>.<run number>`. These are
  self-signed pre-releases until the signing secrets exist (`docs/RELEASING.md`).
- Tests: 521 Core unit tests green; `tests/smoke/smoke.ps1 -Install` green (the "fallback hotkey" check is flaky when
  the user is typing during the run).
- Git history was rewritten on 2026-10-04: noreply author, no personal paths, no certificate password. The dev
  certificate password now lives in `certs/hotline-dev.password` (git-ignored); `build-msix.ps1` reads it.

## Next work

Plan 6 (link above), in order: MCP servers in the kill-on-close job; API polish (Gemini/OpenAI effort pickers, 429
retries, Test connection); quick actions; selected text; OCR for text-only models; push-to-talk voice; public-ready
README and reviews. The user does trusted signing and real-key API tests before making the repo public. They flip the
visibility themselves.

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
  - `Chat/` `ChatController` (turns, cancel, retry, resume), `HistoryStore` (JSONL, `Recent`), `PromptLibrary`.
  - `Settings/` `HotlineSettings` (schema v7), `SettingsStore` (settings.json + `connections/*.json`, migrations,
    side-effect-free `TryRead`), `SettingsService` (live Update/Reload with merge-by-id), `SettingsSchema`.
  - `Text/` `MarkdownModel` (Markdig → block model; math, SVG, task lists), `LatexText`, `SvgSanitizer`.
  - `Tools/` MCP (see above). `Windowing/` geometry, `ToolbarLayout`. `Processes/` line processes.
- `src/Hotline.App` (WinUI 3, packaged MSIX, full trust):
  - `App.xaml.cs` composition root; `PopupWindow` (sizing, eased growth, focus, off-screen mode); `ActivationRouter`.
  - `Chat/ChatPresenter*.cs` (single selectable `RichTextBlock` transcript, attachments, recent chats, self-test,
    demo), `MarkdownRenderer`, `ProviderBar`, `CliRunner`, `PasswordVaultSecretStore`.
  - `Settings/` settings window (pages generated from `SettingsSchema`, connections, prompts, agy permissions).
  - `Interop/` tray, Copilot fast path, hotkey, job object, Win32.

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

## Process that worked

Branch from GitHub `main` per feature → TDD in Core → build → `smoke.ps1 -Install` → commit + push → adversarial review
(Antigravity via the agy MCP tool with a diff file, or a Sonnet subagent) → fix confirmed findings → PR/merge.
