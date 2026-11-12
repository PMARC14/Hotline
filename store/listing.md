# Microsoft Store listing: Hotline AI

Copy each field into Partner Center › your app › the submission › **Store listings** › English. The other languages'
text is in `store/listings/<language>.md` (model-drafted; paste each into that language's listing, see step 4 below). Limits are Partner
Center's. Images: `scripts/make-store-art.sh` (poster, box art, logos) and `scripts/make-store-media.sh` (screenshots
and trailer, captured from the installed app); output in `artifacts/store/`. Per-language images:
`scripts/make-store-panels.sh` (captures each language's panels; turns on diagnostics.testLinks temporarily) and then
`scripts/make-store-localized.sh` (headless, no screen): `artifacts/store/<lang>/screenshot-1..4.png`, `poster-1440x2160.png` and
`boxart-2160x2160.png` for English and the 16 languages, with each language's captions and tagline. The logos have no text and
are shared. The panel in the pictures stays English.

The English **Short description** and **Description** are the owner's own text (the Partner Center version of 2026-11-08), with
the real 0.6 features from the earlier longer description added in the same plain voice (Codex, Continue in, shortcuts,
bar layout, Backup). `store/listings/<language>.md` carry translations of both.

---

## Submission guide: what to paste and change in Partner Center (version 0.6.0.0)

Do these in order. "Paste" means copy the text from the section of this file with the same name. Nothing here is submitted until
you press **Submit to the Store** at the end.

### 1. Start the update

1. Partner Center › Apps and games › **Hotline AI** › **Start update**.
2. **Pricing and availability**: no change (free).
3. **Properties**: check against *Properties (suggested)* below. Change the category, the system requirements or the Generative AI
   notes only if they differ.
4. **Age ratings**: no change unless the questionnaire asks something new.

### 2. Packages

1. Get the bundle: GitHub › Actions › **Store** workflow › the run for version 0.6.0.0 › artifact (or the copy in
   `Downloads\Hotline-store-0.6.0\`). It holds one `.msixbundle` for x64 and ARM64. See [RELEASING.md](../docs/RELEASING.md).
2. **Packages** page: remove the old package, drag in the new `.msixbundle`, wait for validation (a few minutes). The version must
   read 0.6.0.0 (Store versions end in `.0`).
3. **Notes for certification** (the free-text box on the submission options page): paste the Generative AI text from *Properties*
   below, plus: "Needs the user's own Claude/Codex/Gemini sign-in or API key to chat. To test without an account, add a connection of
   type 'Local endpoint' or use any OpenAI-compatible API key."

### 3. Store listing: English (United States)

| Field in Partner Center | What to do |
|---|---|
| Description | Paste **Description** below. It replaces the old text. |
| What's new in this version | Paste **What's new in this version** below. |
| Product features | Replace all 20 with **Product features** below. The old set is shorter: no Codex, Continue in, shortcuts, Backup or button rows. Paste one per line. |
| Screenshots (Desktop) | Keep the 4 and their captions (they match **Screenshot captions**). Optional: add a 5th of Settings > Window > Button rows, and one of Settings > Backup. |
| Trailer | No change. |
| Short description (under *Supplemental fields*) | Empty today. Paste **Short description** below (232 characters; the limit is 270). |
| Keywords | Replace your seven with the expanded set under **Search terms** (still 7 keywords, 21 words in all). |
| Copyright and trademark info | **Change it.** Paste **Copyright and trademark info** below. It now leads with Apache 2.0 and says the MIT license is only for the Microsoft Fluent Emoji artwork. |
| **Additional license terms** | **Change it.** It says "MIT License: …". Hotline is Apache 2.0. Paste **Additional license terms** below. The Fluent Emoji line in the copyright box stays (that artwork is MIT). |
| Developed by | No change (PMARC14). |
| Short title | Paste **Short title** below. |
| Voice title | Leave empty (Xbox only). |
| Store logos / images | No change. |

### 4. The other languages (one listing per package language)

The Store listing languages are the ones the package declares: English (United States) plus the 16 below, which come from the
`src/Hotline.App/Strings/<tag>/` folders ([TRANSLATING.md](../docs/TRANSLATING.md)). There is no separate "additional languages"
section any more (that was for region-specific listings, and it was removed): every language in the package list gets its own
listing, and Partner Center asks for all of them before you can submit. Open each language from the **Store listings** page and
paste, from `store/listings/<file>`, the **Short description**, **Description**, **What's new**, **Product features**,
**Screenshot captions** and **Search terms** (each file has them as sections in the same order). The shared fields (license,
copyright, developer, trailer, logos, short title) are entered once, not per language.

| Partner Center language | File |
|---|---|
| Chinese (Simplified) | `zh-Hans.md` |
| Chinese (Traditional) | `zh-Hant.md` |
| Dutch | `nl.md` |
| French | `fr.md` |
| German | `de.md` |
| Hindi | `hi.md` |
| Indonesian | `id.md` |
| Italian | `it.md` |
| Japanese | `ja.md` |
| Korean | `ko.md` |
| Polish | `pl.md` |
| Portuguese (Brazil) | `pt-BR.md` |
| Spanish | `es.md` |
| Turkish | `tr.md` |
| Ukrainian | `uk.md` |
| Vietnamese | `vi.md` |

Notes: a language left "Not started" blocks submission. To drop a language, remove it from the package (its `Strings/<tag>` folder and
`Languages.Supported`), not from the page. No regional variants are needed: the Store shows a user the listing of their language
whatever their region (after it is live, check the Spanish page from a Latin American setting). The translations are
model-drafted; a native speaker should skim them. Pictures per language (screenshots with translated captions and a translated
panel, poster and box art): `scripts/make-store-panels.sh` then `scripts/make-store-localized.sh`, output in
`artifacts/store/<lang>/`; upload them on that language's listing (the logos are shared).

### 5. Submit

1. Re-read the English page once (Description, What's new, 20 features, short description).
2. **Submit to the Store**. Certification usually takes a few days. The update goes live when it passes.
3. After it is live: open the Store page on this PC, check the version in Hotline > About reads 0.6.0.0, and check the Spanish
   page from another region.

---

## Short title

Hotline

## Product name

Hotline AI

## Short description (shown near the top; keep it under ~270 characters)

You're stuck with the Copilot key, so use it: a fast, native AI chat panel for the AI you already have (Claude, Codex, Gemini), your own API key or a local model. No account with us. Arrange the bar and take the chat to another app.

## Description (up to 10,000 characters)

You're stuck with the Copilot key, so you might as well use it. Hotline AI turns it into a fast, native AI chat panel
that talks to the AI you choose.

Press the key and a panel opens on the monitor you're working on, ready to type. Ask a question, get a streamed answer,
press the key again and it's gone. No browser tab, no account with us.

USE THE AI YOU ALREADY HAVE
• Claude Code: uses your installed Claude Code and its sign-in, so your Claude plan works with no API key.
• OpenAI Codex CLI: uses your installed Codex and its sign-in, so your ChatGPT plan works with no API key.
• Antigravity CLI (agy): Gemini with your Google sign-in.
• Your own API key: Anthropic, Google Gemini, OpenAI and other OpenAI-compatible services such as OpenRouter.
• Local models: any OpenAI-style server on your PC (for example llama.cpp or LM Studio), so nothing leaves your
  computer.
Pick the model, the provider and how hard it should think straight from the panel's bar.

BRING CONTEXT WITH YOU
• Text you had selected in another app is attached automatically when you press the key, and you can remove it before
  sending.
• Capture the window you were in, the whole screen or a region you drag out; paste or drop images and files.
• Models that can't see images get the text in your screenshot instead, read on your PC with Windows' own OCR.

MADE FOR QUICK JOBS
• Quick actions: type / for translate, summarize, fix or explain, or write your own as simple text files.
• Memory: "/remember I prefer metric units" and every chat knows it, whichever AI you use.
• Voice: hold the key and talk; let go to stop.
• Recent chats are one click away, in a searchable list (Ctrl+H).
• Click to Do and Windows actions: send text or an image to Hotline AI from other apps.
• Continue in: take the chat to a terminal, Open WebUI, the llama.cpp web UI, LM Studio or your own target when you
  need more room.
• Shortcuts for the bar's buttons while the panel has focus: new chat, recent chats, Settings, pin, captures and the
  pickers. Your own buttons can have one too. They need Ctrl, Alt or Win, so typing is never touched.
• A Windows notification tells you when an answer finishes while you're elsewhere.

NATIVE, LIGHT AND YOURS
• Built with WinUI 3: fast to open, follows your light or dark theme, no embedded browser. Native on x64 and ARM64.
• Rich answers: headings, lists, tables, code with a Copy button, math and simple drawings.
• Hold Alt to move or resize the panel; pin it, and choose whether the chat grows up or down.
• Everything is a plain file you can edit: settings, connections, prompts, the buttons in the bar.
• Arrange the bar your way: drag buttons between rows in Settings, add your own buttons with your own pictures, and
  keep less-used ones in a More menu.
• Settings > Backup moves your setup to another PC. API keys are never exported, and a file Hotline replaces is backed
  up first.
• Tools, carefully: API connections can use tools from your MCP servers; anything that changes something asks you
  first.
• The Copilot key can also act as a Right Ctrl key, if you'd rather have that.

PRIVATE BY DESIGN
Hotline AI has no server and collects nothing: no account, no analytics, no ads. Your messages go only to the AI
service you choose, under that service's terms. API keys stay in Windows Credential Locker; history, settings and
memory stay on your PC. Free and open source.

No Copilot key? Set any shortcut you like, for example Ctrl+Alt+H.

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

## Search terms (up to 7; 40 characters each, at most 21 words in all; 7 keywords of up to 3 words = 21 words. Adds Codex, assistant, LLM and shortcut to the earlier seven)

AI chat assistant, Copilot key shortcut, Claude Code integration, Codex Gemini CLI, Anthropic OpenAI API key, OpenRouter compatible, Local LLM llama.cpp

## Copyright and trademark info

© 2026 PMARC14. Hotline AI is licensed under the Apache License 2.0. Includes Microsoft Fluent Emoji artwork © Microsoft Corporation, used under the MIT License.

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
