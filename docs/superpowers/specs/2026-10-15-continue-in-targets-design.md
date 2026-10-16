# Continue in: chat apps and web pages — design

Date: 2026-10-15. Branch `continue-in-targets` (stacked on `history-actions`, PR #27).

## Why

"Continue this chat in ▸" only knows the CLIs (Claude Code, Codex, agy): the chat's own CLI resumes its session, another
CLI reads a handoff file. Chats on API connections (Claude, Gemini, OpenAI-compatible) and Local models have nowhere to
go. People already have a chat app for longer sessions — Open WebUI, the llama.cpp web UI, LM Studio — and those apps
can talk to local models and to other providers' APIs alike. Continue in should take any chat there in one click.

**Success:** from any chat, "Continue this chat in ▸ Open WebUI" opens it there as a real chat with the whole
conversation; other targets open with the conversation ready (sent as one message, attached as a file, or copied).

## What the apps allow (checked 2026-10-15)

| App | What an outside app can do | Full copy |
|---|---|---|
| Open WebUI | `POST /api/v1/chats/new` with the user's API key creates a chat with all messages; the page is `{base}/c/{id}` | yes |
| llama.cpp web UI | `?q=` starts a new conversation and sends the text; `?model=` picks the model (`tools/ui/src/routes/(chat)/+page.svelte`). Import is only a JSON file in its Settings | as one message |
| LM Studio | chats are undocumented JSON files the docs say not to rely on; no import, no URL | no: open it with the chat copied |

## Data model

A **target** is one entry in a list:

```json
{ "kind": "web", "name": "llama.cpp", "url": "http://127.0.0.1:8080/?q={chat}&model={model}" }
{ "kind": "openWebUi", "name": "Open WebUI", "url": "http://localhost:3000", "model": null }
{ "kind": "app", "name": "LM Studio", "command": "%LOCALAPPDATA%\\Programs\\LM Studio\\LM Studio.exe", "copyChat": true }
```

- `kind`: `web` (open a URL template in the default browser), `openWebUi` (create a real chat through its API, then open
  it), `app` (start a program; a console program opens in the user's default terminal, as the CLIs do today).
- `name`: shown in the menu.
- `url`: `web` — the template; `openWebUi` — the Open WebUI address.
- `command`: `app` — the program, then arguments, split like a command line (quotes group). Environment variables
  (`%LOCALAPPDATA%`) are expanded. Never run through a shell.
- `model`: `openWebUi` — the model the new chat uses; null = the chat's own model (`{model}`).
- `copyChat`: also put the whole chat (Markdown) on the clipboard first. Default false; true in the LM Studio preset.

**Placeholders** (in `url` and in `command` arguments):

| Placeholder | Value |
|---|---|
| `{chat}` | the whole conversation as one message asking the model to continue it (see below) |
| `{last}` | the user's last message |
| `{file}` | the chat saved as Markdown (the existing handoff file, `CliHandoff.Markdown`) |
| `{model}` | the model the chat used (empty if unknown) |

In a URL each value is URL-encoded; in a command each is one argument (or part of one), unquoted text never reaches a
shell.

**`{chat}` text:** "Here is a conversation from another app. Continue it: reply to the last message.\n\n" + the
Markdown transcript (titles `## User` / `## Assistant`, attachments by name).

**Where lists live:**

- `settings.json` › `chat.continueIn`: the **shared list**, used by every connection.
- `connections\<id>.json` › `continueIn`: a connection's **own list**, replacing the shared one for that connection;
  `[]` = no targets for it; missing = the shared list.
- Missing `chat.continueIn` = the default shared list: the llama.cpp web UI at the first Local connection's address
  (`{origin}/?q={chat}&model={model}`), or empty when there is no Local connection. No schema migration: missing means
  default.

**Ready-made entries** (Settings › Add from a list): llama.cpp web UI (asks for the address, default
`http://127.0.0.1:8080`), Open WebUI (default `http://localhost:3000`), LM Studio (app, copy chat), ChatGPT
(`https://chatgpt.com/?q={chat}`), Claude (`https://claude.ai/new?q={chat}`).

**The CLIs** (Claude Code, Codex, agy) keep their entries exactly as today — the chat's own CLI resumes its session,
other installed CLIs read the handoff file — and the list's targets follow them.

## The menu

"Continue this chat in ▸" (top of the history list) shows, in order: the chat's own CLI (resume), other installed CLIs
(handoff), then the targets of the chat's connection (its own list, or the shared one). Targets need the chat's
messages, so like the CLI handoffs they are offered only when the panel has the whole transcript (not a chat reopened
from a record only). Nothing to offer = the button is hidden, as now.

## Running a target

All run off the UI thread where they do I/O; any failure shows a notice ("Couldn't open {name}: {reason}") and the
panel stays (it hides only once the target has started, as now).

**web**
1. Fill the template; refuse anything but `http`/`https` after filling (a template can't become `file:` or a script
   URL).
2. If the filled URL is longer than 8 000 characters (browsers and servers start refusing long URLs), fill `{chat}`
   with the last message instead, copy the whole chat to the clipboard, and say so in a notice.
3. Open it with the default browser (`Launcher`), hide the panel.

**openWebUi**
1. The API key comes from Credential Locker (`continue:<target url>:apiKey`); it is only sent over https or to the
   same PC (the existing `ApiCommon` rule: https or loopback). No key → a notice that points to Settings.
2. `POST {url}/api/v1/chats/new` with `Authorization: Bearer <key>` and
   `{ "chat": { "title", "models": [model], "messages": [...], "history": { "messages": {id: …}, "currentId" } } }`:
   every message gets a new UUID, `parentId`/`childrenIds` chain them in order, roles `user`/`assistant`,
   `content` the text (attachments by name, as in the handoff file), `timestamp` the message time.
3. Open `{url}/c/{id}` in the browser.
4. 401/403 → "Open WebUI refused the API key"; unreachable → "Open WebUI isn't running at {url}".

**app**
1. Expand `%VAR%`, split into program + arguments, fill placeholders (writing the handoff file if `{file}` is used).
2. If `copyChat`, copy the Markdown chat to the clipboard (through the existing `ClipboardOwner`).
3. Start the program (`UseShellExecute = false`, arguments as a list; a console program gets the user's terminal).

## Settings

- **Settings › Chat › Continue in:** the shared list. Each entry is a card (name, kind, address or command) with Edit
  and Remove; **Add** offers the ready-made entries and "Web page / Open WebUI / App". Editing is a small dialog with
  the fields of its kind; Open WebUI's dialog has an *API key* box (stored in Credential Locker, never in a file) and a
  *Test* button (lists its models). `settings.json` holds everything except the key.
- **Connection editor › Continue in:** *Use the shared list* (default) / *Own list* (the same editor, for this
  connection) / *None*.
- New texts in all 17 languages.

## Components

- Core `Backends/ContinueTargets.cs`: the `ContinueTarget` record, `For(profile, settings)` (own list / shared /
  default), default and ready-made entries, `Fill(template, values, urlEncode)`, the 8 000-character fallback, command
  splitting. Unit-tested.
- Core `Backends/OpenWebUiChat.cs`: builds the `chats/new` request body from the messages and parses the reply.
  Unit-tested against a fake HTTP handler.
- App `Chat/ContinueIn.cs`: adds the targets after the CLI entries and runs them (browser, clipboard, process start).
- App Settings: the shared-list section and the connection editor option.

## Safety

- Templates come from the user's own settings; filled URLs must be `http`/`https`; commands start without a shell, with
  arguments as a list, so chat text can't run as a command.
- The Open WebUI key lives in Credential Locker and only goes over https or loopback.
- The clipboard is written only when the user picks a target with `copyChat` (or the long-URL fallback), never read.

## Testing

- Core: target resolution (own list, `[]`, shared, default with and without a Local connection), placeholder filling
  and encoding, the http/https rule, the long-URL fallback, command splitting and `%VAR%` expansion, the Open WebUI
  request body (UUID chain, roles, order) and error mapping.
- App: the self-test builds the new settings pages; by hand: llama.cpp web UI (`?q=`), Open WebUI (a real chat with
  every message), LM Studio (opens, chat on the clipboard), ChatGPT/Claude links.

## Not in this design

- Writing LM Studio's chat files (undocumented; revisit if its format becomes safe to rely on).
- Importing back from those apps into Hotline.
- Jan and other apps (an `app` or `web` entry covers them by hand).
