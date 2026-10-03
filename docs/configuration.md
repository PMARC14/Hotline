# Configuration reference

Everything lives in `%USERPROFILE%\.hotline` and applies **live** when you save a file (the settings window edits the
same files). Comments and trailing commas are allowed.

| File | What |
|---|---|
| `settings.json` | General options (window, appearance, chat, key actions), `chat.order` (Provider dropdown order) and `chat.defaultBackend` |
| `connections\<id>.json` | One file per AI connection. Add a connection by dropping in a file; remove one by deleting it |
| `prompts\<name>.md` | System prompts (`default.md` is created on first run) |
| `mcp.json` | MCP servers and tool approvals (tools for API connections — in progress, see the roadmap) |
| `history\*.jsonl` | Saved conversations (text only; deleted after `chat.historyRetentionDays`) |
| `logs\hotline.log` | Log (crashes are logged as `FATAL`) |

API keys are **not** in these files: they are stored in Windows Credential Locker.

## settings.json

| Key | Default | Meaning |
|---|---|---|
| `window.widthPercent` / `minWidth` / `maxWidth` | `40` / `600` / `1000` | Panel width: % of the screen, clamped (DIPs) |
| `window.height` / `maxHeightPercent` | `0` / `70` | Minimum height (0 = just the message bar) and how much of the screen it may grow to |
| `window.verticalPosition` | `0.8` | 0 = top, 0.5 = centered, 1 = bottom of the free space |
| `window.backdrop` | `acrylic` | `acrylic`, `acrylicThin`, `mica`, `solid` |
| `window.tintOpacity` / `luminosityOpacity` | `0.15` / `0.35` | Acrylic translucency (0–1; lower = clearer) |
| `window.hideOnBlur` / `alwaysOnTop` | `true` / `true` | Hide when you click elsewhere; keep on top |
| `window.fontSize` / `fontFamily` | Windows default / Segoe UI Variable | Text size (10–32; empty follows Windows' text size) and font |
| `window.scrollbar` | `auto` | `auto` (appears when you scroll or point at the right edge), `visible`, `hidden` |
| `activation.tap` / `hold` | `togglePopup` / `newChat` | Copilot key short / long press. Also `showPopup`, `captureWindow`, `none` |
| `activation.fallbackHotkey` | `null` | Extra hotkey, e.g. `"Ctrl+Alt+H"` |
| `chat.defaultBackend` | `agy` | Connection that answers by default (its `id`) |
| `chat.order` | all | Provider dropdown order (connection ids); unlisted connections follow by name |
| `chat.defaultPrompt` | `default` | System prompt used by connections that don't pick one |
| `chat.growMode` | `grow` | `grow` = fit the conversation; `full` = jump to the maximum height once you chat |
| `chat.maxImagePixels` | `2048` | Attached/captured images are scaled to this longest edge |
| `chat.saveHistory` / `historyRetentionDays` | `true` / `30` | Conversation history on/off and how long it's kept |
| `diagnostics.verboseLogging` | `false` | Detailed log + key-status line in the panel |

A half-saved or broken `settings.json` is ignored while Hotline runs (your file is never overwritten without a
timestamped backup); at startup a broken file is copied to `settings.json.bad` and defaults are used in memory.

## connections\<id>.json

| Key | Applies to | Meaning |
|---|---|---|
| `type` | all | `antigravity`, `claudeCode`, `gemini`, `anthropic`, `openAiCompatible`, `local` |
| `name` | all | Shown in the Provider dropdown |
| `model` | all | Model id (empty = the connection's default; Anthropic defaults to `claude-opus-5-5`) |
| `effort` | agy, Claude Code, Anthropic | `low` … `max` (agy maps it to the nearest level the model has) |
| `prompt` | all | System prompt name (`prompts\<name>.md`); empty = `chat.defaultPrompt` |
| `endpoint` | APIs | Base URL (defaults per type, e.g. `http://127.0.0.1:8080/v1` for `local`) |
| `cliPath` | agy, Claude Code | Program path (empty = auto-detect) |
| `tools` | agy, Claude Code | `chatOnly` (default) or `inherit` = the CLI's own tools and permission rules in `workingDirectory` |
| `workingDirectory` | agy, Claude Code | Folder for `inherit` mode (empty = your user folder) |
| `approveAllTools` | agy, Claude Code | ⚠ Dangerous: auto-approve every tool request (inherit mode only) |
| `args` | agy, Claude Code | Replaces Hotline's launch flags for the mode (the stream/print flags Hotline needs are always added; dangerous flags only via `approveAllTools`) |
| `extraArgs` | agy, Claude Code | Added on top, e.g. `--mcp-config C:\mcp\servers.json` |
| `keepCliSessions` | Claude Code | Also keep Hotline chats in Claude Code's own history (`claude --resume`) |
| `agent` | agy | Custom agent used in chat-only mode (default `hotline`) |
| `refusalFallback` | Anthropic | If a safety check declines a request, the API re-serves it with a suitable model (default on) |

## agy permissions

In *inherit* mode agy works under **its own** rules (`~/.gemini/antigravity-cli/settings.json` → `permissions.allow /
deny / ask`, e.g. `command(git status)`, `read_file(C:/path)`). Hotline runs agy in the background, where anything that
would ask is denied, so shell commands need allow rules. Settings › AI connections › **Allow read-only commands** adds a
curated list (and denies `Remove-Item`, `rm`, `git push`, …) after showing it to you.

## Links

`hotline://settings` opens the settings window, `hotline://tray` starts quietly in the tray, `hotline://demo` shows an
example conversation, `hotline://selftest` renders a scripted conversation off-screen (used by the smoke test).
