# Movable, resizable panel with up/down growth: design

Date: 2026-10-16 (rewritten after the owner's answers). Branch `panel-placement` (stacked on PR #32; rebased on `main`
once #32 merges). The second part of 0.3.

## The idea

The **bar** (the message box with its button rows) is the centre of the design. It opens at a home spot, and the chat
grows out of it, **up or down**. Holding **Alt** shows a grip bar to move it and handles on the corners and edges to
resize it. A move or resize is temporary unless you **pin** (it then stays for the session) or **save it as the
default**. Optional sounds play when it opens and closes.

## Already decided (2026-10-16)

- The owner's button layout is the default (no spacer), with **Continue in** added after the history button (done on
  this branch); `window.backdrop` defaults to `acrylicThin` (done). Position and size defaults stay as they are today.
- The pin's spot lasts only while pinned and only until Hotline restarts. Only "Save as default" writes a spot.
- "Click out with pin" means **clicking into another app** (owner, 2026-10-17). The pinned panel stays visible over
  it, but the moved spot is forgotten: the next open goes back to the default.
- Sounds: Windows sound events *and* .wav files from `~/.hotline/sounds`, plus one shipped sound picked by the owner.

## Behaviour

### Home spot and size (the saved default)

- `window.anchor`: `{ "monitor": "active", "x": 0.5, "y": 0.8 }`.
  - `monitor`: `active` (the monitor you're working on, as today), `primary`, or a monitor's device name (what
    "Save as default" writes when the panel sits on a specific monitor). A saved monitor that isn't connected falls
    back to `active`.
  - `x`: where the bar's centre sits across the monitor's free work area (0 = left, 1 = right).
  - `y`: where the bar sits down it (0 = top, 1 = bottom).
  - Default: centred, 0.8 down, which is today's placement.
  - Migration v13 moves `window.verticalPosition` into `anchor.y` and drops the old key.
- Size:
  - `window.width`: DIPs. `null` = today's rule (`widthPercent` clamped to `minWidth`…`maxWidth`).
  - `window.chatHeight`: DIPs, the most the chat area grows to. `null` = `maxHeightPercent` of the screen.
  - All are numbers in settings.json and in Settings › Appearance › **Position and size**.
- **Save as default** (in the grip bar's menu, and in Settings) writes where the panel is now: monitor choice kept, `x`
  and `y` from its position, and `width` and `chatHeight` from its size. Only the saved default survives a restart.

### Moving and resizing (Alt)

- Hold **Alt** while the panel has focus, or while the pointer is over it. A **grip bar** appears along the bar's outer
  edge (the side away from the chat), and **resize handles** appear on the four edges and four corners.
- The grip and handles stay while you drag and fade out when Alt is released and no drag is running.
- The panel's layout never changes size while they show (they're drawn over the content).
- **Drag the grip** to move. **Drag an edge or corner** to resize:
  - Left/right edges change the width.
  - The chat-side edge changes the chat height. The bar-side edge carries the grip: it moves the panel and doesn't
    resize it.
  - The chat-side corners change both. The bar-side corners change the width only.
  - A resize fixes the panel at that height for the current opening (like `growMode: full`), so streaming content
    never fights the drag. The next open goes back to content-driven growth.
  - Every size has limits: at least `minWidth` × one line of chat, at most the work area.
- On release, the panel is pulled fully inside one monitor's work area. Dropped on a monitor with a different scale, it
  keeps its DIP size on the new monitor.
- Alt alone must not open the window menu or play the "no access key" beep. Hotline marks Alt as handled while the
  panel has focus.
- A move or resize only changes this opening, unless pinned or saved:
  - **Not pinned:** the next open (the key, the tray, a link) uses the saved default.
  - **Pinned:** the panel stays where you put it, at that size, while it is closed and reopened with the key. Any of
    these sends it back to the saved default on the next open:
    - clicking into another app (the pinned panel stays visible meanwhile, but the spot is forgotten);
    - unpinning;
    - Hotline restarting (the spot is kept per session and never written to a file).

### Growing up or down

- `window.growDirection`: `auto` (default), `up` or `down`.
- `up` and `down` **lock** the direction.
- `auto` (the default; one rule, used when the panel opens and after each move or resize): **up** whenever the chat
  at its full height (`chatHeight`) fits above the bar; otherwise **down** when it fits below; when neither fits, the
  side with more room (up on a tie). Owner, 2026-10-17.
- The bar keeps its place and the chat grows away from it. The growth animation, `growMode` (`grow` / `full`) and the
  half-then-content step (`PopupGeometry.TwoStep`) work the same in both directions.
- `PopupGeometry` gains `GrowDown` (keeps the bar's top edge, never past the work area's bottom). `GrowUp` is
  unchanged.
- **Layout follows the direction.** Going up, the chat is above the bar (today). Going down, the bar is on top and the
  chat below it. The following-the-newest-text rule, the jump-to-latest button and the notices sit on the chat's side.
  The message box itself is the same control in both, so the caret, focus and the draft are untouched by a swap.

### Button rows around the message box

`toolbar.json` gets a **`top`** row as well as the existing one:

```json
{ "top": [], "items": ["pin", "captureWindow", "…"] }
```

- `items` stays the bottom row, so old files keep working.
- `top` is a second row above the message box (empty by default).
- Any item may sit in either row, but in only one.
- "Top" and "bottom" are relative to the message box, not the screen, so the rows don't swap when the chat changes
  direction.
- Settings › Appearance › Bottom bar becomes **Button rows**, with a row picker.

### Sounds

- `window.openSound` and `window.closeSound`:
  - `off` (default).
  - `windows:<event>`: a Windows sound event from the current sound scheme, e.g. `windows:Notification.Default`,
    `windows:SystemAsterisk`. Settings lists the events that have a sound in the scheme.
  - `<file name>`: a .wav file in **`~/.hotline/sounds\`**. Only that folder is used: a path elsewhere is refused.
- Hotline ships one sound (`hotline.wav`, chosen by the owner; a placeholder until then). It is copied into the
  `sounds` folder when missing, so it can be picked like any other.
- Played asynchronously (`PlaySound` with `SND_ASYNC | SND_NODEFAULT`), so a missing file plays nothing rather than
  the default beep, and showing the panel is never delayed.
- Not played for the off-screen self-test, or when the panel only re-shows itself (the open check).
- Settings › Appearance › **Sounds**: two pickers (Off / Windows sounds… / Hotline sounds…), a ▶ preview button, and
  "Open sounds folder".
- The beep some hear today when the key toggles a pinned panel is fixed separately, before this ships (task #7: the
  toggle log names the window that had focus).

## Components

- **Core `Windowing/PanelPlacement.cs`** (pure, unit-tested):
  - home rect from the anchor, size settings, work area and scale;
  - anchor from a dropped rect (Save as default);
  - clamping a rect into a work area;
  - the `auto` direction choice;
  - DIP-preserving rescale between monitors;
  - resize from an edge/corner drag with limits.
- **Core `PopupGeometry.GrowDown`**, plus `TwoStep` for both directions.
- **Core `ToolbarConfig`**: `Top` and `Items` rows, the one-row-per-item rule, and loading old files.
- **Core `Sounds`**: setting parsing, `sounds` folder resolution (no paths outside it), and the event list from the
  registry (`HKCU\AppEvents\Schemes\Apps\.Default\<event>\.Current`).
- **App `PopupWindow`**:
  - session placement (`_sessionRect` while pinned);
  - the grip and handle overlay (`PanelGrips`, its own class), with drag and resize through pointer capture. Physical
    pixels come from `GetCursorPos` and the window moves with `AppWindow.Move`/`MoveAndResize`. There is no
    non-client region, so the borderless presenter stays as it is;
  - `HoldResize` blocks the grips while a flyout is open, and a drag pauses the growth animation;
  - the direction applied in one place (`PanelLayout.Apply(direction)`: row order, scroll anchoring, jump button side).
- **App `SoundPlayer`**: `PlaySound` P/Invoke, only on a real open/close.
- **Settings**:
  - schema entries (anchor, sizes, direction, sounds);
  - the Position and size card (numbers, presets, Save current as default);
  - Button rows;
  - Sounds.
- All new texts are in the 17 languages.

## Settings keys (all in settings.json)

| Key | Default | |
|---|---|---|
| `window.anchor` | `{ monitor: active, x: 0.5, y: 0.8 }` | Home spot (replaces `verticalPosition`) |
| `window.width` | `null` | DIPs; null = `widthPercent` rule |
| `window.chatHeight` | `null` | DIPs; null = `maxHeightPercent` |
| `window.growDirection` | `auto` | `auto` / `up` / `down` (`up`/`down` lock it) |
| `window.openSound`, `window.closeSound` | `off` | `off` / `windows:<event>` / file in `sounds\` |
| `toolbar.json` › `top` | `[]` | Row above the message box |

## Glitch checks

- **Unit tests:**
  - placement for every preset at 100/150/200 % scale;
  - monitors left of or above the primary (negative coordinates);
  - a top/left taskbar;
  - drops half off-screen;
  - every resize edge and corner against the limits;
  - the `auto` choice at both halves and at the exact middle;
  - toolbar rows (old file, item in both rows);
  - sound setting parsing and paths outside `sounds\` refused.
- **`hotline://selftest` (off-screen) adds:**
  - the grow-down layout rendered;
  - a direction swap with a draft in the box (draft and caret kept);
  - a simulated move and resize through the same code as a real drag;
  - the grips shown and hidden without the layout changing size;
  - the flyout rule (no resize while a flyout is open).
- **Log:** every placement logs anchor, rect, direction and monitor (Info), so a "nothing appeared" report can be
  traced. The open check from PR #32 stays, as does the guard that ends off-screen mode on a real open.
- **By hand, on the desktop (multi-monitor, mixed DPI):**
  - Alt grips and drag across monitors;
  - resizing from every edge;
  - pin → close → key → same spot; unpin → key → default; restart → default; Save as default → restart → saved spot;
  - auto swap at the top and bottom of each monitor;
  - a locked direction;
  - a monitor unplugged while pinned on it (falls back to the default);
  - sleep and resume;
  - each sound kind and a missing sound file.

## Not in this design

- A spot remembered per app or per virtual desktop.
- Snapping to screen edges (Aero Snap): the panel is a tool window.
