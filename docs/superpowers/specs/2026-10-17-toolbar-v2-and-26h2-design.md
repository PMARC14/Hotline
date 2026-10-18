# Hotline 0.4: button rows v2 and the Windows 11 26H2 pass

Status: **draft for the owner's review** (2026-10-17). Branch `windows-26h2` (PR #9). Version bump `0.3` → `0.4` when it merges;
no Store bundle (HANDOFF: none until just short of 1.0).

## Intent

0.4 makes the button rows configurable enough that the owner (and anyone) can shape the bar, and checks Hotline against
Windows 11 26H2. What the owner asked for (2026-10-17), and what I assumed:

| Said | Taken as |
|---|---|
| Wider default | `window.width` default **680** (decided). |
| "A top bar now": icons left- or right-aligned | Each row has a left group and a right group; replaces positional `spacer`. |
| Custom buttons | User-defined buttons that run a **quick action** or send **saved text**. Nothing else (no URL/file/app/shell). |
| Resize buttons; "wider and smaller version" | Size presets **compact / normal / wide** for the whole bar, plus a per-button override. |
| Outlines "and more" | `outline` and `filled` per button or for the whole bar. Anything beyond that waits (YAGNI). |
| Move buttons row to Windows in settings | The editor moves from Settings › Appearance to the **Window** page (`SettingsPage.Window`). |
| Simple quadrants in Settings; drag and drop; arrows | The editor shows four quadrants (top left, top right, bottom left, bottom right). Drag moves a button or picker within and between quadrants; arrow buttons do the same by click or keyboard. |
| User-edited files must not break rendering | A validation layer between the file and the renderer (section 2): never throws, clamps, falls back per field, reports what it changed. |
| Drop Hotline's own Right Ctrl mode | Removed (section 5); Windows' own Copilot-key remap replaces it. |
| Research Phi Silica and the non-public APIs | A research agent is on it; findings are added here before any Phi Silica work is planned (section 6). |
| Extensive testing | Section 7. |
| Scale/adjust the settings file incl. defaults | `toolbar.json` v2 with migration, defaults, Reset, hidden `.defaults` copy, docs, Guide in 17 languages. |
| Sound | Ship the generated `hotline.wav`; sounds stay off by default. Only the "placeholder until the owner picks" wording goes. |
| Picker width | Fixed by the wider default (no earlier hide). |
| Default layout | **Later** (migrated files keep their look; new installs get today's layout until decided). |

## Out of scope

Agent workspace, Ask Copilot taskbar and new App Actions work (research: no public API or no schema change). Phi Silica is
researched further first (section 6). Custom buttons that launch URLs/files/apps/shell. Per-picker sizing.

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
  outline. The default layout is decided later; until then the defaults are today's layout unchanged.
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

### Robustness against hand-edited files

All reading goes through one pure Core function, `ToolbarModel.Resolve(json)` returning a `ResolvedToolbar` (no UI, no IO,
never throws):
- **Per-field fallback, not per-file:** a bad value is replaced by its default and noted in `Warnings`; the rest of the
  file still applies. Only unparsable JSON or a wrong top-level type falls back to the whole default (as today).
- **Clamps:** `width`/`height` 20-80 DIP; unknown size preset becomes `normal`; booleans accept only booleans; an icon
  must be one hex code of 4-6 digits in the private-use range (else the default glyph); labels trimmed to 40 chars and
  stripped of control characters; text actions capped at 2000 chars; at most 24 custom buttons and 40 items per
  quadrant. Strings from the file are never used as markup, paths (`quickAction` is looked up by name in `actions\`)
  or commands.
- **Structure:** unknown ids dropped; an item listed twice or in two quadrants keeps its first place (order: bottom
  left, bottom right, top left, top right); a custom id colliding with a built-in is ignored; overrides for ids not
  shown are ignored.
- **No lock-out:** if nothing gives access to Settings (no `settings` button; the tray icon remains), nothing is forced,
  but This PC and the log say so. An empty bar is allowed only when the file says so explicitly (all four quadrants
  present and empty); an accidentally empty or all-invalid file gets the defaults.
- **Never overflow:** `ToolbarLayout` is extended to icon buttons: widths come from the resolved sizes; if a row's fixed
  buttons exceed the room, the right group is kept and the left group's last buttons move into a "..." overflow flyout
  (pickers shrink first, as today, then hide in `HideOrder`), so nothing is pushed off-screen. `Resolve` and `Compute`
  are pure and idempotent (same input, same output; tested), so the layout can't oscillate.
- **Never rewritten by accident:** the file on disk changes only through Settings or the v1 migration (with `.bak`). A
  hand edit that needed fixing is applied from the resolved model in memory; This PC lists the warnings and the log has
  one line per warning. Saving from Settings writes the resolved model, which repairs the file.
- **Half-written files:** the 400-ms debounce stays; a parse error keeps the last good bar.

## 3. Settings (Window page)

The editor shows the four quadrants as a 2x2 grid of cards, each a horizontal strip of its items in order:

```
 Top row                                   (above the message box)
 +- top left ------------+ +- top right -----------+
 | [effort][model][prov] | |                       |
 +-----------------------+ +-----------------------+
 Bottom row                                (below the message box)
 +- bottom left ---------+ +- bottom right --------+
 | [pin][cap][cap][cap]  | | [newChat][settings]   |
 +-----------------------+ +-----------------------+
```

- **Drag and drop** (WinUI `CanDragItems`/`AllowDrop`, one model for buttons and pickers): drag an item to another
  position in its quadrant or into another quadrant; a drop marker shows where it lands. A drop never fails: an
  invalid target falls back to "end of the quadrant under the pointer".
- **Buttons per item** (the click and keyboard alternative to dragging, and the accessible path): move **left/right**
  within the quadrant (order), **up/down** (top row <-> bottom row, same side), and to the **other side** (left <->
  right, same row); remove.
- Pickers (effort, model, provider) are items like the others but stay together as one block (the renderer's
  `PickersPanel`): dragging one moves the block's place; their order inside the block is the order in the file.
- Per item and whole bar: size preset, outline, filled. A **Custom buttons** list: add/edit/delete (label, icon from a
  short list, quick action or text, send). Hidden items sit in a "Not shown" strip you can drag from.
- All edits go through the same model class the renderer uses (see Robustness), so Settings can only write valid files.
- Guide section "Rows" and `Guide_Rows_Body` say "Settings > Window > Button rows" and describe quadrants and dragging
  in all 17 languages (Gemini 3.8 Flash checks page names per language). `docs/configuration.md` section toolbar.json
  is rewritten.

## 4. Width and sound

- **Width:** the default width is **680** (decided). Users with their own `width` are unaffected. The self-test PNGs at
  the owner's layout in each size preset must show no picker cut to "H...".
- **Sound:** the shipped `hotline.wav` stays; HANDOFF, Guide and `docs/configuration.md` drop the placeholder wording.

## 5. Copilot key: Hotline's own Right Ctrl mode is dropped

Windows (KB5127648 on 26H2, 25H2 and 24H2) can remap the key in Settings > Bluetooth & devices > Keyboard, so Hotline's
low-level hook is no longer needed (it also relied on Win+Shift+F23 arriving, and hook code risks Defender false positives).
- Delete `CopilotKeyHook`, `CopilotKeyRemap`, `CopilotKeyMode` and `CopilotKeyRemapTests`, the `ActivationRouter` branch,
  the `App.xaml.cs` wiring, the `activation.copilotKey` setting and its `SettingsSchema` entry.
- **Settings compatibility:** schema v13 to v14; an existing `"copilotKey"` entry (any value) is ignored on read and gone
  after the next save (the migration already keeps `settings.json.bak`); unknown keys never make a file fail. Tested.
- Remove the `Set_CopilotKey_*` strings from the 17 resx files; `docs/configuration.md`, `docs/TESTING.md`,
  `store/listing.md` and the Guide drop the mode.
- `docs/copilot-key.md` is rewritten: the Windows remap path and its options (Right Ctrl / Context menu / Search / an
  app), how to pick Hotline, what still arrives (Win+Shift+F23 and Hotline's activation), and that anyone who wants the key
  to act as Right Ctrl chooses that in Windows. The extra hotkey and the tray icon stay.

## 5b. Windows 26H2 pass

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

## 6. Phi Silica and the undocumented APIs

A research agent is covering: the WinAppSDK `Microsoft.Windows.AI.Text` surface, minimum builds, Copilot+ and Limited
Access Feature requirements, what shipped metadata on this PC shows about the agent registry (`odr.exe`, `AgentService`,
`CoreAI`), and a minimal design for a gated "Windows on-device" connection. Boundary: public docs and packages and shipped
metadata only; no bypass of the LAF token or any hardware or licensing gate (a blocker is documented as a blocker). This
section is filled in from its report, and the owner decides whether it is built in 0.4 or later.

## 7. Testing (extensive)

- **Core unit tests, TDD:**
  - `ToolbarModel.Resolve`: a valid file; every clamp; each bad type; unknown, duplicate and cross-quadrant ids; empty and
    all-invalid fall back to defaults; explicit empty bar kept; custom id rules; huge files and deep nesting; comments
    and trailing commas; BOM; case-insensitive ids; version higher than 2; fuzzing (5,000 generated random JSON cases:
    never throws, output always valid, `Resolve` idempotent: resolving its own written output returns the same model).
  - Migration v1 to v2 (no/one/many spacers, spacer first/last, pickers split, both rows, duplicates, junk); the rendered
    order equals v1's; `.bak` made; v2 files untouched; broken files untouched; read-only folder tolerated.
  - The editing model (the one Settings uses): move within a quadrant, between quadrants (all 4x4 pairs), the arrows at
    the edges, drop at an index (before, after, past the end, onto itself, into the other row), the pickers block,
    hide/show from "Not shown", custom add/edit/delete, `Save` round trip, order preserved.
  - `ToolbarLayout` with icon buttons: a property test over widths 200-1600 DIP x presets x quadrants: no overlap, total
    width within the room, right group never dropped before the left's overflow, overflow keeps order, stable under repeats.
  - Settings migration v13 to v14 (`copilotKey` present, absent, odd values), defaults, Reset (settings and toolbar, with
    backups), the `.defaults` copy, `EverythingFiles`.
  - ThisPc wording branches and `PlatformSupport` thresholds.
- **Resource tests:** every `Bar_*`/`Guide_*` key exists in all 17 languages; no `Set_CopilotKey_*` left; Guide page names match.
- **App side:** `hotline://selftest` extended: compact/normal/wide, outline/filled, all four quadrants populated,
  overflow, a custom button, 360/680/1000 DIP, both growth directions, and a deliberately broken toolbar.json (the bar
  still renders, warnings are listed); builds the Window settings page including the quadrant editor; fails on a layout
  cycle. Drag and drop is exercised through the editing model plus a UI Automation script for the arrow buttons; the mouse
  drag is hand-tested by the owner and listed in HANDOFF.
- **Smoke:** `smoke.ps1 -Install`. `docs/TESTING.md` gets the button rows, drag and drop and migration checks.

## Review

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
