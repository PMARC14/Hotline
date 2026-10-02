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

Settings live in `%USERPROFILE%\.hotline\settings.json` (logs, history and `prompts\` next to it). The file is the
configuration: every option is written into it, and saving it applies the change immediately. The settings window
(⚙ in the panel, tray → **Settings…**, or `hotline://settings`) edits the same file. API keys are the exception: they
are stored in Windows Credential Locker. Useful keys:

| Key | Default | Meaning |
|---|---|---|
| `window.widthPercent` / `minWidth` / `maxWidth` | `40` / `600` / `1000` | Panel width: % of the screen, clamped (DIPs) |
| `window.height` / `maxHeightPercent` | `0` / `70` | Minimum height (DIPs; 0 = just the message bar) and how much of the screen it may grow to |
| `window.verticalPosition` | `0.8` | 0 = top, 0.5 = centered, 1 = bottom of the screen's free space |
| `window.backdrop` | `acrylic` | `acrylic`, `acrylicThin`, `mica`, `solid` |
| `window.tintOpacity` / `window.luminosityOpacity` | `0.15` / `0.35` | Acrylic translucency (0–1; lower = clearer) |
| `window.hideOnBlur` | `true` | Hide when you click elsewhere |
| `window.fontSize` / `window.fontFamily` | (Windows default) / (Segoe UI Variable) | Chat text size (10–32; empty = follows Windows' text size) and font, e.g. `"Cascadia Code"` |
| `window.scrollbar` | `auto` | `auto` (appears when you scroll or point at the right edge), `visible`, `hidden` |
| `activation.tap` / `activation.hold` | `togglePopup` / `newChat` | Copilot key: short press opens/closes, long press starts a new chat. Also: `showPopup`, `captureWindow`, `none` |
| `activation.fallbackHotkey` | `null` | Extra hotkey, e.g. `"Ctrl+Alt+H"` |
| `diagnostics.verboseLogging` | `false` | Detailed log + key-status line in the popup |

| `chat.defaultBackend` | `agy` | Which connection answers by default (its `id`). The **order of `chat.backends`** is the order in the Provider dropdown |
| `chat.backends[].model` | (agy default) | e.g. `gemini-3.8-flash-low` (`agy models` lists them; agy puts the effort level in the model id) |
| `chat.backends[].effort` | (agy default) | `low`, `medium`, `high` — only used with the default model |
| `chat.backends[].tools` | `chatOnly` | `chatOnly`, or `inherit` = the CLI's own tools and permission rules in `workingDirectory` |
| `chat.backends[].prompt` / `chat.defaultPrompt` | `null` / `default` | System prompt = `prompts\<name>.md` |
| `chat.backends[].approveAllTools` | `false` | ⚠ Dangerous: agy runs any command / edits any file without asking (inherit mode only) |
| `chat.backends[].args` | `null` | Replaces Hotline's launch flags for the mode, e.g. `["--agent", "mine"]` (agy) or `["--tools", "Read"]` (Claude Code). Hotline always adds the stream/print flags it needs; dangerous flags only via `approveAllTools` |
| `chat.backends[].extraArgs` | `null` | Added on top, e.g. `"--mcp-config C:\mcp\windows.json"` to give a chat-only Claude exactly those MCP servers |
| `chat.backends[].keepCliSessions` | `false` | Claude Code also saves Hotline chats in its own history (`claude --resume`); agy always does |
| `chat.growMode` | `grow` | `grow` = fit the conversation; `full` = jump to the maximum height (`window.maxHeightPercent`) once you chat |
| `chat.maxImagePixels` | `2048` | Attached/captured images are scaled to this longest edge |
| `chat.saveHistory` / `chat.historyRetentionDays` | `true` / `30` | Conversation logs in %USERPROFILE%\.hotline\history (text only) |

Comments and trailing commas are allowed. A half-saved or broken file is ignored while Hotline runs; at startup a broken
file is kept as `settings.json.bad` and defaults are used.

### AI connections, tools and prompts

**AI connections** (settings window) adds, duplicates and removes connections: Antigravity (agy), Claude Code, Gemini
API, Anthropic API, OpenAI-compatible APIs and local endpoints (llama.cpp, LM Studio). Only Antigravity chats today; the
others can already be configured and tested. In the bottom bar, **Effort → Model → Provider** pick who answers.

**Tool use** per connection: *Chat only* (default; reads only your attachments) or *Use the program's own tools*. agy
then works in your chosen folder under **its own** permission rules (`~/.gemini/antigravity-cli/settings.json`,
`permissions.allow/deny`). Hotline runs agy in the background, where anything that would ask is **denied**, so shell
commands need allow rules: **Allow read-only commands** adds a curated list (and denies `Remove-Item`, `rm`,
`git push`…). **Approve everything** skips all checks — dangerous.

**System prompts** are Markdown files in `%USERPROFILE%\.hotline\prompts`; switch them from the 📄 button in the bar.

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
