# Using Hotline for the Copilot key

Windows decides what the Copilot key does. Hotline registers itself as a Copilot key app when installed; you choose it once in Windows.

## Set it up

1. Install Hotline and start it once (it then lives in the notification area).
2. Open the Windows setting, either from Hotline (**Settings › General › Use Hotline for the Copilot key › Open Copilot key
   settings**; the first-run card has the same link) or in Windows: **Settings › Bluetooth & devices › Keyboard** (per Microsoft's
   release notes, KB5127648 adds the Copilot key choices there on Windows 11 26H2, 25H2 and 24H2), or on builds without that page
   **Settings › Personalization › Text input › Customize Copilot key on keyboard**.
3. Choose **Custom** (an app), then pick **Hotline**. The other choices (**Right Ctrl**, **Context menu**, **Search**) do not reach
   Hotline: with Right Ctrl the key acts as Right Ctrl (Hotline's own Right Ctrl mode was removed in 0.4 because Windows now offers it).
4. Press the key. A short press opens or closes the panel; a long press starts a new chat. Both can be changed in Settings › General
   (`activation.tap` and `activation.hold`, see [configuration.md](configuration.md)).

## If the key doesn't open Hotline

- Check that Hotline is still the chosen app in the Windows setting.
- Use the extra hotkey (`activation.fallbackHotkey`, for example **Ctrl+Alt+H**, Settings › General), which also covers a PC without a
  Copilot key, or the tray icon while Hotline is running.
- Hotline isn't in the list: Windows only lists apps installed as signed packages (MSIX) that register as Copilot key providers.
  Hotline from the Microsoft Store or a GitHub release qualifies; a build run straight from Visual Studio does not. Start Hotline once
  after installing, then reopen the Windows setting.
- The option only appears on PCs whose keyboard has a Copilot key. Organizations can lock it with the `SetCopilotHardwareKey` policy
  (the setting is then greyed out).
