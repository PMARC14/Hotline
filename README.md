# Hotline

**Turn the Windows Copilot key into a fast, native AI popup that talks to the AI *you* choose.**

Press the key, ask, get a streamed answer — then it's gone. Hotline uses the AI tools you already have (your
installed Claude Code or Antigravity CLI and their sign-ins) or any API key you bring, and keeps everything
configurable in plain files.

<p align="center">
  <img src="docs/images/panel.png" width="560" alt="The Hotline panel answering a question with a formula, code and a table">
</p>

> Status: early development, personal-use quality. Windows 11 only. Apache-2.0.

## Features

- **The Copilot key, reclaimed** — short press opens/closes, long press starts a new chat (both configurable), plus an
  optional extra hotkey. Opens on the monitor you're working on, focused and ready to type.
- **Your choice of AI** — pick *Effort → Model → Provider* in the bar:
  - **Claude Code** (your Claude plan, no API key) and **Antigravity CLI / agy** (Gemini with your Google sign-in)
  - **Anthropic API**, **Gemini API**, **OpenAI-compatible** APIs (OpenAI, OpenRouter, Groq, …)
  - **Local models** through any OpenAI-style server (llama.cpp's `llama-server`, LM Studio, vLLM)
- **Native and light** — WinUI 3 with acrylic, no embedded browser. Answers stream in and the panel grows smoothly.
- **Rich answers** — Markdown with headings, lists, tables, code (with Copy), math (LaTeX → readable symbols), inline
  SVG drawings, and the whole conversation selectable in one drag.
- **Screens and files** — capture the window you were in, the whole screen, or a region you drag out; paste or drop
  images and files.
- **Recent chats** — reopen the last chats from the 🕘 menu or with Ctrl+↑.
- **Tools, carefully** — API connections can use tools from your **MCP servers** (`mcp.json`, the same format other
  MCP apps use) and, when your Windows has it, the built-in **Windows agent connectors**. Read-only tools run; anything
  else asks you in the panel (*Allow once / Always / Deny*). CLI connections can use their own tools under their own
  permission rules.
- **System prompts** as Markdown files you can switch from the bar.
- **Your bar, your way** — `toolbar.json` picks which buttons and pickers the bar shows, and in what order.
- **Everything is a file** — settings, one file per AI connection, prompts; edits apply instantly. API keys live in
  Windows Credential Locker.

<p align="center">
  <img src="docs/images/settings.png" width="640" alt="The Hotline settings window">
</p>

## Install (from source)

Requires Windows 11 22H2+ and the [.NET SDK 10.0.401+](https://dotnet.microsoft.com/download).

```powershell
git clone https://github.com/PMARC14/hotline
cd hotline
powershell -File scripts\dev-cert.ps1   # once: creates a self-signed certificate and trusts it for sideloading (UAC)
powershell -File scripts\install.ps1    # builds, signs and installs Hotline, then starts it in the tray
```

Then assign the key: **Settings → Personalization → Text input → Customize Copilot key on keyboard → Custom →
Hotline**. (Windows only lets packaged apps take the Copilot key, which is why Hotline installs as a package.)

## Using it

- **Copilot key** — open/close. **Long press** — new chat. **Esc** hides, **Ctrl+N** new chat.
- **+** / bar buttons — attach files, capture the window you were in, the whole screen, or a region. Ctrl+V pastes images.
- **📌 Pin** keeps the panel open while you drag files in.
- **⚙ Settings** — AI connections, prompts, appearance, window size, history, folders.

### Connecting an AI

| Connection | You need |
|---|---|
| Claude Code | [Claude Code](https://claude.com/claude-code) installed and signed in (run `claude` once) |
| Antigravity (agy) | The [Antigravity CLI](https://antigravity.google/cli) installed and signed in (run `agy` once) |
| Anthropic / Gemini / OpenAI-compatible | An API key — Settings › AI connections › Add connection, paste the key, pick a model |
| Local | An OpenAI-style server running (e.g. `llama-server` on `http://127.0.0.1:8080`) |

Keys are only ever sent over https (plain http only to this PC).

### Configuration

All options, the file layout and the agy permission setup: **[docs/configuration.md](docs/configuration.md)**.

## Development

```powershell
dotnet test --project tests\Hotline.Core.Tests\Hotline.Core.Tests.csproj   # unit tests (Core)
powershell -File tests\smoke\smoke.ps1 [-Install]                         # end-to-end against the installed app
```

The smoke test drives the same inputs Windows uses (protocol links, Copilot key messages, the hotkey), renders a
scripted conversation off-screen and builds every settings page, then checks the log. It restores your settings.
Debug build: `scripts\install.ps1 -Configuration Debug` (verbose log, key-status line). Crash dumps (opt-in, admin):
`scripts\enable-crash-dumps.ps1`.

- **Layout:** `src/Hotline.Core` (UI-free logic: backends, settings, markdown, tools — fully unit-tested),
  `src/Hotline.App` (WinUI 3 app), `tests/`, `scripts/`, `docs/` (design spec, plans, handoff notes).
- **Contributors / new sessions:** start with [docs/HANDOFF.md](docs/HANDOFF.md).

### CI and releases

`.github/workflows/ci.yml` (build + tests) and `release.yml` (signed MSIX + `Hotline.appinstaller` for automatic
updates → GitHub Release) are disabled by default;
enable them with the repository variable `HOTLINE_ACTIONS_ENABLED=true`. Releases need the signing certificate as
secrets (its subject must equal the manifest `Publisher`):

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes('certs\hotline-dev.pfx')) | gh secret set HOTLINE_SIGNING_PFX_BASE64
gh secret set HOTLINE_SIGNING_PFX_PASSWORD   # prompts for your certificate password
```

## Roadmap

Trusted signing (Store or Azure Trusted Signing) and one-click installs with automatic updates for the first public
release — see
[docs/RELEASING.md](docs/RELEASING.md) and [docs/HANDOFF.md](docs/HANDOFF.md).

## License

Apache-2.0 — see [LICENSE](LICENSE). Third-party components: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)
(placeholder app icon from Microsoft's [Fluent Emoji](https://github.com/microsoft/fluentui-emoji), MIT).
