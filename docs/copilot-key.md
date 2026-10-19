# Using Hotline for the Copilot key

Windows decides what the Copilot key does. Hotline registers itself as a Copilot key app when it's installed, but you
choose it once in Windows Settings.

## Set it up

1. Install Hotline and start it once (it then lives in the notification area).
2. Open the Windows setting. Either:
   - in Hotline, **Settings › General › Use Hotline for the Copilot key › Open Copilot key settings** (the first-run
     card in the panel has the same link), or
   - in Windows, **Settings › Bluetooth & devices › Keyboard** (per Microsoft's release notes, KB5127648 adds the
     Copilot key choices there on Windows 11 26H2, 25H2 and 24H2), or on builds without that page
     **Settings › Personalization › Text input › Customize Copilot key on keyboard**.
3. Under the Copilot key setting, choose **Custom** (an app), then pick **Hotline** from the list.
4. Press the Copilot key. A short press opens or closes the panel; a long press starts a new chat (both can be changed
   in Settings › General).

## What Windows can map the key to

On the Bluetooth & devices › Keyboard page the Copilot key can be set to **Right Ctrl**, **Context menu**, **Search**
or an **app** (per Microsoft's release notes). For Hotline to answer the key, choose the app option and pick Hotline.
With any other choice use the extra hotkey or the tray icon below.

If you want the key to act as Right Ctrl (Copilot+C = Ctrl+C), choose **Right Ctrl** there. Hotline used to have its own
Right Ctrl mode; it was removed in 0.4 because Windows now offers it.

## What Hotline receives

Hotline reads the key as Windows sends it (Win+Shift+F23 on most keyboards) and starts its own activation from it: the
short and long press actions in Settings › General (`activation.tap` and `activation.hold`, see
[configuration.md](configuration.md)). If the key stops opening Hotline, open the Copilot key setting again and check
that Hotline is still the chosen app.

## If the key doesn't open Hotline

- The extra hotkey (`activation.fallbackHotkey`, for example **Ctrl+Alt+H**, set in Settings › General) opens and closes
  the panel the same way.
- The tray icon in the notification area is always there.
- Hotline isn't in the list: Windows only lists apps installed as signed packages (MSIX) that register as Copilot key
  providers. Hotline from the Microsoft Store or a GitHub release qualifies; a build run straight from Visual Studio
  does not. Start Hotline once after installing, then reopen the Windows setting.
- The option only appears on PCs whose keyboard has a Copilot key. Without one, use the extra hotkey (for example Ctrl+Alt+H) or the tray icon.
- Organizations can lock the key with the `SetCopilotHardwareKey` policy; then the setting is greyed out.
