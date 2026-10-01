namespace Hotline.Core.Activation;

/// <summary>Values match Win32 MOD_ALT/MOD_CONTROL/MOD_SHIFT/MOD_WIN.</summary>
[Flags]
public enum HotkeyModifiers : uint { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private const uint VkF1 = 0x70, VkF13 = 0x7C, VkF24 = 0x87;

    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var mods = HotkeyModifiers.None;
        uint? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim().ToLowerInvariant();
            switch (part)
            {
                case "alt": mods |= HotkeyModifiers.Alt; continue;
                case "ctrl" or "control": mods |= HotkeyModifiers.Control; continue;
                case "shift": mods |= HotkeyModifiers.Shift; continue;
                case "win" or "windows": mods |= HotkeyModifiers.Win; continue;
            }
            if (key is not null)
                return false;
            key = KeyFromName(part);
            if (key is null)
                return false;
        }

        if (key is null)
            return false;
        // A bare key (no modifier) would swallow normal typing; only F13–F24 are allowed alone.
        if (mods == HotkeyModifiers.None && key is < VkF13 or > VkF24)
            return false;

        hotkey = new Hotkey(mods, key.Value);
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(NameFromKey(VirtualKey));
        return string.Join('+', parts);
    }

    private static uint? KeyFromName(string n)
    {
        if (n.Length == 1 && n[0] is >= 'a' and <= 'z') return char.ToUpperInvariant(n[0]);
        if (n.Length == 1 && n[0] is >= '0' and <= '9') return n[0];
        if (n.Length > 1 && n[0] == 'f' && int.TryParse(n.AsSpan(1), out var f) && f is >= 1 and <= 24)
            return VkF1 + (uint)(f - 1);
        return n switch
        {
            "space" => 0x20u,
            "enter" => 0x0Du,
            "tab" => 0x09u,
            "esc" or "escape" => 0x1Bu,
            _ => null,
        };
    }

    private static string NameFromKey(uint vk) => vk switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)vk).ToString(),
        >= VkF1 and <= VkF24 => $"F{vk - VkF1 + 1}",
        0x20 => "Space",
        0x0D => "Enter",
        0x09 => "Tab",
        0x1B => "Esc",
        _ => $"0x{vk:X2}",
    };
}
