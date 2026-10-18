# Hotline 0.4: button rows v2 and the Windows 11 26H2 pass

Status: **draft for the owner's review** (2026-10-17). Branch `windows-26h2` (PR #9). Version bump `0.3` → `0.4` when it merges;
no Store bundle (HANDOFF: none until just short of 1.0).

## Intent

0.4 makes the button rows configurable enough that the owner (and anyone) can shape the bar, and checks Hotline against
Windows 11 26H2. What the owner asked for (2026-10-17), and what I assumed:

| Said | Taken as |
|---|---|
| Wider default | `window.width` default rises from 600; the number is chosen by measuring (see Width). |
| "A top bar now": icons left- or right-aligned | Each row has a left group and a right group; replaces positional `spacer`. |
| Custom buttons | User-defined buttons that run a **quick action** or send **saved text**. Nothing else (no URL/file/app/shell). |
| Resize buttons; "wider and smaller version" | Size presets **compact / normal / wide** for the whole bar, plus a per-button override. |
| Outlines "and more" | `outline` and `filled` per button or for the whole bar. Anything beyond that waits (YAGNI). |
| Move buttons row to Windows in settings | The editor moves from Settings › Appearance to the **Window** page (`SettingsPage.Window`). |
| Scale/adjust the settings file incl. defaults | `toolbar.json` v2 with migration, defaults, Reset, hidden `.defaults` copy, docs, Guide in 17 languages. |
| Sound | Ship the generated `hotline.wav`; sounds stay off by default. Only the "placeholder until the owner picks" wording goes. |
| Picker width | Fixed by the wider default (no earlier hide). |

## Out of scope

Agent workspace, Ask Copilot taskbar, new App Actions work and Phi Silica (research: no public API, no schema change, or
Copilot+-only with no confirmed minimum build). Custom buttons that launch URLs/files/apps/shell. Per-picker sizing.

## 1. `toolbar.json` v2

```jsonc
{
  "version": 2,
  // Each row has a left and a right group. Items: built-in ids or ids from "buttons".
  "top":   { "left": ["effort", "model", "provider"], "right": [] },
  "items": { "left": ["pin", "captureWindow", "captureScreen", "captureRegion", "prompt", "recent", "continueIn"],
             "right": ["newChat", "settings"] },
  // Whole-bar look: size is compact | normal | wide; outline/filled are true | false.
  "style": { "size": "normal", "outline": false, "filled": false },
  // Per-item overrides of the same three keys (and "width"/"height" in DIP for icon buttons).
  "overrides": { "pin": { "outline": true } },
  // Custom buttons. icon is a Segoe Fluent Icons code (hex) ; exactly one action key.
  "buttons": {
    "summarise": { "label": "Summarise", "icon": "E8A5", "quickAction": "summarise" },
    "thanks":    { "label": "Say thanks", "icon": "E8BD", "text": "Thanks!", "send": false }
  }
}
```

Rules:
- An item sits in one row and one group only (listed in both: the bottom row wins, as today). Unknown ids, bad types and
  repeated ids are ignored, never fatal; an empty result falls back to the defaults. Parsing keeps comments and trailing
  commas. A file that doesn't parse is left untouched and the defaults run (current behaviour).
- `effort`, `model`, `provider` stay together in the one `PickersPanel`; they go in the group of the first one listed.
  Pickers ignore `size`, `outline` and `filled` (their width is `ToolbarLayout`'s).
- Custom button ids: `[a-z0-9-]`, 1–32 chars, not a built-in id. `quickAction` is the name of a file in `actions\`
  (executed through `QuickActions.Expand` with the current draft and attachments). `text` inserts the text in the message
  box; `send: true` also sends it. Neither value ever comes from a model. A missing action file shows a notice, not an error.
- Size presets (icon buttons, DIP; **final numbers set from the self-test PNGs**): compact 30×28, font 13 · normal 36×32,
  font 15 (today) · wide 46×32, font 15.
- **Migration v1 → v2** (on first load of a file without `version`): `items`/`top` arrays become `left`; the first
  `spacer` in a row splits it (items after it go `right`), further spacers are dropped. The v1 file is copied to
  `toolbar.json.bak`; the rendered bar is identical. Files with `version` higher than 2 are read tolerantly and never
  rewritten. `Save` (Settings editor) writes v2 with the comment header, keeping `style`, `overrides`, `buttons`.
- **Defaults** (new installs, Reset): the owner's current layout (no spacer; pickers top row), `style` normal, no
  outline. *Open:* should New chat and Settings be right-aligned by default? (Migrated files keep their look either way.)
- **Reset:** Settings › Advanced › Reset settings also restores `toolbar.json` (backed up first); Reset everything keeps
  deleting it as now. A hidden `~/.hotline/.defaults/toolbar.json` is written at startup like `settings.json`.

## 2. Rendering (`Hotline.App`)

- `PopupWindow.ApplyToolbar` builds each row as `[left Auto…] [star spacer] [right Auto…]`; no `spacer` item remains.
- Custom buttons are created at runtime in an id → `Button` map, removed and rebuilt on every apply, with
  `AllowFocusOnInteraction = false` (the 26H2 Copilot-key rule) and the `ToolbarButton` look.
- Style (size, outline via `BorderBrush`/`BorderThickness`, filled via `Background`) is applied after `Fill()`, only when
  a value changes; pointer-over/pressed keep working (set theme resources, not hard-coded colours, so High contrast holds).
- Layout-cycle rules (HANDOFF): no size changes in size events; picker widths still come from `ProviderBar.Layout` via
  `QueueLayout`; a style change just calls `QueueLayout` after `ApplyToolbar`.
- The self-test renders compact, normal and wide bars, outline on, left/right groups, and a custom button, in both
  growth directions, and still restores the owner's file.

## 3. Settings

- The button-rows editor (`BuildToolbarSection`) moves to the **Window** page. Per item: move, left/right group,
  top/bottom row, remove, size, outline, filled. Whole-bar controls at the top of the section. A **Custom buttons**
  list: add/edit/delete (label, icon picker from a short list, action quick action or text, send).
- Guide section "Rows" and `Guide_Rows_Body` say "Settings › Window › Button rows" and describe the new options in all
  17 languages (Gemini 3.8 Flash checks page names per language). `docs/configuration.md` § toolbar.json is rewritten.

## 4. Width and sound

- **Width:** `HotlineSettings.Window.MinWidth`/default width raised from 600 to **680 or 720**, chosen from the self-test
  PNGs at the owner's layout, in each size preset. Users with their own `width` are unaffected.
- **Sound:** the shipped `hotline.wav` stays; HANDOFF, Guide and `docs/configuration.md` drop the placeholder wording.

## 5. Windows 26H2 pass

From the research (a subagent's report, sources in the PR description):
- This PC (26300.9711) has the `HKLM\…\Mcp` key naming `odr.exe` but **no `odr.exe`**; Microsoft still calls agent
  connectors a preview. Keep `PlatformSupport.AgentConnectors`, but This PC must say "preview, not delivered on this PC"
  when the build qualifies but the binary is missing (runtime detection already exists in `ThisPc`). Unit-test the wording
  branch; no connector feature is built.
- `docs/copilot-key.md`: the Settings › Bluetooth & devices › Keyboard remap (Right Ctrl / Context menu / Search / an
  app; also on 25H2 and 24H2) and how it overlaps Hotline's Right Ctrl mode.
- **Hands-on checks, only with the owner's approval** (test-hygiene rule): the key still arrives as Win+Shift+F23, the
  "Customize Copilot key" choice of Hotline survived the upgrade, Right Ctrl mode, TESTING.md (OCR, voice, selection,
  theme following). Results go in HANDOFF; the plan file is replaced by a short "what 26H2 changed" note.

## Testing and review

- TDD in `Hotline.Core`: parse/validate v2, migration (spacer splits, duplicates, bad types, higher version, broken file
  untouched), `Save` round trip keeping comments header, defaults and Reset, custom id rules, ThisPc wording.
- Then `dotnet test`, `smoke.ps1 -Install`, `hotline://selftest` PNGs read by eye (agy `gemini-3.8-flash-high`).
- Every diff goes through an adversarial review with `agy --model gemini-3.8-flash-high` (diff pasted in the prompt,
  "do not use any tools", chunks under ~32k chars; never put hook/injection code on a command line); each finding is
  verified against the code before fixing.

## Delivery (agents)

1. Core: schema, migration, defaults/Reset/`.defaults`, tests. (First; the rest depend on it.)
2. App rendering + self-test. 3. Settings editor + Window page move. (2 and 3 after 1, in parallel on separate files.)
4. Docs + Guide + 17 languages. 5. Width, sound wording, ThisPc wording, `copilot-key.md`. (4 and 5 independent.)
Result: one PR #9 (renamed "0.4: button rows v2 and 26H2"), `version.txt` → `0.4`.
