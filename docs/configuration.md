# Configuration reference

Everything lives in `%USERPROFILE%\.hotline` and applies **live** when you save a file (the settings window edits the
same files). Comments and trailing commas are allowed.

| File | What |
|---|---|
| `settings.json` | General options (window, appearance, chat, key actions), `chat.order` (Provider dropdown order) and `chat.defaultBackend` |
| `connections\<id>.json` | One file per AI connection. Add a connection by dropping in a file; remove one by deleting it. A new install has none (the panel offers the CLIs it finds); every connection can be removed |
| `prompts\<name>.md` | System prompts (`default.md` is created on first run) |
| `memory.md` | Notes every chat sees: edit freely, or type `/remember something` in the message box (Settings › Chat and history › Edit memory) |
| `actions\<name>.md` | Quick actions (`/bullets`, `/explain`, `/fix`, `/formal`, `/reply`, `/shorter`, `/summarize`, `/translate` are created on first run) |
| `mcp.json` | MCP servers and tool approvals (tools for API connections) |
| `toolbar.json` | The button rows around the message box: which items, in what order, how they look, and your own buttons and panel shortcuts (version 3; see *toolbar.json*) |
| `icons\<id>.png` | Pictures of custom buttons |
| `icons\<id>.png.<time>.bak` | The previous picture, kept before a replacement (newest 10 per file). Deleting the button leaves them; a new button with that id adds to them (they are never read as its picture) |
| `sounds\` | .wav files for `window.openSound` / `closeSound` (Hotline's own `hotline.wav` is put here) |
| `history\*.jsonl` | Saved conversations (text only; deleted after `chat.historyRetentionDays`). CLI chats: only when `keepInHotline` |
| `history\*.meta.json` | A CLI chat's record: title, connection, the CLI's session ID and folder, used by Recent chats to resume it |
| `logs\hotline.log` | Log (crashes are logged as `FATAL`) |
| `scratch\` | Working files for CLI chats: agy's `hotline` agent and attached images (pruned after a day), Codex images, Continue-in handoff files |
| `backups\reset-<time>\` | What Settings › Advanced › Reset replaced |
| `backups\import-<time>\` | The files an import (Settings › Backup) replaced, under the same relative paths |
| `settings.json.<time>.bak` | The previous settings.json (`yyyyMMdd-HHmmss`), kept when Hotline replaces a file it did not write itself (a hand edit it rewrites, a damaged file, an older run's file) |
| `settings.json.bad` | The syntax-damaged settings.json found at startup (a second copy is `settings.json.<time>.bad`) |
| `connections\<id>.json.<time>.bak` | The previous connection file, kept when Settings rewrites a hand-edited file or removes a connection. A failed backup or deletion keeps the file and its API key |
| `mcp.json.bak-<time>` | The previous mcp.json, kept when Hotline rewrites it |
| `backups\<yyyyMMdd-HHmmss>-save\toolbar.json` | Toolbar originals preserved before changed saves; newest 20 save sets kept |
| `.defaults\settings.json`, `.defaults\toolbar.json` | (hidden) The default settings.json and toolbar.json of this version, rewritten at every start |

**An old inline connection list** (a `chat.backends` array in `settings.json`, from before connections were files) is split into `connections\` files the first time such a file is read; `settings.json` is copied to a stamped `.bak` first and the connection files are never overwritten.

**Switching settings** is replacing files: copy another `settings.json` (or a file in `connections\`) over the current one and
Hotline applies it right away; `.defaults\settings.json` is a clean start. Settings › Advanced › **Reset settings** puts
settings.json and toolbar.json back to the defaults (connections stay); **Reset everything** also removes the connections and their
API keys, `mcp.json`. Prompts, quick actions, memory and history are never reset; replaced files go to `backups\reset-<time>\`.
Settings › **Backup** exports and imports these files (see *Backup*).

API keys are **not** in these files: they are stored in Windows Credential Locker.
Open WebUI's Continue in keys are stored per address, separately from connection API keys. Both resets leave those
Open WebUI keys in Credential Locker, including keys for targets removed by the reset; adding the same address again
reuses its saved key.

## settings.json

| Key | Default | Meaning |
|---|---|---|
| `general.language` | `system` | Interface language: `system` follows the Windows display language, or one of `en-US`, `es`, `fr`, `de`, `it`, `pt-BR`, `nl`, `pl`, `uk`, `tr`, `ja`, `ko`, `zh-Hans`, `zh-Hant`, `hi`, `id`, `vi`. Applies after restarting Hotline. AI answers follow the language you write in, whatever this says. See [TRANSLATING.md](TRANSLATING.md) |
| `general.supportReminder` | `true` | Shows the "Enjoying Hotline?" donation note once, at the top of the panel the next time you open it after an answer, then turns itself off. Settings › About switches it back on |
| `window.widthPercent` / `minWidth` / `maxWidth` | `40` / `400` / `1200` | Panel width at opening: % of the screen's **real pixels**, clamped to `minWidth`/`maxWidth` (also **real pixels**: the owner's defaults, 400 and 1200). Windows' display scaling is ignored: the same screen gives the same pixel width at 100% and at 200%. Scaling still decides how big buttons and text are (a cell stays 40 scaled units), so a narrow panel simply moves buttons into the "..." box. Settings > Window shows a note above the button rows with this screen's pixels, the scaling and the scaled interface size. Heights (`height`, `chatHeight`) stay in scaled units |
| `window.height` / `maxHeightPercent` | `0` / `60` | Minimum height (0 = just the message bar) and how much of the screen it may grow to |
| `window.anchor` | `{ "monitor": "active", "x": 0.5, "y": 0.8 }` | Where the panel opens (its saved default spot). `monitor`: `active` (where you're working), `primary`, or a monitor's name (written by the grip's *Save as default*; not connected = `active`). `x`/`y`: fractions of the free space, 0 = left/top, 1 = right/bottom (`y` was `window.verticalPosition` before 0.3) |
| `window.width` | `null` | Panel width in real pixels (200-16000; a resize by hand saves it as pixels); `null` = the `widthPercent` rule. A width you saved yourself is kept |
| `window.chatHeight` | `null` | The most the chat grows to, in DIPs; `null` = `maxHeightPercent` |
| `window.chatWidthPercent` | `0` | The panel grows this wide (% of the screen, 20-90, clamped to `minWidth`..`maxWidth`) when a chat starts and shrinks back on a new chat, around its centre. `0` = it keeps its width; a value no wider than the opening width does the same |
| `window.chatWidth` | `null` | The chat width in real pixels (200-16000, kept within `minWidth`..`maxWidth`, as the percentage is); `null` = `chatWidthPercent`. A fixed `window.width` (a resize by hand saves one) or a panel you resized this opening never grows, like a manual height |
| `window.growDirection` | `auto` | Which way the chat grows from the message bar: `auto` = up, unless there's no room above (then down); `up` / `down` keep it that way. The message bar never moves |
| `window.openSound` / `closeSound` | `off` | `off`, `windows:<event>` (a sound from the Windows sound scheme, e.g. `windows:Notification.Default`), or a .wav file name in `sounds\` (only that folder) |
| `window.backdrop` | `acrylicThin` | `acrylic`, `acrylicThin`, `mica`, `solid` |
| `window.theme` | `system` | The panel's colours: `system` (follows Windows), `light`, `dark`, `highContrast` (Hotline's own white-on-black; a Windows contrast theme is followed whatever this says) |
| `window.accent` | `hotline` | The accent colour of the panel (send button, focus line, links) and of the button outline (`toolbar.json` `outline`, when it has no `outlineColor` of its own): `hotline` (Hotline's purple), `windows` (your Windows accent colour) or `#RRGGBB` |
| `window.tintOpacity` / `luminosityOpacity` | `0.15` / `0.35` | Acrylic translucency (0–1; lower = clearer) |
| `window.hideOnBlur` / `alwaysOnTop` | `true` / `true` | Hide when you click elsewhere; keep on top |
| `window.fontSize` / `fontFamily` | Windows default / Segoe UI Variable | Text size (10–32; empty follows Windows' text size) and font |
| `window.scrollbar` | `auto` | `auto` (appears when you scroll or point at the right edge), `visible`, `hidden` |
| `activation.tap` / `hold` | `togglePopup` / `newChat` | Copilot key short / long press. Also `showPopup`, `captureWindow`, `regionSelect`, `voice`, `none`. `voice`: talk while you hold the key, let go to stop (Esc cancels); as the short press it starts/stops. Needs the microphone allowed and Settings › Privacy & security › Speech › Online speech recognition on. Settings › General › **Voice input** switches the long press between `voice` and `newChat` |
| `activation.fallbackHotkey` | `null` | Extra hotkey, e.g. `"Ctrl+Alt+H"` |
| `chat.defaultBackend` | empty | Connection that answers by default (its `id`). Empty on a new install: the panel opens on a "Choose how Hotline connects" card and runs no CLI until one is picked |
| `chat.order` | all | Provider dropdown order (connection ids); unlisted connections follow by name |
| `chat.defaultPrompt` | `default` | System prompt used by connections that don't pick one |
| `chat.growMode` | `grow` | `grow` = fit the conversation; `full` = jump to the maximum height once you chat |
| `chat.maxImagePixels` | `2048` | Attached/captured images are scaled to this longest edge |
| `chat.notifyWhenDone` | `true` | A Windows notification when an answer finishes while the panel is hidden or another app is in front; clicking it opens the answer |
| `chat.thinkingIndicator` | `true` | While an answer is on its way but no text has come for `chat.thinkingDelayMs`, the cursor spins (so a slow model doesn't look stalled) |
| `chat.thinkingDelayMs` | `700` | How long without new text before the cursor spins (0–10000 ms). Not in the Settings window |
| `chat.historyListSize` | `300` | How many saved chats the history list reads (10–5000). Not in the Settings window |
| `chat.resumeWhileTyping` | `true` | Typing `/resume` and a space opens the history list at once and the search continues there (best match selected, Enter opens it). Not in the Settings window |
| `chat.cliTranscripts` | `true` | Reopening a CLI chat Hotline kept only a record of (`keepInHotline` off) shows its earlier messages from the CLI's own history (read-only; see *Chats with a CLI*). Not in the Settings window |
| `chat.saveHistory` / `historyRetentionDays` | `true` / `30` | Conversation history on/off and how long it's kept |
| `chat.memory` | `true` | Add `memory.md` to every chat's system prompt (all connections, CLIs included) |
| `chat.memoryTool` | `ask` | Whether API models may save to `memory.md` with Hotline's remember tool (Anthropic and Gemini even in chat-only mode; OpenAI-style connections once Tool use is on; never while `chat.memory` is off; every save shows a notice): `ask` (approve each; "Always" switches to `allow`), `allow`, `off` |
| `chat.continueIn` | *(llama.cpp of a Local connection)* | The shared "Continue this chat in" targets (see *Continue in* below). Missing = the llama.cpp web UI at your first Local connection's address, or none |
| `chat.voiceAutoSend` | `false` | With `voice`: send the message when you let go of the key (off: check it first, then press Enter) |
| `chat.ocr` | `auto` | Screenshot text, read offline by Windows: `auto` sends it instead of the image to connections whose model can't take images; `always` also adds it alongside images; `off`. Needs a Windows language with "Optical character recognition" (Settings › Time & language › Language & region); Hotline says so if none is installed |
| `chat.attachSelection` | `auto` | Text selected in the app you came from becomes a removable chip when the key opens the panel. `auto`: read through UI Automation (no keystrokes, clipboard untouched; works in most editors, browsers and Office, not in every app). `clipboard`: also permits the restricted Ctrl+C fallback described below (opening can take ~0.5 s). `off`. Password fields are never read |
| `diagnostics.verboseLogging` | `false` | Detailed log (also shows cleaned MCP server stderr excerpts) + key-status line in the panel |
| `diagnostics.testLinks` | `false` | Enables the diagnostic protocol links (`hotline://demo`, `hotline://selftest`, `hotline://grips`). Not in the Settings window |

**Selection clipboard fallback.** Ctrl+C is sent only when UI Automation reports no text pattern, and never to terminals
(Windows Terminal, console hosts, PuTTY, WezTerm, Alacritty, mintty, ConEmu and similar), remote desktop clients, virtual machine
consoles, or editors and IDEs with an integrated terminal (VS Code, Cursor, Visual Studio, JetBrains IDEs): there Ctrl+C is an
interrupt or is forwarded. The built-in list matches window classes and process names (case-insensitive); a window whose process
cannot be identified gets no keystroke. UI Automation remains available in every mode except `off`.
The clipboard must contain only supported memory formats (plain text, HTML, RTF, DIB images, file paths, locale and history/cloud exclusion flags),
totalling at most 64 MB. Unsupported or unreadable formats skip Ctrl+C entirely: bitmap/metafile handles, OLE streams,
Outlook attachments, zip-folder virtual files and unknown registered formats stay on the original clipboard.
After the 450 ms copy deadline, Hotline watches for another two seconds and restores late copies owned by the target
process, without attaching their text. A changed clipboard from another process is left alone, and the sequence and
owner are checked again before restoration. Copies arriving after that watch, copies with no identifiable owner,
and a clipboard that cannot be reopened for restoration remain unrestored. The target's copy can appear in clipboard history.

settings.json is schema version 15. Upgrading a file below 14 converts widths from scaled units to real pixels: the old default
`minWidth` 600 / `maxWidth` 1000 become 400 / 1200, other limits keep their numbers, and saved `width` / `chatWidth` are cleared (one
notice; check Settings > Window). A lower version alone does not rewrite the file; a missing option or a Settings save does. An old
`activation.copilotKey` and `window.layout` are dropped then ([copilot-key.md](copilot-key.md)).

**Files from a newer Hotline.** When `settings.json` has a `schemaVersion` greater than 15, Hotline reads it leniently so settings remain usable in memory, but refuses to overwrite the file on save (connection files continue to save separately). Update Hotline, or edit the file by hand.

**Nothing is replaced without a copy.** Before Hotline overwrites settings.json or a connection file whose content would change, and
that is not exactly what Hotline itself last wrote, it copies it to `<file>.<time>.bak` (never over an older copy; the newest 10 per
file are kept). If the copy can't be made (disk full, locked) that file is left as it is and the panel and Settings › Advanced list
the problem. Hotline's own output replacing Hotline's own output makes no copies.

**A connection keeps its file.** A connection is saved back to the file it was read from, whatever that file is called or whatever
`id` it holds (`mine.json` with `"id": "local"` stays `mine.json`); only a new connection gets `<id>.json` (characters other than
letters, digits, `-`, `_` and `.` become `-`). Two connections that would share one file (`work a` and `work-a`) are not allowed:
the second is not saved and a problem is listed. Removing a connection in Settings deletes its own file; a file you drop in
while Hotline runs is picked up before the next save and is never deleted unless you remove that connection.

### Broken files

A bad hand edit costs only what is wrong, never the whole file, and nothing is deleted or silently overwritten. What is ignored is
listed in the panel (a notice, at most five lines then "and N more"), at the top of Settings › Advanced (with an Open folder button)
and in the log (`settings:` lines, values cut to 40 cleaned characters). Fix the file and the notice goes away by itself.

| File | What happens |
|---|---|
| `settings.json`, one value wrong (a mistyped word for an option, text where a number belongs, `null`, a number too large, an object where a value belongs) | That one setting keeps its default; every other setting applies. The file is not rewritten. A value outside the allowed range is reported too and pulled in to the nearest allowed value. A key given twice uses the last one |
| `settings.json`, not a JSON object, empty, or a syntax error (also a half-saved file) | At startup: defaults run in memory and the file is copied to `settings.json.bad` (not copied again while it has the same text). While Hotline runs: the settings in force stay and the problem is reported once. The file itself is left as it is until you change something in Settings; that writes a valid file and first keeps the damaged one as `settings.json.<time>.bak` |
| `connections\<id>.json`, one value wrong | That value keeps its default (a wrong `approveAllTools` is off, a wrong `tools` is chat-only); the connection loads |
| `connections\<id>.json`, unreadable, not an object, an unknown `type`, or an id already used by another file | The file is not used and not touched (never overwritten or deleted); the notice names it. A file with an ignored value is copied to `<id>.json.<time>.bak` before Settings rewrites it |
| `mcp.json` | A server entry or approval of the wrong kind is skipped alone (an unknown approval word counts as "deny", with a warning). A syntax error or a key given twice makes the whole file unreadable: no servers run and the file is left as it is |
| `prompts\*.md`, `actions\*.md` | A file that is locked, too large (actions), unreadable or badly named is skipped; the rest are listed. A prompt that can't be read falls back to `default.md` |
| `toolbar.json` | See *toolbar.json* below |

## connections\<id>.json

| Key | Applies to | Meaning |
|---|---|---|
| `type` | all | `antigravity`, `claudeCode`, `codex`, `gemini`, `anthropic`, `openAiCompatible`, `local` |
| `name` | all | Shown in the Provider dropdown |
| `model` | all | Model id (empty = the connection's default; Anthropic defaults to `claude-opus-5-5`) |
| `effort` | all | Codex: `low` … `ultra` (`model_reasoning_effort`; what a model supports is Codex's call). agy: Settings and the panel offer `low`/`medium`/`high`, mapped to the nearest level the model has; a hand-edited connection also accepts `max` as a CLI flag. Claude Code and Anthropic: `low`/`medium`/`high`/`xhigh`/`max`. Gemini: `low`/`medium`/`high` thinking level (Gemini 2.5: a thinking budget). OpenAI-compatible and `local`: `low`/`medium`/`high` as `reasoning_effort` (only reasoning models accept it; for `local` also as `chat_template_kwargs.reasoning_effort`, which llama.cpp's `llama-server --jinja` reads for gpt-oss-style models). Empty = the model's default |
| `prompt` | all | System prompt name (`prompts\<name>.md`); empty = `chat.defaultPrompt` |
| `endpoint` | APIs | Base URL (defaults per type, e.g. `http://127.0.0.1:8080/v1` for `local`) |
| `cliPath` | agy, Claude Code, Codex | Program path (empty = auto-detect) |
| `tools` | all | `chatOnly` (default) or `inherit`: CLIs use their own tools and permission rules in `workingDirectory`; API connections use Hotline's MCP tools (`mcp.json`) |
| `workingDirectory` | agy, Claude Code, Codex | Folder for `inherit` mode (empty = your user folder) |
| `approveAllTools` | agy, Claude Code, Codex | ⚠ Dangerous: auto-approve every tool request (inherit mode only; agy/Claude Code `--dangerously-skip-permissions`, Codex `--dangerously-bypass-approvals-and-sandbox`). Settings › AI connections asks before turning it on |
| `args` | agy, Claude Code, Codex | Settings: *Launch flags (advanced)*. Replaces Hotline's usual extra launch flags for the mode. The stream/print flags Hotline needs, and in chat-only mode the flags that keep the program from using its own tools, are always added; flags that approve tools or skip permission checks (`--allowedTools`, `--yolo`, `--trust`, …) are dropped from this list: only `approveAllTools` turns them on |
| `extraArgs` | agy, Claude Code, Codex | Added on top, e.g. Claude Code `--mcp-config C:\mcp\servers.json`, Codex `--search` or `-c model_verbosity="low"` (Codex: sandbox, approval and folder flags are dropped) |
| `keepCliSessions` | agy, Claude Code, Codex | Default `true`: each Hotline chat is one conversation in the CLI's own history, grouped under **Hotline** (see *Chats with a CLI* below), resumed after a restart and from Recent chats. `false`: Hotline keeps the chat and replays it to a new CLI session (Claude Code then keeps nothing; agy always records its runs) |
| `keepInHotline` | agy, Claude Code, Codex | Default `false`: with `keepCliSessions`, Hotline keeps only a small record of the chat (title, connection, session ID, folder). `true`: also its messages in `history\`. Reopened, a record-only chat still shows its messages: they're read from the CLI's own history (`chat.cliTranscripts`) |
| `continueIn` | any | This connection's own "Continue this chat in" targets, replacing `chat.continueIn`; `[]` = none; missing = the shared list |
| `agent` | agy | Custom agent used in chat-only mode (default `hotline`) |
| `images` | APIs | Whether the model reads images (`true`/`false`; default: `false` for `local`, `true` otherwise). With `false`, screenshots go as their text (`chat.ocr`) |
| `maxTokens` | APIs | Most tokens one answer may use (empty = the model's default; Claude API: 64000). Sent as `max_completion_tokens` to api.openai.com and Azure OpenAI, `max_tokens` to other OpenAI-style servers, `maxOutputTokens` to Gemini |
| `temperature` | Gemini, OpenAI-compatible, `local` | 0–2 (empty = the model's default; Gemini 3 works best at its default 1.0). Not sent to OpenAI-style APIs when an `effort` is set (their reasoning models only accept the default); `local` always gets it. The Claude API has none (it rejects one while thinking) |
| `refusalFallback` | Anthropic | If a safety check declines a request, the API re-serves it with a suitable model (default on) |

API connections retry a rate limit or overload (HTTP 429/503/529) up to twice, waiting what the server asks
(`Retry-After`, Gemini's `retryDelay`) or 2 s then 4 s, and say so in the panel. A wait over a minute, or an exhausted
quota, fails at once. Settings › AI connections › **Test connection** lists the models with your key and checks the
chosen one exists.

## Chats with a CLI (agy, Claude Code, Codex)

- **One conversation per chat.** Hotline records the CLI's session ID and resumes it (`agy --conversation`, `claude --resume`,
  `codex exec resume`) after a restart, a prompt change, or from Recent chats. If the CLI can't find it, Hotline starts a new
  conversation and replays its messages. **New chat** (toolbar button, or a long press of the Copilot key) always starts a new one.
- **They run in your user folder** (chat-only mode too), so `claude --resume`, Claude Code's `/resume` and `codex resume` in a terminal
  there list Hotline's chats. agy gets Hotline's working folder (`scratch\agy\Hotline`: the `hotline` agent and attached images) with
  `--add-dir`, and joins the agy project named **Hotline** when it exists (made before 0.3). Chats begun in an older version's folder keep
  resuming there. `inherit` mode runs in `workingDirectory`.
- **Reopened chats show their messages.** When Hotline kept only a record, it reads the conversation read-only from the CLI's own
  history (Claude Code's `~\.claude\projects`, Codex's `~\.codex\sessions`, agy's `~\.gemini\antigravity-cli\conversations`) and removes
  its own wrapping (earlier turns, instructions, attached files). A missing or unrecognised file opens the chat empty; it still resumes.
- **The history list** (🕘 button, Ctrl+↑ in an empty message box, or `/resume`): oldest at the top, newest at the bottom, centered over
  the message bar; type to filter by title, ↑/↓ and Enter (or click) to open. `/resume words` opens it filtered.
- **Continue this chat in ▸** (top of the history list, and the `continueIn` button) opens the chat's own CLI in your default terminal
  on the same conversation. The other installed CLIs are under **More…**: a new session that reads the chat from a Markdown file (only
  when Hotline has the messages). A chat on an API connection lists the CLIs directly.

## Continue in (chat apps and web pages)

"Continue this chat in ▸" (top of the history list) offers, after the CLIs, the targets in `chat.continueIn` (or the
chat's connection's own `continueIn`). Settings › Chat › **Continue in** edits the shared list; a connection's page
picks *Use the shared list*, *Own list* or *None*. Each target:

```json
{ "kind": "web", "name": "llama.cpp", "url": "http://127.0.0.1:8080/?q={chat}&model={model}" }
{ "kind": "openWebUi", "name": "Open WebUI", "url": "http://localhost:3000", "model": null }
{ "kind": "app", "name": "LM Studio", "command": "%LOCALAPPDATA%\\Programs\\LM Studio\\LM Studio.exe", "copyChat": true }
```

| Kind | What happens |
|---|---|
| `web` | `url` is filled and opened in your browser. Only `http`/`https` results open. Longer than 8 000 characters: your last message goes in place of the chat, and the whole chat is copied |
| `openWebUi` | Creates a real chat with every message through Open WebUI's API (`POST {url}/api/v1/chats/new`), then opens it. Its API key lives in Credential Locker (Settings' edit dialog), and is only sent over https or to this PC. `model`: the new chat's model (null = the chat's own) |
| `app` | Starts `command` (a program and arguments, split like a command line; `%VARIABLES%` expanded; never through a shell; `.bat` and `.cmd` files are refused, a console program gets its own terminal window) |

Placeholders: `{chat}` the whole conversation as one message asking the model to continue, `{last}` your last
message, `{file}` the chat saved as Markdown, `{model}` the chat's model. In a URL they are URL-encoded; in a command
each stays inside its own argument. `copyChat: true` also puts the whole chat (Markdown) on the clipboard first.
A target that isn't an object with a known `kind` and a `name` is skipped with a warning in Settings › Advanced; the others
still work. The Open WebUI key is never in a file.
Ready-made entries in Settings: llama.cpp web UI, Open WebUI, LM Studio, ChatGPT, Claude, Gemini.
What the apps accept (checked 2026-10-15): the llama.cpp web UI takes `?q=` (starts a conversation and sends the text) and
`?model=` (picks the model); its import is only a JSON file in its Settings. Open WebUI has the API above. LM Studio has no URL
and no import (its chats are undocumented files), so it opens with the chat copied.

## mcp.json (tools for API connections)

```jsonc
{
  "version": 1, // optional; absent means 1
  "mcpServers": {
    // same format as other MCP apps
    "files": { "command": "npx", "args": ["-y", "@modelcontextprotocol/server-filesystem", "C:\\Users\\you\\Documents"] },
    "off":   { "command": "some-server.exe", "disabled": true, "env": { "TOKEN": "${hotline:secret:some_token}" }, "cwd": "C:\\work" }
  }
}
```

`mcp.json` lists only MCP servers (`approvals` in `mcp.json` is ignored with a warning; non-string `env` values are skipped
with a warning). Hotline writes `mcp.json` only for its starter file, upgrade, or when you click **Move to Credential Locker**
in Settings › **Tools** (after a confirm dialog, backed up under `backups\<time>-save\mcp.json`).

**Secrets:** an `env` value matching `${hotline:secret:<name>}` (letters, digits, `.`, `_`, `-`) is resolved from the Windows
Credential Locker entry `mcp:secret:<name>` when that server starts. A missing secret, or any other value starting with
`${hotline:`, prevents that server from starting and reports an error without affecting other servers.

**Approvals and grants:** A connection uses these tools when its `tools` is `inherit` ("Use Hotline's tools" in
Settings › AI connections). Policy comes from `tools.approvals` rules in `settings.json` (`"deny"` always wins, then
`server/tool`, then `server/*`): `"deny"` and `"ask"` match without regard to case, whereas `"allow"` rules (exact tool or
`server/*`) require exact server and tool case (a case-only match is ignored with a warning). When no rule matches,
**Always allow** grants saved per `(server, tool, launchHash)` in `state.json` apply while `command`, `args` and `cwd` match
the running server. Without a rule or matching grant, tools marked read-only run automatically and everything else asks (`ask`
rules hide the **Always allow** button). Settings › **Tools** lists grants with **Revoke**, shows plaintext `env` keys with
**Move to Credential Locker**, and shows server status. When Windows has the on-device agent registry (`odr.exe`, build
26220.7262+), its connectors are added as `windows-…` servers. Servers start on first use and stop with Hotline, even if it
crashes (they run in its kill-on-close job); their stderr goes to `logs\hotline.log` (at most 20 lines a minute per server,
with resolved secrets and 8+ character `env` values masked).

## Quick actions (actions\<name>.md)

Type `/` at the start of the message to list them (↑/↓, Tab or Enter to pick, Esc to close). `/name some text` sends
the file's text as an instruction followed by your text; `/name` alone applies it to the attachments. The first line is
the description in the list. Add your own by dropping in a file (letters, digits, `-` and `_` in the name); delete or
edit the defaults freely: they're written only when the folder doesn't exist yet; Settings › **Quick actions** ›
*Restore default actions* adds missing defaults and updates untouched ones from an older version (edited files are never
touched). Defaults you haven't edited, and the built-ins `/remember` and `/resume`, describe themselves in the
interface language. Settings › Quick actions lists them with their instructions, and creates, edits (the `.md` file),
renames and deletes them.

## Moving and resizing the panel

Hold **Alt** while the panel has focus (or with the pointer over it): a grip appears on the message bar's outer edge
and handles on the sides. Drag the grip to move the panel; drag a side to change the width, the chat side to change
how tall the chat gets (that height stays until the panel is closed). Right-click the grip for **Save position and
size as default** (writes `window.anchor`, `width`, `chatHeight`) or **Back to the default spot**. A moved panel goes
back to its default the next time it opens, unless it's **pinned**: then the key reopens it where you left it, until
you unpin it, click into another app, or Hotline restarts.

## toolbar.json

Version 3 stores the button rows, their appearance, custom buttons and every panel shortcut, including Stop.
Settings > Window > Button rows edits the layout; Settings > Chat and history edits shortcuts. Changes apply live.

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
    "summarize": { "label": "Summarize", "icon": "E8A5", "quickAction": "summarize", "shortcut": "Ctrl+Shift+1" },
    "thanks": { "label": "Say thanks", "text": "Thanks!", "send": false }
  }
}
```

| Key | Meaning |
|---|---|
| `top`, `bottom` | Rows above and below the message box. Each has `left`, `center` and `right` arrays, in display order. An item appears at its first place in bottom-left, bottom-center, bottom-right, top-left, top-center, top-right order. Leave an id out of all groups to hide it; use empty arrays for empty groups |
| `shortcuts` | Command id to keyboard binding; see [Panel shortcuts](#panel-shortcuts) |
| `menu` | Ordered button ids in a placed `more` button's menu; no pickers, `more`, repeats or items already on the bar. Without a placed `more`, the list is ignored with a warning |
| `style` | Whole-bar `width`, `fill`, `outline`, `filled` and optional `outlineColor` |
| `overrides` | Per-item `width`, `outline`, `filled`, `outlineColor`. Pickers take `span` in place of `width`. Unset fields inherit the bar's style. Known items keep overrides while hidden or in a menu; unknown ids warn and stay on disk |
| `buttons` | Custom definitions keyed by id (below) |

Built-in layout ids are `pin`, `captureWindow`, `captureScreen`, `captureRegion`, `effort`, `model`, `provider`, `prompt`,
`recent`, `continueIn`, `newChat`, `settings`, `more` and `copyLastAnswer`. `stop` is a shortcut command only.

**Width and fill.** A grid cell is 40 × 36 scaled pixels; a button is always one cell high. Numeric `width` accepts 0.5, 1, 2, 3 or 4.
Every shipped button has width 1. Width words and the old `items`, `size`, `cells`, `height` and `spacer` names have no meaning in v3.
With `style.fill` (default `true`), spare row room is shared by button `width` and picker `span`. A button stays at least one cell
wide (half a cell for width 0.5); buttons that cannot fit move into overflow. A picker's span is a share, without a maximum width.
With fill off, buttons use their cell widths and pickers use their preferred span as a maximum. Buttons have a 1-pixel inset on each side.
Settings offers Compact (0.5), Normal (1), Wide (2), 3 and 4; choosing the bar's width clears per-button width overrides.

**Pickers.** Effort, model and provider can sit in any group, between buttons, and keep their span and appearance while hidden.
Their default spans are 3, 4 and 5. Their minimum outer widths are 56, 84 and 72 scaled pixels, capped at their span when fill is off.
In a narrow row they shrink, then hide in effort, provider, model order. Pickers never enter overflow menus.

**Appearance.** `outline: true` draws a 1.5-pixel border in the accent colour, or `outlineColor` (`#RRGGBB`). `filled: true` draws
a background. Buttons and pickers inherit these fields separately from their own override, then the bar, then the embedded defaults.
`fill` controls spacing and is separate from `filled`, which controls appearance.

**Overflow.** A short row moves the center and left groups' last buttons into an automatic “...” menu first, then the right group's
first buttons. At least the rightmost button stays. A placed `more` button stays as long as possible; automatic overflow joins its
`menu` after the explicit entries and a separator. The other row is fitted independently. Settings' **Move what doesn't fit into
the menu** moves current overflow into a placed `more` menu, adding `more` at bottom right when needed.
`ToolbarProfiles.cs` contains reference layouts exercised by the automatic tests and panel self-test.

**Custom buttons.** Up to 24 definitions; each group holds up to 40 ids.

- The id is 1–32 characters from `A-Z`, `a-z`, `0-9` and `-`. Ids match without case throughout placement, menus, overrides, shortcuts, icons and editing. Definition key spelling survives saves; for definitions differing only by case, the first is used and the second warns and stays on disk. Built-in ids, `stop` and `spacer` are reserved in any case. Settings generates lowercase ids.
- `label` is at most 40 characters (default: id). `icon` is a hexadecimal Segoe Fluent Icons code from E000 to F8FF (default: star).
- Exactly one action: `quickAction` names a file in `actions\` (up to 64 characters, never a path); `text` inserts up to 2000 characters.
  `send: true` sends inserted text as plain text. A missing quick action shows a notice.
- Optional `iconFile` is `<id>.png` in `icons\`, matched without regard to id or extension case: PNG, at most 256 KB and 16–256 pixels per side. Missing or invalid pictures use the glyph.
  Settings copies a chosen picture and restores the previous one if saving the toolbar fails. Removing a button removes its picture.
- Optional `shortcut` invokes the action even when hidden or in a menu. Empty or null means no custom shortcut.

**Reading and saving.** Comments and trailing commas are accepted; duplicate keys keep their last value with a warning.
Missing or null fields take embedded defaults in memory. Bad values default individually with warnings. Unknown ids are inactive.
A missing or unreadable file uses defaults; loading never creates or rewrites a file. A file below v3, or without a version, uses
defaults with one warning and is left for the upgrade step. A higher version is read as far as understood and cannot be saved.
Settings checks for outside edits before saving and reloads a changed file with a notice.

An unchanged save preserves exact bytes. An edited save changes only model values that changed, preserves unknown keys at every
level and keeps key order; comments are not retained after an edit. A hand-added `$schema` survives, but Hotline never adds it.
Before replacing outside edits, Hotline copies the original to `backups\<yyyyMMdd-HHmmss>-save\toolbar.json`; same-second sets
receive numeric suffixes. The newest 20 save sets are kept. Files matching `state.json`'s last-written hash need no backup.
A backup failure prevents the write. Other backup reasons and old `.bak`, `.old` and `.bad` files are untouched.

### Panel shortcuts

All panel commands use `toolbar.json`'s `shortcuts` map. Custom button bindings remain under `buttons.<id>.shortcut`.
These bindings work only while the panel has focus and are separate from global `activation.*` hotkeys.

| Command id | Default | Action |
|---|---|---|
| `stop` | `Esc` | Stop a streaming answer |
| `newChat` | `Ctrl+N` | New chat |
| `recent` | `Ctrl+H` | Recent chats |
| `settings` | `Ctrl+,` | Settings |
| `pin` | `Ctrl+P` | Toggle pin |
| `copyLastAnswer` | `Ctrl+Shift+C` | Copy the latest completed, nonempty answer |
| `captureRegion` | `Ctrl+Shift+R` | Capture a region |
| `prompt` | `Ctrl+Shift+P` | Open the prompt library |
| `effort` | `Ctrl+E` | Cycle effort |
| `model` | `Ctrl+M` | Cycle model |
| `provider` | `Ctrl+Shift+M` | Cycle provider / connection |
| `captureWindow` | `Ctrl+Shift+W` | Capture the previous window |
| `captureScreen` | `Ctrl+Shift+S` | Capture the screen |
| `continueIn` | `Ctrl+Shift+I` | Open Continue in |
| `more` | `Ctrl+Shift+O` | Open a placed More menu, otherwise the first automatic overflow menu |

The table is the fixed command conflict order. Stop wins every conflict, including while idle; built-ins claim keys in table order,
then custom buttons in file order. Aliases such as `Control+Q` and `Ctrl+Q` are the same combo. A loser is disabled with a warning;
the requested value stays in the file. Invalid values warn and use the command's default; null and omitted ids default silently.
Unknown command ids are preserved but inactive. Empty strings disable bindings. Settings rejects conflicting combinations.
Reset removes the override. No shortcut is read from settings.json.

Names are case-insensitive. Only Stop allows bare typing keys. Other commands require Ctrl, Alt or Win, except F1–F24.
Enter with any modifiers is reserved. Every command, including Stop, reserves the message box's editing keys: Ctrl+A/C/V/X/Y/Z,
Ctrl+Tab, Ctrl+Backspace, Ctrl+Shift+Z and Ctrl+Shift+Tab. Stop capture accepts the same keys as the file, including bare Esc and
Backspace. In other shortcut rows, Esc cancels capture and Backspace clears it. All cards on a page share the latest toolbar model
and file fingerprint; another card's save is accepted, while an outside edit reloads the page with a notice.
Shortcuts ignore auto-repeat and AltGr; Stop retains its repeat behavior. IME composition suspends shortcuts, Stop and Esc-to-hide.
Idle Esc hides the panel. Hidden buttons still work; pickers cycle only when visible and enabled. Tooltips and accessibility
accelerator keys show effective bindings. Copy last answer skips cancelled or failed partial answers and reports an empty history.

## Backup (Settings > Backup)

Settings > Backup exports the parts you tick to one `hotline-settings-<yyyyMMdd-HHmm>.zip` and imports such a file on
another PC. A package holds `hotline-export.json` (format, app version, date, and every file's size and SHA-256) plus,
per tick: Settings (`settings.json`), Button rows (`toolbar.json` and `icons\*.png`), Tools (`mcp.json`), Sounds
(`sounds\*.wav`), AI connections (`connections\*.json`), Quick actions (`actions\*.md`) and Prompts (`prompts\*.md`).

Never included: logs, chat history, `memory.md`, `backups\`, `.defaults`, `.bak`/`.old` files, and **API keys** (they stay in
the Windows Credential Locker; enter them again on the new PC, the import lists the connections that have none yet).
`mcp.json` is exported exactly as written, and its `env` values can hold secrets: keep such a package private.

An import only accepts the file names above (anything else, `..` paths and oversized or damaged files refuse the package),
checks every file's content, adds and replaces files and never deletes. When the selected categories contain or activate
executable configuration (MCP server commands, arguments, working directories, environment overrides or approval rules,
CLI or tool-enabled connections, `approveAllTools`, `cliPath`, `extraArgs`, `args`, or `Continue in` `app` commands),
Settings > Backup shows the details for review and asks for confirmation before anything is written (Cancel is the default,
and confirming writes the exact bytes that were reviewed). Each file it replaces is first copied to
`backups\import-<time>\` with the same relative path; if that copy fails, nothing is written. The content check is the one
loading uses: a file with a wrong value or a word from a newer version is accepted (the app warns and goes on, as with a hand edit),
a connection file whose `id` differs from its file name is accepted; only a file that is not a JSON object (or a connection with an
unknown `type`) is skipped. A folder that can't be listed is reported as left out, not silently empty. The export keeps every file it
writes importable: a very repetitive text file (which would pass the import's compression-ratio check) is stored uncompressed.
Settings, connections and the bar (`toolbar.json`) apply live (the file watchers pick the files up); sounds and quick actions are
read on their next use.

## Codex

Hotline runs `codex exec --json` once per message, with the message on stdin, and finds `codex.exe` on PATH or inside
an npm global install (`npm install -g @openai/codex`). *Chat only*: a read-only sandbox in your user folder, without
your `config.toml` or rules. *Inherit*: your own Codex setup with a `workspace-write` sandbox in `workingDirectory`
(`approveAllTools` bypasses the sandbox). Codex never asks for approval when run this way, so anything outside the
sandbox is refused. The model list comes from `codex app-server`, else `~\.codex\models_cache.json`, else a built-in list; keep **Default** (Codex's own choice) or type any model name.

## agy permissions

In *inherit* mode agy works under **its own** rules (`~/.gemini/antigravity-cli/settings.json` → `permissions.allow /
deny / ask`, e.g. `command(git status)`, `read_file(C:/path)`). Hotline runs agy in the background, where anything that
would ask is denied, so shell commands need allow rules. Settings › AI connections › **Allow read-only commands** adds a
curated list (and denies `Remove-Item`, `rm`, `git push`, …) after showing it to you.

## App Actions (Click to Do)

Hotline registers App Actions on Windows: **Ask Hotline about this text / this image**, and **Summarize / Translate /
Fix / Explain with Hotline**. Click to Do (Win+click on Copilot+ PCs) and other apps that use the Windows action catalog
list them for selected text or images. The panel opens with the content attached; the four quick actions run at once
when Windows asked for them (another app launching the same link only fills the box — any app can open a link).
Developers: `hotline-action://ask?text=…` or `hotline-action://run/summarize?text=…` (`Public/actions.json` in the package).

## Links

`hotline://settings` opens the settings window and `hotline://tray` starts quietly in the tray. With `"diagnostics": { "testLinks": true }`
in `settings.json`, `hotline://demo` shows an example conversation, `hotline://grips?show=1|0` toggles the Alt grips, and
`hotline://selftest` renders a scripted conversation off-screen and builds every settings page (used by the smoke test; its variants
are in [TESTING.md](TESTING.md)). Protocol `hotline://key` links never start or stop voice input; when a key action is set to `voice`,
a protocol press opens the panel (without reading the selection) and dictation requires a physical key press.
