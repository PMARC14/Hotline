# Hotline

Replace the Windows Copilot key with a fast popup that talks to the AI you choose:
Claude (via your installed Claude Code), Gemini (API or Antigravity CLI), any
OpenAI-compatible endpoint, or local llama.cpp models.

Status: early development. Licensed under Apache-2.0.

## Build & install (dev)

Requires .NET SDK 10.0.401+ on Windows 11 (22H2 or later).

    powershell -File scripts\dev-cert.ps1   # once: self-signed cert, trusted for sideloading (UAC)
    powershell -File scripts\install.ps1    # build, sign, install, launch

Then: Settings → Personalization → Text input → Customize Copilot key on keyboard → Custom → Hotline.

## Settings

Settings live in `%USERPROFILE%\.hotline\settings.json` (logs and history next to it).

Tray icon → right-click → **Edit settings file**, then **Restart (apply settings)**. Useful keys:

| Key | Default | Meaning |
|---|---|---|
| `window.widthPercent` / `minWidth` / `maxWidth` | `40` / `600` / `1000` | Panel width: % of the screen, clamped (DIPs) |
| `window.height` / `maxHeightPercent` | `320` / `70` | Baseline height (DIPs) and how much of the screen it may grow to |
| `window.verticalPosition` | `0.8` | 0 = top, 0.5 = centered, 1 = bottom of the screen's free space |
| `window.backdrop` | `acrylic` | `acrylic`, `acrylicThin`, `mica`, `solid` |
| `window.tintOpacity` / `window.luminosityOpacity` | `0.15` / `0.35` | Acrylic translucency (0–1; lower = clearer) |
| `window.hideOnBlur` | `true` | Hide when you click elsewhere |
| `window.fontSize` / `window.fontFamily` | `14` / (Segoe UI Variable) | Chat text size (10–32) and font, e.g. `"Cascadia Code"` |
| `window.scrollbar` | `auto` | `auto`, `visible`, `hidden` |
| `activation.tap` / `activation.hold` | `togglePopup` / `newChat` | Copilot key: short press opens/closes, long press starts a new chat. Also: `showPopup`, `captureWindow`, `none` |
| `activation.fallbackHotkey` | `null` | Extra hotkey, e.g. `"Ctrl+Alt+H"` |
| `diagnostics.verboseLogging` | `false` | Detailed log + key-status line in the popup |

| `chat.defaultBackend` | `agy` | Which AI answers (`agy` = Gemini via your Antigravity CLI sign-in) |
| `chat.backends[].model` | (agy default) | e.g. `gemini-3.8-flash-low` for faster answers (`agy models` lists them) |
| `chat.backends[].effort` | (agy default) | `low`, `medium`, `high`, `max` |
| `chat.growMode` | `grow` | `grow` = fit the conversation; `full` = jump to the maximum height (`window.maxHeightPercent`) once you chat |
| `chat.maxImagePixels` | `2048` | Attached/captured images are scaled to this longest edge |
| `chat.saveHistory` / `chat.historyRetentionDays` | `true` / `30` | Conversation logs in %USERPROFILE%\.hotline\history (text only) |

Comments and trailing commas are allowed. A broken file is kept as `settings.json.bad` and defaults are used.

### Chatting

Press the Copilot key, type, Enter. **+** or the toolbar attaches files and captures the window you were in or the whole
screen; Ctrl+V pastes images/files. To drag files in, **pin** the panel first (📌) — otherwise it hides when you click
elsewhere. Ctrl+N or a long press starts a new chat, Esc hides. The Antigravity backend needs the
[Antigravity CLI](https://antigravity.google/cli) installed and signed in (run `agy` once).
Chats run through `agy` are saved in its own history (`~/.gemini/antigravity-cli`), so `agy` → `/resume` shows them.

## Debugging

- Log: `%USERPROFILE%\.hotline\logs\hotline.log` (crashes are logged as `FATAL`).
- Debug build (verbose logging always on, key-status line visible): `powershell -File scripts\install.ps1 -Configuration Debug`
- Full crash dumps (opt-in, admin): `powershell -File scripts\enable-crash-dumps.ps1` (`-Disable` to undo).

## Tests

    dotnet test --project tests\Hotline.Core.Tests\Hotline.Core.Tests.csproj   # unit tests
    powershell -File tests\smoke\smoke.ps1 [-Install]                         # end-to-end against the installed app

The smoke test drives the same inputs Windows uses (protocol URIs, Copilot fast-path messages, the
fallback hotkey) and checks the app log. It restarts Hotline and restores your settings afterwards.

## CI and releases (GitHub Actions)

`.github/workflows/ci.yml` (build + tests) and `release.yml` (signed MSIX → GitHub Release) are **disabled by
default**. Enable them with the repository variable `HOTLINE_ACTIONS_ENABLED=true`. Manual runs work any time.

Releases need two secrets. The certificate subject must equal the manifest `Publisher` (`CN=pmarc14 Hotline Dev`):

    [Convert]::ToBase64String([IO.File]::ReadAllBytes('certs\hotline-dev.pfx')) | gh secret set HOTLINE_SIGNING_PFX_BASE64
    gh secret set HOTLINE_SIGNING_PFX_PASSWORD

Then push a tag (`git tag v0.2.0; git push origin v0.2.0`) or run **Release** manually. Each release attaches the
`.msix` plus `Hotline.cer`, which users must trust (Local Machine → Trusted People) before installing a self-signed build.
