# Hotline — handoff for the next session

Read this first, then `CLAUDE.md` (working rules) and, if present, `docs/local/HANDOFF-PRIVATE.md` (machine-specific
notes; git-ignored). The design spec is `docs/superpowers/specs/2026-09-30-hotline-design.md`; plans are in
`docs/superpowers/plans/`.

## What Hotline is

A Windows 11 app that takes over the Copilot key: a native WinUI 3 popup (acrylic, no WebView) that chats with the AI
the user picks — Claude Code CLI, Antigravity CLI (agy), Anthropic / Gemini / OpenAI-compatible APIs, or local
OpenAI-style servers. Everything is configured in files under `%USERPROFILE%\.hotline` and applies live.

## State (2026-10-03)

- `main`: Plans 1–4 complete (key + shell, chat, native UI, settings window, connections, pickers, prompts, tool
  modes for CLIs, Claude Code backend, API backends, recent chats, one file per connection, review fixes).
- Branch **`plan5-tools`**, PR #1 (https://github.com/PMARC14/hotline/pull/1): **Plan 5 complete.**
  1. MCP core — `src/Hotline.Core/Tools/` (`McpConfig`, `ToolPolicy`, `ToolNames`, `OdrDiscovery`, `McpToolHost`,
     `StdioMcpSession`).
  2. Tool loops in the OpenAI-compatible, Gemini and Anthropic backends (`Backends/Api/ToolLoop.cs`, max 20 rounds).
  3. App: tool host in `App.xaml.cs`, approval InfoBar (Allow once / Always / Deny), Settings › Tools page,
     "Use Hotline's tools" for API connections. Verified with the real filesystem MCP server.
  4. Region capture — `Capture/RegionSelectWindow.cs` (frozen, dimmed snapshot of the monitor under the mouse; drag;
     Esc / right-click cancels), `Core/Windowing/RegionMath.cs`; + menu item, optional bar button, key action
     `regionSelect`.
  5. Customizable bar — `~/.hotline/toolbar.json` (`Core/Windowing/ToolbarConfig.cs`, `PopupWindow.ApplyToolbar`),
     live reload.
  6. Release prep — `docs/RELEASING.md` (why MSIX, signing options, `.appinstaller` updates, checklist),
     `THIRD-PARTY-NOTICES.md`; CI ran green on GitHub (manual run; still gated by `HOTLINE_ACTIONS_ENABLED`).
  Also fixed: code-block / table Copy buttons were clipped off the right edge (now content-sized headers).
- Tests: 521 Core unit tests green; `tests/smoke/smoke.ps1 -Install` green (the "fallback hotkey" check is flaky when
  the user is typing during the run — it sends a synthetic Ctrl+Alt+H).
- Needs a human check: region capture (drag on a real screen; multi-monitor and mixed DPI).

## Next work

- Final Antigravity review of Plan 5 done and fixed (a33f6a4). Merge PR #1 after the user's review
  (`/code-review ultra 1` is the user's call — it's billed).
- First public release checklist: `docs/RELEASING.md` (installer, trusted signing, final icon).
- Known limit: MCP server processes are started by the MCP SDK, not `ChildProcessJob`, so a Hotline *crash* can leave
  them running (Quit and Restart stop them). Fix idea: start them through our own process factory/transport.
- Known limit: tool calls and results live only inside one answer; later turns replay the answer text (tool notes
  stripped), so follow-ups about tool output may need the tool again. Fix idea: store tool turns in the conversation.
- 2026-10-04 reviews, all real findings fixed: Opus 5.5 via agy (9e3f560), Gemini 3.1 Pro via agy (33e85a9), and
  `/code-review high` findings: tool host caches lists, restarts only changed servers, 30 s
  start+list timeout, failed servers retried after 1 min / "Check servers", consistent snapshot for calls; mcp.json
  backed up before "Always allow" rewrites it; Gemini-safe tool names; shared tool-call/result handling; whitespace
  text blocks dropped for Anthropic; demo resets the chat properly.
- Windows agent registry (`odr.exe`) is untested on a real build (needs 26220.7262+; the dev PC is 26200).

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
