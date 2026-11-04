# Microsoft Store listing: Hotline AI

Copy each field into Partner Center › your app › the submission › **Store listings** › English. The other languages'
text is in `store/listings/<language>.md` (model-drafted; add those listings in Partner Center with *Add languages*). Limits are Partner
Center's. Images: `scripts/make-store-art.sh` (poster, box art, logos) and `scripts/make-store-media.sh` (screenshots
and trailer, captured from the installed app); output in `artifacts/store/`.

---

## Product name

Hotline AI

## Short description (shown near the top; keep it under ~270 characters)

Use the Copilot key for a native AI chat panel. Bring your Claude, Codex or Gemini sign-in, your own API key, or a local model. Arrange the bar, add your own buttons, and take the chat to another app when you need more room.

## Description (up to 10,000 characters)

I made this because the Copilot key does nothing useful for me. Hotline AI turns it into a native AI chat panel
that talks to the AI you choose.

Press the key and a panel opens on the monitor you're working on, ready to type. Ask a question, get a streamed answer,
press the key again and it's gone. No browser tab, no account with us.

USE THE AI YOU ALREADY HAVE
• Claude Code: uses your installed Claude Code and its sign-in, so your Claude plan works with no API key.
• OpenAI Codex CLI: uses your installed Codex and its sign-in, so your ChatGPT plan works with no API key.
• Antigravity CLI (agy): Gemini with your Google sign-in.
• Your own API key: Anthropic, Google Gemini, OpenAI and other OpenAI-compatible services such as OpenRouter.
• Local models: any OpenAI-style server on your PC (for example llama.cpp or LM Studio), so the chat stays on your
  computer.
Pick the model, the provider and how hard it should think straight from the panel's bar.
You bring the service or run the model yourself. Hotline doesn't include an AI subscription.

BRING CONTEXT WITH YOU
• Text you had selected in another app is attached automatically when you press the key, and you can remove it before
  sending.
• Capture the window you were in, the whole screen or a region you drag out; paste or drop images and files.
• Models that can't see images get the text in your screenshot instead, read on your PC with Windows' own OCR.

MADE FOR QUICK JOBS
• Quick actions: type / for translate, summarize, fix, explain, reply, shorter, formal or bullets, or write your own
  as simple text files.
• Memory: "/remember I prefer metric units" and every chat knows it, whichever AI you use.
• Voice: set the long press to Voice input, hold the key and talk; let go to stop. This uses Windows' online speech
  recognition.
• Recent chats are one click away, in a searchable list. Ctrl+H or /resume opens it too. CLI chats stay in the CLI's
  own history and can be resumed after a restart.
• Click to Do and Windows actions: send text or an image to Hotline AI from other apps.
• Continue in: take the chat to a terminal, Open WebUI, the llama.cpp web UI, LM Studio or your own web or app target.
  Open WebUI gets a real chat. LM Studio opens with the chat copied for you to paste; other targets vary.
• While the panel has focus, use shortcuts for new chat, recent chats, Settings, pin, copy last answer, captures,
  prompts and the model, provider and effort pickers. Your own buttons can have one too. They need Ctrl, Alt or Win, so
  typing is never touched. Change them in Settings or in toolbar.json.
• Get a Windows notification when an answer finishes while you're elsewhere.

NATIVE, LIGHT AND YOURS
• Built with WinUI 3: fast to open, follows your light or dark theme, no embedded browser. Native on x64 and ARM64.
• Pick an accent colour or use Windows' own. There is a High contrast theme, and Hotline follows Windows' contrast
  themes and transparency setting.
• Rich answers: headings, lists, tables, code with a Copy button and sideways scrolling, math and simple drawings.
  The panel follows answers as they stream in.
• Hold Alt to move or resize the panel. Pin it, choose whether the chat grows up or down, and set open/close sounds.
  Window widths use real pixels. You can have a narrow opening panel that grows wider when a chat starts.
• Everything is a plain file you can edit: settings, connections, prompts, the buttons in the bar.
• Button rows v2: drag buttons between rows in Settings > Window > Button rows, or edit toolbar.json. Add buttons for
  quick actions or preset text, with your own PNG picture. Choose widths in cells, size each picker separately, and
  put less-used buttons in a More menu. A narrow bar moves buttons that don't fit into a menu.
• A bad settings value falls back with a warning; the other settings still apply. Before replacing a hand-edited
  settings, connection or toolbar file, Hotline keeps a backup. If that backup fails, it leaves the file alone.
  It doesn't make another backup when replacing its own last save.
• Settings > Backup exports and imports settings, button rows and pictures, MCP tools, sounds, connections, quick
  actions and prompts. Import backs up every file it replaces first; no backup means no write. API keys are never
  exported. Enter them again on the other PC. History and memory aren't in the package; MCP environment values are,
  so keep the package private if you've put secrets there.
• Tools, carefully: API connections can use tools from your MCP servers; anything that changes something asks you
  first unless you've allowed it. Read-only tools run without asking, based on what the server declares.

PRIVATE BY DESIGN
Hotline AI has no server and collects nothing: no account, no analytics, no ads. Your messages go only to the AI
service you choose, under that service's terms. API keys stay in Windows Credential Locker; history, settings and
memory stay on your PC. Free and open source.

No Copilot key? Set any shortcut you like, for example Ctrl+Alt+H, by pressing it in Settings.

Hotline AI is an independent app and isn't affiliated with or endorsed by Microsoft, Anthropic, Google or OpenAI. Using
Claude Code, Codex, the Antigravity CLI or an AI API requires your own account with that service.

## What's new in this version

Arrange button rows in Settings, add buttons with your own pictures, and use a More menu. Set button widths in cells and size each picker separately. The bar's buttons, including your own, can have keyboard shortcuts while the panel has focus. Settings > Backup moves your setup without API keys; bad settings values warn, and your edits get backed up before replacement. Continue in opens the chat in Open WebUI, llama.cpp, LM Studio or your own targets, sometimes with a paste needed. Window widths use real pixels, and the chat can grow wider. I also fixed Copilot key focus handling on Windows 11 26H2, MCP tool checks, API response limits and backup failures.

## Product features (up to 20; 200 characters each)

1. Opens from the Copilot key (or any shortcut) on the monitor you're working on, ready to type
2. Works with your Claude Code, ChatGPT (Codex CLI) or Gemini (Antigravity CLI) sign-in: no API key needed
3. Bring your own key: Anthropic, Google Gemini, OpenAI and OpenAI-compatible APIs
4. Local models: any OpenAI-style server on your PC, so the chat stays on your computer
5. Pick the model, provider and thinking effort from the panel
6. Selected text from the app you came from is attached automatically
7. Capture a window, the screen or a region; paste or drop images and files
8. Text in screenshots is read on your PC (OCR) for models that can't see images
9. Quick actions: /translate, /summarize, /fix, /explain and more, or your own text files
10. Memory that every chat sees, whichever AI you use
11. Push-to-talk voice input using Windows' online speech recognition
12. Send text or images from Click to Do and other apps; continue chats in a terminal, Open WebUI, llama.cpp or LM Studio
13. Rich answers: tables, code with Copy, math and drawings
14. Native WinUI 3 on x64 and ARM64; light, dark and contrast themes, your accent colour, no embedded browser
15. Drag and drop button rows in Settings; custom buttons with your own pictures, a More menu and separate picker widths
16. Optional MCP tools for API connections; changes ask first unless you've allowed them
17. No Hotline account, analytics or ads; API keys stay in Windows Credential Locker; free and open source
18. Editable settings, connections, prompts and toolbar.json; bad values warn and fall back, backups protect your edits
19. Settings > Backup exports and imports settings, bar, MCP, sounds, connections, quick actions and prompts; never API keys
20. In-panel shortcuts for the bar's buttons, your own included; move, pin and resize the panel, with pixel widths and a wider chat option

## Screenshot captions (200 characters each; same text as on the images)

1. Press the Copilot key: ask anything, answers stream in.
2. Selected text comes along with your question.
3. Quick actions with /: translate, summarize, fix, explain.
4. Use the AI you already have, and set everything your way.

## Trailer

`trailer.mp4` (1920x1080, 38 s, no audio) with `trailer-thumbnail.png`. Title: "Hotline AI in 30 seconds".

## Search terms (up to 7)

AI chat, Copilot key, Claude, Gemini, AI assistant, LLM, local AI

## Copyright and trademark info

© 2026 PMARC14. Microsoft Fluent Emoji artwork © Microsoft Corporation (MIT License).

## Additional license terms

Apache License 2.0: https://github.com/PMARC14/Hotline/blob/main/LICENSE

## Developed by

PMARC14

## Links (Properties / Store listing)

- Privacy policy: https://github.com/PMARC14/Hotline/blob/main/PRIVACY.md
- Website: https://github.com/PMARC14/Hotline
- Support: https://github.com/PMARC14/Hotline/issues

## Properties (suggested)

- Category: Productivity (subcategory: none, or "Personal assistant" if offered)
- Personal information: Yes (sends what you type, attach or say to the AI service you choose; see the privacy
  policy). Payments/purchases: No in-app purchases; optional donation links in Settings > About.
- Generative AI: Yes. Notes for certification: "Hotline AI is a client for AI services the user configures with their
  own account or key; it hosts no model. Each answer has a ⋯ menu with 'Report this answer', which opens the
  provider's report page (Anthropic or Google). Content filtering is done by the chosen provider; local models run on
  the user's PC. Problems with Hotline itself: github.com/PMARC14/Hotline/issues. To test without an account, add a
  connection of type 'Local endpoint' or use any OpenAI-compatible API key."
- System requirements: Windows 11 22H2 or later; x64 or ARM64. A Copilot key is optional (any shortcut works).
- Age rating: answer the questionnaire honestly; it has unrestricted AI-generated text through third-party services.

## Notes for the Store submission

Version: 0.6.0.0

Ratings / notes for reviewers: "Needs the user's own Claude/Codex/Gemini sign-in or API key to chat"

Change in Partner Center for this update:

- Short description and Description: replace with the fields above.
- What's new: replace with the update text above.
- Features: replace with the 20 features above.
- Screenshots: recapture the new button rows editor, the bar with custom buttons, and the Backup page.
