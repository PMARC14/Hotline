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
runs. It ends with Hotline quit; start it again with `hotline://tray` (Win+R). It waits for the self-test's last line,
`selftest complete: <n> checks, <m> failed` (after the size scenarios and profiles), and fails on any `FAILED` line.

Headless self-tests (nothing is shown; results in the log, lines starting `selftest`):

| Link (Win+R) | Checks |
|---|---|
| `hotline://selftest` | Transcript rendering (off-screen), OCR of the rendered text, the `/` list, a rate-limit note, every settings page |
| `hotline://selftest?voice` | Dictation can be set up (never opens the microphone): `Success` or the reason |
| `hotline://selftest?selection` | Reads the selection in the window in front via UI Automation; logs only its length. **Needs the marker file below** |
| `hotline://selftest?tools` | Starts the `mcp.json` servers and lists their tools. **Needs the marker file below** |

Any web page can open a `hotline://` link, so `?selection` (reads another app) and `?tools` (starts programs) are only run when the
file `%USERPROFILE%\.hotline\.allow-selftest-extras` exists (create it yourself, empty: `type nul > "%USERPROFILE%\.hotline\.allow-selftest-extras"`;
a web page cannot). Without it the log says `selftest ?tools refused: ...` and the plain self-test still runs. Delete the file when done.

## 2. Hands-on checklist

### Install and start
- [ ] Install the `.msixbundle` from Releases (test builds: trust `Hotline.cer` first, see README). Hotline appears in Start.
- [ ] Settings → Personalization → Text input → Customize Copilot key → Custom → Hotline.
- [ ] Settings › General › Start with Windows: on → sign out/in → Hotline is in the tray, panel closed.
- [ ] Uninstall and reinstall: settings in `.hotline` survive; the Copilot key choice needs setting again only after uninstall.

### The key
- [ ] Short press opens the panel on the monitor you're working on, ready to type; press again → it closes.
- [ ] Long press starts a new chat (default).
- [ ] Settings › General › Extra hotkey `Ctrl+Alt+H` → it opens/closes the panel.
- [ ] Windows Settings › Bluetooth & devices › Keyboard (older builds: Personalization › Text input › Customize Copilot key
      on keyboard) → Custom → Hotline: the key opens Hotline; tap and long press do what Settings › General says.
- [ ] Settings › General › Open Copilot key settings opens that Windows page.
- [ ] Windows' own "Right Ctrl" choice for the key (Hotline's own Right Ctrl mode was removed in 0.4): the key then acts as
      Right Ctrl and doesn't open Hotline.

### Panel shortcuts (Settings › Chat and history)
- [ ] Add custom quick-action, text-only and text/send buttons in Settings › Window. Type or capture `Ctrl+Shift+1` in Shortcut;
      check Esc cancels capture, Backspace clears it, Enter (also Ctrl+Enter) is refused, and a blank field saves no shortcut.
      Compare each shortcut with clicking the button, including slash-prefixed text (sent as plain text) and a missing quick action.
- [ ] Edit a custom button's label/action/picture and move it between groups, More/overflow and Not shown: its shortcut survives.
      Focus the composer, transcript and pickers; check shortcuts and translated tooltips, and that other apps are unaffected.
- [ ] Hand-edit custom shortcuts to invalid types/keys, Enter, duplicate aliases, built-in combos and the Stop combo. Check warnings:
      Stop wins even idle, then built-ins (including defaults), then customs in JSON order; losing shortcuts disappear, actions survive.
      Remap/disable a built-in or change Stop live: a freed custom combo works again; the old combo and tooltip update immediately.
      Save/reload and export/import the toolbar: shortcuts survive; the toolbar version stays 2.
- [ ] With the message box focused, try each default in configuration.md's Panel shortcuts table; compare with clicking its button.
      Picker keys cycle, stop while an answer streams, and resume afterwards. Copy with no completed answer shows a short notice;
      cancelled/failed partial answers are skipped, and an earlier completed answer can still be copied.
- [ ] Focus the transcript or a picker: shortcuts still work. Check prompts, history, Continue in and More open without closing on resize.
      Hide a button or move it into overflow and repeat; add the optional Copy last answer button and compare with its shortcut.
- [ ] Change a binding in Settings and toolbar.json: it and its tooltip update live; the previous combo stops working. Check Ctrl+C/V/X/A/Z
      still edit text, Esc stops an answer / hides an idle panel, and shortcuts do nothing when another app has focus.
- [ ] Set duplicate combos, a Stop conflict, an unknown id and a bad value: warnings and fallback match configuration.md. Reset restores defaults;
      export/import preserves custom bindings and disabled entries. Spot-check translated shortcut tooltips.
- [ ] Type and capture Space, Tab, A and Shift+A: toolbar bindings reject them; F1–F24 and Ctrl/Alt/Win combinations work.
      Stop still accepts bare keys. Settings rows reject conflicts with Stop, built-ins and custom buttons; the custom dialog warns.
- [ ] Blank a built-in row: toolbar.json contains `""`; Reset removes that override. settings.json's legacy map stays untouched.
      Start with a toolbar without `shortcuts` and an explicit legacy map: migrate raw values once, then save the map in toolbar.json.
      An explicit empty toolbar map prevents migration; invalid/unknown values survive edits and round trips; duplicate ids use the last.
      Move/style buttons and export/import the toolbar, then inspect the shortcut map and stamped backup for preservation.
- [ ] Hold a toolbar shortcut: it runs once. Stop keeps its repeat behavior. On an AltGr keyboard, Ctrl + right Alt + a toolbar key
      types text without invoking the toolbar; a deliberate left Ctrl+Alt binding still works.
- [ ] Compose text with an IME and press toolbar combos, Stop and Esc: none invokes an action or hides the panel; test normal keys
      after composition ends too. Virtual key 0xE5 must not invoke a shortcut.
- [ ] With Narrator, inspect toolbar buttons, pickers and overflow entries: effective AcceleratorKey follows tooltip changes.
      The custom capture button reads its combo/waiting/error state distinctly from the shortcut text box.
- [ ] Crowd the top row into automatic overflow and place More on the bottom row: the More shortcut opens the placed menu first.

### Button rows (Settings › Window)
- [ ] The six groups (top / bottom × left / center / right) show the buttons in the bar; the pickers (effort, model, provider) are three
      separate items.
- [ ] Drag a button inside a group, to another group, and to "Not shown" and back: the panel's bar follows within a second. Drop on an
      empty group and between two buttons. Drag one picker: only it moves.
- [ ] The arrow buttons on a button and Alt+arrow / Ctrl+arrow give the same moves.
- [ ] Bar look: Width compact, normal, wide, 3 and 4 cells (one list); outline and filled (light, dark, a Windows contrast theme); one button
      with its own look; "Use bar look" resets it. Hiding a styled button and showing it again keeps its look.
- [ ] Outline on the whole bar outlines the three drop-downs too, in the same colour (accent or `outlineColor`) and thickness, and they keep
      their height; hover, press and open each: the outline stays and the usual hover/press background shows. Filled on the bar fills them like
      the buttons. A picker's own style button (next to its width box) offers Outline, Outline color and Filled, no width; an override
      (`"model": { "outline": false }`) switches one off; an old `toolbar.json` with `"style": { "size": "normal" }` loads with no warning.
- [ ] Add a custom button that runs a quick action and one that inserts text (with and without Send): both work; delete the quick
      action's file → a notice, not a crash. A button called "Spacer" gets another id. A custom button with a PNG picture shows it.
- [ ] Narrow the panel to its minimum (400 px) with many buttons: what doesn't fit moves into a "..." menu; widen it: they come back; Pin, Prompt and
      Continue in work from the menu; no flicker or blank panel.
- [ ] The "..." button's box (card above "Not shown"): drag a button onto it and press "Move to the ... button's box" on another (with no
      `more` placed, one appears at the end of the bottom-right group); the bar loses those buttons and `more` opens them in order (Pin
      stays a toggle, Continue in opens its menu at `more`). Drag one back, reorder with the arrows, remove one (it goes to "Not shown");
      hiding `more` sends all to "Not shown". An empty box opens a disabled "Nothing here".
- [ ] "Move what doesn't fit into the menu" with the panel narrow: the buttons the bar would overflow end up in the box in bar order;
      at a wide panel nothing changes.
- [ ] With `more` in one row and the panel narrow: that row's overflow is in the `more` menu after your entries (separator between),
      no second "..." in the row; the other row keeps its own. Self-test steps P32-P34 draw these (`logs\selftest\`).
- [ ] Edit `toolbar.json` by hand (a wrong value, a repeated key, a half-saved file): the bar keeps working, Settings › About › This PC
      lists what was ignored; a file Hotline can't read is left as it is.
- [ ] Hand-edit `toolbar.json` (add a comment) and save a change in Settings › Button rows: the edited file is kept once as
      `toolbar.json.<time>.bak` (a second edit in Settings makes no new copy). With the page open, edit the file by hand, then drag an item:
      the page reloads the file, shows "toolbar.json changed while this page was open" and does not overwrite it.
- [ ] Settings › Button rows: replace a custom button's picture with another PNG of the same name: the bar shows it at once (no other edit).
      Make toolbar.json read-only first and replace the picture: the old picture comes back and a save error is shown.
- [ ] Narrator / Accessibility Insights on the panel: every bar button, the three pickers, the "..." button, Add (+) and Send/Stop have a
      spoken name (not a symbol); Send becomes Stop while an answer streams. Make 12+ mistakes in `toolbar.json`: the Settings InfoBar lists 10 and "(and N more)".
- [ ] A version 1 `toolbar.json` is left untouched: the default bar runs with the "version 1 files are no longer read" warning;
      Settings › Advanced › Reset settings puts the default bar back (a backup is made first).
- [ ] `"backdrop": "glass"` in the `window` section of `settings.json`, saved: a notice "Part of your settings was ignored" appears in the
      panel at once (naming `window.backdrop`), the look doesn't change, Settings › Advanced shows the same at the top. Fix the word → the
      notice goes. Type `{ "window": ` and save → "A settings file can't be read", the current settings stay, the same text is not
      announced twice. Then change a setting in Settings: a `settings.json.<time>.bak` with your text appears beside a valid file.
- [ ] `"type": "nope"` in a `connections\*.json` file: the notice names that file, the connection is not listed, the file is unchanged.

### Panel size and cell grid
- [ ] Padding is 10 DIPs on all four sides; the Alt grips still sit on the outer edge.
- [ ] The default panel is 40% of the screen's real pixels wide (768 px at 1920 px; never under 400 px) and the default bar fits it at
      100% scaling: nothing in the "..." menu, three pickers filling the top row. At 200% scaling the same panel holds fewer cells and uses "...".
- [ ] Every row is one cell (36 DIP) high whatever the widths (compact, wide, `width` 0.5-4, custom buttons, outlined or filled pickers); pickers are as tall as
      buttons and carry the bar's outline (the self-test checks all of it after every look step, P46 and P47 included, and logs `selftest FAILED` otherwise).
- [ ] The Accent swatch (and "Like the bar") in Settings › Button rows matches the outline colour drawn on the bar (Hotline purple by default;
      the Windows accent when the theme matches it; a contrast theme's highlight).
- [ ] A button's flyout offers one Width list (Like the whole bar, Compact, Normal, Wide, 3 cells, 4 cells); the bar's Width list shows Custom when
      widths differ and picking a width resets them.
- [ ] Buttons and drop-downs line up on one grid: one button `wide`, or four `compact`, and the rows above and below still end at the
      same edges; touching items show a 2 DIP gap; compact buttons still show their icon.
- [ ] `window.chatWidthPercent` 50 (or `chatWidth` 700): the first message widens the panel around its centre in the same resize as the
      height; New chat (Ctrl+N) shrinks it back; near a screen edge it slides back inside. With `window.width` set, or after resizing by
      hand, it never widens; the default (0) never widens. Moving the widened panel with Alt keeps its start width.
- [ ] Panel width is real pixels, scaling ignored: set `widthPercent` 20, then 40, at Windows scaling 150% and at 200% (sign out/in if asked). The panel
      is the same share of the screen at both (2880 px wide: 40% = 1152 px, 20% = 576 px but never under `minWidth` 400);
      buttons and text are bigger at 200%, and a narrow panel moves buttons into the "..." box.
      Resize by hand with Alt, Save as default, reopen: same pixel width; `window.width` in settings.json holds pixels (about 1152, not 576).

### Chat basics
- [ ] Ask something with each connection you use (Provider picker); answers stream; Esc hides, Ctrl+N new chat.
- [ ] While answering: Enter doesn't stop it (a note says how); Esc stops it; the Stop button stops it; Retry on an
      error retries. Settings › Chat › Stop an answer → `Ctrl+.` → now Ctrl+. stops and Esc hides the panel.
- [ ] Effort picker: change it for Claude / Gemini / OpenAI; the next answer uses it (no error).
- [ ] Settings › AI connections › Test connection: "Connected: N models, including …"; a wrong key shows the server's message.
- [ ] 🕘 Recent chats reopens a chat; Ctrl+↑ in an empty box reopens the last one.
- [ ] Code blocks and tables: Copy buttons work; the whole conversation can be selected in one drag.
- [ ] A custom button with `"text": "/resume foo"` and `"send": false`: pressing it only puts the text in the box (the history list
      does not open). Typing `/resume ` yourself still opens the history list.
- [ ] Provider bar: pick a model while the connection's file is read-only or the app can't save: a notice appears and the picker
      shows the previous model again (nothing is thrown). Same for the prompt in the "..." menu.

### Selected text
- [ ] Select text in Notepad, Edge and Word, press the key → a "Selected text from …" chip; ✕ removes it; sending uses it.
- [ ] Select text in a password box → no chip.
- [ ] Settings › Chat › Attach selected text → "Yes, also with Ctrl+C": in an app that doesn't share its selection,
      the chip appears and the clipboard still holds what you had copied before (Win+V history doesn't show the restore).
- [ ] In Windows Terminal with nothing selected, pressing the key never interrupts a running command.

### Screens, files, OCR
- [ ] + menu: Attach files, Capture window, Capture screen, Capture region (drag a rectangle; Esc cancels).
- [ ] The Capture region button in the bar works (add it in Settings › Window › Button rows if the bar lacks it).
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
- [ ] Settings › Window shows Chat width (percent) and Chat width beside the width settings. Type 1 in Chat width (percent): the
      box shows 20 once saved; type 100 in a fixed width: it shows 200. `chatWidth` 5000 grows to `maxWidth`, not beyond.
- [ ] Settings › Window › Growth = Full: an empty panel stays a bar (a long draft only fits its text); the first message jumps to the
      maximum height; after dragging the panel to a height by hand, a new message keeps that height.
- [ ] AI connections: move a connection up/down → the Provider picker order follows.
- [ ] AI connections: Max tokens 99999999 or Temperature 5 → the box shows the kept value (1000000 / 2) after you leave it.
- [ ] Lock a connection file (open it with a tool that denies sharing) and press Remove / Move / Duplicate, or lock a quick-action
      `.md` and press Delete: a red notice names the problem at the top of the page; Hotline keeps running.
- [ ] Settings › Advanced › Reset everything with one connection file locked: the others are removed, a yellow notice lists the
      locked file, and the removed connections' API keys are gone from the Credential Locker (check Settings › AI connections).
- [ ] Edit a connection file by hand (compact JSON, or a file called `mine.json` holding `"id": "local"`), then change something in
      Settings: the file keeps its name, and `<file>.<time>.bak` holds your version. Drop a new connection file in and change any
      setting within a second: the new file is still there.

### Backup (Settings > Backup)
- [ ] Export with everything ticked: the save dialog proposes `hotline-settings-<date>-<time>.zip`; the InfoBar shows the path
      and file count; the zip has no logs, history, memory or `.bak` files. The window stays responsive while it writes.
- [ ] "Select none" disables Export; the MCP tick shows the secrets note, the connections tick the "no API keys" note.
- [ ] Import on a second user profile / after deleting some actions: Choose file lists only the categories present with
      counts, date and version; Import writes them, the InfoBar shows the counts and "Open backup folder" opens
      `backups\import-<time>`; replaced files are in there.
- [ ] A connection imported without a key is listed as needing one; the Credential Locker is unchanged.
- [ ] A renamed/corrupted zip, or one with a changed file inside, is refused with a message and nothing is written; the message and
      any "Skipped: ..." lines are in the interface language (try German).
- [ ] Choose a good package, then a corrupted one: after the failure the Import button and the tick boxes of the first package are
      gone (the old package is not importable). Export over an existing zip while the disk is full or the file is open elsewhere: the
      old zip is still intact (no `.tmp` is left).
- [ ] A hand-edited `settings.json` with one wrong value, or a connection file called `renamed.json` holding another `id`, exports
      and imports without being skipped.
- [ ] After an import: a changed theme/bar applies at once; a new sound, quick action and button row are usable.

### Continue in targets
- [ ] Settings › Chat › Continue in: Add the ready-made entries (llama.cpp, Open WebUI, LM Studio, ChatGPT, Claude, Gemini), Edit, Remove;
      each button has a name a screen reader reads ("Edit ChatGPT"). Open WebUI: Test shows the model count; a wrong key or an `http://`
      address that isn't this PC says why.
- [ ] A connection's page: *Use the shared list* / *Own list* / *None*; each chat's Continue in menu (toolbar button and history list) shows
      only its own targets. A Local connection with no list offers the llama.cpp web UI.
- [ ] A web target opens the browser with the chat in the box; a very long chat gives the "only your last message" notice and the chat is on
      the clipboard. Open WebUI creates a real chat with every message. An app target opens the program; one pointing at a .cmd file only
      shows a notice. A hand-edited `continueIn` with one wrong target: the others work and Settings › Advanced lists the problem.
- [ ] The llama.cpp web UI (a Local connection's default target) opens with the conversation as its first message. LM Studio opens with
      the whole chat on the clipboard; ChatGPT and Claude open with the chat in their box; Gemini (ready-made entry; a Gemini API
      connection has none by default) opens with the chat on the clipboard to paste.
- [ ] The toolbar's Continue in button and the "..." menu show the same targets.

### First run and support
- [ ] Delete `%USERPROFILE%\.hotline\.first-run-shown`, open the panel → the "Welcome to Hotline" note with "What's sent".
- [ ] Delete `.support-note-shown`, ask something → after the answer, "Enjoying Hotline?" with "Support Hotline"; it
      doesn't come back on the next answer or after a restart.
- [ ] Settings › About › This PC: each feature shows ✓ or ✗ with what to do (e.g. agent connectors ✗ until 26H2).
- [ ] An `mcp.json` server whose command isn't installed (e.g. `"command": "nosuchtool"`) → Settings › Tools shows
      "\"nosuchtool\" isn't installed or isn't on PATH…"; for `npx` without Node.js it says to install Node.js.
- [ ] Settings › About: version, and the GitHub Sponsors / Ko-fi / GitHub / License links open the right pages.
- [ ] On GitHub, the repository shows a **Sponsor** button listing GitHub Sponsors and Ko-fi (after you've enrolled).

### Not hand-tested yet (0.4 leftovers)
- [ ] Mouse drags in Button rows: inside and between groups, to "Not shown", across monitors and DPI.
- [ ] The "..." menu (Prompt, Continue in) survives a `toolbar.json` change.
- [ ] Outlined pickers' hover and press look; outline, filled and widths in light, dark and contrast themes.
- [ ] 24 custom buttons at a narrow panel; the pixel width model at 150% and 200%.
- [ ] Alt-drags across mixed-DPI monitors (a pinned panel keeps its size in scaled units: `PanelPlacement.Rescale`).
- [ ] The Copilot key still arrives after the 26H2 upgrade.

When you're done, leave Hotline running in the tray with the panel closed.
