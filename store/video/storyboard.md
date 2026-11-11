# Hotline AI: in-depth video storyboard

A video that shows everything, about 3 to 4 minutes, 1920x1080, no audio, English captions burned in. Not part of the 0.6
Store submission (that uses the 38 s trailer); this is ready for when you want it, for example for 1.0.

How it is made:

1. `scripts/make-store-tour.sh` records the automatic part (shots 1 to 14, about 1:40) from the installed app: demo
   conversations and Settings page by page, with the captions below. It calls no AI and sends no keystrokes. The screen is in use
   for about two minutes. `scripts/make-store-tour.sh --dry-run` checks the caption and card pipeline without touching the screen
   (done: 80 s output, captions render).
2. The manual shots (M1 to M12) show what links cannot: they need clicks, keys or a real AI answer. Record each with Xbox Game Bar
   (Win+Alt+R) or OBS at 1920x1080, 30 fps, 5 to 15 seconds each.
3. Join them in the order below (ffmpeg concat, or any editor), keeping the title card (3 s) first and the end card last. Burn in
   the manual shots' captions with the same font (Segoe UI, 46 px, white on 60% black, lower centre).

## Before recording

- Install the release build (`Hotline.appinstaller` from the GitHub release), not the dev build, so About shows the real
  version. Close other apps; turn on Focus assist so no toast appears; plug in the power.
- A clean data folder looks better and shows no personal names: Settings > Connections lists your real connections and
  anything typed into a field is visible. Use a spare Windows user, or review the picture of each page first. Never show an API
  key field with a value, `mcp.json` environment values, or the history list with real chats.
- For the manual shots with a real answer, use a short question and a quick model (Gemini 3.8 Flash) so nothing waits.

## Shot list

| # | Shot | Seconds | Caption | How |
|---|---|---|---|---|
| T | Title card | 3 | (none) | tour script |
| 1 | Math demo: question, streamed answer with equation, code and table | 9 | Press the Copilot key: ask anything, answers stream in. | tour script (`hotline://demo?scene=math`) |
| 2 | Selected text chip and the summary question | 8 | Text you selected in another app comes along with your question. | tour script (`scene=summary`) |
| 3 | The `/` quick-action list | 4 | Type / for quick actions. Write your own as simple text files. | tour script (`scene=slash`) |
| 4 | Settings > Connections | 7 | Use Claude Code, Codex or Gemini sign-ins, your own API key, or a local model. | tour script |
| 5 | Settings > Window: the button rows | 8 | Arrange the bar: drag buttons between rows and add your own. | tour script |
| 6 | Settings > Appearance | 5 | Light, dark or high contrast, with your accent colour. | tour script |
| 7 | Settings > Chat and history | 5 | Recent chats and memory stay on your PC. | tour script |
| 8 | Settings > Quick actions | 5 | Quick actions are plain text files. | tour script |
| 9 | Settings > Tools | 6 | Tools from your MCP servers; anything that changes something asks first. | tour script |
| 10 | Settings > Backup | 6 | Move your setup to another PC. API keys are never exported. | tour script |
| 11 | Settings > General | 5 | Use the Copilot key, or set any shortcut you like. | tour script |
| E | End card | 3 | Free and open source, Microsoft Store | tour script |
| M1 | A real answer from Claude Code or Gemini (one short question) | 12 | Your own Claude Code or Gemini sign-in: no API key needed. | by hand |
| M2 | Region capture: drag a rectangle, the chip appears, send | 10 | Capture a region, a window or the whole screen. Paste or drop files. | by hand (bar button or Ctrl+Shift+R) |
| M3 | A model that cannot see images gets the screenshot's text | 8 | No vision? Windows' own OCR reads the picture on your PC. | by hand (an image attached to a text-only model) |
| M4 | Voice: hold the key, speak, release | 10 | Hold the key and talk. | by hand (needs Windows online speech recognition on) |
| M5 | `/remember` then a new chat that knows it | 10 | Memory every chat knows, whichever AI you use. | by hand |
| M6 | Recent chats list, search, Ctrl+H | 6 | Recent chats are one click away. | by hand |
| M7 | Continue in: terminal and Open WebUI | 10 | Take the chat to a terminal or Open WebUI when you need more room. | by hand (bar button) |
| M8 | Shortcuts: Ctrl+N, Ctrl+H, Ctrl+Shift+C, tooltip with the key | 8 | Shortcuts for the bar's buttons while the panel has focus. | by hand |
| M9 | Hold Alt: move and resize, pin; grows up or down | 10 | Move it, resize it, pin it. | by hand |
| M10 | A tool approval bar (MCP) | 8 | Anything that changes something asks you first. | by hand (a real MCP server) |
| M11 | Two monitors: the panel opens on the one you are working on | 8 | It opens on the monitor you're working on. | by hand (needs the window-capture fix in HANDOFF first) |
| M12 | The Copilot key as a Right Ctrl key, if you prefer | 5 | The Copilot key can also act as a Right Ctrl key. | by hand |

Suggested order: T, 1, M1, 2, M2, M3, 3, M5, M4, M6, M7, M8, M9, M10, M11, 4, 5, 6, 7, 8, 9, 10, 11, M12, E.

## Other languages

The tour and these captions are English. A language version needs the captions translated (the strings are in the `steps` list of
`scripts/make-store-tour.sh`) and the interface switched with `scripts/make-store-panels.sh`'s method (set `general.language`,
restart, record). Not planned for 0.6: the Store accepts one trailer for all languages.

## Notes

- The Settings window is moved to the same place each time (`place_settings`), because Windows opens it partly below the screen.
- The scenes use the real panel code, so the video shows the real app; only the conversations are scripted
  (`Strings.Demo_*`, which are translated, so the same demo works in every language).
