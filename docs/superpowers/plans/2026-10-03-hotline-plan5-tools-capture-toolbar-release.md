# Hotline Plan 5 — Tools (MCP + Windows agent registry), Region Capture, Customizable Bar, Release Prep

**Goal:** API connections can use tools; tool calls are approved in the panel; the Windows on-device agent registry is
picked up when present; region capture; a file-driven bottom bar; release groundwork.

**Spec:** `docs/superpowers/specs/2026-09-30-hotline-design.md` (Plan 5, Priorities, Bottom bar, ODR, UI follow-ups).

**Workflow:** branch `plan5-tools` from GitHub `main`, pushed after every task; merged to `main` through a pull request.

## Global constraints
- Config stays in files under `%USERPROFILE%\.hotline` and applies live: `mcp.json` (servers + approvals), `toolbar.json`.
- `mcp.json` uses the common `{"mcpServers": {name: {command, args, env, disabled}}}` shape (pasteable from other apps),
  plus Hotline's `"approvals": {"server/tool": "allow" | "ask" | "deny"}`.
- Default policy: tools with `readOnlyHint: true` run without asking; everything else asks; `deny` always wins.
- Windows agent registry: if `odr.exe` exists, `odr.exe mcp list` (JSON) adds servers named `windows/<id>`; otherwise the
  Tools page says "not available on this Windows (needs build 26220.7262+)". Never required.
- API connections: `tools: "inherit"` = use Hotline's MCP tools; `chatOnly` = no tools (default). CLI connections keep
  their own tools. Max 20 tool rounds per answer; tool results truncated to 20k chars.
- Tool activity is visible in the transcript; approvals are an InfoBar in the panel (Allow once / Always / Deny) and
  time out as Deny after 5 minutes.
- Tests: Core logic unit-tested (config, policy, ODR parsing, tool loops per API with fake HTTP and fake tool host);
  smoke test stays green; panel left closed; commits authored by pmarc14 with the Co-Authored-By trailer.

## Tasks
1. **Core tools model:** `McpConfig` (parse/merge/save approvals), `ToolPolicy`, `OdrDiscovery` (parse `odr mcp list`),
   `IToolHost` (`Tools`, `CallAsync` with approval callback), `McpToolHost` (ModelContextProtocol.Core stdio clients,
   lazy start, per-server failure isolation, process cleanup).
2. **Tool loops:** OpenAI (`tools` + streamed `tool_calls`), Anthropic (SDK tool_use / tool_result), Gemini
   (`functionDeclarations` / `functionCall` / `functionResponse`); transcript notes for each call.
3. **App:** approval InfoBar, Tools settings page (servers, status, tools, approvals, open mcp.json, ODR status), Tool
   use switch for API connections, live reload of `mcp.json`.
4. **Region capture:** full-screen overlay to drag a rectangle (Esc cancels), key action `regionSelect`, + menu item.
5. **Customizable bar:** `toolbar.json` (`items` in order: pin, captureWindow, captureScreen, captureRegion, effort,
   model, provider, prompt, recent, newChat, settings; hide by omitting), defaults written on first run, live reload.
6. **Release prep:** README (features, setup, screenshot placeholders), CI build+test workflow on, packaging notes,
   LICENSE check, sparse-package installer design note.
