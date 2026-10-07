# Third-party notices

Hotline is licensed under Apache-2.0 (see `LICENSE`). It uses the following third-party components:

| Component | Use | License |
|---|---|---|
| [Windows App SDK](https://github.com/microsoft/WindowsAppSDK) (incl. WinUI 3) | UI framework, packaging, app lifecycle | Microsoft Software License Terms (`license.txt` in the NuGet package) |
| [Markdig](https://github.com/xoofx/markdig) | Markdown parsing | BSD-2-Clause |
| [Anthropic C# SDK](https://github.com/anthropics/anthropic-sdk-csharp) | Anthropic API connection | MIT |
| [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) | MCP tool servers | Apache-2.0 |
| [Interop.UIAutomationClient](https://github.com/Roemer/UIAutomation-Interop) | UI Automation (reading selected text) | MIT |
| [Fluent Emoji](https://github.com/microsoft/fluentui-emoji) | Placeholder app icon (telephone receiver) | MIT (see `assets/source/ATTRIBUTION.md`) |

Optional external programs Hotline can launch are installed and licensed separately by the user: Claude Code,
the Antigravity CLI (agy), MCP servers listed in `mcp.json`, and the Windows on-device agent registry (`odr.exe`).
