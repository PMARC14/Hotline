# Configuration reference

Everything lives in `%USERPROFILE%\.hotline` and applies **live** when you save a file (the settings window edits the
same files). Comments and trailing commas are allowed.

| File | What |
|---|---|
| `settings.json` | General options (window, appearance, chat, key actions), `chat.order` (Provider dropdown order) and `chat.defaultBackend` |
| `connections\<id>.json` | One file per AI connection. Add a connection by dropping in a file; remove one by deleting it. A new install has none (the panel offers the CLIs it finds); every connection can be removed |
| `prompts\<name>.md` | System prompts (`default.md` is created on first run) |
| `memory.md` | Notes every chat sees: edit freely, or type `/remember something` in the message box (Settings › Chat and history › Edit memory) |
| `actions\<name>.md` | Quick actions (`/translate`, `/summarize`, `/fix`, `/explain` are created on first run) |
| `mcp.json` | MCP servers and tool approvals (tools for API connections) |
| `toolbar.json` | The button rows around the message box (six groups): which items, in what order, how they look, and your own buttons. Version 2 since 0.4, see *toolbar.json* below |
| `sounds\` | .wav files for `window.openSound` / `closeSound` (Hotline's own `hotline.wav` is put here) |
| `history\*.jsonl` | Saved conversations (text only; deleted after `chat.historyRetentionDays`). CLI chats: only when `keepInHotline` |
| `history\*.meta.json` | A CLI chat's record: title, connection, the CLI's session ID and folder, used by Recent chats to resume it |
| `logs\hotline.log` | Log (crashes are logged as `FATAL`) |
| `scratch\` | Working files for CLI chats: agy's `hotline` agent and attached images (pruned after a day), Codex images, Continue-in handoff files |
| `backups\reset-<time>\` | What Settings › Advanced › Reset replaced |
| `settings.json.old`, `toolbar.json.old` | The previous file, kept when 0.4 replaced it (see *Upgrading to 0.4*). A later replacement is kept as `.old2`, `.old3`, and so on; an archive is never overwritten |
| `.defaults\settings.json`, `.defaults\toolbar.json` | (hidden) The default settings.json and toolbar.json of this version, rewritten at every start |

**Switching settings** is replacing files: copy another `settings.json` (or a file in `connections\`) over the current
one and Hotline applies it right away; `.defaults\settings.json` is a clean start. Settings › Advanced › **Reset
settings** puts settings.json and toolbar.json back to the defaults (connections stay); **Reset everything** also removes the
connections and their API keys, `toolbar.json` and `mcp.json`. Prompts, quick actions, memory and history are never
reset, and the previous files are kept in `backups\reset-<time>\`.

API keys are **not** in these files: they are stored in Windows Credential Locker.

## settings.json

| Key | Default | Meaning |
|---|---|---|
| `general.language` | `system` | Interface language: `system` follows the Windows display language, or one of `en-US`, `es`, `fr`, `de`, `it`, `pt-BR`, `nl`, `pl`, `uk`, `tr`, `ja`, `ko`, `zh-Hans`, `zh-Hant`, `hi`, `id`, `vi`. Applies after restarting Hotline. AI answers follow the language you write in, whatever this says. See [TRANSLATING.md](TRANSLATING.md) |
| `general.supportReminder` | `true` | Shows the "Enjoying Hotline?" donation note once, at the top of the panel the next time you open it after an answer, then turns itself off. Settings › About switches it back on |
| `window.widthPercent` / `minWidth` / `maxWidth` | `40` / `680` / `1000` | Panel width: % of the screen, clamped (DIPs). On upgrading to 0.4, a `minWidth` of exactly 600 (the old default) becomes 680; any other value you set stays |
| `window.height` / `maxHeightPercent` | `0` / `70` | Minimum height (0 = just the message bar) and how much of the screen it may grow to |
| `window.anchor` | `{ "monitor": "active", "x": 0.5, "y": 0.8 }` | Where the panel opens (its saved default spot). `monitor`: `active` (where you're working), `primary`, or a monitor's name (written by the grip's *Save as default*; not connected = `active`). `x`/`y`: fractions of the free space, 0 = left/top, 1 = right/bottom (`y` was `window.verticalPosition` before 0.3) |
| `window.width` | `null` | Panel width in DIPs; `null` = the `widthPercent` rule. A width you saved yourself is kept |
| `window.chatHeight` | `null` | The most the chat grows to, in DIPs; `null` = `maxHeightPercent` |
| `window.growDirection` | `auto` | Which way the chat grows from the message bar: `auto` = up, unless there's no room above (then down); `up` / `down` keep it that way. The message bar never moves |
| `window.openSound` / `closeSound` | `off` | `off`, `windows:<event>` (a sound from the Windows sound scheme, e.g. `windows:Notification.Default`), or a .wav file name in `sounds\` (only that folder) |
| `window.backdrop` | `acrylicThin` | `acrylic`, `acrylicThin`, `mica`, `solid` |
| `window.tintOpacity` / `luminosityOpacity` | `0.15` / `0.35` | Acrylic translucency (0–1; lower = clearer) |
| `window.hideOnBlur` / `alwaysOnTop` | `true` / `true` | Hide when you click elsewhere; keep on top |
| `window.fontSize` / `fontFamily` | Windows default / Segoe UI Variable | Text size (10–32; empty follows Windows' text size) and font |
| `window.scrollbar` | `auto` | `auto` (appears when you scroll or point at the right edge), `visible`, `hidden` |
| `activation.tap` / `hold` | `togglePopup` / `newChat` | Copilot key short / long press. Also `showPopup`, `captureWindow`, `regionSelect`, `voice`, `none`. `voice` as the long press: talk while you hold the key, let go to stop (Esc cancels); as the short press it starts/stops. Uses Windows speech recognition: allow the microphone and turn on Settings › Privacy & security › Speech › Online speech recognition. Settings › General › **Voice input** switches the long press between `voice` and `newChat` |
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
| `chat.stopShortcut` | `Esc` | Stops an answer while it's being written: a key alone (`Esc`) or a combo (`Ctrl+.`, `Ctrl+Shift+Backspace`). Enter never stops an answer (it waits in the box); the Stop button always does. When nothing is being answered, Esc hides the panel |
| `chat.memory` | `true` | Add `memory.md` to every chat's system prompt (all connections, CLIs included) |
| `chat.memoryTool` | `ask` | Whether API models may save a fact to `memory.md` with Hotline's remember tool (Anthropic and Gemini get it even in chat-only mode; OpenAI-style connections once their Tool use is on; never while `chat.memory` is off; every save shows a notice): `ask` (approve each one in the panel; "Always" switches to `allow`), `allow`, `off` |
| `chat.voiceAutoSend` | `false` | With `voice`: send the message when you let go of the key (off: check it first, then press Enter) |
| `chat.ocr` | `auto` | Screenshot text, read offline by Windows: `auto` sends it instead of the image to connections whose model can't take images; `always` also adds it alongside images; `off`. Needs a Windows language with "Optical character recognition" (Settings › Time & language › Language & region); Hotline says so if none is installed |
| `chat.attachSelection` | `auto` | Text selected in the app you came from becomes a removable chip when the key opens the panel. `auto`: read through UI Automation (no keystrokes, clipboard untouched; works in most editors, browsers and Office, not in every app). `clipboard`: in apps that don't share text through UI Automation, also send Ctrl+C and put your clipboard back afterwards (never in terminals or password fields; Visual Studio/VS Code whole-line copies are ignored; the app's copy can show up in clipboard history, the restore doesn't; opening can take up to about half a second). `off`. Password fields are never read |
| `diagnostics.verboseLogging` | `false` | Detailed log + key-status line in the panel |

settings.json is schema version 14 (0.4). See *Upgrading to 0.4* below for what the first start does with an older file.

A half-saved or broken `settings.json` is ignored while Hotline runs (your file is never overwritten without a
timestamped backup); at startup a broken file is copied to `settings.json.bad` and defaults are used in memory.

## connections\<id>.json

| Key | Applies to | Meaning |
|---|---|---|
| `type` | all | `antigravity`, `claudeCode`, `codex`, `gemini`, `anthropic`, `openAiCompatible`, `local` |
| `name` | all | Shown in the Provider dropdown |
| `model` | all | Model id (empty = the connection's default; Anthropic defaults to `claude-opus-5-5`) |
| `effort` | all | Codex: `low` … `ultra` (`model_reasoning_effort`; what a model supports is Codex's call). agy, Claude Code, Anthropic: `low` … `max` (agy maps it to the nearest level the model has). Gemini: `low`/`medium`/`high` thinking level (Gemini 2.5: a thinking budget). OpenAI-compatible and `local`: `low`/`medium`/`high` as `reasoning_effort` (only reasoning models accept it; for `local` also as `chat_template_kwargs.reasoning_effort`, which llama.cpp's `llama-server --jinja` reads for gpt-oss-style models). Empty = the model's default |
| `prompt` | all | System prompt name (`prompts\<name>.md`); empty = `chat.defaultPrompt` |
| `endpoint` | APIs | Base URL (defaults per type, e.g. `http://127.0.0.1:8080/v1` for `local`) |
| `cliPath` | agy, Claude Code, Codex | Program path (empty = auto-detect) |
| `tools` | all | `chatOnly` (default) or `inherit`: CLIs use their own tools and permission rules in `workingDirectory`; API connections use Hotline's MCP tools (`mcp.json`) |
| `workingDirectory` | agy, Claude Code, Codex | Folder for `inherit` mode (empty = your user folder) |
| `approveAllTools` | agy, Claude Code, Codex | ⚠ Dangerous: auto-approve every tool request (inherit mode only; agy/Claude Code `--dangerously-skip-permissions`, Codex `--dangerously-bypass-approvals-and-sandbox`). Settings › AI connections asks before turning it on |
| `args` | agy, Claude Code, Codex | Settings: *Launch flags (advanced)*. Replaces Hotline's launch flags for the mode (the stream/print flags Hotline needs are always added; dangerous flags only via `approveAllTools`) |
| `extraArgs` | agy, Claude Code, Codex | Added on top, e.g. Claude Code `--mcp-config C:\mcp\servers.json`, Codex `--search` or `-c model_verbosity="low"` (Codex: sandbox, approval and folder flags are dropped) |
| `keepCliSessions` | agy, Claude Code, Codex | Default `true`: each Hotline chat is one conversation in the CLI's own history, grouped under **Hotline** (see *Chats with a CLI* below), resumed after a restart and from Recent chats. `false`: Hotline keeps the chat and replays it to a new CLI session (Claude Code then keeps nothing; agy always records its runs) |
| `keepInHotline` | agy, Claude Code, Codex | Default `false`: with `keepCliSessions`, Hotline keeps only a small record of the chat (title, connection, session ID, folder). `true`: also its messages in `history\`. Reopened, a record-only chat still shows its messages: they're read from the CLI's own history (`chat.cliTranscripts`) |
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

- **One conversation per chat.** Hotline records the CLI's session ID and resumes it (`agy --conversation`,
  `claude --resume`, `codex exec resume`) when the session has to start again: after a restart, a prompt change, or from Recent chats. If
  the CLI can't find it any more, Hotline starts a new conversation and replays the messages it has.
- **New chat** (the toolbar button, or a long press of the Copilot key) always starts a new conversation.
- **They run in your user folder.** In chat-only mode too, the CLIs start in `%USERPROFILE%`, so `claude --resume`,
  Claude Code's `/resume` and `codex resume` in a terminal there list Hotline's chats. agy gets Hotline's working folder
  (`scratch\agy\Hotline`: the `hotline` agent and attached images) with `--add-dir`, and joins the agy project named
  **Hotline** when it exists (made by Hotline before 0.3); a conversation you move to another agy project stays there.
  Chats begun in an older version's folder keep resuming there. `inherit` mode runs in `workingDirectory`.
- **Reopened chats show their messages.** When Hotline kept only a record, it reads the conversation from the CLI's
  own history (read-only): Claude Code's `~\.claude\projects\*\<session>.jsonl`, Codex's
  `~\.codex\sessions\…\rollout-…-<thread>.jsonl`, and agy's `~\.gemini\antigravity-cli\conversations\<id>.db`
  (SQLite). Hotline's own wrapping of what it sent (earlier turns, instructions, attached files) is removed. If a file
  is missing or in a format Hotline doesn't recognise, the chat opens empty as before and still resumes.
- **The history list** (the 🕘 button, Ctrl+↑ in an empty message box, or `/resume`): chats run oldest at the top to
  newest at the bottom, centered over the message bar; type to filter by title, ↑/↓ and Enter (or click) to open.
  `/resume words` opens it filtered.
- **Continue this chat in ▸** (top of the history list) opens the chat's own CLI in your default terminal (Windows
  Terminal unless you chose another in Windows Settings › System › For developers › Terminal) on the same conversation.
  The other installed CLIs are under **More…**: a new session that reads the chat from a Markdown file (only when
  Hotline has the messages). A chat on an API connection lists the CLIs directly.

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

## Moving and resizing the panel

Hold **Alt** while the panel has focus (or with the pointer over it): a grip appears on the message bar's outer edge
and handles on the sides. Drag the grip to move the panel; drag a side to change the width, the chat side to change
how tall the chat gets (that height stays until the panel is closed). Right-click the grip for **Save position and
size as default** (writes `window.anchor`, `width`, `chatHeight`) or **Back to the default spot**. A moved panel goes
back to its default the next time it opens, unless it's **pinned**: then the key reopens it where you left it, until
you unpin it, click into another app, or Hotline restarts.

## toolbar.json

The button rows around the message box. Each of the two rows has three groups: **left**, **center** and **right**.
**top left / center / right** sit in the row above the message box, **bottom left / center / right** in the row below it.
Within a group, items run left to right; the center group sits in the free space between the left and right groups. Settings › **Window** › **Button rows** edits this file (planned UI, see below); you can also edit it by hand.

```jsonc
{
  "version": 2,
  "top":   { "left": ["effort", "model", "provider"], "right": [] },
  "items": { "left": ["pin", "captureWindow", "captureScreen", "captureRegion"],
             "center": ["prompt", "recent", "continueIn"],
             "right": ["newChat", "settings"] },
  "style": { "size": "normal", "outline": false, "filled": false },
  "overrides": { "pin": { "outline": true } },
  "buttons": {
    "summarize": { "label": "Summarize", "icon": "E8A5", "quickAction": "summarize" },
    "thanks":    { "label": "Say thanks", "icon": "E8BD", "text": "Thanks!", "send": false }
  }
}
```

| Key | Meaning |
|---|---|
| `top`, `items` | `top` is the row above the message box, `items` the row below it. Each is an object with `left`, `center` and `right` arrays of item ids (`center` is optional). Leave an id out to hide it. An item sits in one place only |
| built-in ids | `pin`, `captureWindow`, `captureScreen`, `captureRegion`, `effort`, `model`, `provider`, `prompt`, `recent`, `continueIn`, `newChat`, `settings`. `continueIn` opens the "Continue this chat in" menu (the chat's own CLI, others under More…) |
| `style` | The look of the whole bar: `size` is `compact`, `normal` or `wide`; `outline` and `filled` are `true` or `false`; `width` and `height` (20-80 DIPs) can also be set bar-wide |
| `overrides` | Per item id, the same `size`, `outline` and `filled`, plus `width` and `height` in DIPs (20-80) for icon buttons. A key you leave out is taken from `style`, then from the defaults |
| `buttons` | Your own buttons, by id (see below) |

**The pickers.** `effort`, `model` and `provider` always stay together as one block. The block goes in the first group
(checked in the order bottom left, bottom center, bottom right, top left, top center, top right) that lists one of them, at that picker's
position, with the pickers in the order of the groups above and then as listed within each group: `effort` in top
left plus `model` in bottom right puts the block in bottom right, `model` first and `effort` after it. They ignore `style` and `overrides`.

**Custom buttons.** Each entry in `buttons` is a button you can list in any group by its id:

- The id is 1-32 characters from `a-z`, `0-9` and `-` (lowercase only), and not a built-in id. Built-in ids in the
  groups are not case-sensitive.
- `label` is the button's name (at most 40 characters; it defaults to the id). `icon` is a Segoe Fluent Icons code in hex, four to six digits
  in the range E000-F8FF; anything else gives a default star.
- `iconFile` (optional) shows a picture instead of the `icon`: it is always `"<id>.png"` (`"summarize.png"` for the
  button `summarize`), a file in `%USERPROFILE%\.hotline\icons\`. Any other name (another button's, a path, `.PNG`) is
  ignored with a warning. Settings › Window › Button rows (add or edit a custom button) › **Choose a picture…** copies the PNG you pick there
  under that name (your original is never changed) when you save; **Use a built-in icon instead** removes it, and
  deleting the button deletes its picture. The picture must be a PNG of at most 256 KB, between 16 and 256 pixels on
  each side (no SVG, ICO or animation); it is drawn at about 60% of the button's smaller side. If the file is missing,
  damaged or outside those limits, the button shows its `icon` instead and the log says why (by file name).
- Exactly one of `quickAction` and `text`. `quickAction` is the **name** of a file in `actions\` (`"summarize"` for
  `actions\summarize.md`, at most 64 characters; never a path; the file must exist); it runs that quick action on the current message and attachments. `text` is
  put in the message box (up to 2000 characters), and `"send": true` also sends it. If the action file is missing, the
  button shows a notice.
- Nothing else can be run from a button: no URLs, files, programs or shell commands.

**Limits.** At most 24 custom buttons and 40 items per group. `width` and `height` are kept between 20 and 80, an
unknown `size` becomes `normal`, and labels lose control characters. A bar too narrow for its buttons is meant to move
the last of the left group into a "..." menu rather than push anything off the panel.

**A hand-edited file.** Comments and trailing commas are allowed. A bad value is replaced by its default on its own,
and the rest of the file still applies; the file is never thrown away for one mistake. In detail:

- An unknown id, an id that isn't text, or an item listed twice or in two groups is ignored; the first place wins
  (checked in the order bottom left, bottom center, bottom right, top left, top center, top right). A v1 `"spacer"`
  left in the file is ignored.
- A key written twice: the last one wins.
- A wrong type for `outline`, `filled` or `size` gives the default for that key. A custom button that is invalid is
  skipped. An override for an item that isn't shown is ignored.
- If the file shows nothing at all by mistake (or only unknown ids), the default layout is used. An empty bar is kept
  only when the `left` and `right` groups of both rows are present and empty (`center` may be left out).
- A file that isn't valid JSON, or whose top level isn't an object, is left untouched and the defaults run until you fix
  it; a half-written file keeps the bar it had.
- Hotline does not rewrite a version 2 file by itself. A missing file is created with the defaults (delete
  `toolbar.json` to get the default bar back at the next start), and a version 1 file is migrated (below). What it had to fix is listed under Settings › This PC and in the log;
  saving from Settings writes the corrected version, and the file it replaces is kept as `toolbar.json.bak` (the comments in
  it are not carried over).
- A `version` higher than 2 is read as far as it makes sense and never rewritten.

**Settings › Window › Button rows (planned).** The editor shows the six groups as cards (three per row), each a strip of its items.
Drag an item to another place in its group or into another group (a marker shows where it lands), or use the buttons
on an item: left and right to reorder, up and down to change rows, and left and right to move one group over
(left → center → right). Items you removed wait in a
"Not shown" strip. Size, outline and fill can be set for the whole bar or per item, and custom buttons are added,
edited and deleted there. Moving a picker moves all three together.

**Reset and defaults.** Settings › Advanced › **Reset settings** also restores `toolbar.json`, with the previous file
in `backups\reset-<time>\`. A hidden copy of the default file is rewritten at every start in
`.defaults\toolbar.json`. New installs get the same layout as before 0.4 (pickers in the top row, the other buttons
bottom left).

**Upgrading from the old format.** Before 0.4, `toolbar.json` had `top` and `items` as plain arrays and a `spacer`
item. On the first start of 0.4 the old file is copied to `toolbar.json.old` and a version 2 file is written that
looks the same: the items before the first `spacer` of a row go left, the ones after it right, further spacers are
dropped. A file that already has `"version": 2` is not touched. The rewrite happens only if the `.old` copy could be
made; otherwise the old file stays, the converted result runs in memory and the next start tries again.

## Upgrading to 0.4

Hotline is pre-1.0, so old files may be replaced rather than converted: the first start of 0.4 keeps `settings.json` as
`settings.json.old` (schema 13 to 14) and writes the new one, and does the same for `toolbar.json` as above. If a
`.old` already exists the copy is called `.old2`, then `.old3`; an archive is never overwritten. Kept values carry
over where that is cheap. The `activation.copilotKey` setting no longer exists (an old entry is dropped; see
[copilot-key.md](copilot-key.md)). **Connections are untouched**: `connections\*.json`, the default connection and the
API keys in Windows Credential Locker stay exactly as they were. As with `toolbar.json`, `settings.json` is
replaced only if its `.old` copy could be made; otherwise it stays and the next start tries again.

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

`hotline://settings` opens the settings window, `hotline://tray` starts quietly in the tray, `hotline://demo` shows an
example conversation, `hotline://selftest` renders a scripted conversation off-screen and builds every settings page
(used by the smoke test); `hotline://selftest?tools` also starts the `mcp.json` servers and logs the result.
