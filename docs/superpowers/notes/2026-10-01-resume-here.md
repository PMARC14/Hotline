# Resume here (paused 2026-10-01)

## State
- `main` is pushed to private GitHub `PMARC14/hotline`. Working tree clean after this commit.
- **Done:** Plan 1 (Copilot key + shell) plus the polish batch:
  - translucent acrylic, compact 560×120 bar at `verticalPosition` 0.8
  - verbose/debug logging, `FATAL` crash log, crash-dump script
  - smoke test (15 checks), CI/release workflows (disabled until `HOTLINE_ACTIONS_ENABLED=true`)
- **Tests:** 91 unit tests (`dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`) and
  `tests/smoke/smoke.ps1 -Install` both pass. Hotline is installed and selected as the Copilot key app.
- **Crashes fixed:**
  1. NRE in `PlaceOnActiveMonitor` (`DisplayArea` null): monitor fallback + logging.
  2. `0x800706BE` from reading redirected activation args too late: `ActivationRouter.Snapshot`.
  3. Any handler exception is now contained (`MessageDispatcher`, guarded dispatcher callbacks).
- **Next step:** write `docs/superpowers/plans/2026-10-02-hotline-plan2-chat-gemini.md` with the writing-plans skill
  (full code, TDD steps), get the user's review, then execute **inline (Native)**.

## User decisions for Plan 2 (2026-10-01)
- **Backend priority:** Gemini API → Antigravity CLI (agy) → OpenAI-compatible → Claude Code.
  Plan 2 = chat foundation + Gemini. Plan 3 = agy, OpenAI-compatible, Claude Code (in that order).
- **On send:** the bar grows **upward** into a chat panel (bottom edge anchored) up to a max height setting.
- **+ menu in Plan 2:** file/image picker, paste and drag-drop, capture window / capture screen.
  Region select comes later, designed in contrast with Microsoft Click to Do.
- Everything tunable via settings. Acrylic stays the default. Commits are authored by pmarc14 <16502495+PMARC14@users.noreply.github.com>.

## Verified facts
- **Gemini:**
  - `POST https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse`, header
    `x-goog-api-key`. Google labels it "legacy" but "fully supported".
  - The newer stateful Interactions API (`/v1beta/interactions`, `previous_interaction_id`) was rejected: stateless calls
    allow mid-chat backend switching.
  - Default model `gemini-3.8-flash`. Request `contents[{role:user|model, parts:[{text}|{inline_data:{mime_type,data}}]}]`
    plus `system_instruction`. Each SSE chunk carries `candidates[0].content.parts[].text`; skip parts with `thought: true`.
- **agy 1.2.14** at `%LOCALAPPDATA%\agy\bin\agy.exe`:
  - Flags: `-p`, `--output-format stream-json`, `--input-format stream-json` (persistent, NDJSON per turn),
    `--model`, `--agent`, `--mode`, `--effort`, `--conversation`, `-c`, `--dangerously-skip-permissions`, `--sandbox`.
  - Events: `{"event":"init",...}`, `{"event":"step_update","step_update":{...,"text_delta":"..."}}`,
    `{"event":"result","result":{"response":...,"status":"SUCCESS"}}`.
  - It loads about 19k input tokens of tools per turn, so look for a lighter agent (`agy agents`).
  - `agy models` lists gemini-3.8-flash-{high,medium,low}, gemini-3.1-pro, claude-sonnet-4-6, claude-opus-4-6-thinking, gpt-oss-120b.
  - Exit code was 255 in a PowerShell pipeline even on SUCCESS; check before relying on exit codes.

## Plan 2 design (to turn into the full plan)
**Core (TDD):**
1. `Chat/ChatModels.cs`:
   - `ChatRole`, `AttachmentKind {Image, Text}`, `Attachment(Id, Name, Kind, MimeType, byte[] Data)`,
     `ChatMessage(Id, Role, Text, Attachments, At, BackendId?)`
   - `BackendErrorKind {NotConfigured, NotInstalled, NotLoggedIn, Unauthorized, RateLimited, ServerDown, Unsupported, Failed}`,
     `BackendException`, `BackendCapabilities(Images, TextFiles)`
   - `IChatBackend { Id; DisplayName; Capabilities; IAsyncEnumerable<string> StreamAsync(conversation, ct) }`
2. `AttachmentFactory.FromBytes(name, mime?, bytes, limits)`: classify image/text by extension or mime; text must be UTF-8;
   limits are 10 files, 20 MB per image, 200 KB per text file; reject unknown types with a reason.
   `AttachmentTray`: Add / Remove / TakeAll / limits.
3. `Net/SseReader.ReadDataAsync(stream, ct)`: data lines, multi-line data, `:` comments, final event without a trailing blank line.
4. `Backends/GeminiBackend`:
   - injected `HttpClient` and `Func<string?> apiKey`; a missing key gives NotConfigured with no HTTP call
   - static `BuildRequestJson` / `ParseChunkText` for tests; text attachments are inlined as fenced blocks
   - error mapping: 400 "API key not valid"/401/403 → Unauthorized, 404 → Unsupported, 429 → RateLimited,
     5xx or `HttpRequestException` → ServerDown
   - tested with a fake `HttpMessageHandler`
5. `Chat/HistoryStore`: JSONL per conversation under `history/`, attachment metadata only (no bytes),
   retention pruning, can be disabled.
6. `Chat/ChatController`:
   - events: `UserMessageAdded`, `AssistantStarted`, `AssistantDelta`, `AssistantCompleted`, `AssistantCancelled`,
     `AssistantFailed(kind, msg)`, `ConversationReset`
   - a second send while busy is ignored; `CanAccept(attachments, out reason)` checks backend capabilities
   - on failure, remove the dangling user message from the context and keep it for `RetryAsync()`
   - `Cancel()` keeps partial text; `NewChat()`
7. Settings (no schema bump; missing properties take defaults):
   - `ChatSettings { DefaultBackend="gemini", Backends=[{id:gemini,type:Gemini,name:Gemini,model:gemini-3.8-flash}],
     MaxHeight=560, SaveHistory=true, HistoryRetentionDays=30, MaxImagePixels=2048, SystemPrompt? }`
   - `BackendProfile { Id, Type(Gemini|OpenAiCompatible|ClaudeCode|Antigravity), Name, Model?, Endpoint?, CliPath?, ExtraArgs? }`
   - normalize: empty list → defaults; unknown default → first; clamps
   - `BackendFactory.Create(profile, http, ISecretStore, systemPrompt)`; non-Gemini types return null until Plan 3;
     `GEMINI_API_KEY` env var as fallback
8. `Theming/ThemeTokens`: built-in Dark/Light, `ToCss()` produces `:root{--hl-*}`. Sanitize values (reject `;{}<>`).
9. `PopupGeometry.GrowUp(current, contentPx, minPx, maxPx, workArea)` keeps the bottom edge, clamped to the work area.
   `ImageMath.FitWithin(w, h, maxPx)` never upscales.

**Web (`src/Hotline.App/Web/`, served via `SetVirtualHostNameToFolderMapping("hotline.app", …)`):**
- **Layout:** `index.html`, `chat.css` (only `--hl-*` variables, transparent background), `chat.js` (DOM), and `chat-core.js`
  (pure state reducer, tested with `node --test tests/web`).
- **Vendored libraries:** markdown-it 14.1.0 with `html:false`, highlight.js 11.11.1 (cdnjs).
- **Composer:** + menu (Attach files… / Capture window / Capture screen), attachment chips, auto-growing textarea
  (Enter sends, Shift+Enter newline), backend pill selector, send/stop button, "📞 Hotline" placeholder.
- **Keys and paste:** Esc hides; Ctrl+N starts a new chat; paste images and drag-drop files are read via FileReader and sent as base64.
- **Height:** a `ResizeObserver` posts the content height to the host.
- **Missing key:** on NotConfigured, show an inline "Paste Gemini API key" banner with a link to aistudio.google.com/apikey,
  then send `setSecret` and retry.
- **Bridge protocol:**
  - host→web: `user`, `assistantStart`, `delta`, `done`, `cancelled`, `error`, `reset`, `theme{css}`,
    `backends{items,selected}`, `attachmentAdded{id,name,kind,thumb}`, `attachmentRemoved`, `focus`
  - web→host: `ready`, `send{text}`, `retry`, `cancel`, `newChat`, `pickFiles`, `captureWindow`, `captureScreen`,
    `pasteImage`/`dropFile{name,mime,base64}`, `removeAttachment`, `selectBackend`, `setSecret{backendId,value}`,
    `height{value}`, `escape`

**App:**
- **WebView2 setup:**
  - `PopupWindow` hosts a `WebView2`
  - **user data folder must be under LocalState**: create via `CoreWebView2Environment.CreateWithOptionsAsync`, because
    the default next to the exe is read-only under WindowsApps
  - `DefaultBackgroundColor = Transparent` so acrylic shows through
- **Bridge and keys:** `Chat/ChatBridge` connects the web view, controller, tray and capture. `PasswordVaultSecretStore` uses resource "Hotline".
- **Modal dialogs:** `BeginModal`/`EndModal` suppresses hide-on-blur while the FileOpenPicker is open; the picker uses
  `InitializeWithWindow`.
- **Capture:** `Capture/ScreenCapture` hides the popup, waits ~200 ms, grabs the window or monitor rectangle with GDI
  (BitBlt from the screen DC with SRCCOPY|CAPTUREBLT and GetDIBits as BGRA, using DWMWA_EXTENDED_FRAME_BOUNDS for the
  window), encodes a PNG via `SoftwareBitmap` + `BitmapEncoder` (scaled to MaxImagePixels), then shows the popup again.
  Thumbnails are a small re-encode.
- **Image processing:** `Capture/ImageProcessor` decodes pasted or dropped images and re-encodes them as PNG, scaled to MaxImagePixels.
- **Growth:** the popup grows upward from the bar's placed bottom; `NewChat` shrinks back to the bar.
- **Tests:** smoke test adds a "chat view ready" log check. CI adds a `node --test tests/web` step.

**Review Focus candidates:**
- hide-on-blur during the file picker or capture
- WebView2 user data folder under WindowsApps
- huge pasted images
- a missing or invalid key
- Esc while streaming
- switching backend mid-conversation
- rapid key toggles during streaming
- mixed-DPI growth
