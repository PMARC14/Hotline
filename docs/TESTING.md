# Testing Hotline

Run the automatic checks first, then the hands-on checklist for physical input, multi-monitor behavior, and visuals. Logs live in `%USERPROFILE%\.hotline\logs\hotline.log` (**Settings › Advanced › Detailed logging** adds `DEBUG` lines).

## 1. Automatic checks

```powershell
dotnet test --project tests\Hotline.Core.Tests\Hotline.Core.Tests.csproj   # unit tests (Core logic, schemas, fixtures, localization)
powershell -File tests\smoke\smoke.ps1 -Install                           # builds, installs, drives the real app (-WithAgy optional)
```

`smoke.ps1` launches the installed package, tests protocol/fast-path/hotkey/live-reload/corrupt-settings/quit behavior, and waits for `selftest complete: <n> checks, <m> failed` (failing on any `FAILED` line). Do not type while it runs; it leaves Hotline running in the tray.

Headless self-tests (requires `"diagnostics": { "testLinks": true }` in `settings.json`; results logged as `selftest`):

| Link (`Win+R`) | Checks |
|---|---|
| `hotline://selftest` | Off-screen transcript rendering, OCR, `/` list, rate-limit notice, all settings pages, toolbar size/profile layouts |
| `hotline://selftest?voice` | Dictation setup (never opens the mic): `Success` or reason |
| `hotline://selftest?selection` | Reads foreground selection via UIA (logs length only). **Requires marker file** |
| `hotline://selftest?tools` | Starts `mcp.json` servers and lists tools. **Requires marker file** |

`?selection` and `?tools` require an empty marker file `%USERPROFILE%\.hotline\.allow-selftest-extras` (`type nul > "%USERPROFILE%\.hotline\.allow-selftest-extras"`) so web pages cannot trigger them. Delete it when finished.

## 2. Hands-on checklist

### Key, focus, and windowing
- [ ] **Copilot key**: short press opens/closes on the active monitor; long press runs `activation.hold`; **Settings › General › Open Copilot key settings** opens Windows Settings.
- [ ] **Extra hotkey & startup**: `activation.fallbackHotkey` (e.g. `Ctrl+Alt+H`) toggles the panel; **Start with Windows** launches Hotline in the tray at sign-in.
- [ ] **Scaled widths**: at 100% and 200% scaling, test `window.width` as `640` and `40%`, and `window.chatWidth` (`700`, `60%`) growth and reset on `Ctrl+N`. Check `minWidth: 320` / `maxWidth: 800` clamps and manual resize override; verify validation rejects invalid values (`19%`, `4001`, words).
- [ ] **Saved spot (`state.json`)**: Alt-drag and **Save position and size as default**. Verify `settings.json` is untouched and `state.json` records `panel.width`, `panel.chatHeight` (in scaled pixels), and `panel.anchor` with `over` values. Reopen at 100%/200%, test **Back to the default spot**, and verify changing `window.width` overrides only width.
- [ ] **Grow mode & multi-monitor**: verify `window.growMode: "full"` jumps to max height on first message and `growDirection` (`auto`/`up`/`down`) holds. Alt-drag and capture region/window across monitors with different DPI scaling; verify a pinned panel keeps its size in scaled units.

### Toolbar v3, shortcuts, and appearance
- [ ] **Button rows & fill**: drag buttons and pickers across the 6 groups, `Not shown`, and "The … button's box" (`menu`). Toggle **Fill available row space** (`style.fill`) at narrow and wide widths at 100%/200% scaling; verify numeric `width` (`0.5, 1, 2, 3, 4`) and picker `span`.
- [ ] **Outline & themes**: test `outline` and `filled` on bar and pickers (`effort`, `model`, `provider`) across light, dark, high contrast, and Windows contrast themes.
- [ ] **Custom buttons & icons**: add quick-action and text/send buttons with custom `.png` icons; verify icon replacement, missing quick-action notice, and case-insensitive id matching.
- [ ] **Panel shortcuts**: verify all defaults, Stop (`Esc` and remapped), conflict order (Stop > built-ins > custom), `""` disable, AltGr pass-through, IME composition suppression, and that `toolbar.json` stays `"version": 3`.

### Files, upgrade, reset, and backup
- [ ] **Safe edits**: add unknown keys to `settings.json`, `connections\*.json`, and `toolbar.json`; save a Settings edit and verify unknown keys survive, `version` stays `1`/`3`, originals go to one `backups\<stamp>-save\` folder, and no `.bak`/`.bad` files are created.
- [ ] **Broken & newer files**: set a higher `version` on reload (file stays untouched, notice shown); save syntax-broken `settings.json` or `mcp.json` (active session stays running, notice shown).
- [ ] **Pre-freeze upgrade**: in an isolated profile with pre-freeze fixtures, verify one `backups\<stamp>-upgrade\` folder, one-time notice listing reset files, and no new backup on restart. Test locked-file / denied-backup retry and connection id / Locker key normalization.
- [ ] **Starter files & Reset**: delete each starter file and restart (restored from defaults, `.defaults\` refreshed). Run **Reset settings** and **Reset everything**; verify one `backups\<stamp>-reset\` folder, `state.json` cleared, newer files kept, and connections/MCP/Locker keys handled per mode.
- [ ] **Backup export/import**: export with plaintext MCP `env` (notice warns without values); import on a fresh profile (review masks `env`, lists `tools.approvals` allow rules first, lists missing secrets; Cancel writes nothing; Import creates one `backups\<stamp>-import\` folder and applies live).

### Chat, attachments, voice, tools, and stability
- [ ] **Core chat**: stream an answer with each connection, stop (`Esc`/Stop), retry on error, cycle effort/model/provider, open Recent chats (`🕘`, `Ctrl+↑`, `/resume`), and test code/table Copy and single-drag selection.
- [ ] **Selected text, captures & OCR**: verify UIA chip, password-box exclusion, `clipboard` mode restore (excluding terminals), `+` window/screen/region captures, drag-drop/`Ctrl+V` paste, and offline OCR for non-vision connections.
- [ ] **Quick actions, memory & voice**: test `/`, custom action create/rename/delete (including locked-file notice), `/remember`, model remember-tool approval, and hold-to-talk voice (`Esc` cancels, `chat.voiceAutoSend`, missing Speech setting prompt).
- [ ] **MCP tools & Continue in**: test read-only auto-run vs `Allow once`/`Always`/`Deny`, `ask` rules hiding Always, **Revoke** in Settings › Tools, **Move to Credential Locker** (`backups\<stamp>-save\mcp.json`), server shutdown on exit, App Actions (Click to Do), and **Continue in** targets (CLIs, Open WebUI, `llama.cpp`, LM Studio).
- [ ] **Notices, guards & Narrator**: clear `shown.firstRun` / `shown.supportNote` in `state.json` and verify one-time notices + `chat.notifyShowsText` on/off toasts. Close Settings during failing saves or open dialogs, toggle flyouts rapidly, quit/relaunch (`hotline://tray`) with one tray icon, and spot-check Narrator names on bar items, remove chips, and Settings rows.

### Not yet checked by hand (0.6.3, 0.7 and multi-monitor fixes)
- [ ] `Ctrl+Shift+F10` then Move or Resize with the arrow keys, Enter and Esc; tray icon by keyboard (Tab to the tray, Enter).
- [ ] **Capture window** from the Copilot key on a second monitor gives `window-HHmmss.png` of the window you were in; minimized,
      it shows a notice. If not, read the `toggle:` and `deactivated:` lines in `hotline.log`.
- [ ] Unplug a monitor, change scaling or move the taskbar under an open pinned panel: it recovers on screen at the right width.
- [ ] Hold Esc while an answer streams (stops, panel stays). Tap the voice button and click another app (stops listening).
- [ ] A Windows App Action (Click to Do › Summarize) with a draft already in the box only fills the box; it never sends the draft.
      Needs a Copilot+ PC.
- [ ] Two monitors of different scaling: each first open on the other monitor has the right size (log `place: DPI change to …:
      keeping …`). Click the other monitor's empty desktop or taskbar, press the key: it opens there, reopens there after a key
      or Esc close, and focus goes back to that desktop or taskbar. Click an app: the next open follows the app.
- [ ] Close the panel during Ctrl+Shift+F10 Move or an Alt-drag (unpinned): the next open uses the default spot. Capture screen
      after opening from the other monitor's desktop grabs that monitor.
- [ ] **Move to Credential Locker** opens its dialog; **Reset the bar** keeps your shortcuts; a height-only Alt-drag keeps the
      chat width growing; a saved width wider than `maxWidth` reopens at that width.
- [ ] The upgrade notice appears once on the first panel open after upgrading and names the backup folder.

When done, leave Hotline running in the tray with the panel closed.
