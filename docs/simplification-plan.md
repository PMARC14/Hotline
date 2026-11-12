# Simplification plan before 1.0

Merged from the Codex Sol design review and the agy Sonnet review (2026-11-12), each claim checked against the code where the reviewer
had a shell. Sonnet had no shell, so its line numbers and "I did not read" gaps are marked. Owner rule: fix or replace a mechanism, never
stack another; design and review the whole system before building; live-test each phase in the real client.

## Order of work

1. **Owner decisions** (below), then **S1**: fixtures first, then the code.
2. **One writer and one backup policy.** One `SafeFile.Replace(path, bytes, backup)` and one `backups\<stamp>\<relative path>` policy
   replace the seven hand-written writers (`SettingsStore`, `ToolbarFile`, `McpConfig`, `SettingsReset`, `SettingsPackageImport`,
   `SettingsWindow.Backup`, `SettingsWindow.Connections`; `DurableFile` from 0.6.3 is the start) and the eight to nine backup forms
   (`.<stamp>.bak`, `mcp.json.bak-<stamp>`, `.old/.oldN`, `.bad`, `backups\reset-*`, `.defaults\`, `backups\import-*`, the copy before
   a connection delete, icon `.bak`). Keep "backup failure prevents replacement". Old backups stay readable; never delete them.
3. **Round-trip safety.** Unknown keys survive every save (settings, connections, toolbar; `mcp.json` already does). Loading never
   rewrites a file (remove `HasMissingOptions`, the pre-v14 width fix, the legacy `backends` list rewrite, and the on-read creation of
   `toolbar.json`, `mcp.json` and `.defaults\*`). One `version` rule with a `SettingsMigrations` class. A newer file is never
   overwritten (0.6.3 added the guard for `settings.json` and `toolbar.json`; make it the one rule for all four families).
4. **Renames and cuts in one migration**, with a backup: file stem is the connection id (no `id` key; collision handling before
   writing; an id outside `[A-Za-z0-9._-]` needs its Credential Locker key copied); `chat.backends` to `connections`,
   `defaultBackend` to `defaultConnection`; `window.height` to `minHeight`; one `window.width` taking px or `"40%"`; `growMode` into
   `window`; `extraArgs` as an array (a string accepted once); toolbar `version: 3` with numeric widths only, no `size`, `cells`,
   `height`, `spacer`, `items`; shortcuts in one place (`toolbar.json` `shortcuts` map, `chat.shortcuts` and `chat.stopShortcut`
   dropped); `mcp.json` servers only, approvals move to `settings.json` (removes `McpConfig.WriteBackup` and `SaveApproval`).
5. **Core decisions out of WinUI**, each with tests: previous-window choice (`ForegroundWindowPolicy` is done in 0.6.3), blur and focus
   policy (`PopupWindow` `OnActivated`, `ConsiderBlur`, `IsOurConsole`: Hide, Reclaim, Retry, Keep, Forget), pinned-spot restore and
   drop (`PanelPlacement.AfterDrag`, `RestoreSession`), sizing rules written twice (opening width, max height, the `60` that
   duplicates `SettingsRanges["WindowSettings.ChatHeight"]`), credential lifecycle (`ConnectionLifecycle` with `ISecretStore`:
   duplicate, remove, reset), key routing (`ChatPresenter` `OnPanelKey`), model and save rollback (`ProviderBar`), completed-answer
   eligibility (`TranscriptView`), `MonitorChoice.Pick`.
6. **Splits that follow ownership** (not size): `SettingsStore` into `ConnectionStore`, `SettingsMigrations`, `SettingsNormalizer`;
   `ToolbarModel` into reader, writer, shortcuts (breaks the Settings and Windowing namespace cycle); `ToolbarSession` in Core (load,
   fingerprint, save-if-unchanged, picture commit and rollback) so `ToolbarQuadrantEditor._stash` can go (keep overrides for hidden
   ids); `PopupWindow` into Core `PopupSessionState` and App `PopupPlacement`. After 1.0: `SettingsWindow` page classes,
   `ToolbarQuadrantEditor` views, `TranscriptView` state and scrolling, SVG handling, `App` runtime.
7. **Freeze** the formats with the fixtures as the gate.

## Owner decisions needed

Shortcut home (the `toolbar.json` map is proposed); width keys (one key, px or percent); where `mcp.json` approvals go; whether `env`
secrets in `mcp.json` move to the Credential Locker (existing literal values stay readable; conversion explicit and warned as not
portable); the connection id character set; the shape of `backups\`; whether to remove the Voice toggle (the Hold action is the single
source); `state.json` for reminder and first-run state (`general.supportReminder`, `.support-note-shown`, `.first-run-shown`).

## Dead or doubled code to remove

`SettingsService._knownWriteUtc` and its +5 s file-time test line; `ConnectionTypes.ListsModels` (true for all seven); toolbar JSON
parsed twice per load; `ChatSettings.Toolbar` (`[JsonIgnore]` copy that `CopyInto` resets); `VerticalPosition`; the `Serialize` and
`SerializeMain` duplicate; the `toolbar.json` `spacer` handled in three places; the localized "Default" text used as a sentinel in
the model pickers (`SettingsWindow.Connections.cs` `BuildEditor`). Naming drift to fix in the migration: `defaultBackend`,
`Backends`, `BackendProfile`, `growMode` beside `growDirection`, heights in DIP but widths in px.

Done in 0.6.3 from this list: `_knownWriteUtc`, `ListsModels`, the double parse, the narrow-panel clipped row (items that were real).

## Shared mechanisms (not protocols)

CLI: Agy and Claude Code repeat locking, context tracking, resume fallback and cancellation cleanup; all three repeat working-directory
validation. Extract small `CliTurnState`, directory and argument-token helpers; keep Codex's per-turn start. API: the three backends
repeat tool-round policy (call notes, dispatch, error notes, round limit); share it and keep each provider's replay envelope. Share
settings-row and confirmation-dialog helpers; keep target-specific validation. Do not merge Local into OpenAI-compatible, do not build
a universal action engine or a generic settings framework, do not add transcript virtualization without a measurement.

## Tests

- Frozen fixtures per format in `tests/fixtures/<family>/v<N>/`, JSON Schemas shipped, old fixtures load and migrate, a save keeps
  unknown keys, defaults validate, and `Resolve(ToJson(x)) == x` as a property test for the toolbar.
- Warnings as codes with arguments (App turns them into text); logic tests stop asserting English prose.
- At most about 15 file-system tests against one injected `IFileSystem` fake that can throw on demand (replaces ACL, working-directory,
  500-iteration race and file-time tests). Separate real-process tests (`McpProcessTests`) from the fast suite.
- Redistribute the review-named files by behavior; pin no schema constant in unrelated tests (`SettingsStoreTests` pins `15`).
- `TESTING.md` says "all logic in Core": not true for the decisions in step 5. No coverage tool runs; test count is not coverage.
- 0.6.3 cleaned the duplicates the reviewers named (see HANDOFF).

## Process

Release deliberately from a tag or dispatch after both architecture checks, not on every push to `main` (test and capture-script
pushes publish today; Release does not wait for the ARM64 result). Trim the disabled winget workflow and Store auto-submit
scaffolding; keep MSIX, the packaging script and the appinstaller. Shrink HANDOFF to architecture, invariants and links; one active
brief. Replace the versioned checklists in `TESTING.md` with one scenario matrix and a release-critical subset; record executed SHA,
environment and evidence. One scoped review per change; independent multi-reviewer passes only for schema, security and release
candidates; every fix gets a regression test, a rejected claim does not. Freeze language expansion (868 keys × 17 files); generate key
inventories. Defer the video pipeline; isolate capture in its own profile with readiness signals.

## Leave alone

Native `RichTextBlock` selection, provider-specific parsers, credential isolation, output, path and argument safety limits, ZIP
validation, the real process, speech and MCP test seams. Do not split `SettingsService`, `SettingsSchema` or `LenientReader` for size.
