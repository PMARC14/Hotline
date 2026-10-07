# Hotline Plan 8 — multi-language support and Windows 11 26H2

**Status: planned, not started.** Draft PR. The two parts are independent; the 26H2 part waits until this PC is on
26H2 (build 26300). Read `docs/HANDOFF.md` and the local `CLAUDE.md` first.

## Part A — multi-language support

### Already works in any language
- Chat: models answer in the language you write in; `/translate` exists.
- OCR: `OcrEngine.TryCreateFromUserProfileLanguages()` follows the Windows profile languages.
- Voice: `SpeechRecognizer` uses the Windows speech language.

### Missing: the interface is English-only
Every string (panel, notices, settings schema, tray, first-run and support notes, error messages from Core) is
written directly in code.

**Approach**
1. **Resources:**
   - **App:** WinUI strings move to `Strings/<lang>/Resources.resw`, read through MRT Core, with `x:Uid` in XAML and a small `Text.Get("Key")` helper for code.
   - **Core** stays UI-free: its user-facing messages (`CommandResolver`, `PlatformSupport`, settings validation, notices) move to a standard `.resx` with `ResourceManager`, so the unit tests can check them.
2. **Setting:** `general.language` = `system` (default) or a language tag. It is applied with `ApplicationLanguages.PrimaryLanguageOverride` and takes effect on the next start (say so in the UI). Schema bumps to v9 with a migration test.
3. **Quick actions:** `/translate` with no target uses the interface language. Built-in action prompts stay English (models understand them) but ask for the answer in the user's language.
4. **Manifest:** list the shipped languages in `Package.appxmanifest` `<Resources>`, so the Store shows them.
5. **Catching what's missed:** a pseudo-language (`qps-ploc`) build makes leftover English and cut-off text easy to see. A unit test fails if any `SettingsSchema` header or description isn't a resource key.
6. **Translations:** English first (all strings extracted, no behaviour change), then one more language end to end. A model drafts the translation and a speaker reviews it. **Decide which languages with the user.**
7. **Store:** one listing (description and screenshots) per shipped language.

**Order:** extract strings in small PRs (one area at a time) → add the setting → pseudo-loc pass → first extra language.

## Part B — Windows 11 26H2 (when this PC has it)

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
- Part A: every interface string comes from resources, `general.language` works, the pseudo-loc build has no leftover
  English, and one extra language ships.
- Part B: the TESTING.md pass on 26H2 is recorded in HANDOFF, agent connectors are working or their blocker is written
  down, and adopted 26H2 APIs fall back cleanly on 25H2.
