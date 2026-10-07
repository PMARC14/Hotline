# Hotline Plan 6 — public-ready build

Branch `plan6-public` (from `main` after PR #1), draft PR #2. Goal: the build the user makes public, once they've done
signing and real-key API tests themselves. Read `docs/HANDOFF.md` and `CLAUDE.md` first.

## Ground rules (from the user)

- TDD in `src/Hotline.Core`; app changes are verified with `tests/smoke/smoke.ps1 -Install`. Leave the panel **closed**
  after tests. No keystrokes, clipboard use or visible windows on the user's PC without asking.
- Settings live in files under `%USERPROFILE%\.hotline` and apply live. New options go in `settings.json` (bump the
  schema version with a migration) or in their own file. Document them in `docs/configuration.md`.
- No browser UI, no Ollama, no Azure OpenAI. Keep the panel light.
- Commits: `pmarc14 <16502495+PMARC14@users.noreply.github.com>` (already the global git identity; GitHub blocks pushes
  that use the old address) and the Co-Authored-By trailer. Push as you go.
- Reviews after each feature: agy with `--model claude-opus-5-5-high` and `--model gemini-3.8-flash-high` (never
  3.1 Pro). Paste the code into the prompt and say "do not use any tools". Split into chunks under about 30k characters
  (see HANDOFF → hard-won facts). Verify every finding against the code before fixing it.

## Tasks (suggested order)

### 1. MCP servers in the kill-on-close job (crash-proof cleanup)

**Problem:** the MCP SDK's `StdioClientTransport` starts server processes itself, so `ChildProcessJob` never sees them,
and a Hotline crash leaves node/npx/uvx servers running.

**Plan:**
- Start the process ourselves: `Process` with redirected stdin/stdout/stderr, `job.Add(process)`. Grandchildren
  inherit the job.
- Hand its streams to the SDK's stream-based client transport. Verify the exact type in ModelContextProtocol.Core
  2.2.0, probably `StreamClientTransport(Stream serverInput, Stream serverOutput)`.
- Wire it up: `StdioMcpSession.ConnectAsync` gets a process-start delegate. The App passes one that adds the process to
  the job; tests pass a fake.
- Keep env, cwd and args handling identical. Resolve `npx`/`.cmd` shims the way the SDK did (check what it does on
  Windows).
- Drain stderr to the log, rate-limited.

**Done when:** killing Hotline.exe leaves no MCP server processes behind. Test with the real filesystem server from
`npx`.

### 2. API polish (needed for public use)

**Effort pickers:**
- Gemini API: thinking level via `generationConfig.thinkingConfig`. Look up the current field and allowed values first
  (agy + `gemini-3.8-flash-high`, or web research).
- OpenAI-compatible: `reasoning_effort` (already sent when set in the connection file). Show the picker with the levels
  the model accepts, and hide it for `local`.
- Follow the uniform low/medium/high mapping already used elsewhere (`ConnectionTypes.EffortLevels`).

**Retries:**
- Retry 429/503/529 with backoff in `ApiCommon.SendAsync`. Honour `Retry-After`, at most 3 tries.
- Never retry once streaming has started.
- Show "rate limited, retrying in Ns" as a status note.

**Test connection:**
- A button per API connection in Settings › AI connections: list models (or send a tiny request) and show OK/the error.
- Uses `ModelCatalog`; no key in logs.

**Tests:** fake HTTP handlers, as in `*BackendTests.cs`.

### 3. Quick actions (`/translate`, `/summarize`, `/fix`, `/explain`)

- `~/.hotline/actions/<name>.md`: an instruction applied to the message (or to the attachment/selection when the
  message is empty).
- Typing `/` at the start of the composer opens a small suggestion list (keyboard navigable).
- First run writes the four defaults. Users add their own by dropping in files, which apply live.
- Core: parsing and expansion in `Hotline.Core` (pure, tested). App: suggestion flyout plus composer integration.

### 4. Selected text → attachment

- On key press, before showing the panel, read the selection from the foreground window. Use UI Automation (focused
  element → `TextPattern.GetSelection()`), which needs no keystrokes and doesn't touch the clipboard.
- If there's a selection, add it as a removable text chip ("Selected text from <app>").
- Setting `chat.attachSelection`: `auto` (UIA only, default) | `off` | `clipboard`. `clipboard` is opt-in: send Ctrl+C,
  then restore the clipboard exactly. Explain the trade-off in the docs.
- Timeout of about 150 ms so opening the panel never feels slower.
- Core: the setting and size limits. App: the UIA reader.

### 5. Screenshot text for text-only models (OCR)

- `Windows.Media.Ocr` (offline, built in): `OcrEngine.TryCreateFromUserProfileLanguages()`.
- For connections whose model can't take images, attach the recognized text alongside or instead of the image, as
  "Text in screenshot:". Track image support per connection type, or let the user set `images: false`. Default:
  `local` → no images unless set.
- Setting `chat.ocr`: `auto` (text-only connections) | `always` | `off`.
- If no OCR language is installed, say so once.

### 6. Voice (push-to-talk)

- `activation.hold = "voice"`: hold the Copilot key to dictate, release to stop. The text lands in the composer and is
  sent if `chat.voiceAutoSend` is set (default off). The hold start/stop events already arrive in `ActivationRouter`.
- `Windows.Media.SpeechRecognition` (continuous dictation). Needs the `microphone` capability in
  `Package.appxmanifest`.
- Check which privacy setting it needs: the "Online speech recognition" toggle vs offline language packs. Handle denial
  with a clear message and a link to the settings page.
- Show a small listening indicator in the panel. Esc cancels.

### 7. Public-ready polish

- README:
  - install from Releases (`.msix` / `.appinstaller`; the self-signed note until signing)
  - feature list with the new items
  - fresh screenshots via `hotline://demo` (blur anything personal)
- `docs/configuration.md` covers every new option. Update `THIRD-PARTY-NOTICES.md` if dependencies change.
- Final reviews (Opus and Gemini Flash via agy), fix findings, keep `docs/HANDOFF.md` current, mark PR #2 ready.

## Known limits to keep in mind (from HANDOFF)

- Tool calls and results live only inside one answer. Follow-ups replay the text only, with notes stripped. Fine for
  now.
- Windows agent registry (`odr.exe`) untested; it needs build 26220.7262+ and the dev PC is 26200.
- Deferred UI items: the provider name shrinks small in the bar; code blocks wrap instead of scrolling; SVG text.
