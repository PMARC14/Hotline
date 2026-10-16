# Plan 9: CLI chats live in the CLI's history, resumable, and "Continue in…"

Status: **approved 2026-10-12** (llama.cpp / web page target: to be decided later)

## Goal

A chat with a CLI connection (Antigravity, Claude Code, Codex) is one conversation in that CLI's own history, grouped
under **Hotline**, resumable from Hotline's Recent chats and from the CLI itself, and can be moved into a terminal
window with **Continue this chat in ▸**. API and local connections keep using Hotline's own history.

## How it worked before (design pass)

- Claude Code and Antigravity keep one process per Hotline chat. When that process ends (restart, prompt change,
  idle stop, Recent chats), the next message starts a **new** CLI conversation and replays the transcript into it.
  Nothing recorded the CLI's session ID, so a Hotline chat ended up as several CLI conversations, and nothing could
  resume one.
- agy always keeps its conversations (about 150 on this PC, 63 from Hotline, most of them one turn long). Claude Code
  kept them only with `keepCliSessions` (off by default).
- Hotline kept every chat's full transcript in `history/<id>.jsonl`.

## Checked against the CLIs (agy 1.3.3, claude 2.1.x)

| | Session ID | Resume | Unknown ID |
|---|---|---|---|
| agy | `{"event":"init","conversation_id":…}` | `--conversation <id>`: the same ID, context kept | warns and silently starts a new conversation (new ID) |
| Claude Code | `{"type":"system","subtype":"init","session_id":…}` | `--resume <id>`: same ID, context kept (needs the same folder) | `No conversation found…`, error result, no init |

- agy projects: `--new-project` creates a project **named after the working folder** (stored in
  `~/.gemini/config/projects/<id>.json` with the folder); `--project <id|name>` uses an existing one and silently
  falls back to the default project when there is none.
- Claude Code and Codex group sessions by working folder.

## Decisions

1. **Long press of the Copilot key (New chat) always starts a new conversation**, for every connection.
2. **CLI connections keep chats in the CLI's history by default** (`keepCliSessions`, now default on for Antigravity,
   Claude Code and Codex; existing connections are switched on by the settings migration).
3. **Hotline keeps the transcript too only when asked:** new per-connection switch `keepInHotline` (default off). For
   API and other connections Hotline keeps its own history as before. With `keepCliSessions` off, Hotline keeps the
   transcript (otherwise nothing would).
4. **Recent chats still lists every chat:** for a CLI chat without a Hotline transcript, Hotline keeps a small record
   (`history/<id>.meta.json`: title from the first message, connection, CLI session ID, folder, time). Opening it
   resumes the CLI session; the panel says the earlier messages are in that CLI's history.
5. **Grouped under "Hotline":** chat-only CLI chats run in a folder named `Hotline` (agy: `…\agy\Hotline`, Claude Code:
   `…\claude\Hotline`, Codex: `…\codex\Hotline`), so Claude Code and Codex list them under that folder. For agy, a new
   conversation joins the agy project for that folder (found read-only in agy's project list) or creates it with
   `--new-project` (named "Hotline"). **Only new conversations** get a project; a resumed one keeps whatever project
   the user moved it to. Inherit mode runs in the user's own folder and gets no project flags.
6. **Resume:** every chat records its CLI session ID and folder. When a chat's process has to start again, it resumes
   (`--conversation` / `--resume` / `codex exec resume`) in the original folder instead of replaying. If the resume
   fails before any answer text (unknown ID, different ID reported), Hotline starts a new conversation and replays the
   transcript if it has one; without one it says so and suggests a new chat.
7. **Continue this chat in ▸** (in the Recent chats menu): the chat's own CLI with its resume command; other installed
   CLIs get a handoff file (Markdown of the transcript; the first prompt points at it), only when Hotline has the
   transcript. Started as a normal console program, so Windows opens it in the user's **default terminal** (Windows
   Terminal unless they chose another in Settings › System › For developers › Terminal).
8. **Web page target (llama.cpp / routing page):** later.

## Settings and docs to keep current

- `keepCliSessions` (default true for CLI types) and `keepInHotline` (default false): Settings › Connections switches
  for each CLI connection; `docs/configuration.md`; schema 11 migration.
- Folder changes need no setting; old `agy-workspace` content is left to the attachment cleanup.

## Tests

- Core (TDD): parsers report the session ID; the controller records it, writes the meta record, passes it back for
  resume, and drops it on New chat; transcript kept or not per connection; Recent lists meta-only chats; resume of a
  meta-only chat; backends resume with the right flags and folder, fall back on an unknown ID; agy project lookup;
  the Continue-in command builder; settings defaults and migration.
- Real CLIs (manual, a few tiny prompts): resume after a Hotline restart, unknown ID fallback, Continue in a terminal.
