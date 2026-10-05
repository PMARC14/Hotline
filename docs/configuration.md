# Configuration reference

Everything lives in `%USERPROFILE%\.hotline` and applies **live** when you save a file (the settings window edits the
same files). Comments and trailing commas are allowed.

| File | What |
|---|---|
| `settings.json` | General options (window, appearance, chat, key actions), `chat.order` (Provider dropdown order) and `chat.defaultBackend` |
| `connections\<id>.json` | One file per AI connection. Add a connection by dropping in a file; remove one by deleting it |
| `prompts\<name>.md` | System prompts (`default.md` is created on first run) |
| `memory.md` | Notes every chat sees: edit freely, or type `/remember something` in the message box (Settings › Chat and history › Edit memory) |
| `actions\<name>.md` | Quick actions (`/translate`, `/summarize`, `/fix`, `/explain` are created on first run) |
| `mcp.json` | MCP servers and tool approvals (tools for API connections) |
| `toolbar.json` | The bottom bar: which items, in what order |
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
| `activation.tap` / `hold` | `togglePopup` / `newChat` | Copilot key short / long press. Also `showPopup`, `captureWindow`, `regionSelect`, `voice`, `none`. `voice` as the long press: talk while you hold the key, let go to stop (Esc cancels); as the short press it starts/stops. Uses Windows speech recognition: allow the microphone and turn on Settings › Privacy & security › Speech › Online speech recognition. Settings › General › **Voice input** switches the long press between `voice` and `newChat` |
| `activation.fallbackHotkey` | `null` | Extra hotkey, e.g. `"Ctrl+Alt+H"` |
| `chat.defaultBackend` | `agy` | Connection that answers by default (its `id`) |
| `chat.order` | all | Provider dropdown order (connection ids); unlisted connections follow by name |
| `chat.defaultPrompt` | `default` | System prompt used by connections that don't pick one |
| `chat.growMode` | `grow` | `grow` = fit the conversation; `full` = jump to the maximum height once you chat |
| `chat.maxImagePixels` | `2048` | Attached/captured images are scaled to this longest edge |
| `chat.saveHistory` / `historyRetentionDays` | `true` / `30` | Conversation history on/off and how long it's kept |
| `chat.memory` | `true` | Add `memory.md` to every chat's system prompt (all connections, CLIs included) |
| `chat.memoryTool` | `ask` | Whether API models may save a fact to `memory.md` with Hotline's remember tool (Anthropic and Gemini get it even in chat-only mode; OpenAI-style connections once their Tool use is on; never while `chat.memory` is off; every save shows a notice): `ask` (approve each one in the panel; "Always" switches to `allow`), `allow`, `off` |
| `chat.voiceAutoSend` | `false` | With `voice`: send the message when you let go of the key (off: check it first, then press Enter) |
| `chat.ocr` | `auto` | Screenshot text, read offline by Windows: `auto` sends it instead of the image to connections whose model can't take images; `always` also adds it alongside images; `off`. Needs a Windows language with "Optical character recognition" (Settings › Time & language › Language & region); Hotline says so if none is installed |
| `chat.attachSelection` | `auto` | Text selected in the app you came from becomes a removable chip when the key opens the panel. `auto`: read through UI Automation (no keystrokes, clipboard untouched; works in most editors, browsers and Office, not in every app). `clipboard`: in apps that don't share text through UI Automation, also send Ctrl+C and put your clipboard back afterwards (never in terminals or password fields; Visual Studio/VS Code whole-line copies are ignored; the app's copy can show up in clipboard history, the restore doesn't; opening can take up to about half a second). `off`. Password fields are never read |
| `diagnostics.verboseLogging` | `false` | Detailed log + key-status line in the panel |

A half-saved or broken `settings.json` is ignored while Hotline runs (your file is never overwritten without a
timestamped backup); at startup a broken file is copied to `settings.json.bad` and defaults are used in memory.

## connections\<id>.json

| Key | Applies to | Meaning |
|---|---|---|
| `type` | all | `antigravity`, `claudeCode`, `gemini`, `anthropic`, `openAiCompatible`, `local` |
| `name` | all | Shown in the Provider dropdown |
| `model` | all | Model id (empty = the connection's default; Anthropic defaults to `claude-opus-5-5`) |
| `effort` | all but `local` | agy, Claude Code, Anthropic: `low` … `max` (agy maps it to the nearest level the model has). Gemini: `low`/`medium`/`high` thinking level (Gemini 2.5: a thinking budget). OpenAI-compatible: `low`/`medium`/`high` as `reasoning_effort` (only reasoning models accept it). Empty = the model's default |
| `prompt` | all | System prompt name (`prompts\<name>.md`); empty = `chat.defaultPrompt` |
| `endpoint` | APIs | Base URL (defaults per type, e.g. `http://127.0.0.1:8080/v1` for `local`) |
| `cliPath` | agy, Claude Code | Program path (empty = auto-detect) |
| `tools` | all | `chatOnly` (default) or `inherit`: CLIs use their own tools and permission rules in `workingDirectory`; API connections use Hotline's MCP tools (`mcp.json`) |
| `workingDirectory` | agy, Claude Code | Folder for `inherit` mode (empty = your user folder) |
| `approveAllTools` | agy, Claude Code | ⚠ Dangerous: auto-approve every tool request (inherit mode only) |
| `args` | agy, Claude Code | Replaces Hotline's launch flags for the mode (the stream/print flags Hotline needs are always added; dangerous flags only via `approveAllTools`) |
| `extraArgs` | agy, Claude Code | Added on top, e.g. `--mcp-config C:\mcp\servers.json` |
| `keepCliSessions` | Claude Code | Also keep Hotline chats in Claude Code's own history (`claude --resume`) |
| `agent` | agy | Custom agent used in chat-only mode (default `hotline`) |
| `images` | APIs | Whether the model reads images (`true`/`false`; default: `false` for `local`, `true` otherwise). With `false`, screenshots go as their text (`chat.ocr`) |
| `refusalFallback` | Anthropic | If a safety check declines a request, the API re-serves it with a suitable model (default on) |

API connections retry a rate limit or overload (HTTP 429/503/529) up to twice, waiting what the server asks
(`Retry-After`, Gemini's `retryDelay`) or 2 s then 4 s, and say so in the panel. A wait over a minute, or an exhausted
quota, fails at once. Settings › AI connections › **Test connection** lists the models with your key and checks the
chosen one exists.

## mcp.json (tools for API connections)

```jsonc
{
  "mcpServers": {
    // same format as other MCP apps
    "files": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\you\\Documents"] },
    "off":   { "command": "some-server.exe", "disabled": true, "env": { "TOKEN": "..." }, "cwd": "C:\\work" }
  },
  // "allow" (no prompt) | "ask" (prompt in the panel) | "deny"; "server/*" covers a whole server
  "approvals": { "files/write_file": "ask", "files/*": "allow" }
}
```

A connection uses these tools when its `tools` is `inherit` ("Use Hotline's tools" in Settings › AI connections).
Without a rule, tools the server marks read-only run and everything else asks; denied tools aren't offered at all.
"Always allow" in the panel writes an `allow` rule here. When Windows has the on-device agent registry (`odr.exe`,
build 26220.7262+), its connectors are added as `windows-…` servers. Settings › **Tools** shows each server's status.
Servers start on first use and stop with Hotline, even if it crashes (they run in its kill-on-close job); their stderr
goes to `logs\hotline.log` (at most 20 lines a minute per server).

## Quick actions (actions\<name>.md)

Type `/` at the start of the message to list them (↑/↓, Tab or Enter to pick, Esc to close). `/name some text` sends
the file's text as an instruction followed by your text; `/name` alone applies it to the attachments. The first line is
the description in the list. Add your own by dropping in a file (letters, digits, `-` and `_` in the name); delete or
edit the defaults freely: they're written only when the folder doesn't exist yet. Settings › **Quick actions** lists,
creates, edits and deletes them.

## toolbar.json

`{ "items": ["pin", "captureWindow", "captureScreen", "captureRegion", "spacer", "effort", "model", "provider", "prompt", "recent", "newChat", "settings"] }`
— left to right; remove an item to hide it; `spacer` takes the free space (repeatable). The pickers (effort, model,
provider) sit together in the order listed. Settings › Appearance › **Bottom bar** edits this file (the previous version
is kept as `toolbar.json.bak`). `captureRegion` (drag out a part of the screen) is in the default bar since Plan 6; an
older toolbar.json keeps its own list, so add it there if you want the button.

## agy permissions

In *inherit* mode agy works under **its own** rules (`~/.gemini/antigravity-cli/settings.json` → `permissions.allow /
deny / ask`, e.g. `command(git status)`, `read_file(C:/path)`). Hotline runs agy in the background, where anything that
would ask is denied, so shell commands need allow rules. Settings › AI connections › **Allow read-only commands** adds a
curated list (and denies `Remove-Item`, `rm`, `git push`, …) after showing it to you.

## Links

`hotline://settings` opens the settings window, `hotline://tray` starts quietly in the tray, `hotline://demo` shows an
example conversation, `hotline://selftest` renders a scripted conversation off-screen and builds every settings page
(used by the smoke test); `hotline://selftest?tools` also starts the `mcp.json` servers and logs the result.
