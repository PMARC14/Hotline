# Hotline Plan 8 — multi-language support

**Status: planned, not started.** Draft PR. Windows 11 26H2 follow-ups and native ARM64 builds are separate PRs.
Read `docs/HANDOFF.md` and the local `CLAUDE.md` first. Default language: follow Windows (`general.language = system`).

## Scope

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

## Done when
- Every interface string comes from resources, `general.language` works (default: follow Windows), the pseudo-loc
  build shows no leftover English, and one extra language ships.
