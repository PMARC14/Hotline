# Hotline — Windows 11 26H2 follow-ups

**Status: waiting for this PC to get 26H2** (build 26300). Draft PR, separate from multi-language (Plan 8) and
ARM64. Read `docs/HANDOFF.md` and the local `CLAUDE.md` first.

## Research first (on a 26H2 PC)

Research first, on the 26H2 PC, before changing code:
1. **Settings › About › This PC:** do agent connectors (`odr.exe`, needs 26220.7262+) turn ✓? If so, test Hotline's
   MCP tools registered as Windows agent connectors, as planned in Plan 7.
2. **App Actions / Click to Do:** re-run the TESTING.md App Actions section and note any catalog or schema changes.
3. **Copilot key:** check the key still sends Win+Shift+F23, that the Settings "Customize Copilot key" choice of
   Hotline survives the upgrade, and that Right Ctrl mode still works.
4. **The rest of TESTING.md** after the upgrade: OCR, voice, selection, theme following.
5. Read Microsoft's 26H2 release notes for new developer APIs relevant to a popup assistant (on-device AI APIs,
   agent features, screen and selection access) and write down which are worth adopting, with the minimum build for
   each. Gate every new feature with `PlatformSupport` and show it in This PC, so older Windows keeps working.

## Done when
- The TESTING.md pass on 26H2 is recorded in HANDOFF, agent connectors work or their blocker is written down, and any
  adopted 26H2 APIs fall back cleanly on 25H2.
