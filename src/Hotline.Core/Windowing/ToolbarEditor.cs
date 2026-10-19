namespace Hotline.Core.Windowing;

/// <summary>An arrow of the button-rows editor: Left/Right go to the other side of the same row, Up/Down to the other row on the same side.</summary>
public enum Shift { Left, Right, Up, Down }

/// <summary>
/// The one editing model for the button rows (Settings' quadrant editor: drag and drop, the arrows, "Not shown", custom
/// buttons, styles). Every edit is pure and never throws: it builds the new model, writes it with
/// <see cref="ToolbarModel.ToJson"/> and reads it back with <see cref="ToolbarModel.Resolve"/>, so a result is valid by
/// construction (<c>Resolve(result.ToJson()) == result</c>). An edit that is invalid or changes nothing returns the input
/// instance. Ids match case-insensitively and keep their own spelling.
/// <para>The pickers (effort, model, provider) are one block: moving, dragging, shifting or showing one of them moves the
/// whole block, in its order. Hiding one keeps the others together; any item may be hidden, down to an empty bar.</para>
/// </summary>
public static class ToolbarEditor
{
    private static readonly Quadrant[] Order = Enum.GetValues<Quadrant>();

    /// <summary>Moves an item <paramref name="delta"/> places within its quadrant (the picker block counts as one place),
    /// stopping at the ends.</summary>
    public static ToolbarModel Move(ToolbarModel m, string id, int delta)
    {
        if (m is null || Placed(m, id) is not { } at) return m!;
        var units = Units(m.Items(at.Quadrant));
        var from = units.FindIndex(u => u.Contains(at.Id));
        var to = (int)Math.Clamp((long)from + delta, 0, units.Count - 1);
        if (to == from) return m;
        var unit = units[from];
        units.RemoveAt(from);
        units.Insert(to, unit);
        var lists = Lists(m);
        lists[at.Quadrant] = [.. units.SelectMany(u => u)];
        return Strict(m, With(m, lists));
    }

    /// <summary>Drag and drop: puts an item (shown or not) at a gap of <paramref name="q"/>, <paramref name="index"/> counted
    /// in its current list (0 = before the first, Count = after the last; clamped). A picker brings the whole block.</summary>
    public static ToolbarModel MoveTo(ToolbarModel m, string id, Quadrant q, int index)
    {
        if (m is null || !Order.Contains(q) || Known(m, id) is not { } c) return m!;
        var lists = Lists(m);
        List<string> unit = ToolbarModel.Pickers.Contains(c)
            ? [.. Order.SelectMany(x => lists[x]).Where(ToolbarModel.Pickers.Contains).Union([c])]
            : [c];
        var target = lists[q];
        var at = Math.Clamp(index, 0, target.Count);
        at -= target.Take(at).Count(unit.Contains);
        foreach (var list in lists.Values) list.RemoveAll(unit.Contains);
        target.InsertRange(at, unit);
        return Strict(m, With(m, lists));
    }

    /// <summary>The arrows: to the end of the neighbouring quadrant; nothing at the edge (e.g. Up from the top row).</summary>
    public static ToolbarModel ShiftTo(ToolbarModel m, string id, Shift direction)
    {
        if (m is null || Placed(m, id) is not { } at) return m!;
        var left = at.Quadrant is Quadrant.BottomLeft or Quadrant.TopLeft;
        var top = at.Quadrant is Quadrant.TopLeft or Quadrant.TopRight;
        Quadrant? target = direction switch
        {
            Shift.Left when !left => top ? Quadrant.TopLeft : Quadrant.BottomLeft,
            Shift.Right when left => top ? Quadrant.TopRight : Quadrant.BottomRight,
            Shift.Up when !top => left ? Quadrant.TopLeft : Quadrant.TopRight,
            Shift.Down when top => left ? Quadrant.BottomLeft : Quadrant.BottomRight,
            _ => null,
        };
        return target is { } q ? MoveTo(m, at.Id, q, int.MaxValue) : m;
    }

    /// <summary>Takes an item off the bar (to "Not shown"); its style override goes with it, as <see cref="ToolbarModel.Resolve"/>
    /// keeps overrides only for shown items.</summary>
    public static ToolbarModel Hide(ToolbarModel m, string id)
    {
        if (m is null || Placed(m, id) is not { } at) return m!;
        var lists = Lists(m);
        lists[at.Quadrant].Remove(at.Id);
        var overrides = Overrides(m);
        overrides.Remove(at.Id);
        return Strict(m, With(m, lists) with { Overrides = overrides });
    }

    /// <summary>Puts a hidden item at the end of <paramref name="q"/> (a picker brings the block there, itself last).
    /// An item already shown stays where it is (moving is <see cref="MoveTo"/>).</summary>
    public static ToolbarModel Show(ToolbarModel m, string id, Quadrant q) =>
        m is null || Known(m, id) is not { } c || Placed(m, c) is not null ? m! : MoveTo(m, c, q, int.MaxValue);

    /// <summary>The items not on the bar: built-ins in <see cref="ToolbarModel.BuiltIn"/> order, then custom buttons in theirs.</summary>
    public static IReadOnlyList<string> Hidden(ToolbarModel m)
    {
        if (m is null) return [];
        var placed = Order.SelectMany(m.Items).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. All(m).Where(id => !placed.Contains(id))];
    }

    /// <summary>Adds a custom button, shown at the end of the bottom-left group. Rejected (input returned) for an id that
    /// isn't 1-32 characters a-z, 0-9 or -, is a built-in or already used, past 24 buttons, or for a button
    /// <see cref="ToolbarModel.Resolve"/> would drop; label, text and icon are clamped like Resolve does.</summary>
    public static ToolbarModel AddCustom(ToolbarModel m, CustomButton b)
    {
        if (m is null || !OneAction(b) || !ToolbarModel.IsCustomId(b.Id) || Known(m, b.Id) is not null) return m!;
        var lists = Lists(m);
        lists[Quadrant.BottomLeft].Add(b.Id);
        return Lenient(m, With(m, lists) with { Buttons = [.. m.Buttons, b] }, b.Id);
    }

    /// <summary>Replaces the custom button with the same id, keeping its place in the list and on the bar.</summary>
    public static ToolbarModel UpdateCustom(ToolbarModel m, CustomButton b)
    {
        if (m is null || !OneAction(b) || b.Id is null) return m!;
        var i = m.Buttons.ToList().FindIndex(x => x.Id == b.Id);
        if (i < 0) return m;
        var buttons = m.Buttons.ToList();
        buttons[i] = b;
        return Lenient(m, m with { Buttons = buttons }, b.Id);
    }

    /// <summary>Deletes a custom button, its place on the bar and its style override.</summary>
    public static ToolbarModel RemoveCustom(ToolbarModel m, string id)
    {
        if (m is null || m.Buttons.FirstOrDefault(b => string.Equals(b.Id, id, StringComparison.OrdinalIgnoreCase)) is not { } button) return m!;
        var lists = Lists(m);
        foreach (var list in lists.Values) list.Remove(button.Id);
        var overrides = Overrides(m);
        overrides.Remove(button.Id);
        return Strict(m, With(m, lists) with { Overrides = overrides, Buttons = [.. m.Buttons.Where(b => b != button)] });
    }

    /// <summary>Sets the whole bar's style (<paramref name="id"/> null; unset fields get the defaults) or one shown item's
    /// override (unset fields inherit; an empty style removes it). Width and height are clamped to 20-80. The pickers
    /// take no styles.</summary>
    public static ToolbarModel SetStyle(ToolbarModel m, string? id, ButtonStyle s)
    {
        if (m is null || s is null) return m!;
        if (id is null) return Lenient(m, m with { Style = s }, null);
        if (Placed(m, id) is not { } at || ToolbarModel.Pickers.Contains(at.Id)) return m;
        var overrides = Overrides(m);
        if (s == new ButtonStyle()) overrides.Remove(at.Id);
        else overrides[at.Id] = s;
        return Lenient(m, m with { Overrides = overrides }, null);
    }

    // ---- Helpers ----

    /// <summary>Exactly one of quick action and text, as in the file (the writer would otherwise drop the text silently).</summary>
    private static bool OneAction(CustomButton? b) => b is not null && (b.QuickAction is null) != (b.Text is null);

    private static IEnumerable<string> All(ToolbarModel m) => ToolbarModel.BuiltIn.Concat(m.Buttons.Select(b => b.Id));

    /// <summary>The stored spelling of a built-in or custom id, or null.</summary>
    private static string? Known(ToolbarModel m, string? id) =>
        id is null ? null : All(m).FirstOrDefault(k => k.Equals(id, StringComparison.OrdinalIgnoreCase));

    private static (Quadrant Quadrant, string Id)? Placed(ToolbarModel m, string? id)
    {
        foreach (var q in Order)
            if (m.Items(q).FirstOrDefault(i => i.Equals(id, StringComparison.OrdinalIgnoreCase)) is { } found)
                return (q, found);
        return null;
    }

    /// <summary>A quadrant's items as movable units: each item alone, the (adjacent) pickers together.</summary>
    private static List<List<string>> Units(IReadOnlyList<string> items)
    {
        var units = new List<List<string>>();
        foreach (var id in items)
            if (ToolbarModel.Pickers.Contains(id) && units.Count > 0 && ToolbarModel.Pickers.Contains(units[^1][0])) units[^1].Add(id);
            else units.Add([id]);
        return units;
    }

    private static Dictionary<Quadrant, List<string>> Lists(ToolbarModel m) => Order.ToDictionary(q => q, q => m.Items(q).ToList());

    private static Dictionary<string, ButtonStyle> Overrides(ToolbarModel m) => new(m.Overrides, StringComparer.OrdinalIgnoreCase);

    private static ToolbarModel With(ToolbarModel m, Dictionary<Quadrant, List<string>> lists) =>
        m with { Placed = Order.ToDictionary(q => q, IReadOnlyList<string> (q) => lists[q]) };

    /// <summary>The written-and-read-back form of <paramref name="candidate"/>, or null if it can't be written.</summary>
    private static ToolbarModel? RoundTrip(ToolbarModel candidate)
    {
        try { return ToolbarModel.Resolve(candidate.ToJson()); }
        catch (Exception) { return null; } // e.g. text the writer can't encode: the edit is refused
    }

    /// <summary>A layout edit: kept only if reading it back changes nothing (Resolve would otherwise have fixed something).</summary>
    private static ToolbarModel Strict(ToolbarModel m, ToolbarModel candidate) =>
        RoundTrip(candidate) is { } r && r.Equals(candidate) && !r.Equals(m) ? r with { Warnings = [] } : m;

    /// <summary>A content edit: Resolve's clamps apply, but the layout and the button list must come back whole
    /// (<paramref name="button"/>, when given, must survive as a valid button).</summary>
    private static ToolbarModel Lenient(ToolbarModel m, ToolbarModel candidate, string? button) =>
        RoundTrip(candidate) is { } r
        && Order.All(q => r.Items(q).SequenceEqual(candidate.Items(q)))
        && r.Buttons.Select(b => b.Id).SequenceEqual(candidate.Buttons.Select(b => b.Id))
        && (button is null || r.Buttons.Any(b => b.Id == button))
        && !r.Equals(m)
            ? r with { Warnings = [] }
            : m;
}
