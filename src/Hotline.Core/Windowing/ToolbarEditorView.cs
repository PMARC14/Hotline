using System.Globalization;
using System.Text;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>One place in a quadrant strip of Settings' editor: an item, or the picker block (<see cref="Ids"/> in file
/// order), with which arrows would change something.</summary>
public sealed record EditorUnit(
    IReadOnlyList<string> Ids, bool CanMoveLeft, bool CanMoveRight, bool CanShiftUp, bool CanShiftDown, bool CanShiftLeft, bool CanShiftRight)
{
    /// <summary>The id the editing calls take (for the block, its first picker: any picker moves the whole block).</summary>
    public string Id => Ids[0];
    public bool IsPickers => ToolbarPlan.IsPicker(Id);
}

/// <summary>Why a custom button can't be added or saved as it is (Settings shows a notice for each).</summary>
public enum CustomProblem { None, NoLabel, NoAction, BadQuickAction, TooManyButtons, GroupFull }

/// <summary>The whole bar's size as Settings shows it: one of the sizes (same values as <see cref="ButtonSize"/>), or Custom
/// when the items don't all share it.</summary>
public enum BarSizeChoice { Compact = ButtonSize.Compact, Normal = ButtonSize.Normal, Wide = ButtonSize.Wide, Custom }

/// <summary>
/// The pure half of Settings' quadrant editor (<see cref="ToolbarEditor"/> does the edits): what each strip shows, the
/// arrows' states, drop gaps, ids for new custom buttons, the icon choices and the checks behind its notices.
/// </summary>
public static class ToolbarEditorView
{
    /// <summary>The icons a custom button can have (Segoe Fluent Icons): star, message, document, edit, copy, search, globe,
    /// chat, lightning, code, mail, calendar, flag, heart, check mark, help.</summary>
    public static readonly IReadOnlyList<string> Icons =
        [ToolbarModel.DefaultIcon, "E8BD", "E8A5", "E70F", "E8C8", "E721", "E774", "E8F2", "E945", "E943", "E715", "E787", "E7C1", "EB51", "E8FB", "E897"];

    /// <summary>The sizes the whole bar and each item can pick, smallest first.</summary>
    public static readonly IReadOnlyList<ButtonSize> Sizes = Enum.GetValues<ButtonSize>();

    public static string SizeName(ButtonSize size) => size switch
    {
        ButtonSize.Compact => Strings.Bar_SizeCompact,
        ButtonSize.Wide => Strings.Bar_SizeWide,
        _ => Strings.Bar_SizeNormal,
    };

    /// <summary>What the whole bar's size choice shows: the bar's size, or <see cref="BarSizeChoice.Custom"/> while an item has
    /// a size of its own that differs from it. Derived, never stored: the file keeps the bar's size and the overrides.</summary>
    public static BarSizeChoice BarSize(ToolbarModel m)
    {
        var bar = m.Style.Size is { } s && Enum.IsDefined(s) ? s : ButtonSize.Normal;
        return m.Overrides.Values.Any(o => o.Size is { } own && own != bar) ? BarSizeChoice.Custom : (BarSizeChoice)bar;
    }

    /// <summary>The outline colour swatches besides the accent colour: red, orange, yellow, green, teal, blue, purple, pink
    /// (the Windows accent palette's, readable on light and dark).</summary>
    public static readonly IReadOnlyList<string> OutlineColors =
        ["#E81123", "#F7630C", "#FFB900", "#107C10", "#00B7C3", "#0078D4", "#8764B8", "#E3008C"];

    /// <summary>A colour's accessible name: the accent colour for null, a swatch's name, or the hex of one set by hand.</summary>
    public static string ColorName(string? hex) => hex?.ToUpperInvariant() switch
    {
        null => Strings.Bar_ColorAccent,
        "#E81123" => Strings.Bar_ColorRed,
        "#F7630C" => Strings.Bar_ColorOrange,
        "#FFB900" => Strings.Bar_ColorYellow,
        "#107C10" => Strings.Bar_ColorGreen,
        "#00B7C3" => Strings.Bar_ColorTeal,
        "#0078D4" => Strings.Bar_ColorBlue,
        "#8764B8" => Strings.Bar_ColorPurple,
        "#E3008C" => Strings.Bar_ColorPink,
        _ => hex!,
    };

    /// <summary>A quadrant's strip: each item alone, the pickers as one block.</summary>
    public static IReadOnlyList<EditorUnit> Units(ToolbarModel m, Quadrant q)
    {
        var groups = Groups(m.Items(q));
        var (column, top) = (ToolbarEditor.Column(q), q is Quadrant.TopLeft or Quadrant.TopCenter or Quadrant.TopRight);
        return [.. groups.Select((ids, i) => new EditorUnit(ids, i > 0, i < groups.Count - 1, !top, top, column > 0, column < 2))];
    }

    /// <summary>The gap in <paramref name="q"/>'s item list (as <see cref="ToolbarEditor.MoveTo"/> counts) for the gap
    /// <paramref name="unitGap"/> between the strip's units (clamped): the picker block counts as one unit.</summary>
    public static int ItemIndex(ToolbarModel m, Quadrant q, int unitGap) =>
        Groups(m.Items(q)).Take(Math.Max(0, unitGap)).Sum(g => g.Count);

    /// <summary>The "Not shown" strip's add button: a hidden picker joins the end of the block where it is, anything else
    /// (or a picker with no block left) goes to the end of bottom left. A shown or unknown id returns the input.</summary>
    public static ToolbarModel Show(ToolbarModel m, string id)
    {
        if (id is not null && ToolbarPlan.IsPicker(id) && ToolbarEditor.Hidden(m).Contains(id))
            foreach (var q in Enum.GetValues<Quadrant>())
                if (m.Items(q).ToList().FindLastIndex(ToolbarPlan.IsPicker) is var last and >= 0)
                    return ToolbarEditor.MoveTo(m, id, q, last + 1);
        return ToolbarEditor.Show(m, id!, Quadrant.BottomLeft);
    }

    private static List<List<string>> Groups(IReadOnlyList<string> items)
    {
        var groups = new List<List<string>>();
        foreach (var id in items)
            if (ToolbarPlan.IsPicker(id) && groups.Count > 0 && ToolbarPlan.IsPicker(groups[^1][0])) groups[^1].Add(id);
            else groups.Add([id]);
        return groups;
    }

    /// <summary>
    /// An id for a new custom button made from its label: lower case a-z, 0-9 and single dashes (accents dropped, other
    /// characters become dashes), at most 32 characters, "button" when nothing is left, and "-2", "-3"… when it is a
    /// built-in or already used (ignoring case).
    /// </summary>
    public static string NewCustomId(ToolbarModel m, string? label)
    {
        var slug = new StringBuilder();
        foreach (var c in (label ?? "").Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            var lower = char.ToLowerInvariant(c);
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9') slug.Append(lower);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        var id = Cut(slug.ToString(), ToolbarModel.MaxId);
        if (id.Length == 0) id = "button";
        var taken = ToolbarModel.BuiltIn.Concat(m.Buttons.Select(b => b.Id)).Append("spacer").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains(id)) return id;
        for (var n = 2; ; n++)
        {
            var suffix = "-" + n;
            var candidate = Cut(id, ToolbarModel.MaxId - suffix.Length) + suffix;
            if (!taken.Contains(candidate)) return candidate;
        }

        static string Cut(string s, int max) => (s.Length > max ? s[..max] : s).Trim('-');
    }

    /// <summary>The quick actions a custom button can run: valid names (see <see cref="ToolbarModel"/>), not Hotline's own
    /// commands, once each, sorted; plus <paramref name="current"/> (the button's own) when its file is gone.</summary>
    public static IReadOnlyList<string> ActionChoices(IEnumerable<string> names, string? current)
    {
        var builtIns = Chat.QuickActions.BuiltIns.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = names.Append(current).OfType<string>()
            .Where(n => ToolbarModel.IsActionName(n) && !builtIns.Contains(n))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        return [.. choices.Order(StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>What stops <paramref name="b"/> from being added (<paramref name="adding"/>) or saved: no label, neither a
    /// quick action nor text, an invalid quick action name, or no room (24 buttons, or 40 items bottom left, where a new
    /// button goes). <see cref="CustomProblem.None"/> means <see cref="ToolbarEditor.AddCustom"/> takes it.</summary>
    public static CustomProblem Check(ToolbarModel m, CustomButton b, bool adding)
    {
        if (string.IsNullOrWhiteSpace(b.Label)) return CustomProblem.NoLabel;
        if (b.QuickAction is { Length: > 0 } action) { if (!ToolbarModel.IsActionName(action)) return CustomProblem.BadQuickAction; }
        else if (string.IsNullOrWhiteSpace(b.Text)) return CustomProblem.NoAction;
        if (adding && m.Buttons.Count >= ToolbarModel.MaxButtons) return CustomProblem.TooManyButtons;
        if (adding && m.Items(Quadrant.BottomLeft).Count >= ToolbarModel.MaxItems) return CustomProblem.GroupFull;
        return CustomProblem.None;
    }
}
