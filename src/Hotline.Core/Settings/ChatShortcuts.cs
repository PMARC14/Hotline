using System.Collections.ObjectModel;
using Hotline.Core.Activation;
using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Settings;

/// <summary>Panel commands, keyed by ToolbarModel ids. Stop always wins; otherwise the first combo wins.</summary>
public static class ChatShortcuts
{
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
    {
        ["newChat"] = "Ctrl+N", ["recent"] = "Ctrl+H", ["settings"] = "Ctrl+,", ["pin"] = "Ctrl+P",
        ["copyLastAnswer"] = "Ctrl+Shift+C", ["captureRegion"] = "Ctrl+Shift+R", ["prompt"] = "Ctrl+Shift+P",
        ["effort"] = "Ctrl+E", ["model"] = "Ctrl+M", ["provider"] = "Ctrl+Shift+M",
        ["captureWindow"] = "Ctrl+Shift+W", ["captureScreen"] = "Ctrl+Shift+S",
        ["continueIn"] = "Ctrl+Shift+I", ["more"] = "Ctrl+Shift+O",
    });

    public static Dictionary<string, string> CreateDefaults() => new(Defaults);

    /// <summary>Empty text disables a binding. Invalid entries use their default, if it is still free.</summary>
    public static Dictionary<string, string> Normalize(IEnumerable<KeyValuePair<string, string>>? entries, string stopShortcut,
        Action<string>? warn = null)
    {
        var result = new Dictionary<string, string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<Hotkey>();
        if (Hotkey.TryParseShortcut(stopShortcut, out var stop)) used.Add(stop);
        void Add(string id, string? text)
        {
            if (text == "") { result[id] = ""; return; }
            if (!Hotkey.TryParseToolbarShortcut(text, out var key))
            {
                warn?.Invoke(L.F(Strings.Shortcuts_Invalid, id));
                Hotkey.TryParseToolbarShortcut(Defaults[id], out key);
            }
            if (!Claim(used, key, id, warn))
            {
                Hotkey.TryParseToolbarShortcut(Defaults[id], out key);
                if (!used.Add(key)) { result[id] = ""; return; }
            }
            result[id] = key.ToString();
        }
        var requested = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in entries ?? [])
        {
            var id = Defaults.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (id is null) { warn?.Invoke(L.F(Strings.Shortcuts_Unknown, LenientReader.Quote(name))); continue; }
            if (!seen.Add(id)) warn?.Invoke(L.F(Strings.Shortcuts_Duplicate, id));
            requested[id] = value;
        }
        foreach (var (id, value) in requested) Add(id, value);
        foreach (var (id, value) in Defaults)
            if (seen.Add(id)) Add(id, value);
        return result;
    }

    /// <summary>The toolbar command whose requested or effective binding (built-in or custom; switched-off ones don't count) is
    /// <paramref name="stop"/>, or null. Stop wins at run time, so Settings refuses such a Stop instead of disabling the command.</summary>
    public static string? StopConflict(ToolbarModel toolbar, string? stop)
    {
        if (!Hotkey.TryParseShortcut(stop, out var key)) return null;
        // What each command asked for, and what it falls back to (an invalid value takes its default), counted without any Stop.
        var effective = toolbar.EffectiveShortcuts(stop: "");
        foreach (var id in Defaults.Keys)
            if (Hotkey.TryParseToolbarShortcut(toolbar.ShortcutText(id), out var binding) && binding == key
                || Hotkey.TryParseToolbarShortcut(effective.GetValueOrDefault(id), out binding) && binding == key) return id;
        foreach (var button in toolbar.Buttons)
            if (Hotkey.TryParseToolbarShortcut(button.RequestedShortcut ?? button.Shortcut, out var binding) && binding == key) return button.Id;
        return null;
    }

    /// <summary>Settings rejects collisions with requested bindings, including bindings currently disabled by a conflict.</summary>
    public static bool CanBind(ToolbarModel toolbar, ChatSettings chat, string id, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!Hotkey.TryParseToolbarShortcut(text, out var key)) return false;
        if (Hotkey.TryParseShortcut(chat.StopShortcut, out var stop) && stop == key) return false;
        var effective = toolbar.EffectiveShortcuts(chat.StopShortcut);
        foreach (var other in Defaults.Keys.Where(k => !k.Equals(id, StringComparison.OrdinalIgnoreCase)))
            if (Hotkey.TryParseToolbarShortcut(toolbar.ShortcutText(other), out var binding) && binding == key
                || Hotkey.TryParseToolbarShortcut(effective[other], out binding) && binding == key) return false;
        foreach (var button in toolbar.Buttons.Where(b => !b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            if (Hotkey.TryParseToolbarShortcut(button.RequestedShortcut ?? button.Shortcut, out var binding) && binding == key) return false;
        return true;
    }

    /// <summary>Custom bindings have no fallback. Stop and the effective built-in bindings reserve their combos first;
    /// custom buttons then claim them in file order. Without chat settings, only custom conflicts are resolved.</summary>
    public static IReadOnlyList<CustomButton> NormalizeCustom(IReadOnlyList<CustomButton> buttons, ChatSettings? chat = null,
        Action<string>? warn = null)
    {
        var used = new HashSet<Hotkey>();
        if (chat is not null)
        {
            if (Hotkey.TryParseShortcut(chat.StopShortcut, out var stop)) used.Add(stop);
            foreach (var text in Normalize(chat.Shortcuts, chat.StopShortcut).Values)
                if (Hotkey.TryParseShortcut(text, out var key)) used.Add(key);
        }
        return buttons.Select(button =>
        {
            var requested = button.RequestedShortcut ?? button.Shortcut;
            if (requested is null or "") return button with { Shortcut = null };
            if (!Hotkey.TryParseToolbarShortcut(requested, out var key))
            {
                warn?.Invoke(L.F(Strings.BarW_ShortcutInvalid, button.Id));
                return button with { Shortcut = null, RequestedShortcut = requested };
            }
            return Claim(used, key, button.Id, warn, custom: true) ? button with { Shortcut = key.ToString(), RequestedShortcut = null }
                : button with { Shortcut = null, RequestedShortcut = requested };
        }).ToList();
    }

    private static bool Claim(HashSet<Hotkey> used, Hotkey key, string id, Action<string>? warn, bool custom = false)
    {
        if (used.Add(key)) return true;
        warn?.Invoke(L.F(custom ? Strings.BarW_ShortcutConflict : Strings.Shortcuts_Conflict, id));
        return false;
    }
}
