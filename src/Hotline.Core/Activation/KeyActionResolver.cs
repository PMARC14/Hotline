using Hotline.Core.Settings;

namespace Hotline.Core.Activation;

public static class KeyActionResolver
{
    public static KeyAction Resolve(KeyEvent e, ActivationSettings s) => e switch
    {
        KeyEvent.Tap => s.Tap,
        KeyEvent.HoldStart => s.Hold,
        _ => KeyAction.None, // HoldStop is reserved for push-to-talk (v2)
    };
}
