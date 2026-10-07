namespace Hotline.Core.Activation;

/// <summary>What the Copilot key does: open Hotline (default) or act as a Right Ctrl key.</summary>
public enum CopilotKeyMode { Hotline, RightCtrl }

/// <summary>A key event to send (virtual-key code; key up or down).</summary>
public readonly record struct KeyStroke(ushort Vk, bool Up);

/// <summary>
/// The Copilot key as Right Ctrl. The key sends Win+Shift+F23 (Win and Shift go down first). When F23 goes down, the
/// remap lets go of Win and Shift — after a "mask" key, so releasing Win doesn't open Start — and presses Right Ctrl;
/// when F23 comes up it lets go of Right Ctrl. So Copilot+C is Ctrl+C. F23 itself never reaches Windows, so Hotline
/// isn't opened by it (the extra hotkey still opens Hotline). Decisions only; the app's keyboard hook applies them.
/// </summary>
public sealed class CopilotKeyRemap
{
    public const ushort F23 = 0x86, LWin = 0x5B, LShift = 0xA0, RCtrl = 0xA3;
    /// <summary>An unassigned key (VK 0xE8): pressing it between Win down and up stops Start from opening.</summary>
    public const ushort Mask = 0xE8;

    public bool Held { get; private set; }

    /// <summary>What to do with a real key event: swallow it, and which keys to send instead.</summary>
    public (bool Suppress, IReadOnlyList<KeyStroke> Inject) OnKey(ushort vk, bool up)
    {
        if (vk != F23) return (false, []);
        if (!up)
        {
            if (Held) return (true, []); // auto-repeat
            Held = true;
            return (true, [new(Mask, false), new(Mask, true), new(LShift, true), new(LWin, true), new(RCtrl, false)]);
        }
        if (!Held) return (true, []);
        Held = false;
        return (true, [new(RCtrl, true)]);
    }
}
