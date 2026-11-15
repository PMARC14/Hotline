# S1 design: reasons

Why [s1-design.md](s1-design.md) is the way it is. Owner decisions of 2026-11-14 that change the 2026-11-12 ones.

## R6 (2026-11-15): settled, not sealed

- **Decision:** the formats may keep improving after 1.0. A change is free when every existing file stays valid and keeps its
  meaning (a new key, a wider rule). A change that breaks an existing file needs a version bump and a conversion.
- **Why now is different:** before 1.0 one hard break is taken without converting old settings, so 1.0 ships no legacy readers.
- **Example:** custom button ids gained upper-case letters on 2026-11-15; lowercase ids are unaffected.

## R5 (2026-11-14): the upgrade runs without a backup if it must

- **Decision:** the upgrade tries the backup folder and replaces the files even when the copy fails.
- **Rejected:** "no backup, no write" for the upgrade too. The app would run on defaults each session and retry, and the owner
  prefers one clean reset.
- **Scope:** the upgrade only. Saves, Reset and Import keep "no backup, no write".

## R4 (2026-11-14): buttons fill the row

- **Decision:** spare room is shared by `width` and `span`; all default buttons are width 1.
- **Problem:** left, centre and right groups with mixed widths left uneven gaps.
- **Rejected:** fixed-size buttons with equal gaps (smaller click targets, not what the owner pictured).

## R3 (2026-11-14): defaults come from files

- **Decision:** `Defaults/<family>.json` is the single source.
- **Problem:** a default lived in the settings class, `SettingsRanges`, the toolbar's code layout and, soon, the schema.
- **Limit:** build-time only. A user-editable defaults file would repeat `settings.json`.
- **Not done:** comments in the starter `settings.json` and `toolbar.json`; the first save from Settings would drop them.

## R2 (2026-11-14): reset, do not migrate

- **Decision:** pre-freeze `settings.json` and `toolbar.json` are replaced by defaults; connections and `mcp.json` are rewritten
  in the new format.
- **Why:** Hotline is pre-release with few users, and the freeze follows. Converters for widths, shortcuts, approvals and old
  toolbar keys would ship in 1.0 and be read once.
- **Rejected:** removing connections too (API keys and CLI setup would have to be entered again).
- **Carried over:** `chat.defaultConnection` and `chat.order`, so the kept connections stay selected and ordered. Claude's call.

## R1 (2026-11-14): scaled pixels everywhere, width as a number or a percent

- **Decision:** every pixel number is in scaled pixels; `width` and `chatWidth` take a number or `"N%"`.
- **Problem:** widths were real pixels and heights scaled pixels. A real-pixel width is a different share and a different apparent
  size on every screen: 900 is 31% of a 2880 panel at 200%, 47% of a 1920 monitor.
- **Why keep the number:** a scaled-pixel width is the same apparent size on every screen, which is what a reading width should be.
- **Rejected:** percent only (no fixed reading width); pixel sizes tied to one monitor in `state.json` (not needed once the unit
  carries across screens).
- **Clamps:** `minWidth` 320 and `maxWidth` 800 keep the panel readable on low-resolution and ultrawide screens.
