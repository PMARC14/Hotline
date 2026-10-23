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
| `toolbar.json` | The button rows around the message box: which items, in what order, how they look, and your own buttons (version 2; see *toolbar.json*) |
| `icons\<id>.png` | Pictures of custom buttons |
| `sounds\` | .wav files for `window.openSound` / `closeSound` (Hotline's own `hotline.wav` is put here) |
| `history\*.jsonl` | Saved conversations (text only; deleted after `chat.historyRetentionDays`). CLI chats: only when `keepInHotline` |
| `history\*.meta.json` | A CLI chat's record: title, connection, the CLI's session ID and folder, used by Recent chats to resume it |
| `logs\hotline.log` | Log (crashes are logged as `FATAL`) |
| `scratch\` | Working files for CLI chats: agy's `hotline` agent and attached images (pruned after a day), Codex images, Continue-in handoff files |
| `backups\reset-<time>\` | What Settings › Advanced › Reset replaced |
| `settings.json.bak` | The previous settings.json, kept when Hotline rewrites a file you edited by hand (to add newly available options or to move an old inline `chat.backends` list into `connections\`) |
| `toolbar.json.bak`, `toolbar.json.old` | The previous toolbar.json, kept when Settings replaces it (`.bak`: saving the bar; `.old`, then `.old2`, `.old3`…: Settings buttons that rewrite it; an archive is never overwritten) |
| `.defaults\settings.json`, `.defaults\toolbar.json` | (hidden) The default settings.json and toolbar.json of this version, rewritten at every start |

**Switching settings** is replacing files: copy another `settings.json` (or a file in `connections\`) over the current one and
Hotline applies it right away; `.defaults\settings.json` is a clean start. Settings › Advanced › **Reset settings** puts
settings.json and toolbar.json back to the defaults (connections stay); **Reset everything** also removes the connections and their
API keys, `mcp.json`. Prompts, quick actions, memory and history are never reset; replaced files go to `backups\reset-<time>\`.
Settings › **Backup** exports and imports these files (see *Backup*).

API keys are **not** in these files: they are stored in Windows Credential Locker.

## settings.json

| Key | Default | Meaning |
|---|---|---|
| `general.language` | `system` | Interface language: `system` follows the Windows display language, or one of `en-US`, `es`, `fr`, `de`, `it`, `pt-BR`, `nl`, `pl`, `uk`, `tr`, `ja`, `ko`, `zh-Hans`, `zh-Hant`, `hi`, `id`, `vi`. Applies after restarting Hotline. AI answers follow the language you write in, whatever this says. See [TRANSLATING.md](TRANSLATING.md) |
| `general.supportReminder` | `true` | Shows the "Enjoying Hotline?" donation note once, at the top of the panel the next time you open it after an answer, then turns itself off. Settings › About switches it back on |
| `window.widthPercent` / `minWidth` / `maxWidth` | `25` / `460` / `1000` | Panel width at opening: % of the screen, clamped (DIPs). The default `minWidth` is a whole number of buttons across plus the panel padding: 11 buttons x 40 + 2 x 10 = 460 |
| `window.height` / `maxHeightPercent` | `0` / `60` | Minimum height (0 = just the message bar) and how much of the screen it may grow to |
| `window.anchor` | `{ "monitor": "active", "x": 0.5, "y": 0.8 }` | Where the panel opens (its saved default spot). `monitor`: `active` (where you're working), `primary`, or a monitor's name (written by the grip's *Save as default*; not connected = `active`). `x`/`y`: fractions of the free space, 0 = left/top, 1 = right/bottom (`y` was `window.verticalPosition` before 0.3) |
| `window.width` | `null` | Panel width in DIPs; `null` = the `widthPercent` rule. A width you saved yourself is kept |
| `window.chatHeight` | `null` | The most the chat grows to, in DIPs; `null` = `maxHeightPercent` |
| `window.chatWidthPercent` | `0` | The panel grows this wide (% of the screen, 20-90, clamped to `minWidth`..`maxWidth`) when a chat starts and shrinks back on a new chat, around its centre. `0` = it keeps its width; a value no wider than the opening width does the same |
| `window.chatWidth` | `null` | The chat width in DIPs; `null` = `chatWidthPercent`. A fixed `window.width` (a resize by hand saves one) or a panel you resized this opening never grows, like a manual height |
| `window.growDirection` | `auto` | Which way the chat grows from the message bar: `auto` = up, unless there's no room above (then down); `up` / `down` keep it that way. The message bar never moves |
| `window.openSound` / `closeSound` | `off` | `off`, `windows:<event>` (a sound from the Windows sound scheme, e.g. `windows:Notification.Default`), or a .wav file name in `sounds\` (only that folder) |
| `window.backdrop` | `acrylicThin` | `acrylic`, `acrylicThin`, `mica`, `solid` |
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
| `chat.stopShortcut` | `Esc` | Stops an answer while it's being written: a key alone (`Esc`) or a combo (`Ctrl+.`, `Ctrl+Shift+Backspace`). Enter never stops an answer (it waits in the box); the Stop button always does. When nothing is being answered, Esc hides the panel |
| `chat.memory` | `true` | Add `memory.md` to every chat's system prompt (all connections, CLIs included) |
| `chat.memoryTool` | `ask` | Whether API models may save to `memory.md` with Hotline's remember tool (Anthropic and Gemini even in chat-only mode; OpenAI-style connections once Tool use is on; never while `chat.memory` is off; every save shows a notice): `ask` (approve each; "Always" switches to `allow`), `allow`, `off` |
| `chat.voiceAutoSend` | `false` | With `voice`: send the message when you let go of the key (off: check it first, then press Enter) |
| `chat.ocr` | `auto` | Screenshot text, read offline by Windows: `auto` sends it instead of the image to connections whose model can't take images; `always` also adds it alongside images; `off`. Needs a Windows language with "Optical character recognition" (Settings › Time & language › Language & region); Hotline says so if none is installed |
| `chat.attachSelection` | `auto` | Text selected in the app you came from becomes a removable chip when the key opens the panel. `auto`: read through UI Automation (no keystrokes, clipboard untouched; works in most editors, browsers and Office, not in every app). `clipboard`: for apps that don't share text through UI Automation, also send Ctrl+C and restore your clipboard afterwards (never in terminals; whole-line copies from Visual Studio/VS Code are ignored; the app's copy can show up in clipboard history; opening can take ~0.5 s). `off`. Password fields are never read |
| `diagnostics.verboseLogging` | `false` | Detailed log + key-status line in the panel |

settings.json is schema version 14. Hotline is pre-1.0 and does not convert older files: a lower or missing `schemaVersion`
is read as it is (every value checked and clamped) and gets the current version when Settings next saves it; no archive is made. An
old `activation.copilotKey` entry is dropped then ([copilot-key.md](copilot-key.md)). Connection files and API keys are untouched;
the one remaining conversion splits an inline `chat.backends` list into `connections\` files (the old file kept as `settings.json.bak`).

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
| `effort` | all | Codex: `low` … `ultra` (`model_reasoning_effort`; what a model supports is Codex's call). agy, Claude Code, Anthropic: `low` … `max` (agy maps it to the nearest level the model has). Gemini: `low`/`medium`/`high` thinking level (Gemini 2.5: a thinking budget). OpenAI-compatible and `local`: `low`/`medium`/`high` as `reasoning_effort` (only reasoning models accept it; for `local` also as `chat_template_kwargs.reasoning_effort`, which llama.cpp's `llama-server --jinja` reads for gpt-oss-style models). Empty = the model's default |
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

The button rows around the message box. There are two rows, **top** (above the message box) and **items** (below it), each with
three groups: **left**, **center** (the free space between the other two) and **right**. Items run left to right within a group.
Settings › **Window** › **Button rows** edits this file; you can also edit it by hand.

```jsonc
{
  "version": 2,
  "top":   { "left": ["effort"], "center": ["model"], "right": ["provider"] },
  "items": { "left": ["pin", "captureWindow", "captureScreen", "captureRegion"],
             "center": ["prompt", "continueIn"],
             "right": ["recent", "newChat", "settings"] },
  "menu": [],
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
| `top`, `items` | Objects with `left`, `center` (optional) and `right` arrays of item ids. Leave an id out to hide it. An item sits in one place only |
| built-in ids | `pin`, `captureWindow`, `captureScreen`, `captureRegion`, `effort`, `model`, `provider`, `prompt`, `recent`, `continueIn`, `newChat`, `settings`, `more`. `continueIn` opens the "Continue this chat in" menu; `more` is the "..." button you place yourself (below) |
| `menu` | Ids of buttons (built-in or custom; no pickers, not `more`, no repeats) shown in the `more` button's menu instead of on the bar, in this order |
| `style` | The look of the whole bar: `size` (`compact`, `normal`, `wide`), `outline` and `filled` (`true`/`false`), `outlineColor` (`"#RRGGBB"`; left out: the accent colour), `width` and `height` (20-80 DIPs) |
| `overrides` | Per item id: the same `size`, `outline`, `outlineColor`, `filled`, `width`, `height`. A key you leave out comes from `style`, then the defaults. For a picker only `span` |
| `buttons` | Your own buttons, by id (below) |

**Cells and sizes.** Every item is a **cell** of a grid whose unit is 40 × 36 DIPs (icon 16). `compact` is half a cell (at least
20 × 20, icon 13), `normal` one cell, `wide` two (80 × 72, icon 19). The size is the cell's whole size, padding included: cells touch
with no gap, and the visible button is the cell less 1 DIP on each side (a normal button shows 38 × 34, so neighbours are 2 DIPs
apart). So two normal buttons are as wide as one wide or four compact ones, and a row is the sum of its cells. `width`/`height` win over
`size` but stay within 20-80. Custom buttons never exceed 80 × 80 (icon 22). The "..." button takes the bar's size. In Settings the
bar's size shows **Custom** while some button has its own; picking a size there clears every button's own size.
The bar's room is the panel's width less its padding (10 DIPs on every side).

**Outline.** `"outline": true` draws a 1.5-DIP border in the accent colour (a contrast theme: the system highlight colour) or in
`outlineColor` (`#` and six hex digits; anything else is ignored with a warning). With `filled` a button keeps both its background and outline.

**Pickers.** `effort`, `model` and `provider` are three independent items: each can sit in any group of either row, between any
buttons, and be moved, hidden or sized on its own. An id listed twice is shown at its first place (bottom left, bottom center, bottom
right, top left, top center, top right). Pickers ignore `style`. Their width is `span` cells of 40 DIPs (1-8, whatever the bar's size);
`span` is the only `overrides` key a picker takes (anything else is dropped with a warning):

```jsonc
"overrides": { "model": { "span": 6 }, "effort": { "span": 1 } }
```

| Picker | Default `span` (shipped file) | Width (DIPs) | Narrowest before it hides (outer width) |
|---|---|---|---|
| `effort` | 2 (3) | 80 (120) | 56 |
| `model` | 4 (4) | 160 | 84 |
| `provider` | 3 (4) | 120 (160) | 72 |

A span is a maximum as well as the preferred width (the drop-down itself is 1 DIP narrower on each side). In a short row the pickers
shrink evenly down to the narrowest width (never past their own width); when that is not enough they hide one at a time: `effort`,
then `provider`, then `model`. A span for a hidden picker is kept. Pickers never go into an overflow menu: the row first moves
buttons there (left group, then center, from their ends, then the right group's first buttons) so the pickers keep their narrowest
widths, and only when not even one right button, the "..." button and those widths fit do the pickers shrink and hide. Pickers of one
row share what is left evenly; the other row is fitted on its own.

**Overflow and the `more` button.** A row too narrow for its buttons moves them into an automatic "..." menu: the center group's
and left group's last buttons first; if the right group alone is too wide it gives up its first buttons (from its left end), so the
rightmost (typically Settings) stay, and at least one right button always stays. The menu lists buttons in bar order. Only a panel
narrower than about two buttons can make a row stick out.
List `more` in any group to place that "..." button yourself, with a menu you fill: the ids in `menu` are not drawn on the bar and
appear in that menu (a toggle such as Pin stays a toggle). If a row holds `more`, that row's automatic overflow joins the same menu
after your entries and a separator, with no second "..." button; a row without `more` keeps its own. `more` is the last button to
leave a short row; with nothing in the menu it still opens a disabled "Nothing here". A `menu` id that is also in a group stays in the
group (warning); unknown ids, pickers, `more` and repeats are dropped (warning); `menu` without a `more` button is ignored (one
warning). Settings › Button rows has a card for this box (drag items in and out, **Move to the ... button's box** on each item) and a
button **Move what doesn't fit into the menu**, which plans the bar at the panel's current width and moves what would overflow into
the box (adding `more` at the end of the bottom-right group if there is none).

**Reference layouts.** `ToolbarProfiles` holds complete `toolbar.json` examples, tried automatically at every panel width and bar size
and drawn by the panel's self-test (steps P01-P33); use any as a starting point:

| Name | What it shows |
|---|---|
| `default` | the shipped layout: pickers across the top row, nine buttons in three groups below |
| `pickers-bottom-left-before` / `-after` / `-between` | the pickers in the bottom-left group before, after, or in the middle of the buttons |
| `pickers-bottom-centre` / `-crowded` | the pickers in the bottom-centre group, with few or many buttons on their left |
| `pickers-bottom-right` / `-after-buttons` | the pickers in the bottom-right group, alone or after some buttons |
| `pickers-top-right`, `pickers-top-centre-both-rows` | the pickers in the top row's right or centre group (centre: with buttons in both rows) |
| `pickers-apart-corners`, `picker-between-buttons`, `pickers-one-per-row-right` | pickers placed one by one: effort top left, model top right, provider bottom centre; one picker between buttons; one in each row's right group |
| `more-menu-bottom-right-with-5-items`, `more-menu-top-centre-with-overflow`, `more-in-both-rows` | `more` last in the bottom-right group with five buttons in its menu; `more` in the top-centre group with its row's overflow merging in; one row with `more` beside a crowded row that keeps its automatic "..." |
| `spans-all-1`, `spans-all-8`, `spans-mixed` | every picker 1 or 8 cells wide; spans 1, 4 and 8 |
| `all-compact`, `all-wide`, `mixed-button-sizes` | the whole bar compact or wide; a different size on several buttons |
| `custom-24-pickers-bottom`, `custom-24-wide-pickers-bottom-centre` | 24 custom buttons around the pickers in the bottom row |
| `only-pickers`, `only-right-group`, `all-items-in-one-group` | a row of pickers only; a right group only; every possible item (36) in one group |
| `empty-bar`, `damaged-file` | an explicitly empty bar; a damaged file that still renders |

**Custom buttons.** Each entry in `buttons` can be listed in any group by its id:

- The id is 1-32 characters from `a-z`, `0-9` and `-`, and not a built-in id (built-in ids in the groups are case-insensitive).
- `label` is the name (at most 40 characters; default the id). `icon` is a Segoe Fluent Icons code in hex (four to six digits,
  E000-F8FF); anything else gives a default star.
- `iconFile` (optional) shows a picture instead: always `"<id>.png"` in `%USERPROFILE%\.hotline\icons\` (any other name is ignored with
  a warning). It must be a PNG of at most 256 KB, 16-256 pixels per side (no SVG, ICO or animation); it is drawn at about 60% of the
  button's smaller side. If it is missing, damaged or outside those limits, the button shows its `icon` and the log says why (by file
  name). Settings (add or edit a custom button) › **Choose a picture…** copies your PNG to that name when you save (the original is never
  changed); **Use a built-in icon instead** removes it; deleting the button deletes its picture.
- Exactly one of `quickAction` and `text`. `quickAction` is the **name** of a file in `actions\` (`"summarize"` for `actions\summarize.md`;
  at most 64 characters, never a path); it runs on the current message and attachments, and a missing file shows a notice. `text` (up to
  2000 characters) is put in the message box, and `"send": true` also sends it.
- Nothing else can be run from a button: no URLs, files, programs or shell commands.

**Limits.** At most 24 custom buttons and 40 items per group. An unknown `size` becomes `normal`; labels lose control characters.

**A hand-edited file.** Comments and trailing commas are allowed. A bad value is replaced by its default alone, a key written twice
uses the last, and the rest of the file still applies. An unknown id, an id that isn't text, or an item in two places is ignored (the
first place wins, in the order above); a `spacer` item (it no longer exists) is ignored; an invalid custom button is skipped; an override
for an item that isn't shown is ignored. A file that shows nothing by mistake gets the default layout; an empty bar is kept only when
the `left` and `right` groups of both rows are present and empty. A file that isn't valid JSON or an object is left untouched and the
defaults run (a half-written file keeps the bar it had). A `version` above 2 is read as far as it makes sense and never rewritten, and
so is a version 2 file with warnings; Hotline never rewrites a version 2 file by itself. A missing file is created with the defaults.
**A version 1 file** (`top` and `items` as plain arrays, with a `spacer`; before 0.4) is not converted: the defaults run with a
warning and the file is left as it is. To change the bar, write the version 2 format or delete the file. What was ignored is listed in
the panel, under Settings › About › This PC and in the log. Saving from Settings writes the corrected file and keeps the one it replaces
as `toolbar.json.bak` (comments are not carried over).

**Reset and defaults.** Settings › Advanced › **Reset settings** also restores `toolbar.json` (the previous file goes to
`backups\reset-<time>\`); `.defaults\toolbar.json` is rewritten at every start. New installs get the owner's layout: effort, model and
provider across the top row (spans 3, 4, 4); pin and the three captures on the bottom left, prompt and Continue in in the centre,
recent, new chat and settings on the right.

## Backup (Settings > Backup)

Settings > Backup exports the parts you tick to one `hotline-settings-<yyyyMMdd-HHmm>.zip` and imports such a file on
another PC. A package holds `hotline-export.json` (format, app version, date, and every file's size and SHA-256) plus,
per tick: Settings (`settings.json`), Button rows (`toolbar.json` and `icons\*.png`), Tools (`mcp.json`), Sounds
(`sounds\*.wav`), AI connections (`connections\*.json`), Quick actions (`actions\*.md`) and Prompts (`prompts\*.md`).

Never included: logs, chat history, `memory.md`, `backups\`, `.defaults`, `.bak`/`.old` files, and **API keys** (they stay in
the Windows Credential Locker; enter them again on the new PC, the import lists the connections that have none yet).
`mcp.json` is exported exactly as written, and its `env` values can hold secrets: keep such a package private.

An import only accepts the file names above (anything else, `..` paths and oversized or damaged files refuse the package),
checks every file's content, adds and replaces files and never deletes. Each file it replaces is first copied to
`backups\import-<time>\` with the same relative path; if that copy fails, nothing is written. Settings and the bar apply
live (the file watchers pick the files up); sounds, button rows and quick actions are read on their next use.

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
