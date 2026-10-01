# Hotline — Copilot-key AI popup for Windows (design + plan)

## Context
The Windows 11 Copilot key (and Win+C) can only launch MSIX-packaged, signed apps that register as a
`com.microsoft.windows.copilotkeyprovider`. No existing open-source app does what we want: a native,
low-bloat, Copilot-style centered popup that routes to **any** backend — local llama.cpp models (NPU
for light, Arc GPU via oneAPI/SYCL for heavy), Claude Pro via the user's installed Claude Code CLI,
Gemini via Antigravity CLI (`agy`) or the Gemini API, or any OpenAI-compatible endpoint — with
Copilot/Gemini-style screen sharing. Research: Witsy/PyGPT/Cherry Studio are Electron/Python and
bloated with no CLI-subscription or llama.cpp lifecycle support. Omnigent shows the right pattern
(drive the vendor's own logged-in CLI), but its claude/agy wrappers need tmux, which Windows
doesn't have. So we build our own and copy that pattern directly. Goal: personal use first,
public GitHub release later.

## Decisions (agreed)
- **Name** Hotline · repo `PMARC14/hotline` · protocol `hotline:` · Settings picker name "Hotline".
- **Stack** C# / .NET 10, WinUI 3 (Windows App SDK), packaged MSIX (full trust). Chat transcript rendered in
  WebView2 (ships with Win11), with markdown-it + highlight.js bundled locally and no JS framework.
- **Signing** Self-signed dev cert + install script for now; Azure Trusted Signing or the Store later.
- **License** Apache-2.0. Monetization via donations only (e.g. GitHub Sponsors link in the README). No CLA.
- **No Ollama.** Local inference via llama.cpp `llama-server` (and optionally OpenVINO Model Server). The app does **not**
  bundle inference engines; it points at the user's installed binaries. **Low priority:** the user sets up
  llama.cpp themselves; the local-server milestone comes last.
- **Key handling:** register both URI activation (`hotline://key?state=Tap|Down|Up`) and the Copilot key
  **fast path** (window message with `MessageWParam` 0/1/2 sent to the running app's registered window), so a
  resident app reacts instantly. The tray icon is native (`Shell_NotifyIcon`), with no tray library dependency.
- **Default UI** centered floating "quick view" panel like Copilot's (≈ 640×520, expandable); alternative
  layouts are a command bar and a right-docked side panel (both settings).
- **Local warmup default:** the NPU model is always warm (low power); the GPU model wakes on key press and sleeps after an idle period.
- **v1 inputs:** typed chat + history, window/screen capture, region select, file/image paste & drag-drop,
  clipboard/selected-text attach. Voice (push-to-talk on key hold) → v2.

## Architecture
```
Copilot key ──► Windows shell ──► hotline://tap | hold-start | hold-stop
                                    │ (AppInstance redirect to the running instance)
Fallback hotkey (RegisterHotKey) ───┤
                                    ▼
Hotline.App (WinUI 3, tray-resident, single instance)
  ActivationRouter → PopupWindow (+ WebView2 chat view) ↔ ChatController
                     CaptureService (window / region / screen)   ContextService (clipboard, selection)
                                    │ IChatBackend
Hotline.Core (net10 lib, no UI)    ▼
  OpenAiCompatBackend  GeminiApiBackend  AnthropicApiBackend  ClaudeCodeBackend  AgyBackend
  LocalServerManager (spawn / warm / sleep / stop llama-server & OVMS, Job Object)
  SettingsStore (JSON + schema version)  SecretStore (Credential Manager)  HistoryStore (JSONL)
```

### Components
| Unit | Responsibility |
|---|---|
| `ActivationRouter` | Handles protocol URIs, fallback hotkey and tray clicks. Maps tap, hold, and double-tap to configurable actions (toggle popup, open with screen capture, open region select, new chat). Captures the foreground window (`HWND`) **before** showing the popup. |
| `PopupWindow` | Borderless, Mica/Acrylic, rounded, centered on the active monitor. Hides on deactivation (setting). Pre-created and hidden at startup so it appears instantly. Supports the three layouts. |
| Chat view (`Web/`) | Static HTML/JS/CSS. Streams tokens via `postMessage`, shows attachment chips, a backend/model picker, copy buttons and history sidebar. |
| `IChatBackend` | `StreamAsync(conversation, attachments, ct) → IAsyncEnumerable<ChatDelta>`, `ListModelsAsync`, `WarmAsync`, and `Capabilities` (images, files, system prompt). |
| `OpenAiCompatBackend` | Any `/v1/chat/completions` server: llama-server, OVMS, OpenRouter, LM Studio, vLLM, and so on. |
| `GeminiApiBackend` | `streamGenerateContent` with an API key; sends images inline. |
| `AnthropicApiBackend` | Messages API with an API key: the officially supported Claude path for public users. |
| `ClaudeCodeBackend` | Keeps one persistent `claude -p --input-format stream-json --output-format stream-json --verbose` process per conversation, so there is no per-prompt startup. Tools are restricted by setting (default: none or read-only). Images go as content blocks, falling back to a temp-file path in the prompt. Uses the user's own login, framed as "uses your installed Claude Code". |
| `AgyBackend` | Runs `agy -p` per turn and streams stdout. Attachments are saved to temp files and referenced by path. Context is kept by replaying a transcript summary or using agy's session flag if one exists (verify). |
| `LocalServerManager` | Profiles, each with exe, args, port, device, `keepWarm`, `warmOnKey`, `idleSleepSec`, and `sleepMode` = unload (`--sleep-idle-seconds` / router unload) or stopProcess (works around llama.cpp #19379, GPU memory held after sleep). Warm-up is a router `/models/load` or a 1-token completion, because `/health` doesn't wake the model. Child processes live in a Job Object so they die with the app. |
| `SettingsStore` / `SecretStore` / `HistoryStore` | JSON settings in app LocalFolder with a migration step; API keys in Windows Credential Manager; conversations as JSONL with retention and incognito settings. |

### Local model presets (user supplies binaries + GGUF)
- **Light / NPU (always warm):** llama.cpp with OpenVINO backend (`-DGGML_OPENVINO=ON`, OpenVINO 2026.1+,
  device NPU, preview: no NPU model cache, `-np 1`). Alternative preset: OpenVINO Model Server NPU (OpenAI API).
  Suggested model ~1–4B (e.g. Qwen/Gemma small, Q4_0/Q4_K_M).
- **Heavy / Arc B390 GPU (wake on key):** llama.cpp SYCL build (oneAPI) in router mode with `--sleep-idle-seconds`;
  Vulkan build as fallback preset. Suggested 7–14B Q4_K_M given the 32 GB of shared memory.
- The README links official llama.cpp / OpenVINO release builds; the app has a "detect & test" button per profile.

### Settings (v1 surface, all in Settings page + JSON)
- **Activation:** tap / hold / double-tap actions; fallback hotkey (default Alt+Space off, user-chosen); start with Windows.
- **Window:** layout (quick view / command bar / side panel), size, monitor (active / primary), hide on blur,
  always on top, opacity, theme (system / light / dark), font size.
- **Context:** auto-attach clipboard (off / ask / on), grab selection via synthesized Ctrl+C (off by default),
  default capture target (foreground window / pick / full screen / none), capture border, image downscale max px.
- **Backends:** list of backend profiles (type, endpoint, model, system prompt, temperature, max tokens), default
  backend, quick-switch shortcut keys, per-backend CLI path + extra args + tool permissions.
- **Local servers:** profiles as above; idle timers; keep-warm; port; logs viewer.
- **History:** save on/off, retention days, incognito toggle, export.

### Theming (architecture requirement; full UI later)
The user wants Hotline to look distinctive, not "default Windows". Design for it from the start:
- **Theme = data, not code.** A theme is a JSON file (`themes/<name>.json`) holding design tokens: colors
  (background, surface, text, accent, borders), font family and size, corner radius, spacing, shadow, popup opacity,
  and backdrop (`acrylic` / `mica` / `solid` / `transparent`). Built-in themes ship in the package; user themes live
  in LocalFolder, and `settings.window.theme` names the active one. There are separate light and dark variants, plus a "follow system" option.
- **One token pipeline.** `Hotline.Core` loads and validates the theme, with a fallback to the built-in default. The chat view gets
  tokens as CSS custom properties (`--hl-bg`, `--hl-accent`, …), so nearly all visual styling lives in CSS. The native frame
  applies only backdrop, corner radius and border/title-bar colors. No colors are hard-coded in XAML or JS.
- **Escape hatch:** an optional per-theme `custom.css` lets users override anything in the chat view.
- Layouts (quick view / command bar / side panel), and later animations, are independent of the theme.
- Theme switching applies live (no restart) once the settings UI lands.

### Error handling
Every backend surfaces typed errors (`NotInstalled`, `NotLoggedIn`, `ServerDown`, `RateLimited`, `Unsupported`) shown in the
chat view as an actionable banner, for example "claude not found — set path" or "Start local server". There is a cancel button,
and a stalled stream times out. If a CLI process crashes, it is restarted on the next turn. LocalServerManager logs child
stdout/stderr to a rotating file.

## Repo layout
```
hotline/
  Hotline.sln
  src/Hotline.Core/        Backends/, Local/, Settings/, History/, Models/
  src/Hotline.App/         App.xaml, Activation/, Windows/, Capture/, Tray/, Web/ (html/js/css), Package.appxmanifest
  tests/Hotline.Core.Tests/  (xUnit) fake HTTP servers + fake CLI exes for stream parsing & lifecycle
  scripts/ dev-cert.ps1, build-msix.ps1, install.ps1
  docs/superpowers/specs/2026-09-30-hotline-design.md   (this design, committed first)
  LICENSE (Apache-2.0), README.md
```
Dependencies are kept minimal: Microsoft.WindowsAppSDK, Microsoft.Web.WebView2 and xUnit. Win32 interop is hand-written `DllImport`s.

## Milestones (each ends with a working, committed state)
0. **Prereqs + risk spike.** Install the .NET 10 SDK (user approval). Create a minimal packaged WinUI app with the
   `copilotkeyprovider` extension (with `SingleTap` / `PressAndHoldStart` / `PressAndHoldStop` properties) and a `hotline:` protocol,
   signed with a self-signed cert. **Verify** that it appears in Settings → Personalization → Text input → Copilot key, and that tap and
   hold launch the URIs. Also verify that the MSIX builds without full Visual Studio. If the picker refuses it, fall back to the policy key + hotkey.
1. **Shell:** single instance + activation redirect, tray, pre-warmed hidden centered popup, fallback hotkey, SettingsStore.
2. **Chat:** WebView2 chat view, ChatController, OpenAiCompatBackend streaming, HistoryStore.
3. **Cloud APIs:** Gemini API + Anthropic API backends, SecretStore.
4. **CLIs:** ClaudeCodeBackend (persistent stream-json) and AgyBackend (install `agy`, verify flags and image handling).
5. **Context:** window capture (Windows.Graphics.Capture, foreground or picker), region-select overlay, drag-drop/paste,
   clipboard/selection.
6. **Polish:** Settings UI, command-bar and side-panel layouts, double-tap action, README, GitHub repo.
7. **Local (low priority):** LocalServerManager + NPU (OpenVINO) and GPU (SYCL) presets, warm-on-key / keep-warm / idle sleep.

Each milestone group gets its own implementation plan in `docs/superpowers/plans/`. Plan 1 covers milestones 0–1.

Execution follows superpowers flow: write spec into repo → user reviews → writing-plans for detailed task plan → TDD on Core.

## Verification
- **Unit (Core):** stream parsers for the OpenAI SSE, Gemini, Anthropic and Claude stream-json formats against recorded fixtures; settings migration;
  LocalServerManager using a fake server exe (start, warm, idle-sleep, stop, crash restart, Job Object cleanup).
- **Manual end-to-end per milestone:** press the Copilot key and see the popup in under 300 ms when warm; tap vs hold actions; switch backends mid-session;
  ask about a captured window with Gemini API and with Claude Code; NPU model answers instantly while the GPU model cold-loads and
  then sleeps (check via `/props` sleeping state and Task Manager GPU memory); kill `claude` mid-stream and see the error banner and recovery.
- **Packaging:** a clean install on a second user account with `install.ps1`; uninstall removes everything and leaves no orphaned servers.
