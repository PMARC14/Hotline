# Continue in: chat apps and web pages — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "Continue this chat in ▸" offers user-chosen targets — web pages (llama.cpp web UI, ChatGPT, claude.ai), Open WebUI (a real copy through its API) and apps (LM Studio with the chat copied) — from a shared list, overridable per connection.

**Architecture:** Core owns the target model, list resolution, placeholder filling and the Open WebUI request (all unit-tested); the App's `ContinueIn` lists and runs targets (browser, clipboard, process start), and Settings edits the lists. Lists live in `settings.json` (`chat.continueIn`) and `connections\<id>.json` (`continueIn`); the Open WebUI key lives in Credential Locker.

**Tech Stack:** .NET 10, C#, WinUI 3, xUnit (`tests/Hotline.Core.Tests`), System.Text.Json, HttpClient.

**Spec:** `docs/superpowers/specs/2026-10-15-continue-in-targets-design.md`

## Global Constraints

- Branch `continue-in-targets`; commits authored `pmarc14 <16502495+PMARC14@users.noreply.github.com>`, no Claude trailer.
- Missing `chat.continueIn` = default shared list; missing connection `continueIn` = shared list; `[]` = none. No schema migration.
- Default shared list: the llama.cpp web UI at the first Local connection's origin, `{origin}/?q={chat}&model={model}`; empty when there is no Local connection.
- Placeholders exactly `{chat}`, `{last}`, `{file}`, `{model}`; URL-encoded in URLs, plain arguments in commands; no shell ever.
- Filled URLs must be `http` or `https`. Longer than 8 000 characters → `{chat}` becomes the last message and the whole chat is copied.
- `{chat}` text: `Here is a conversation from another app. Continue it: reply to the last message.\n\n` + `CliHandoff.Markdown(title, messages)`.
- Open WebUI key: Credential Locker key `continue:<target url>:apiKey`; sent only over https or loopback (the `ApiCommon` rule).
- Ready-made entries: llama.cpp web UI (`http://127.0.0.1:8080`), Open WebUI (`http://localhost:3000`), LM Studio (app, `%LOCALAPPDATA%\Programs\LM Studio\LM Studio.exe`, copyChat), ChatGPT (`https://chatgpt.com/?q={chat}`), Claude (`https://claude.ai/new?q={chat}`).
- Targets only when the panel has the whole transcript (`ChatController.HasWholeTranscript` and messages > 0), after the CLI entries.
- Every new interface text in all 17 `Strings*.resx` (the localization tests enforce it).

## Review Focus

- A Local connection whose endpoint has a path (`http://127.0.0.1:8080/v1`) — the default must use the origin only (`http://127.0.0.1:8080/?q=…`). Test in Task 1.
- Chat text with `&`, `#`, `%`, emoji, newlines — must survive URL encoding intact. Test in Task 2.
- A template that fills to `javascript:` / `file:` (e.g. `url = "{last}"` with a typed `file:///…`) — must be refused. Test in Task 2.
- A command path with spaces and quoted arguments (`"C:\Program Files\X\x.exe" --open "{file}"`) — split into exactly program + 2 args. Test in Task 2.
- Open WebUI on a non-loopback `http://` address with a key set — the key must not be sent. Test in Task 3.

---

### Task 1: Target model and list resolution

**Files:**
- Create: `src/Hotline.Core/Backends/ContinueTargets.cs`
- Modify: `src/Hotline.Core/Settings/HotlineSettings.cs` (`ChatSettings.ContinueIn`, `BackendProfile.ContinueIn`)
- Test: `tests/Hotline.Core.Tests/ContinueTargetsTests.cs`

**Interfaces:**
- Produces: `enum ContinueKind { Web, OpenWebUi, App }`; `sealed record ContinueTarget(ContinueKind Kind, string Name, string? Url = null, string? Command = null, string? Model = null, bool CopyChat = false)` (camelCase JSON via the existing settings serializer); `List<ContinueTarget>? ChatSettings.ContinueIn`, `List<ContinueTarget>? BackendProfile.ContinueIn`; `static IReadOnlyList<ContinueTarget> ContinueTargets.For(BackendProfile? profile, HotlineSettings settings)`; `static IReadOnlyList<ContinueTarget> ContinueTargets.Shared(HotlineSettings settings)`; `static IReadOnlyList<(string Key, ContinueTarget Target)> ContinueTargets.ReadyMade` (keys `llamacpp`, `openwebui`, `lmstudio`, `chatgpt`, `claude`).

- [ ] **Step 1: Write the failing tests** — `Shared_defaults_to_the_llama_cpp_web_ui_at_the_local_connection` (Local endpoint `http://127.0.0.1:8080/v1` → one Web target, Url `http://127.0.0.1:8080/?q={chat}&model={model}`); `No_local_connection_means_an_empty_default`; `A_set_shared_list_is_used_as_is`; `A_connection_list_replaces_the_shared_one`; `An_empty_connection_list_means_none`; `Lists_round_trip_through_settings_and_connection_files` (SettingsStore save/load, kinds serialize as `"web"`, `"openWebUi"`, `"app"`); `Ready_made_entries_match_the_spec` (the five URLs/commands from Global Constraints, LM Studio `CopyChat == true`).
- [ ] **Step 2: Run** `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj --filter ContinueTargetsTests` — Expected: FAIL (types not defined).
- [ ] **Step 3: Implement** the record, enum, the two settings properties (null by default, `JsonStringEnumConverter` camelCase like other enums) and `For`/`Shared`/`ReadyMade` in `ContinueTargets.cs`. Origin = `new Uri(endpoint).GetLeftPart(UriPartial.Authority)`.
- [ ] **Step 4: Run** the same command — Expected: PASS; then the full test project — Expected: all pass.
- [ ] **Step 5: Commit** `feat(core): Continue in targets — model, shared and per-connection lists, defaults`.

### Task 2: Filling targets (URLs, the long-URL fallback, commands)

**Files:**
- Modify: `src/Hotline.Core/Backends/ContinueTargets.cs`
- Test: `tests/Hotline.Core.Tests/ContinueTargetsTests.cs`

**Interfaces:**
- Consumes: `ContinueTarget` (Task 1), `CliHandoff.Markdown(string title, IReadOnlyList<ChatMessage>)`.
- Produces: `sealed record ContinueValues(string Chat, string Last, string? File, string Model)`; `static ContinueValues ContinueTargets.Values(string title, IReadOnlyList<ChatMessage> messages, string? file, string? model)`; `sealed record FilledUrl(Uri Uri, bool Shortened)`; `static FilledUrl? ContinueTargets.FillUrl(string template, ContinueValues values)` (null = not http/https or not a URL); `static (string Program, IReadOnlyList<string> Args) ContinueTargets.FillCommand(string command, ContinueValues values)`; `const int ContinueTargets.MaxUrlLength = 8000`.

- [ ] **Step 1: Write the failing tests** — `Chat_value_is_the_continue_prompt_and_the_transcript` (starts with the exact `{chat}` text from Global Constraints); `Values_are_url_encoded` (`a&b #c %d 😀\nline` round-trips through `Uri.UnescapeDataString`); `Only_http_and_https_are_opened` (`{last}` = `file:///c:/x`, `javascript:alert(1)` → null; `https://x/?q={chat}` → not null); `A_long_chat_falls_back_to_the_last_message` (5 000 user messages × 10 chars → `Shortened == true`, URL ≤ 8 000, contains the last message); `Commands_split_like_a_command_line` (`"C:\Program Files\X\x.exe" --open "{file}"` → program `C:\Program Files\X\x.exe`, args `["--open", "<file path>"]`); `Environment_variables_expand_in_the_command` (`%LOCALAPPDATA%\x.exe`); `Chat_text_stays_one_argument` (a `{chat}` with quotes and `&` is exactly one argument).
- [ ] **Step 2: Run** `--filter ContinueTargetsTests` — Expected: FAIL.
- [ ] **Step 3: Implement** `Values`, `FillUrl` (replace with `Uri.EscapeDataString`; if longer than `MaxUrlLength`, refill with `Chat = Last` and set `Shortened`; then `Uri.TryCreate` absolute and scheme http/https), `FillCommand` (expand `%VAR%` with `Environment.ExpandEnvironmentVariables` on the template before splitting; split on whitespace outside double quotes; fill placeholders per argument afterwards so chat text never splits).
- [ ] **Step 4: Run** — Expected: PASS; full project — all pass.
- [ ] **Step 5: Commit** `feat(core): fill Continue in targets — encoded URLs, http(s) only, long-chat fallback, commands without a shell`.

### Task 3: Open WebUI chat

**Files:**
- Create: `src/Hotline.Core/Backends/OpenWebUiChat.cs`
- Test: `tests/Hotline.Core.Tests/OpenWebUiChatTests.cs`

**Interfaces:**
- Consumes: `ChatMessage`, the https-or-loopback rule (as in `ApiCommon`).
- Produces: `static JsonObject OpenWebUiChat.Body(string title, string? model, IReadOnlyList<ChatMessage> messages, Func<Guid> newId)`; `static async Task<Uri> OpenWebUiChat.CreateAsync(HttpClient http, Uri baseUrl, string apiKey, JsonObject body, CancellationToken ct)` returning `{base}/c/{id}`; `sealed class OpenWebUiException(OpenWebUiError error, string message) : Exception` with `enum OpenWebUiError { KeyRefused, Unreachable, InsecureKey, BadReply }`.

- [ ] **Step 1: Write the failing tests** — `Body_chains_every_message_in_order` (3 messages → `chat.messages` has 3, each `parentId` = previous id, `childrenIds` = next, `history.currentId` = last id, roles `user`/`assistant`, `models` = `[model]`, attachments appended as `[attached: name]`); `Creates_the_chat_and_returns_its_page` (fake handler: asserts `POST {base}/api/v1/chats/new`, `Authorization: Bearer k`; replies `{"id":"abc"}` → `http://localhost:3000/c/abc`); `A_refused_key_says_so` (401 and 403 → `KeyRefused`); `Not_running_says_so` (`HttpRequestException` → `Unreachable`); `The_key_never_goes_over_plain_http_to_another_pc` (`http://192.168.1.5:3000` → `InsecureKey`, handler never called); `A_reply_without_an_id_is_an_error` (→ `BadReply`).
- [ ] **Step 2: Run** `--filter OpenWebUiChatTests` — Expected: FAIL.
- [ ] **Step 3: Implement** both methods (`System.Text.Json.Nodes`; timestamps as Unix seconds; title from `HistoryStore.TitleOf(first user text)` is the caller's job).
- [ ] **Step 4: Run** — Expected: PASS; full project — all pass.
- [ ] **Step 5: Commit** `feat(core): Open WebUI — create a real chat with the whole conversation`.

### Task 4: Running targets from "Continue this chat in ▸"

**Files:**
- Modify: `src/Hotline.App/Chat/ContinueIn.cs`, `src/Hotline.App/Chat/MarkdownRenderer.cs` (make `SetClipboardText` `internal static` for reuse), `src/Hotline.App/App.xaml.cs` (pass the secret store and an `HttpClient` to `ContinueIn`)
- Modify: `src/Hotline.Core/Text/Localization/Strings*.resx` (17 files): `Continue_Shortened` ("The chat was too long for a link: only your last message was sent, and the whole chat is on the clipboard."), `Continue_NoKey` ("Add the Open WebUI API key in Settings › Chat › Continue in."), `Continue_KeyRefused` ("Open WebUI refused the API key."), `Continue_Unreachable` ("Open WebUI isn't running at {0}."), `Continue_InsecureKey` ("The API key is only sent over https or to this PC.")

**Interfaces:**
- Consumes: `ContinueTargets.For/Values/FillUrl/FillCommand` (Tasks 1–2), `OpenWebUiChat.Body/CreateAsync` (Task 3), `ISecretStore`, the existing `Target(string Label, Func<Task> Run)` and `Launch(CliCommand)`.
- Produces: `ContinueIn.Targets()` returns the CLI entries followed by one `Target` per resolved `ContinueTarget` (Label = `Name`), only when `chat.Messages.Count > 0 && chat.HasWholeTranscript`.

- [ ] **Step 1: Implement** in `ContinueIn`: resolve with `ContinueTargets.For(profile of chat.BackendId, settings)`; Web → `FillUrl` (null → throw `InvalidOperationException` so the existing failure notice shows), `Shortened` → copy the chat and show `Continue_Shortened`, `Launcher.LaunchUriAsync`; OpenWebUi → key from `ISecretStore.Get($"continue:{url}:apiKey")` (none → `Continue_NoKey`), `CreateAsync`, map `OpenWebUiError` to the four texts, open the page; App → write the handoff file when the command has `{file}`, copy the chat when `CopyChat`, start with `Launch(new CliCommand(program, args, userFolder))`.
- [ ] **Step 2: Add** the five texts to all 17 resx files.
- [ ] **Step 3: Run** the Core tests — Expected: all pass (localization tests included). Build `src/Hotline.App` — Expected: Build succeeded.
- [ ] **Step 4: Verify** with `scripts/install.sh` then `hotline://selftest`: the log has `selftest step: completed` and no new `ERROR`.
- [ ] **Step 5: Commit** `feat: Continue in runs web pages, Open WebUI and apps`.

### Task 5: Settings — the shared list and the per-connection choice

**Files:**
- Create: `src/Hotline.App/Settings/SettingsWindow.ContinueIn.cs`
- Modify: `src/Hotline.App/Settings/SettingsWindow.xaml.cs` (Chat page calls `BuildContinueInSection()`), `src/Hotline.App/Settings/SettingsWindow.Connections.cs` (connection editor option)
- Modify: `src/Hotline.Core/Text/Localization/Strings*.resx` (17 files): section title/intro, Add, Edit, Remove, kinds (Web page / Open WebUI / App), field labels (Name, Address, Command, Model, Copy the chat, API key), Test and its result, connection option (Use the shared list / Own list / None)

**Interfaces:**
- Consumes: `ContinueTarget`, `ContinueTargets.Shared/ReadyMade` (Task 1), `ISecretStore`, `ModelCatalog` model listing for Open WebUI's Test (an OpenAI-style `GET {url}/api/models` with the key), the existing `Card`, `ShowDialogAsync`, `_settings.Update`.
- Produces: `chat.continueIn` and connection `continueIn` written through `SettingsService`/the connection editor; keys written to `ISecretStore` only.

- [ ] **Step 1: Implement** the Chat-page section: one card per shared target (name; kind; address or command) with Edit and Remove; Add = MenuFlyout of the ready-made entries plus the three kinds; Edit = `ContentDialog` with the kind's fields (Open WebUI: API key `PasswordBox` + Test). The first edit of a default list writes the full list to `chat.continueIn`.
- [ ] **Step 2: Implement** the connection editor choice (ComboBox: shared → `ContinueIn = null`; none → `[]`; own → a copy of the shared list, edited with the same section bound to the connection).
- [ ] **Step 3: Add** the texts to all 17 resx files.
- [ ] **Step 4: Run** the Core tests — all pass; build the App; `scripts/install.sh` and `hotline://selftest` — `selftest settings ok`, no new `ERROR`.
- [ ] **Step 5: Commit** `feat(settings): Continue in — shared list, ready-made entries, per-connection choice`.

### Task 6: Docs, review, PR

**Files:**
- Modify: `docs/configuration.md` (the `continueIn` keys and placeholders), `docs/HANDOFF.md` (state, next work), `README.md` (one line under features)

- [ ] **Step 1: Write** the docs changes.
- [ ] **Step 2: Review**: agy (`gemini-3.8-flash-high`, code pasted, no tools, parts under ~30k characters); verify each finding against the code; fix the real ones with a test where Core is involved.
- [ ] **Step 3: Run** the Core tests and the self-test once more — Expected: all pass, no new `ERROR`.
- [ ] **Step 4: Commit and open the PR** (base `history-actions`), listing what to test by hand: llama.cpp `?q=`, Open WebUI real chat, LM Studio with the chat copied, ChatGPT and Claude links, a per-connection list.
