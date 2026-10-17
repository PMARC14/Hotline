# Plan: movable, resizable panel with up/down growth (0.3, part 2)

Spec: `docs/superpowers/specs/2026-10-16-panel-placement-design.md`. Branch `panel-placement`, stacked on PR #32
(`codex-feedback`). Once #32 merges, merge `main` into it; the diff is then only this work.

**Done on the branch already:**
- the spec;
- the toolbar's **Continue in** button, added to the default bar;
- the owner's layout (no spacer) and `acrylicThin` as the defaults (7fdd5c3).

**Before starting:**
- Read the spec's two open questions; the owner answers them.
- Read HANDOFF's hard-won facts (a resize closes flyouts; `HoldResize`; no `TextHighlighter`; self-test off-screen).

**Pitfalls to design for** (from the Argon second opinion, 2026-10-16):
- **Alt alone:** opens the window menu (`WM_SYSCOMMAND` / `SC_KEYMENU`) and can beep. Subclass the HWND
  (`SetWindowSubclass`) and swallow `SC_KEYMENU`, and turn off access-key tips on the root.
- **Grips while hovering:** XAML key events don't fire when the panel has no focus. While the pointer is over the
  panel, poll `GetAsyncKeyState(VK_MENU)` on a timer. Hide the grips on deactivation, so an Alt+Tab never leaves them
  stuck.
- **Dragging across mixed-DPI monitors** gets `WM_DPICHANGED` mid-drag. Keep the cursor's position within the window
  as a ratio, recompute the origin at the new scale, and move with `SetWindowPos` (`SWP_NOZORDER | SWP_NOACTIVATE`).
  This stops the panel bouncing between monitors.
- **A resize drag** stops the growth animation and fixes the height for this opening. Content-driven sizing waits
  until the next open.
- **Flyouts:** any open flyout (toolbar menus, the history list, the Continue in menu) freezes grips *and*
  auto-resize (`HoldResize` already covers auto-resize; extend it to the grips).
- **Layout swap:** never reparent the `TextBox`. Only `Grid.SetRow` / `RowDefinitions` change, focus-loss hiding is
  held off during the swap, and the selection is restored if it moved.
- **Sounds:** `PlaySound` with `SND_ASYNC` can still stall on a sleeping audio device. Call it from `Task.Run`, with
  `SND_FILENAME | SND_ASYNC | SND_NODEFAULT`.
  - Windows events: resolve the event to its .wav path from `HKCU\AppEvents\Schemes\Apps\.Default\<event>\.Current`.
  - Files: `Path.GetFullPath` must stay inside `~/.hotline/sounds`.

## Tasks (TDD in Core; commit after each; Core tests + build each time)

1. **The key-toggle warning sound (task #7, PR #32).** The toggle log now names the focused element and the previous
   window. With the owner's repro, fix the cause. Candidates:
   - keystrokes reaching a window after the hide;
   - `SC_KEYMENU`;
   - focus on the pin button.

   It must be fixed before sounds are added, so the two are never confused.
2. **Core placement maths:** `Windowing/PanelPlacement.cs` and `PopupGeometry.GrowDown`, with unit tests for the
   cases in the spec's glitch list. Migration v13: `verticalPosition` → `anchor.y`; new keys `anchor`, `width`,
   `chatHeight` and `growDirection` in `WindowSettings` and `SettingsSchema`.
3. **Placement in the app:**
   - `PlaceOnActiveMonitor` uses `PanelPlacement`, including a saved monitor and the fallback to `active`;
   - every placement is logged at Info;
   - the session rect while pinned;
   - unpin or restart goes back to the default.
4. **Up/down layout:** `PanelLayout.Apply(direction)`.
   - Swap rows, the scroll anchoring (`MessagesPanel.VerticalAlignment`, the follow logic in `TranscriptView`), the
     jump button and the notices.
   - `ApplyHeight` uses `GrowDown` when going down.
   - The self-test renders down and swaps with a draft in the box.
5. **Button rows:** `ToolbarConfig` gets `Top` + `Items` (one row per item, old files load). The app gets a second
   toolbar grid above the composer. In Settings, "Bottom bar" becomes "Button rows" with a row picker.
6. **Alt grips:** `PanelGrips` (overlay, never changes layout size).
   - the `SC_KEYMENU` subclass;
   - Alt focus/hover detection;
   - move via the grip, resize via edges/corners (the spec's edge rules, limits);
   - DPI-safe drag; clamp on release;
   - "Save as default" in the grip's menu;
   - the self-test simulates a move and a resize through the same code.
7. **Sounds:** Core `Sounds` (parse, folder rule, registry events) with tests; App `SoundPlayer`, played on a real
   open and close only. The shipped `hotline.wav` (placeholder until the owner picks) is copied into
   `~/.hotline/sounds`.
8. **Settings UI:** Appearance › Position and size (numbers, presets, Save current as default, direction), Button
   rows, Sounds (pickers, ▶ preview, open folder). All texts in 17 languages: run the agy Flash translation batch, as
   for #32, and check that the placeholders survive.
9. **Docs and review:** configuration.md (window keys, toolbar `top`, sounds folder), HANDOFF, then an agy Argon
   review in chunks under 30k characters. Fix the confirmed findings, delete the review files, then `smoke.ps1`
   (ask first: it sends keystrokes) and the PR.
10. **By hand, on the desktop** (multi-monitor, mixed DPI): the spec's hand-test list. That is also where the 0.5 work
    happens.
