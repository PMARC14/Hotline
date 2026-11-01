# Hotline — Windows 11 26H2: what it changed (2026-10-17)

This was the plan for the 26H2 follow-ups; the work is done in 0.4 (PR #9). The design and plan that replaced it:
`docs/superpowers/specs/2026-10-17-toolbar-v2-and-26h2-design.md` and
`docs/superpowers/plans/2026-10-17-toolbar-v2-and-26h2.md`.

Research on a 26H2 PC (build 26300.9711), recorded in the spec (section 5b and 6):

- **Agent connectors (`odr.exe`):** Windows registers the `Mcp` key naming `odr.exe`, but the binary isn't delivered on this PC
  and Microsoft still calls the connectors a preview. This PC now says "preview, not delivered"; no connector feature was built.
- **Copilot key:** Windows lets you remap the key in Settings › Bluetooth & devices › Keyboard (also on 25H2 and 24H2), so
  Hotline's own Right Ctrl mode was removed. `CopilotKeyPressFocusFrame` handling from 0.3 stays.
- **App Actions / Click to Do:** no catalog or schema change found.
- **New APIs:** nothing worth adopting. Phi Silica (replaced by "Aion Instruct" per Microsoft's Learn page of 2026-10-12) and the
  Windows AI APIs were deferred, maybe never: Hotline's on-device route is a local API connection.
- **Still to hand-test (owner):** the key arriving as before after the upgrade and the "Customize Copilot key" choice of Hotline
  surviving it; the TESTING.md pass on 26H2.
