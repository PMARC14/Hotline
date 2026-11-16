# S1: schema freeze design

The file formats Hotline freezes for 1.0, the mechanisms that keep them safe, and the work split. Status: **awaiting owner approval;
nothing is built.** Reasons are in [s1-design.reasoning.md](s1-design.reasoning.md), cited `(R#)`. This file replaces the
"Owner decisions" section of [simplification-plan.md](simplification-plan.md) where they differ.

## Contents

1. [Goals](#goals)
2. [Rules for every file](#rules-for-every-file)
3. [Formats](#formats)
4. [Mechanisms](#mechanisms)
5. [The one-time upgrade](#the-one-time-upgrade)
6. [Behaviour changes](#behaviour-changes)
7. [Tests](#tests)
8. [Work split](#work-split)
9. [Glossary](#glossary)

## Goals

- **Goal:** from 1.0, a file written by any 1.x loads in every later 1.x without loss. The formats are settled, not sealed: a
  change that keeps every existing file valid is allowed at any time; a change that breaks one needs a conversion `(R6)`.
- **Now:** one hard break before 1.0, with no conversion of old settings (see [the one-time upgrade](#the-one-time-upgrade)).
- **Non-goals:** plan steps 5 and 6 (Core decisions out of WinUI, class splits), warnings as codes, new languages.
- **Anti-goals:** a second mechanism beside an existing one; a load that writes; a save that drops a key it does not know.

## Rules for every file

| Rule | Detail |
|---|---|
| Version | Each file has `"version": N`. Adding a key or accepting more values never bumps it (lowercase ids still work once mixed case is allowed). A rename, removal, narrower rule or change of meaning bumps it and needs a migration in `SettingsMigrations` (empty at 1.0) |
| Newer file | A file with a higher `version` than the app knows is read as far as possible and never written |
| Unknown keys | Survive every save, in place, at every level |
| Loading | Never writes. A missing key takes its default in memory |
| Saving | Changes only the keys whose values changed. Key order stays. Comments are not kept |
| `null` | Means "default", the same as a missing key |
| Pixels | Every pixel number is in scaled pixels (the size Windows shows at its scaling). A percent is a share of the screen's work area `(R1)` |
| Defaults | Come from `src/Hotline.Core/Defaults/<family>.json`, built into the app `(R3)` |
| `$schema` | Never written by Hotline. A hand-added one survives as an unknown key |

## Formats

Five families. Fixtures: `tests/fixtures/<family>/v<N>/`. Schemas: `schemas/<family>.v<N>.schema.json` (JSON Schema 2020-12),
shipped in the package.

### `settings.json` (settings, v1)

```jsonc
{
  "version": 1,
  "general": { "language": "system", "supportReminder": true },
  "activation": { "tap": "togglePopup", "hold": "newChat", "fallbackHotkey": null },
  "window": {
    "width": "40%",            // number (scaled px, 200-4000) or "N%" (20-90)
    "chatWidth": null,         // same forms; null = no growth when a chat starts
    "minWidth": 320,           // clamps for width and chatWidth
    "maxWidth": 800,
    "minHeight": 0,            // was "height"
    "maxHeightPercent": 60,
    "chatHeight": null,
    "growMode": "grow",        // was chat.growMode
    "growDirection": "auto",
    "anchor": { "monitor": "active", "x": 0.5, "y": 0.8 },
    "openSound": "off", "closeSound": "off", "hideOnBlur": true, "alwaysOnTop": true,
    "theme": "system", "accent": "hotline", "fontSize": null, "fontFamily": null,
    "scrollbar": "auto", "backdrop": "acrylicThin", "tintOpacity": 0.15, "luminosityOpacity": 0.35
  },
  "diagnostics": { "verboseLogging": false, "testLinks": false },
  "chat": {
    "defaultConnection": "",   // was defaultBackend
    "defaultPrompt": "default", "continueIn": null, "order": [],
    "saveHistory": true, "notifyWhenDone": true,
    "notifyShowsText": false,  // new: false = "Answer ready" and the chat title
    "historyListSize": 300, "thinkingIndicator": true, "thinkingDelayMs": 700, "resumeWhileTyping": true,
    "cliTranscripts": true, "historyRetentionDays": 30, "maxImagePixels": 2048, "attachSelection": "auto",
    "ocr": "auto", "voiceAutoSend": false, "memory": true, "memoryTool": "ask"
  },
  "tools": { "approvals": {} } // "server/tool" or "server/*": "allow" | "ask" | "deny"
}
```

Removed: `schemaVersion`, `window.widthPercent`, `window.chatWidthPercent`, `window.height`, `window.verticalPosition`,
`chat.growMode`, `chat.defaultBackend`, `chat.backends`, `chat.shortcuts`, `chat.stopShortcut`.

### `connections\<id>.json` (connection, v1)

- The file name is the id: `[A-Za-z0-9._-]`, compared without case; device names and over-long names are refused.
- No `id` key. Keys otherwise as today, plus `"version": 1`. Keys with no value are left out.
- `extraArgs` is a list of strings.
- The Credential Locker key uses the lower-cased id.

### `toolbar.json` (toolbar, v3)

```jsonc
{
  "version": 3,
  "shortcuts": { "stop": "Esc", "newChat": "Ctrl+N", "recent": "Ctrl+H" /* ... every command */ },
  "top":    { "left": ["effort"], "center": ["model"], "right": ["provider"] },
  "bottom": { "left": ["pin", "captureWindow", "captureScreen", "captureRegion"],
              "center": ["prompt", "continueIn"], "right": ["recent", "newChat", "settings"] },
  "menu": [],
  "style": { "width": 1, "fill": true, "outline": false, "filled": false },
  "overrides": { "provider": { "span": 5 } },
  "buttons": {}
}
```

- `bottom` replaces `items`. `width` is a number only: 0.5, 1, 2, 3 or 4. `size`, `cells`, `height`, `spacer` and the width words are gone.
- **Shortcuts:** one map for every panel command, `stop` included. `stop` alone may be a bare key. An omitted id takes its default,
  `""` disables. Every command may be a bare F1-F24. `stop` is refused as a custom button id. Custom buttons keep `buttons.<id>.shortcut`.
- **Custom button ids:** letters in either case, digits and `-`, 1-32 characters, matched without case; the file keeps its spelling.
- **Conflicts:** `stop` wins, then the built-in commands in their fixed list order, then custom buttons in file order. The loser is
  disabled with a warning; the file keeps what was asked.
- **`style.fill`** (default `true`): see [Button fill](#button-fill). Default layout: every button is width 1.

### `mcp.json` (mcp, v1)

- `{ "mcpServers": { ... } }` in the common format. `version` is optional; absent means 1.
- Hotline writes it in three cases only: the starter file, the upgrade, and the explicit "Move to Credential Locker" action.
- An `approvals` key is ignored with a warning.
- **Secrets:** an `env` value that is exactly `${hotline:secret:<name>}` (name `[A-Za-z0-9._-]`) is read from the Locker key
  `mcp:secret:<name>` at launch. A missing secret, or any other value starting with `${hotline:`, keeps that server from starting and
  is reported. A non-string `env` value is skipped with a warning.

### `state.json` (state, v1)

App state, not configuration: written only by the app, never exported, never backed up, cleared by Reset. Losing it is harmless.

```jsonc
{
  "version": 1,
  "shown": { "firstRun": "2026-11-14T20:00:00Z", "supportNote": null, "upgradeNotice": null },
  "panel": {                   // from "Save position and size as default"
    "width":      { "value": 640, "over": "40%" },
    "chatHeight": { "value": 500, "over": null },
    "anchor":     { "value": { "monitor": "active", "x": 0.3, "y": 0.8 }, "over": { "monitor": "active", "x": 0.5, "y": 0.8 } }
  },
  "grants": [ { "server": "files", "tool": "read_file", "launch": "<sha256 of command, args, cwd>", "at": "2026-11-14T20:00:00Z" } ],
  "lastWritten": { "settings.json": "<sha256>", "connections/claude.json": "<sha256>" },
  "upgrade": { "at": "2026-11-14T20:00:00Z", "backup": "backups\\20261114-200000-upgrade" }
}
```

- **Saved spot:** each item applies while the `settings.json` value equals `over`. A changed setting makes it step aside.

## Mechanisms

### One writer

`SafeFile.Replace(path, bytes, backupSet)` in `src/Hotline.Core/Files/` is the only code that replaces a config file. It returns a
result (`Written`, `Unchanged`, `NewerVersion`, `BackupFailed`, `WriteFailed`); expected failures are not exceptions. It replaces the
writers in `SettingsStore`, `ToolbarFile`, `McpConfig`, `SettingsReset`, `SettingsPackageImport`, `SettingsWindow.Backup` and
`SettingsWindow.Connections`, and `BackupFiles`, `FileArchive` and the `.bad` copy. File access goes through an `IFileSystem` that
tests replace with a fake that can fail on demand.

### Backups

| Item | Rule |
|---|---|
| Place | `backups\<yyyyMMdd-HHmmss>-<reason>\<relative path>`; one folder per operation; `-2`, `-3` in the same second |
| Reasons | `save`, `reset`, `import`, `upgrade` |
| Pruning | Newest 20 `save` folders kept. Other reasons are never pruned |
| Skipped | For `save` only, when the file on disk matches `state.json` `lastWritten`. Reset, import and upgrade always copy |
| Failure | No backup, no write. Exception: [the upgrade](#the-one-time-upgrade) `(R5)` |
| Old backups | `.bak`, `.old`, `.bad`, `backups\reset-*`, `backups\import-*` stay and are never pruned |

### Round trip

`JsonMerge` (Core `Files/`) applies a typed model's changes to the file's JSON tree: it serializes the model before and after an
edit and sets only the keys that differ. Settings, connections and toolbar all save through it.

### Defaults from files

`Defaults/settings.json`, `toolbar.json` and `mcp.json` are embedded resources and the single source for starter files, `.defaults\`,
Reset, the in-memory value of a missing key, and the default shortcuts and layout. Class initializers, `SettingsRanges` defaults and
`ToolbarModel.Defaults` read them. A test validates each against its schema.

### Startup order

1. `PreFreezeUpgrade.Run` (below).
2. `StarterFiles.Ensure`: writes a missing `settings.json`, `toolbar.json` or `mcp.json` from the defaults and refreshes `.defaults\`.
3. Load. Nothing in the load path writes.

### Tool approvals

- **Rules** (`tools.approvals`): deny wins, then the tool's rule, then `server/*`.
- **Grants** (`state.json`): an "Always allow" click. A grant applies only where no rule matches and only while the server's
  command, args and cwd give the same `launch` hash.
- **No rule, no grant:** read-only tools run, others ask.
- A tool with an `ask` rule shows no "Always allow" button.
- Settings › Tools lists grants with **Revoke**. `SaveApproval`, `WriteBackup` and `_remembered` are removed.

### Backup export and import

- `state.json` is never exported. The Toolbar category reads "Button rows and shortcuts".
- Export writes secret tokens, never secrets. Import lists servers whose secrets are missing; the review masks `env` values and
  shows `tools.approvals`.

## The one-time upgrade

Runs once when any pre-freeze file is found (`settings.json` without `version`, `toolbar.json` below 3, a connection file with `id`
or without `version`, `mcp.json` with `approvals`) `(R2)`.

| File | Action |
|---|---|
| `settings.json` | Replaced by the defaults. Only `chat.defaultConnection` and `chat.order` carry over |
| `toolbar.json` | Replaced by the defaults |
| `connections\*.json` | Rewritten as v1: id from the `id` key (it wins over the file name), made to fit the id rule, collisions numbered; `extraArgs` split into a list. A changed id copies its Locker key first; the old entry is deleted after the copy reads back |
| `mcp.json` | Rewritten with its servers and without `approvals` |
| `.first-run-shown`, `.support-note-shown` | Flags move to `state.json`; the files are deleted |
| Everything else | Untouched |

- All originals go to one `backups\<stamp>-upgrade\` folder first. If that fails, the upgrade still runs `(R5)`.
- One notice, shown once, says what was reset and names the backup folder, or says there is none. Settings › Guide gets a section.
- 0.6.3 does not recognise a v1 `settings.json` and rewrites it in the old format with a `.bak`; the next upgrade resets it again.

## Behaviour changes

### Button fill

- With `style.fill`, a row's spare room is shared by its items: a button takes `width` shares, a picker takes `span` shares.
- A button is never narrower than one cell (half a cell for width 0.5); below that, buttons move to the "..." menu as today.
- A picker shrinks to its current narrowest width and then hides, in today's order. `span` is no longer a maximum.
- Groups set the order and the overflow order. With `fill` off, the row is laid out as in 0.6.3.

### Width

- `PanelWidth` (Core `Windowing/`) parses the two forms and resolves a width from the work area in scaled pixels, `minWidth` and
  `maxWidth`. The App multiplies by the monitor's scale.
- Settings › Window shows one width box that takes both forms. The line explaining real pixels and scaling is removed.

### Smaller changes

- **Save position and size as default** writes `state.json`, never `settings.json`.
- The support note records "shown" in `state.json`; `general.supportReminder` is only the user's preference.
- The answer toast shows the title only unless `chat.notifyShowsText` is on.
- Settings › General loses the Voice toggle; the Hold action picker remains.
- `version.txt` becomes `0.7`.
- `BackendProfile` becomes `ConnectionProfile`, `ChatSettings.Backends` becomes `Connections`, in one last mechanical commit.

## Tests

- Fixtures per family: `default`, `sparse`, `unknown-keys`, `newer-version`, `broken`; plus `tests/fixtures/pre-freeze/` (a 0.6.3
  folder built from code, never copied from a real `~\.hotline`).
- Each fixture validates against its schema, loads, and saves back byte-identical when nothing changed.
- Unknown keys survive a save in every family. A newer file is never written. A load writes nothing (the fake file system fails the
  test on any write).
- Toolbar: `Resolve(ToJson(x)) == x` as a property test; fill shares; the conflict order.
- File-system cases run on the `IFileSystem` fake, about 15 in all. No schema constant is pinned in an unrelated test.
- By hand, on the desktop: the upgrade notice, fill at several panel widths, width in `%` and pixels on two screens, saved spot,
  grants and revoke, the toast.

## Work split

One branch, `s1/schema-freeze`, one PR. Workers build in worktrees from the branch head; each patch gets a cross-vendor check and
is integrated with the full test run before the next wave starts. Briefs: `~/.claude/briefs/hotline/s1-*.md`.

| Wave | Task | Worker | Owns |
|---|---|---|---|
| 0 | **F0** fixtures, schemas, defaults files, fixture tests | Hydrogen high | `tests/fixtures/`, `schemas/`, `src/Hotline.Core/Defaults/`, `tests/Hotline.Core.Tests/Fixtures/` |
| 1 | **F1** `SafeFile`, backup sets, `IFileSystem`, `JsonMerge` | Sol high | `src/Hotline.Core/Files/`, `tests/Hotline.Core.Tests/Files/` |
| 1 | **F2** `StateStore` | Hydrogen high | `src/Hotline.Core/State/`, `tests/Hotline.Core.Tests/State/` |
| 1 | **F3** button fill | Sol high | `ToolbarPlan.cs`, `ToolbarLayout.cs`, `ToolbarRenderer.cs`, their tests |
| 2 | **S1** settings and connections v1, defaults from files, `PanelWidth`, round trip, no write on load | Sol high | `Settings/HotlineSettings.cs`, `SettingsStore.cs`, `LenientReader.cs`, `SettingsService.cs`, `SettingsSchema.cs`, `Windowing/PanelWidth.cs`, compile fixes elsewhere |
| 2 | **S2** toolbar v3, shortcuts with Stop, lossless save | Sol high | `Windowing/ToolbarModel.cs`, `ToolbarFile.cs`, `ToolbarEditor*.cs`, `ToolbarProfiles.cs`, `Settings/ChatShortcuts.cs`, `ToolbarQuadrantEditor*`, `SettingsWindow.Extras.cs` |
| 2 | **S3** tool rules, grants, secret tokens | Hydrogen high | `src/Hotline.Core/Tools/`, the Settings › Tools page |
| 3 | **U1** upgrade, starter files, Reset, Backup export and import | Sol high | `src/Hotline.Core/Upgrade/`, `SettingsReset.cs`, `SettingsPackage*.cs`, `BackupFiles.cs`, `FileArchive.cs`, startup wiring |
| 3 | **A1** saved spot, notes and toast in the App | Hydrogen high | `PanelGrips`, `FirstRunNote`, `SupportNote`, `AnswerNotifier`, the save command in `ChatPresenter` |
| 4 | Rename commit, docs, version, review, smoke | Claude | the rest |

- **Quota:** Codex 5h 69%, weekly 74% on 2026-11-14; a Sol job costs about 8% of the 5-hour window. Sol jobs run at most two at a
  time and wait for a reset when the window is short. Gemini has room (5h 6%, weekly 11%).
- **Review before the PR:** Argon high and Sol high (Astra is over its 40% gate).

## Glossary

- **Family:** one file format (settings, connection, toolbar, mcp, state).
- **Scaled pixel:** a pixel as Windows counts it after scaling; 2880 real pixels at 200% are 1440 scaled.
- **Pre-freeze file:** a config file written by 0.6.x or earlier.
- **Backup set:** one `backups\<stamp>-<reason>\` folder holding every file an operation replaced.
- **Rule / grant:** a hand-written tool approval in `settings.json` / an "Always allow" click stored in `state.json`.
- **Saved spot:** the position and size stored by "Save position and size as default".
- **Fill:** sharing a button row's spare room among its items.
