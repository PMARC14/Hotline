# Using Hotline for the Copilot key

Windows decides which app the Copilot key opens. Hotline registers itself as a Copilot key app when it's installed,
but you choose it once in Windows Settings.

## Set it up

1. Install Hotline and start it once (it then lives in the notification area).
2. Open the Windows setting. Either:
   - in Hotline, **Settings › General › Use Hotline for the Copilot key › Open Copilot key settings** (the first-run
     card in the panel has the same link), or
   - in Windows, **Settings › Personalization › Text input › Customize Copilot key on keyboard**. On newer Windows
     builds the same option can be under **Bluetooth & devices › Keyboard**.
3. Under **Customize Copilot key on keyboard**, choose **Custom**, then pick **Hotline** from the list.
4. Press the Copilot key. A short press opens or closes the panel; a long press starts a new chat (both can be changed
   in Settings › General).

## If Hotline isn't in the list

- Windows only lists apps installed as signed packages (MSIX) that register as Copilot key providers. Hotline from the
  Microsoft Store or a GitHub release qualifies; a build run straight from Visual Studio does not.
- Start Hotline once after installing, then reopen the Windows setting.
- The option only appears on PCs whose keyboard has a Copilot key. Without one, use the fallback shortcut
  (**Ctrl+Alt+H** by default, Settings › General) or the tray icon.
- Organizations can lock the key with the `SetCopilotHardwareKey` policy; then the setting is greyed out.

## Using the key as Right Ctrl instead

Settings › General › **Copilot key** › *Right Ctrl* makes the key behave like Right Ctrl (Copilot+C = Ctrl+C) and stops
it opening Hotline. Use the fallback shortcut or the tray icon to open the panel then.
