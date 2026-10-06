# Testing Hotline

Two parts: the **automatic checks** (run them first; they need no hands) and a **hands-on checklist** for what only a
person at the PC can test: keys, voice, other apps, how things look. Tick the boxes as you go. Logs:
`%USERPROFILE%\.hotline\logs\hotline.log` (Settings › Advanced › Detailed logging adds more).

## 1. Automatic checks

```powershell
dotnet test --project tests\Hotline.Core.Tests\Hotline.Core.Tests.csproj   # unit tests (all logic in Core)
powershell -File tests\smoke\smoke.ps1 -Install                           # builds, installs, drives the real app
```

The smoke test opens and closes the panel a few times and presses the fallback hotkey once — don't type while it
runs. It ends with Hotline quit; start it again with `hotline://tray` (Win+R).

Headless self-tests (nothing is shown; results in the log, lines starting `selftest`):

| Link (Win+R) | Checks |
|---|---|
| `hotline://selftest` | Transcript rendering (off-screen), OCR of the rendered text, the `/` list, a rate-limit note, every settings page |
| `hotline://selftest?selection` | Reads the selection in the window in front via UI Automation; logs only its length |
| `hotline://selftest?voice` | Dictation can be set up (never opens the microphone): `Success` or the reason |
| `hotline://selftest?tools` | Starts the `mcp.json` servers and lists their tools |

## 2. Hands-on checklist

### Install and start
- [ ] Install the `.msix` from Releases (test builds: trust `Hotline.cer` first, see README). Hotline appears in Start.
- [ ] Settings → Personalization → Text input → Customize Copilot key → Custom → Hotline.
- [ ] Settings › General › Start with Windows: on → sign out/in → Hotline is in the tray, panel closed.
- [ ] Uninstall and reinstall: settings in `.hotline` survive; the Copilot key choice needs setting again only after uninstall.

### The key
- [ ] Short press opens the panel on the monitor you're working on, ready to type; press again → it closes.
- [ ] Long press starts a new chat (default).
- [ ] Settings › General › Extra hotkey `Ctrl+Alt+H` → it opens/closes the panel.
- [ ] Settings › General › Copilot key → **Acts as Right Ctrl**: in Notepad, Copilot+C / Copilot+V copy and paste; the
      Start menu doesn't open; the key no longer opens Hotline (the extra hotkey does). Switch back → the key opens Hotline.
- [ ] Quit Hotline from the tray while holding the key in Right Ctrl mode: Ctrl isn't left stuck down.

### Chat basics
- [ ] Ask something with each connection you use (Provider picker); answers stream; Esc hides, Ctrl+N new chat.
- [ ] While answering: Enter doesn't stop it (a note says how); Esc stops it; the Stop button stops it; Retry on an
      error retries. Settings › Chat › Stop an answer → `Ctrl+.` → now Ctrl+. stops and Esc hides the panel.
- [ ] Effort picker: change it for Claude / Gemini / OpenAI; the next answer uses it (no error).
- [ ] Settings › AI connections › Test connection: "Connected: N models, including …"; a wrong key shows the server's message.
- [ ] 🕘 Recent chats reopens a chat; Ctrl+↑ in an empty box reopens the last one.
- [ ] Code blocks and tables: Copy buttons work; the whole conversation can be selected in one drag.

### Selected text
- [ ] Select text in Notepad, Edge and Word, press the key → a "Selected text from …" chip; ✕ removes it; sending uses it.
- [ ] Select text in a password box → no chip.
- [ ] Settings › Chat › Attach selected text → "Yes, also with Ctrl+C": in an app that doesn't share its selection,
      the chip appears and the clipboard still holds what you had copied before (Win+V history doesn't show the restore).
- [ ] In Windows Terminal with nothing selected, pressing the key never interrupts a running command.

### Screens, files, OCR
- [ ] + menu: Attach files, Capture window, Capture screen, Capture region (drag a rectangle; Esc cancels).
- [ ] Bottom bar region button (Appearance › Bottom bar → Add "Capture region" if your bar lacks it).
- [ ] Paste an image (Ctrl+V) and drop a file onto the panel → chips.
- [ ] Set a connection's "Model reads images" off (or use a local one), attach a screenshot, send → the model answers
      about the text in it ("Text in …" attachment).

### Quick actions and memory
- [ ] Type `/` → list with remember, translate, summarize, fix, explain; ↑/↓, Tab/Enter, Esc work; Esc doesn't close the panel.
- [ ] `/fix teh cat sat` sends a corrected sentence; `/summarize` with an attachment summarizes it.
- [ ] Settings › Quick actions › New action → edit the file → it's in the `/` list straight away.
- [ ] `/remember I prefer metric units` → "Remembered."; a new chat answers in metric; Settings › Chat › Edit memory shows the line.
- [ ] Tell an API model (Claude or Gemini) "remember that my name is …" → it asks to save (Allow once) → a "Saved to memory" note.

### Voice
- [ ] Settings › General › Voice input on. First hold of the key: Windows asks for the microphone → allow.
- [ ] Hold the key and talk: words appear live in the box; let go → the text stays (or is sent, with Send after dictation on).
- [ ] Esc while talking → the box goes back to what it was. Clicking away while talking → listening stops, text kept.
- [ ] With Online speech recognition off (Settings › Privacy & security › Speech) → a clear note with a "Turn it on" button.

### Tools (API connections, `mcp.json`)
- [ ] Add a server (e.g. the filesystem server) in `mcp.json`, set a connection's Tool use to "Use Hotline's tools".
- [ ] A read-only tool runs; a writing tool asks (Allow once / Always / Deny) with its full arguments.
- [ ] Settings › Tools shows the server Ready. Kill Hotline in Task Manager → the server's node process is gone too.

### App Actions (Click to Do)
- [ ] Install the "App Actions Testing Playground" from the Microsoft Store → Hotline's six actions are listed.
- [ ] Run "Ask Hotline about this text" with some text → the panel opens with it attached, nothing sent.
- [ ] Run "Summarize with Hotline" → it's sent at once and summarized.
- [ ] Run "Ask Hotline about this image" with a PNG → the image is attached.
- [ ] Copilot+ PC: Win+click on text in any app → Click to Do lists the Hotline actions.

### Settings window and looks
- [ ] Title bar dark in dark mode; Windows Settings → Personalization → Colors → switch light/dark while Hotline runs →
      the panel and the settings window follow (Appearance › Theme = System).
- [ ] Every page opens; changes apply at once and show up in `settings.json`.
- [ ] Appearance › Bottom bar: add, remove, move items, Reset → the panel's bar follows.
- [ ] AI connections: move a connection up/down → the Provider picker order follows.

### First run and support
- [ ] Delete `%USERPROFILE%\.hotline\.first-run-shown`, open the panel → the "Welcome to Hotline" note with "What's sent".
- [ ] Delete `.support-note-shown`, ask something → after the answer, "Enjoying Hotline?" with "Support Hotline"; it
      doesn't come back on the next answer or after a restart.
- [ ] Settings › About › This PC: each feature shows ✓ or ✗ with what to do (e.g. agent connectors ✗ until 26H2).
- [ ] An `mcp.json` server whose command isn't installed (e.g. `"command": "nosuchtool"`) → Settings › Tools shows
      "\"nosuchtool\" isn't installed or isn't on PATH…"; for `npx` without Node.js it says to install Node.js.
- [ ] Settings › About: version, and the GitHub Sponsors / Ko-fi / GitHub / License links open the right pages.
- [ ] On GitHub, the repository shows a **Sponsor** button listing GitHub Sponsors and Ko-fi (after you've enrolled).

When you're done, leave Hotline running in the tray with the panel closed.
