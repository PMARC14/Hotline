# Configuration reference

All configuration lives in `%USERPROFILE%\.hotline` and applies **live** when saved (the Settings window edits the same files). Comments and trailing commas are allowed.

## Contents

[File rules](#file-rules) | [Backups](#backups) | [settings.json](#settingsjson) | [Connections](#connectionsidjson) | [CLI chats](#chats-with-a-cli-agy-claude-code-codex) | [Continue in](#continue-in-chat-apps-and-web-pages) | [Tools](#mcpjson-tools-for-api-connections) | [Quick actions](#quick-actions-actionsnamemd) | [Moving and resizing](#moving-and-resizing-the-panel) | [Toolbar](#toolbarjson) | [Backup](#backup-settings--backup) | [Codex](#codex) | [agy permissions](#agy-permissions) | [App Actions](#app-actions-click-to-do) | [Links](#links) | [Glossary](#glossary)

## File rules

From 1.0, a file written by any 1.x release loads in every later 1.x release without loss. Compatible additions (new keys or accepted values) never bump `version`; a breaking change (rename, removal, narrower rule, or change of meaning) bumps `version` and adds a conversion in `SettingsMigrations`.

| Rule | Behavior |
|---|---|
| Version | Each config file carries `"version": N` (`1` for settings, connections, mcp, state; `3` for toolbar). |
| Newer files | Files with a higher `version` than Hotline knows are read as far as understood and never overwritten or deleted. |
| Unknown keys | Survive every save in place at every JSON level. Removed legacy fields remain untouched as unknown keys. |
| Loading | Never writes to disk. Missing keys and `null` use embedded defaults (`Defaults/<family>.json`) in memory. |
| Saving | Updates only keys whose values changed and preserves key order. Unchanged saves keep exact bytes; edited saves drop comments. |
| `$schema` | Never written by Hotline; a hand-added `$schema` survives as an unknown key. |
| Pixels | Every pixel setting is in scaled pixels (DIPs). Percentages use the monitor's work area. |

| File | Purpose |
|---|---|
| `settings.json` | General options (window, appearance, chat, key actions, tool approvals), `chat.order`, and `chat.defaultConnection` |
| `connections\<id>.json` | One file per AI connection (drop in to add, delete to remove). Empty on first install; panel offers detected CLIs |
| `prompts\<name>.md` | System prompts (`default.md` created on first run) |
| `memory.md` | Notes added to every chat's prompt (`/remember` or Settings › Chat and history › Edit memory) |
| `actions\<name>.md` | Quick actions (`/bullets`, `/explain`, `/fix`, `/formal`, `/reply`, `/shorter`, `/summarize`, `/translate` on first run) |
| `mcp.json` | MCP servers for API connections; approval rules live in `settings.json` (`tools.approvals`) |
| `toolbar.json` | Button rows, appearance, custom buttons, and panel shortcuts (version 3) |
| `icons\<id>.png` | Custom button images |
| `sounds\` | `.wav` files for `window.openSound` / `closeSound` (`hotline.wav` placed here) |
| `history\*.jsonl` | Saved text conversations (pruned after `chat.historyRetentionDays`). CLI chats: only when `keepInHotline` is on |
| `history\*.meta.json` | CLI chat metadata (title, connection, session ID, folder) used by Recent chats to resume |
| `logs\hotline.log` | Diagnostic log (`toggle:`, `deactivated:`, `settings:`, `FATAL`) |
| `scratch\` | CLI working files (`agy` agent/images pruned after a day, Codex images, Continue-in handoff files) |
| `backups\<yyyyMMdd-HHmmss>-<reason>\` | Originals replaced by one `save`, `reset`, `import`, or `upgrade` operation (see [Backups](#backups)) |
| `.defaults\settings.json`, `toolbar.json`, `mcp.json` | Hidden reference copies of embedded defaults, refreshed on startup |
| `state.json` | Shown notices, saved panel spot, tool grants, `lastWritten` hashes, and upgrade record; never exported or backed up |

**Switching settings:** copy another `settings.json` or `connections\*.json` over the current one to apply it live (`.defaults\settings.json` is a clean start).

**Resetting:** Settings › Advanced › **Reset settings** resets `settings.json`, `toolbar.json`, and `state.json` (connections stay); **Reset everything** also removes connections, deletes their API keys, and resets `mcp.json`. Prompts, quick actions, memory, and history are never deleted by a reset. Replaced files go to a `reset` backup folder; newer-version files are left untouched and reported.

**Credential Locker:** API keys live in Windows Credential Locker as `connection:<id>:apiKey` (lower-cased id), never in JSON files. Open WebUI Continue-in keys are stored per address separately from connection keys and survive resets.

## Backups

Before replacing or removing a file, Hotline copies the original under its relative path into `backups\<yyyyMMdd-HHmmss>-<reason>\` (`save`, `reset`, `import`, or `upgrade`). Same-second operations receive `-2`, `-3`, etc.

- **Pruning:** only the newest 20 `save` folders are kept; `reset`, `import`, `upgrade`, and older `.bak`/`.old`/`.bad` or legacy backup folders are never pruned.
- **Skip rule:** only `save` skips copying files whose on-disk hash matches `lastWritten` in `state.json`; `reset`, `import`, and `upgrade` always copy.
- **Failure guard:** if a backup fails, the write or removal is aborted (exception: pre-freeze upgrades proceed anyway).

### Startup and upgrading

Startup runs any pending pre-freeze upgrade, ensures missing `settings.json`, `toolbar.json`, and `mcp.json` exist from embedded defaults (refreshing `.defaults\`), and loads configuration without writing.

| Pre-freeze file | Upgrade action |
|---|---|
| Settings without `version` | Reset to defaults, keeping only `chat.defaultConnection` (or `defaultBackend`) and `chat.order` |
| Toolbar below v3 | Reset to defaults |
| Connection with `id` or without `version` | Converted to v1 (`id` moved to filename, invalid chars repaired, collisions numbered, string `extraArgs` split, Locker key copied and read back before deleting old key) |
| MCP with `approvals` | Servers kept; `approvals` removed |
| `.first-run-shown`, `.support-note-shown` | Flags moved to `state.json`; marker files deleted |

Originals go to one `upgrade` [backup](#backups) folder. Locked or unreadable files stay untouched and retry on the next launch; a one-time notice reports what was reset or failed.

## settings.json

Format `"version": 1`. Missing values and `null` take embedded defaults in memory.

| Key | Default | Meaning |
|---|---|---|
| `general.language` | `system` | `system` (Windows display language) or `en-US`, `es`, `fr`, `de`, `it`, `pt-BR`, `nl`, `pl`, `uk`, `tr`, `ja`, `ko`, `zh-Hans`, `zh-Hant`, `hi`, `id`, `vi`. Applies on restart. See [Translations](../README.md#translations) |
| `general.supportReminder` | `true` | Preference for showing the support reminder |
| `activation.tap` / `hold` | `togglePopup` / `newChat` | Copilot key short / long press (`showPopup`, `captureWindow`, `regionSelect`, `voice`, `none`). `voice` requires microphone permission and Windows *Online speech recognition* |
| `activation.fallbackHotkey` | `null` | Extra hotkey, e.g. `"Ctrl+Alt+H"` |
| `window.width` | `"40%"` | Opening width: scaled pixels (`200–4000`) or `"N%"` (`20–90`), clamped to `minWidth`/`maxWidth` and work area |
| `window.chatWidth` | `null` | Optional wider chat width (same forms as `width`; grows only if wider; manual resize overrides for that opening) |
| `window.minWidth` / `maxWidth` | `320` / `800` | Width clamps in scaled pixels (`200–4000`; `maxWidth` ≥ `minWidth`) |
| `window.minHeight` / `maxHeightPercent` | `0` / `60` | Minimum height in scaled pixels (`0` fits bar) and max height percentage (`30–95`) of work area |
| `window.chatHeight` | `null` | Max chat height in scaled pixels (`60–8000`); `null` uses `maxHeightPercent` |
| `window.growMode` | `grow` | `grow` (fit conversation) or `full` (jump to max height on first message) |
| `window.growDirection` | `auto` | `auto` (up unless no room above, then down), `up`, or `down`; message bar stays fixed |
| `window.anchor` | `{ "monitor": "active", "x": 0.5, "y": 0.8 }` | Default spot: `monitor` (`active`, `primary`, or device name), `x`/`y` (`0–1` fractions of free work area) |
| `window.openSound` / `closeSound` | `off` | `off`, `windows:<event>` (e.g. `windows:Notification.Default`), or a `.wav` filename in `sounds\` |
| `window.backdrop` | `acrylicThin` | `acrylic`, `acrylicThin`, `mica`, `solid` |
| `window.theme` | `system` | `system`, `light`, `dark`, `highContrast` (Windows contrast themes always override) |
| `window.accent` | `hotline` | `hotline` (purple), `windows`, or `#RRGGBB` |
| `window.tintOpacity` / `luminosityOpacity` | `0.15` / `0.35` | Acrylic translucency (`0–1`) |
| `window.hideOnBlur` / `alwaysOnTop` | `true` / `true` | Hide when clicking elsewhere; stay on top |
| `window.fontSize` / `fontFamily` | Windows / Segoe UI Variable | Text size (`10–32`; `null` = Windows default) and font family |
| `window.scrollbar` | `auto` | `auto` (on scroll/hover), `visible`, `hidden` |
| `chat.defaultConnection` | `""` | Default connection `id` (empty prompts first-run chooser and runs no CLI) |
| `chat.order` | `[]` | Provider dropdown order (connection ids; unlisted connections follow by name) |
| `chat.defaultPrompt` | `default` | System prompt for connections without an override |
| `chat.continueIn` | `null` | Shared "Continue this chat in" targets (`null` = first Local connection's `llama.cpp` web UI, or none) |
| `chat.saveHistory` / `historyRetentionDays` | `true` / `30` | Save history on/off and retention days (`1–3650`) |
| `chat.notifyWhenDone` | `true` | Windows toast when an answer finishes in background; clicking opens it |
| `chat.notifyShowsText` | `false` | Include answer text in toast (`false` shows `"Answer ready"` and title) |
| `chat.historyListSize` | `300` | Saved chats read for history list (`10–5000`; file-only) |
| `chat.thinkingIndicator` / `thinkingDelayMs` | `true` / `700` | Spin cursor after delay (`0–10000` ms) with no streamed text (`thinkingDelayMs` is file-only) |
| `chat.resumeWhileTyping` | `true` | Typing `/resume ` opens and filters the history list immediately (file-only) |
| `chat.cliTranscripts` | `true` | Read CLI history for reopened record-only chats (file-only) |
| `chat.maxImagePixels` | `2048` | Max longest edge (`256–8192`) for attached/captured images |
| `chat.attachSelection` | `auto` | `auto` (UI Automation only, no clipboard), `clipboard` (adds restricted `Ctrl+C` fallback), or `off` |
| `chat.ocr` | `auto` | Offline Windows OCR: `auto` (for non-vision models), `always` (alongside images), `off` |
| `chat.voiceAutoSend` | `false` | Send dictated text automatically on key release |
| `chat.memory` | `true` | Include `memory.md` in every chat's system prompt |
| `chat.memoryTool` | `ask` | Allow API remember tool (`ask`, `allow`, `off`; requires `chat.memory`; OpenAI-style needs tools enabled) |
| `diagnostics.verboseLogging` | `false` | Verbose `hotline.log` (including scrubbed MCP stderr) + key-status line in panel |
| `diagnostics.testLinks` | `false` | Enable `hotline://demo`, `hotline://selftest`, `hotline://grips` (file-only) |
| `tools.approvals` | `{}` | MCP approval rules (`server/tool` or `server/*`: `allow`, `ask`, `deny`; unknown words act as `deny` with warning) |

**Selection clipboard fallback:** `Ctrl+C` runs only when UIA reports no text pattern, and never in terminals (Windows Terminal, console hosts, PuTTY, WezTerm, Alacritty, mintty, ConEmu, etc.), RDP/VM consoles, or IDEs with integrated terminals (VS Code, Cursor, Visual Studio, JetBrains). Clipboard must hold only supported memory formats (≤ 64 MB). Hotline waits up to 450 ms, restores within a 2 s watch window if owned by the target process, and leaves external clipboard changes untouched.

**Widths and pixels:** at 200% Windows scaling, `640` scaled pixels occupy `1280` real pixels. Percent widths resolve against monitor work area before `minWidth`/`maxWidth` clamps apply. Settings › Window has one opening-width box and one optional chat-width box accepting either form.

**Safe edits and newer files:** see [File rules](#file-rules) and [Backups](#backups). A settings file without `version` uses defaults in memory and stays untouched for the upgrade pass.

### Broken files

Bad edits affect only the broken key; nothing is deleted or silently overwritten. Problems show in a panel notice (up to 5 lines), **Settings › Advanced**, and `settings:` lines in `hotline.log` (values truncated to 40 cleaned chars).

| File | Behavior |
|---|---|
| `settings.json` (bad value) | That key falls back to default (out-of-range numbers clamp with a warning); duplicate keys use last value |
| `settings.json` (syntax error / unreadable) | Startup uses defaults; live reload keeps active settings; explicit Settings save backs up broken file first |
| `connections\<id>.json` (bad value) | Bad key defaults (`approveAllTools` → `false`, `tools` → `chatOnly`); connection still loads |
| `connections\<id>.json` (unreadable / unknown `type` / bad filename id) | Skipped and untouched; named in notice. Explicit edits preserve ignored fields after backing up original |
| `mcp.json` | Bad server entry skipped; `approvals` ignored with warning; syntax error keeps last working server list in session (`_lastGood`) or runs no servers if never valid |
| `prompts\*.md`, `actions\*.md` | Locked, oversized, or badly named files skipped; unreadable prompts fall back to `default.md` |

## connections\<id>.json

Format `"version": 1`. The file stem is the connection id (case-insensitive, 1–64 chars `[A-Za-z0-9._-]`, no Windows device names or trailing dots; no `id` key is written). New files omit unset options; existing files retain unknown keys and explicit `null`s. API keys use Locker key `connection:<id>:apiKey` (lower-cased id).

| Key | Applies to | Meaning |
|---|---|---|
| `type` | all | `antigravity`, `claudeCode`, `codex`, `gemini`, `anthropic`, `openAiCompatible`, `local` |
| `name` | all | Provider dropdown display name |
| `model` | all | Model id (empty = connection default; Anthropic defaults to `claude-opus-5-5`) |
| `effort` | all | Codex: `low`…`ultra`; `agy`: `low`/`medium`/`high` (`max` via JSON); Claude/Anthropic: `low`/`medium`/`high`/`xhigh`/`max`; Gemini: `low`/`medium`/`high`; OpenAI/`local`: `low`/`medium`/`high` (`reasoning_effort`, plus `chat_template_kwargs` for `local`) |
| `prompt` | all | System prompt name (`prompts\<name>.md`; empty = `chat.defaultPrompt`) |
| `endpoint` | APIs | Base URL (`local` defaults to `http://127.0.0.1:8080/v1`) |
| `cliPath` | CLIs | Executable path (empty = auto-detect) |
| `tools` | all | `chatOnly` (default) or `inherit` (CLIs use own tools in `workingDirectory`; APIs use `mcp.json`) |
| `workingDirectory` | CLIs | Working folder for `inherit` mode (empty = user folder) |
| `approveAllTools` | CLIs | ⚠ Auto-approve all tool requests in `inherit` mode (`--dangerously-skip-permissions` / `--dangerously-bypass-approvals-and-sandbox`) |
| `args` | CLIs | Replaces default launch flags (required stream/isolation flags stay; approval flags like `--yolo`/`--trust` are stripped) |
| `extraArgs` | CLIs | Array of extra CLI arguments; permission and isolation filters still apply |
| `keepCliSessions` | CLIs | `true` (default): one conversation per chat in CLI history; `false`: Hotline keeps and replays |
| `keepInHotline` | CLIs | `false` (default): keep only metadata when `keepCliSessions` is on; `true`: also store messages in `history\` |
| `continueIn` | any | Per-connection Continue-in targets (`[]` = none; omitted = shared `chat.continueIn`) |
| `agent` | `agy` | Custom agent in `chatOnly` mode (default `hotline`) |
| `images` | APIs | Model reads images (`false` default for `local`, `true` otherwise; `false` sends OCR text) |
| `maxTokens` | APIs | Max output tokens (empty = model default; Anthropic: `64000`) |
| `temperature` | Gemini, OpenAI, `local` | `0–2` (omitted on OpenAI APIs when `effort` is set; always sent to `local`) |
| `refusalFallback` | Anthropic | Re-serve safety-declined requests with a suitable model (default `true`) |

API connections retry HTTP `429`/`503`/`529` up to twice (honoring `Retry-After` / `retryDelay` or `2 s` then `4 s`; waits over 60 s or exhausted quotas fail immediately). **Test connection** checks the key and lists models.

## Chats with a CLI (agy, Claude Code, Codex)

- **Resume & working folder:** Hotline records each CLI session ID and resumes (`agy --conversation`, `claude --resume`, `codex exec resume`). Chats run in your user folder in `chatOnly` mode (`agy` adds `scratch\agy\Hotline` via `--add-dir` and joins the **Hotline** project if present), or `workingDirectory` in `inherit` mode.
- **Reopened transcripts:** record-only chats read messages read-only from CLI history (`~\.claude\projects`, `~\.codex\sessions`, `~\.gemini\antigravity-cli\conversations`), stripping Hotline's prompt wrappers. Missing files open empty and still resume.
- **History & Continue in:** `🕘`, `Ctrl+↑` (empty box), or `/resume [filter]` opens Recent chats. **Continue this chat in ▸** opens the active CLI session in your default terminal, or hands off a Markdown export under **More…** to other CLIs/targets.

## Continue in (chat apps and web pages)

Targets in `chat.continueIn` (or connection `continueIn`) appear after CLIs:

```json
{ "kind": "web", "name": "llama.cpp", "url": "http://127.0.0.1:8080/?q={chat}&model={model}" }
{ "kind": "openWebUi", "name": "Open WebUI", "url": "http://localhost:3000", "model": null }
{ "kind": "app", "name": "LM Studio", "command": "%LOCALAPPDATA%\\Programs\\LM Studio\\LM Studio.exe", "copyChat": true }
```

| Kind | Behavior |
|---|---|
| `web` | Opens `http`/`https` URL. If over 8,000 chars, `{last}` substitutes for `{chat}` and the full chat copies to clipboard |
| `openWebUi` | Posts full history to `{url}/api/v1/chats/new` using Credential Locker API key (HTTPS or localhost only) and opens it |
| `app` | Launches `command` directly (no shell; `.bat`/`.cmd` refused; console apps get a terminal window) |

Placeholders: `{chat}` (whole conversation), `{last}` (last message), `{file}` (Markdown file), `{model}` (`copyChat: true` also copies Markdown). Ready-made Settings presets: `llama.cpp` (`?q=`, `?model=`), Open WebUI, LM Studio (`copyChat`), ChatGPT, Claude, Gemini. Invalid targets warn in Settings › Advanced without blocking valid ones.

## mcp.json (tools for API connections)

```jsonc
{
  "version": 1,
  "mcpServers": {
    "files": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\you\\Documents"] },
    "off":   { "command": "some-server.exe", "disabled": true, "env": { "TOKEN": "${hotline:secret:some_token}" }, "cwd": "C:\\work" }
  }
}
```

Hotline writes `mcp.json` only for starter creation, upgrade, or **Move to Credential Locker** in Settings › **Tools** ([backup](#backups) created first). If `mcp.json` breaks mid-session, Hotline keeps its last valid configuration (`_lastGood`) until fixed; if never valid, no servers start.

- **Secrets:** `env` values matching `${hotline:secret:<name>}` (`[A-Za-z0-9._-]`) resolve from Credential Locker key `mcp:secret:<name>` at launch. Missing secrets or malformed `${hotline:` tokens fail only that server.
- **Approvals & grants:** enabled when a connection sets `tools: "inherit"`. Rules in `settings.json` `tools.approvals` evaluate in order: `deny` > `server/tool` > `server/*` (`deny`/`ask` are case-insensitive; `allow` requires exact case). Otherwise **Always allow** grants in `state.json` apply while `(server, tool, launchHash)` matches `command`, `args`, and `cwd`. Unmatched read-only tools run automatically; others prompt (`ask` rules hide **Always allow**).
- **Windows connectors & lifecycle:** on Windows build `26220.7262+`, `odr.exe` connectors appear as `windows-…`. Servers launch lazily in a kill-on-close job and log up to 20 scrubbed stderr lines/min per server.

## Quick actions (actions\<name>.md)

Type `/` in the message box (`↑`/`↓`, `Tab`/`Enter` to pick, `Esc` to close). `/name text` appends your text; `/name` alone applies to attachments. The first line is the description. Defaults are created only when `actions\` is missing; **Settings › Quick actions › Restore default actions** adds missing defaults and updates untouched ones without touching edited files.

## Moving and resizing the panel

Hold **Alt** over the focused panel to show the move grip and edge handles. Dragging the grip moves the panel; dragging an edge resizes it for the current opening. Right-click the grip for **Save position and size as default** (writes `panel.anchor`, `width`, and `chatHeight` in scaled pixels to `state.json`, leaving `settings.json` untouched; each saved item yields if you later change its `settings.json` key) or **Back to the default spot**. Pinning keeps a moved panel's spot until unpinned, blurred, or restarted.

## toolbar.json

Format `"version": 3`. Edited by Settings › Window › Button rows and Settings › Chat and history.

```json
{
  "version": 3,
  "shortcuts": { "stop": "Esc", "pin": "Ctrl+K", "captureScreen": "" },
  "top": { "left": ["effort"], "center": ["model"], "right": ["provider"] },
  "bottom": { "left": ["pin", "captureWindow", "captureScreen", "captureRegion"],
              "center": ["prompt", "continueIn"], "right": ["recent", "newChat", "settings"] },
  "menu": [],
  "style": { "width": 1, "fill": true, "outline": false, "filled": false },
  "overrides": { "provider": { "span": 5 }, "pin": { "outline": true } },
  "buttons": {
    "summarize": { "label": "Summarize", "icon": "E8A5", "quickAction": "summarize", "shortcut": "Ctrl+Shift+1" }
  }
}
```

| Key | Meaning |
|---|---|
| `top`, `bottom` | `left`, `center`, `right` arrays (first placement wins in bottom-left→bottom-right→top-left→top-right order; omit to hide) |
| `shortcuts` | Command id to key combo (see [Panel shortcuts](#panel-shortcuts)) |
| `menu` | Ordered button ids inside a placed `more` button (no pickers, `more`, duplicates, or bar items) |
| `style` | Bar defaults: `width`, `fill`, `outline`, `filled`, optional `outlineColor` (`#RRGGBB`) |
| `overrides` | Per-item `width`, `outline`, `filled`, `outlineColor`; pickers use `span` instead of `width` |
| `buttons` | Custom buttons keyed by id (`1–32` chars `[A-Za-z0-9-]`, case-insensitive, up to 24 definitions, 40 items/group) |

Built-in layout ids: `pin`, `captureWindow`, `captureScreen`, `captureRegion`, `effort`, `model`, `provider`, `prompt`, `recent`, `continueIn`, `newChat`, `settings`, `more`, and `copyLastAnswer` (`stop` and `spacer` are reserved).

- **Width and fill:** a cell is `40 × 36` scaled pixels; buttons are 1 cell high with `width` in `0.5, 1, 2, 3, 4` (shipped default `1`). With `style.fill` (`true` by default), spare row room is shared by button `width` and picker `span` shares; buttons stay at least their cell minimum (half a cell for `0.5`) and overflow into `...` instead. With `fill: false`, items use exact `width`/`span`.
- **Pickers:** `effort`, `model`, `provider` have default spans `3, 4, 5` (min widths `56, 84, 72` DIPs), shrink then hide in `effort → provider → model` order, and never enter overflow.
- **Appearance:** `outline: true` draws a 1.5 px border (`accent` or `outlineColor` `#RRGGBB`); `filled: true` draws a background. Pickers and buttons inherit separately (`override` → `style` → defaults).
- **Overflow:** short rows move `center` then `left` last buttons, then `right` first buttons, into an automatic `...` menu (keeping at least the rightmost button, and keeping `more` as long as possible).
- **Custom buttons:** `label` (≤ 40 chars), `icon` (Segoe Fluent hex `E000–F8FF`), optional `<id>.png` in `icons\` (PNG ≤ 256 KB, `16–256` px), one action (`quickAction` name or `text` ≤ 2000 chars + `send`), and optional `shortcut`.
- **Reading and saving:** follows [File rules](#file-rules) and [Backups](#backups). Files below v3 use defaults with a warning; higher versions load read-only. Outside edits trigger a reload notice in Settings.

### Panel shortcuts

Apply only while the panel has focus. Built-in commands use `toolbar.json` `shortcuts` (custom buttons use `buttons.<id>.shortcut`):

| Command | Default | Action |
|---|---|---|
| `stop` | `Esc` | Stop a streaming answer |
| `newChat` | `Ctrl+N` | New chat |
| `recent` | `Ctrl+H` | Recent chats |
| `settings` | `Ctrl+,` | Open Settings |
| `pin` | `Ctrl+P` | Toggle pin |
| `copyLastAnswer` | `Ctrl+Shift+C` | Copy latest completed, nonempty answer |
| `captureRegion` | `Ctrl+Shift+R` | Capture a region |
| `prompt` | `Ctrl+Shift+P` | Open prompt library |
| `effort` | `Ctrl+E` | Cycle effort |
| `model` | `Ctrl+M` | Cycle model |
| `provider` | `Ctrl+Shift+M` | Cycle provider / connection |
| `captureWindow` | `Ctrl+Shift+W` | Capture previous window |
| `captureScreen` | `Ctrl+Shift+S` | Capture screen |
| `continueIn` | `Ctrl+Shift+I` | Open Continue in |
| `more` | `Ctrl+Shift+O` | Open placed More menu, else first automatic overflow |

Conflict priority: `stop` > built-ins in table order > custom buttons in file order; losers disable with a warning while keeping the requested string. Only `stop` accepts bare typing keys; other commands require `Ctrl`, `Alt`, or `Win` (or `F1–F24`). `Enter` and editing combos (`Ctrl+A/C/V/X/Y/Z`, `Ctrl+Tab`, `Ctrl+Backspace`, `Ctrl+Shift+Z/Tab`) are reserved. `""` disables; `null`/omitted uses default. Repeats (except `stop`), AltGr, and IME composition are ignored.

## Backup (Settings > Backup)

Exports ticked categories (`settings.json`, `toolbar.json` + `icons\*.png`, `mcp.json`, `sounds\*.wav`, `connections\*.json`, `actions\*.md`, `prompts\*.md`) plus manifest `hotline-export.json` into `hotline-settings-<yyyyMMdd-HHmm>.zip`. Never exports `state.json`, logs, history, `memory.md`, backups, or API keys. `mcp.json` exports verbatim (export warns on plaintext `env` variables and suggests **Move to Credential Locker**).

Import validates filenames and content, leaves newer files untouched, creates one `import` [backup](#backups) folder before replacing files, and requires confirmation whenever executable config (MCP commands/`env`/`approvals`, CLI/tool-enabled connections, `args`/`extraArgs`/`cliPath`, or `Continue in` `app` commands) is included—masking `env` values and listing `allow` rules first.

## Codex

Runs `codex exec --json` per message. *Chat only*: read-only sandbox in your user folder without `config.toml`; *Inherit*: `workspace-write` sandbox in `workingDirectory` (`approveAllTools` bypasses). Models resolve from `codex app-server`, `~\.codex\models_cache.json`, or a built-in list.

## agy permissions

In *Inherit* mode, `agy` uses `~/.gemini/antigravity-cli/settings.json` (`permissions.allow/deny/ask`), denying anything that would prompt. Settings › AI connections › **Allow read-only commands** adds a curated read-only allowlist after preview.

## App Actions (Click to Do)

Registers **Ask Hotline about this text/image** and **Summarize/Translate/Fix/Explain with Hotline** (`hotline-action://ask?text=…`, `hotline-action://run/summarize?text=…` in `Public/actions.json`). Quick actions auto-run only when invoked by Windows.

## Links

`hotline://settings` opens Settings; `hotline://tray` starts in the tray. With `diagnostics.testLinks: true`, `hotline://demo`, `hotline://grips?show=1|0`, and `hotline://selftest` are enabled (see [TESTING.md](TESTING.md)). Protocol `hotline://key` links never trigger voice or selection capture.

## Glossary

- **DIP / scaled pixel:** Windows UI unit; multiply by monitor scale to get real pixels.
- **Work area:** monitor space excluding the taskbar.
- **Connection id:** case-insensitive stem of a `connections\<id>.json` file.
- **Backup set:** `backups\<yyyyMMdd-HHmmss>-<reason>\` folder holding files replaced by one operation.
