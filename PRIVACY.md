# Privacy policy

*Last updated: 8 October 2026*

Hotline is a free, open-source app for Windows that opens an AI chat panel from the Copilot key. This policy explains
what Hotline does with your information. The source code is public, so you can check every statement here.

## The short version

- **Hotline collects nothing.** It has no server, no account, no analytics, no telemetry and no ads. The developer
  never receives your messages, files or any other data.
- **What you send goes only to the AI service you choose**, under that service's own privacy terms.
- **Everything else stays on your PC.**

## What Hotline sends, and where

When you send a message, Hotline sends it to the **AI connection you picked**, along with:

- **attachments** you added: files, screenshots or screen regions you captured, and text you had selected in another
  app (shown as a chip you can remove before sending; it can also be turned off);
- the **earlier messages** in that conversation, for context;
- your **system prompt** and **memory notes** (`memory.md`), if you use them;
- **results from tools** you allowed, when a connection uses tools.

Nothing is sent until you send it. The exception is when you start an action that sends straight away, such as
"Summarize with Hotline" from Click to Do, because that is what you asked for.

The AI connection is something you set up. It can be:

| Connection | Who receives your data | Whose terms apply |
|---|---|---|
| Claude Code (your Claude sign-in) | Anthropic | Anthropic's |
| Antigravity CLI / agy (your Google sign-in) | Google | Google's |
| Anthropic API, Gemini API, OpenAI-compatible APIs (your key) | That provider (Anthropic, Google, OpenAI, OpenRouter, …) | That provider's |
| A local model server | Only a program running on your own PC | — |

Hotline also asks your chosen provider for its list of models, so it can show them in the model picker. That request
contains no messages.

**Voice input** uses Windows' own speech recognition, which is Microsoft's online service. Microsoft's privacy terms
apply, and it only works when you turn on Online speech recognition in Windows Settings. Voice input is off unless
you turn it on.

**Reading text in images (OCR)** runs entirely on your PC, using Windows' built-in text recognition.

## What stays on your PC

Everything is kept in `%USERPROFILE%\.hotline` in your user profile. Hotline uploads none of it:

- **Settings**, as plain files you can read and edit.
- **Chat history**: deleted after 30 days by default, adjustable, and can be turned off.
- **Memory notes** (`memory.md`): you can edit or delete them at any time.
- **Logs**: they record what the app did (for example "sent 3 messages to model X"), never the text of your messages.
  The optional detailed logging adds technical details for troubleshooting.

**API keys** are stored in Windows Credential Locker, are never written to files, and only travel over encrypted
connections (https). The one exception is a model server on your own PC, which may use plain http.

If you turn on "keep Claude Code sessions", Claude Code also keeps those chats in its own history on your PC.

To remove everything, uninstall Hotline and delete the `%USERPROFILE%\.hotline` folder. Saved API keys can be removed
in Hotline's settings, or in Windows Credential Manager.

## Permissions Hotline uses

- **Microphone**: only for voice input, and only while you hold the key to talk. Windows asks for your permission
  first.
- **Screen capture**: only when you choose to capture a window, the screen or a region.
- **Selected text**: read from the app you were in when you opened Hotline, so it can be attached. It is never read
  from password fields. In the optional clipboard mode, Hotline copies the selection and then puts your clipboard back
  as it was.

## Donations

Hotline is free. Its "Support Hotline" links open GitHub Sponsors or Ko-fi in your browser; any donation happens on
those sites under their terms. Hotline itself never handles payments or payment details, and a donation unlocks
nothing in the app.

## Children

Hotline is not directed at children. It collects no data from anyone. Your chosen AI provider's terms, including
their age requirements, apply to its service.

## Changes

Changes to this policy are made in this file. Its history on GitHub shows every change and its date.

## Contact

Questions or concerns: open an issue at <https://github.com/PMARC14/hotline/issues>.
