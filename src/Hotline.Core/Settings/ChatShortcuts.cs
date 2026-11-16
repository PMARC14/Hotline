using System.Collections.ObjectModel;
using Hotline.Core.Activation;
using Hotline.Core.Text.Localization;
using Hotline.Core.Windowing;

namespace Hotline.Core.Settings;

/// <summary>Panel commands in priority order: Stop, built-ins in shipped order, then custom buttons in file order.</summary>
public static class ChatShortcuts
{
    public static IReadOnlyDictionary<string, string> Defaults { get; } = new ReadOnlyDictionary<string, string>(
        ToolbarModel.DefaultTree()["shortcuts"]!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<string>()));

    public static Dictionary<string, string> CreateDefaults() => new(Defaults);

    public static bool TryParse(string id, string? text, out Hotkey key) => id.Equals("stop", StringComparison.OrdinalIgnoreCase)
        ? Hotkey.TryParseShortcut(text, out key) && !Hotkey.IsEditingKey(key) : Hotkey.TryParseToolbarShortcut(text, out key);

    /// <summary>Missing/null entries default. Invalid entries warn and default. A conflicting binding is disabled.</summary>
    public static Dictionary<string, string> Normalize(IEnumerable<KeyValuePair<string, string>>? entries, Action<string>? warn = null)
    {
        var requested = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in entries ?? [])
        {
            var id = Defaults.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (id is null) { warn?.Invoke(L.F(Strings.Shortcuts_Unknown, LenientReader.Quote(name))); continue; }
            if (requested.ContainsKey(id)) warn?.Invoke(L.F(Strings.Shortcuts_Duplicate, id));
            requested[id] = value;
        }
        var result = new Dictionary<string, string>();
        var used = new HashSet<Hotkey>();
        foreach (var id in new[] { "stop" }.Concat(Defaults.Keys.Where(k => k != "stop")))
        {
            var text = requested.GetValueOrDefault(id) ?? Defaults[id];
            if (text == "") { result[id] = ""; continue; }
            if (!TryParse(id, text, out var key))
            {
                warn?.Invoke(L.F(Strings.Shortcuts_Invalid, id));
                TryParse(id, Defaults[id], out key);
            }
            result[id] = Claim(used, key, id, warn) ? key.ToString() : "";
        }
        return result;
    }

    // Kept until the parallel settings task removes its legacy reader and normalizer.
    public static Dictionary<string, string> Normalize(IEnumerable<KeyValuePair<string, string>>? entries, string stopShortcut,
        Action<string>? warn = null) => Normalize((entries ?? []).Where(p => !p.Key.Equals("stop", StringComparison.OrdinalIgnoreCase))
            .Append(new("stop", stopShortcut)), warn);

    /// <summary>Settings rejects collisions with requested and effective bindings.</summary>
    public static bool CanBind(ToolbarModel toolbar, string id, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (!TryParse(id, text, out var key)) return false;
        var effective = toolbar.EffectiveShortcuts();
        foreach (var other in Defaults.Keys.Where(k => !k.Equals(id, StringComparison.OrdinalIgnoreCase)))
            if (TryParse(other, toolbar.ShortcutText(other), out var binding) && binding == key
                || TryParse(other, effective[other], out binding) && binding == key) return false;
        foreach (var button in toolbar.Buttons.Where(b => !b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
            if (Hotkey.TryParseToolbarShortcut(button.RequestedShortcut ?? button.Shortcut, out var binding) && binding == key) return false;
        return true;
    }

    public static string? StopConflict(ToolbarModel toolbar, string? stop)
    {
        if (!Hotkey.TryParseShortcut(stop, out var key)) return null;
        return Defaults.Keys.Where(id => id != "stop").FirstOrDefault(id => TryParse(id, toolbar.ShortcutText(id), out var binding) && binding == key)
            ?? toolbar.Buttons.FirstOrDefault(b => Hotkey.TryParseToolbarShortcut(b.RequestedShortcut ?? b.Shortcut, out var binding) && binding == key)?.Id;
    }

    public static IReadOnlyList<CustomButton> NormalizeCustom(IReadOnlyList<CustomButton> buttons,
        IReadOnlyDictionary<string, string>? commands = null, Action<string>? warn = null)
    {
        var used = new HashSet<Hotkey>();
        foreach (var text in (commands ?? Normalize(null)).Values)
            if (Hotkey.TryParseShortcut(text, out var key)) used.Add(key);
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
        warn?.Invoke(L.F(custom ? Strings.BarW_ShortcutConflict : Strings.Bar_CommandConflict, id));
        return false;
    }
}
