using Hotline.Core.Settings;

namespace Hotline.Core.Activation;

public static class KeyActionResolver
{
    public static KeyAction Resolve(KeyEvent e, ActivationSettings s) => e switch
    {
        KeyEvent.Tap => s.Tap,
        KeyEvent.HoldStart => s.Hold,
        KeyEvent.HoldStop when s.Hold == KeyAction.Voice => KeyAction.Voice, // release ends push-to-talk
        _ => KeyAction.None,
    };
}
