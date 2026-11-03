# Remaining PLAUSIBLE review findings

1. **APP-ONLY credential cleanup; retention documented.** Both resets remove shared Continue in targets. Reset everything
   also removes connection targets. `SettingsReset` returns connection IDs, and the App deletes only
   `SecretKeys.ApiKey(id)`. Open WebUI keys use `ContinueIn.ApiKeyName(url)` in the App and stay in Credential Locker.
   `docs/configuration.md` now states this exception. Core coverage:
   `Reset_removes_shared_OpenWebUi_targets_and_reports_only_removed_connections` (both reset modes).
   Credential Locker deletion cannot be exercised through the Core reset API.
2. **REJECTED:** `ContinueTargets.Shared` already skips Local connections without absolute endpoints.
   `A_local_connection_without_an_endpoint_has_no_default_target` passes for null, empty and whitespace endpoints.
3. **REPRODUCED+FIXED:** Gemini ran function calls despite MAX_TOKENS / MALFORMED_FUNCTION_CALL.
   `Gemini_does_not_run_function_calls_from_a_truncated_candidate` failed with a real tool invocation in all four cases
   (finish reason in the function-call chunk or a later chunk). Both reasons now stop the answer before executing tools.
4. **REJECTED:** `Resume` clears the old controller session, and sending assigns the CLI's Resume property to null.
   `Reopening_an_api_chat_in_a_cli_replays_once_without_the_previous_session` passes on existing code: the API
   transcript appears once, the unrelated session is discarded, and the next turn resumes the new session.
5. **REPRODUCED+FIXED:** simultaneous `CopyStamped` calls could choose the same name and throw, causing a SettingsStore
   save to skip its replacement. Existing backups were protected by overwrite=false. The copy now retries a claimed
   filename; other copy failures still propagate so SettingsStore leaves the original alone.
   `Concurrent_copies_at_the_same_timestamp_keep_every_backup` failed on existing code with filename collisions.
6. **APP-ONLY; no App changes.** Core portions that can be tested independently:
   - Settings reload vs unsaved edits: `SettingsService.Reload` and failed-save handling in `Update` are Core-testable;
     open controls, pending edit buffers and file-watcher dispatch belong to the App.
   - Horizontal Alt-resize: `PanelPlacement.Resize` geometry is Core-testable; grips, height application and drag state
     are App behavior (existing `Resize_moves_only_the_dragged_edges_within_limits` covers horizontal geometry).
   - Filled-dropdown brushes: WinUI resource lookup and templates are entirely App-only.
   - Move what doesn't fit stale width: `ToolbarEditor.FillMenuFromOverflow` is Core-testable with explicit widths;
     obtaining and refreshing the actual available width belongs to the App.
   - Hold leak: `PopupWindow.HoldResize`, menu event subscriptions and disposal are entirely App-only.
