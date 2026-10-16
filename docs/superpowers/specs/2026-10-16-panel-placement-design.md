# Movable, pinnable panel: design (draft for the owner's review)

Date: 2026-10-16. Branch: `panel-placement`, from `main` once PR #32 (v0.3.0) has merged. Ships as the second part
of 0.3 (v0.3.x).

## Why

The panel always opens centred on the active monitor, at `window.verticalPosition` (0.8), and grows upward. When you
work alongside it (following instructions, copying commands), it can't be moved out of the way, and it can't sit at
the top of the screen. The owner asked for:

- a configured position and size that the Copilot key always opens at;
- dragging the panel anywhere, with pinning keeping it there across closing and reopening;
- opening from the top or the bottom, chosen automatically from where the panel is or set by the user;
- an optional open/close sound (off by default);
- everything in settings.json, most of it in Settings, and checks that guarantee no UI glitches.

## Questions for the owner (the draft assumes the first answer)

1. **Where is the drag handle?** (a) The toolbar's empty space (the spacer) plus a slim grip strip along the panel's
   edge; (b) only a grip strip; (c) Alt+drag anywhere.
2. **Opening from the top: does the message box move to the top as well?** (a) Yes: box at the top edge, the
   conversation grows downward below it (like Spotlight); (b) no: the box stays at the bottom and the whole panel grows
   downward.
3. **Does a pinned spot survive a restart of Hotline?** (a) Yes, saved to `window.pinnedPlacement` in settings.json;
   (b) only until Hotline restarts.
4. **Sound:** (a) one built-in short sound, plus a path to your own .wav; (b) Windows' own sounds only.

## Behaviour

**Home spot (configured).** `window.anchor` gives where the panel opens:
`{ "monitor": "active" | "primary" | "<device name>", "x": 0.5, "y": 0.8 }`. `x` and `y` are fractions of the
monitor's free work area, giving where the panel's centre sits horizontally and its bar vertically. The defaults
reproduce today's placement exactly, and `verticalPosition` becomes `anchor.y` (migration v13). The size keeps using
`widthPercent`, `minWidth`, `maxWidth`, `height` and `maxHeightPercent`. Settings › Appearance gets a **Position**
card: monitor, a 3×3 grid of presets (top-left … bottom-right), and "Use the current spot" (takes where the panel is
now).

**Moving.** Dragging moves the panel for this opening only. Unpinned, the next press of the key opens it at the home
spot again. Pinned, it stays where you dropped it: closing and reopening with the key (or a restart, per question 3)
brings it back there until you unpin. Unpinning sends it home the next time it opens. A dropped panel is kept fully
inside one monitor's work area. Dropping it across monitors with a different DPI rescales it to that monitor.

**Direction.** `window.growDirection`: `auto` (default) | `up` | `down`. `auto` grows up when the bar sits in the
lower half of its monitor and down in the upper half, decided at open and after each drop, never mid-answer. The
layout follows the direction (question 2): grid rows reversed, the transcript's "follow the newest text" pinned to
the matching edge, and the jump-to-latest button moved to that side. `PopupGeometry` gets `GrowDown` next to
`GrowUp` (unit-tested: never past the work area, keeps the bar's top edge).

**Sound.** `window.openSound` / `window.closeSound`: `off` (default) | `hotline` (a short built-in sound) | a path to a
.wav file. Played asynchronously (no delay to showing the panel), at the Windows sound volume, and never while
Windows' "Do not disturb" is on. The **beep some hear today** when the key toggles a pinned panel is tracked
separately (the toggle now logs the focused window). That is fixed before adding sounds, so the two are never confused.

**Settings window.** Appearance › Position (monitor, presets, use current spot, direction) and Appearance › Sounds.
Every option is also a settings.json key; `window.pinnedPlacement` is settings.json only.

## Components

- Core `Windowing/PanelPlacement.cs`: home spot from the anchor and work area; pinned spot; clamping a drop into a
  work area; picking the direction from the bar's position; DPI rescale between monitors. Pure and unit-tested.
- Core `PopupGeometry.GrowDown`, plus `TwoStep` used for both directions.
- App `PopupWindow`: the drag regions (`InputNonClientPointerSource` caption regions over the handle, so Windows does
  the moving: smooth, Aero Snap off). On `AppWindow.Changed` (position) it records the drop, re-clamps and re-picks the
  direction. While the user is dragging, the grow animation pauses and no resize happens; a flyout's `HoldResize`
  pauses dragging.
- App layout: the row order switches by direction in one place (`PanelLayout.Apply(direction)`).
- Settings: schema entries and a small Position card.

## Glitch checks (the "no UI glitches" requirement)

- Unit: placement and geometry for every preset on 100/150/200 % monitors, negative virtual-screen coordinates (a
  monitor left of or above the primary one), a work area with a top/left taskbar, and drops half off-screen.
- `hotline://selftest` gains: grow-down layout rendered off-screen; a direction switch mid-session; a drop simulated
  through the same code path as a real drag (no real mouse). It checks the flyout-resize rule (no resize while a
  flyout is open) and that the caret, notices and jump button sit on the correct side.
- By hand, on the desktop (multi-monitor, mixed DPI): drag across monitors, pin and reopen, unpin and reopen, a
  monitor unplugged while pinned there (falls back to home), top and bottom taskbar, sleep and resume while pinned.
- The open check from PR #32 stays on: a panel that opens off-screen is logged and shown again.

## Not in this design

- Resizing by dragging an edge (the size comes from settings and the content). It could follow if wanted.
- Remembering a spot per app or per virtual desktop.
