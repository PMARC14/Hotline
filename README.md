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
