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
- Branch **`plan5-tools`** (pushed, open a PR to merge): Plan 5 **Task 1 done** — `src/Hotline.Core/Tools/`
  (`McpConfig`, `ToolPolicy`, `ToolNames`, `OdrDiscovery`, `McpToolHost`, `StdioMcpSession`), plus the settings-window
  crash fix, a settings-window self-test, `hotline://demo`, README/config docs and screenshots.
- Tests: 483 Core unit tests green; `tests/smoke/smoke.ps1 -Install` green (the "fallback hotkey" check is flaky when
  the user is typing during the run — it sends a synthetic Ctrl+Alt+H).

## Next work (Plan 5, `docs/superpowers/plans/2026-10-03-hotline-plan5-tools-capture-toolbar-release.md`)

2. **Tool loops in the API backends** (`src/Hotline.Core/Backends/Api/`): give `OpenAiBackend`, `AnthropicBackend`
   (official SDK — `Anthropic.Models.Beta.Messages` types; load the `claude-api` skill first) and `GeminiBackend` an
   `IToolHost?`; when the connection's `tools` is `inherit`, send the tool list, run requested calls through
   `IToolHost.CallAsync` (approval happens inside), feed results back, loop (max 20 rounds), and stream short
   transcript notes ("🔧 files/search_files…"). Add `ToolHost` to `BackendDeps`; show the Tool use field for API types
   (`ConnectionField.Tools`). Unit-test each loop with the fake HTTP handlers already used in `*BackendTests.cs`.
3. **App:** construct `McpToolHost` in `App.xaml.cs` (config `~/.hotline/mcp.json`, ODR servers when `odr.exe` exists,
   `StdioMcpSession.ConnectAsync`), an approval InfoBar in the panel (Allow once / Always / Deny, 5-minute timeout =
   Deny), a Tools settings page (server status from `McpToolHost.Status`, open mcp.json, ODR availability), live reload
   of mcp.json (the watcher already covers `*.json` in the data folder — extend `Relevant()`), and put MCP server
   processes in the kill-on-close job if possible (`ChildProcessJob`).
4. **Region capture:** full-screen overlay window to drag a rectangle (Esc cancels); key action `RegionSelect` exists.
5. **Customizable bar:** `~/.hotline/toolbar.json` with ordered `items`; `ProviderBar`/`ToolbarLayout` already compute
   widths — generalize the fixed buttons.
6. **Release prep:** sparse-package installer design (identity needed for the Copilot key), enable CI, signing.

UI follow-ups the user deferred (only fix if they become big problems): code-block Copy button isn't visible in its
header row; provider name shrinks very small in the bar; full-fidelity SVG (Windows' renderer drops text); code blocks
wrap instead of scrolling; panel-growth smoothness; caret visibility on all themes.

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
- **Copilot key:** needs package identity (hence MSIX / sparse package later); cold start arrives as
  `ProtocolForResults`; the fast path is a `WM_APP+1` message to the registered window.
- **Windows on-device agent registry:** connectors run only via `odr.exe` (build 26220.7262+).
- **Scripting edits from bash heredocs mangles `\n`** in C# strings — write Python edit scripts to a file with raw
  strings, or use the Edit tool.

## Process that worked

Branch from GitHub `main` per feature → TDD in Core → build → `smoke.ps1 -Install` → commit + push → adversarial review
(Antigravity via the agy MCP tool with a diff file, or a Sonnet subagent) → fix confirmed findings → PR/merge.
