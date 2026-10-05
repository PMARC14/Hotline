# Hotline Plan 7 — one job per class, App Actions, privacy

Branch `plan6-public` (continues PR #2). Requested by the user on 2026-10-05: "break up the panel controller … or any
class or file that does too much. Keep a philosophy of do one thing well", "build out registry for app actions",
"address 4" (privacy messaging), clean up build artifacts, and explain installing a loose .msix.

## Ground rules

Same as Plan 6: TDD in Core, `smoke.ps1 -Install` for the app, panel closed afterwards, Gemini 3.8 Flash (not Opus)
for agy reviews, commit per task.

## Tasks

1. **Artifacts.** Delete old `artifacts\Hotline.App_*` builds (8.4 GB); `build-msix.ps1` keeps the newest three.
2. **One class per file in Interop.** Split `SelectionReader.cs` (UIA reader, Ctrl+C copier, clipboard owner window,
   Win32 declarations) into one file each.
3. **Send decisions in Core.** `SendRouter` (pure): a typed message becomes Remember(fact) / NeedsText(action) /
   Message(text). Removes the parsing from the presenter.
4. **Voice state machine in Core.** `VoiceSession` owns idle → starting → listening → stopping, the draft and the
   dictated text, behind an `ISpeechSession` interface; unit-tested for the races the reviews found. The app keeps a
   thin Windows speech adapter and the listening bar.
5. **Split ChatPresenter** into single-purpose pieces: `NoticeArea` (info bars + tool approvals), `Composer` (message
   box keys, `/` suggestions), `AttachmentPanel` (chips, paste/drop/pick/capture), `TranscriptView` (rendering),
   `VoiceInput`, `OcrReader`. `ChatPresenter` only coordinates chat events and the send flow.
6. **App composition root.** Move self-test link handling out of `App.xaml.cs` into `SelfTests`.
7. **App Actions on Windows.** `Assets\actions.json` (URI-launched, protocol `hotline-action`): Ask Hotline about
   text / an image; Summarize / Translate / Fix / Explain text. Core `AppActionRequest` parses the launch; the app
   reports completion, opens the panel with the content attached and runs the quick action. Only auto-sends when
   Windows is the caller (any app can launch a URI).
8. **Privacy (complaint 4).** README "What leaves your PC" and a one-time first-run note in the panel.
9. **Docs + review.** configuration.md, HANDOFF, Gemini Flash review, fix findings.
