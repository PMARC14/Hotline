# Copilot key spike — results (2026-09-30)

- Self-signed package listed in Settings picker: **YES**. Selected as the Copilot key app by the user.
- Tap while running: source = **FastPath only** (no protocol duplicates observed in the log); latency feel = instant.
- Hold while running: Down and Up both arrive via **FastPath**. Up arrives only ~10–45 ms after Down in the log,
  so the shell may emit both at release. **Re-check before building push-to-talk (v2).**
- Win+C: not tested (laptop Copilot key used).
- Cold start via key: works, quick. Arrives as `ExtendedActivationKind.ProtocolForResults` (not `Protocol`); the router
  now parses both.
- Startup task launches to tray: **YES**.
- Crash found: intermittent `NullReferenceException` in `PlaceOnActiveMonitor` (`DisplayArea.GetFromWindowId` returned
  null for the previous foreground window) on fast-path taps. Fixed with a monitor fallback and logging of the window class;
  any handler exception is now contained and logged instead of killing the process.

## Decisions for Plan 2
- Keep the fast path as the primary trigger; protocol is only the cold-start path. The cross-source dedupe stays as a safety net.
- Hold is reserved for "open with capture" (v1) and push-to-talk (v2), pending the Down/Up timing check above.
- Add verbose/debug logging (requested after the crash) so field issues can be diagnosed.
