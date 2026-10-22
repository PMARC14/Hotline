namespace Hotline.Core.Windowing;

/// <summary>An arrow of the button-rows editor: Left/Right go to the other side of the same row, Up/Down to the other row on the same side.</summary>
public enum Shift { Left, Right, Up, Down }

/// <summary>
/// The one editing model for the button rows (Settings' quadrant editor: drag and drop, the arrows, "Not shown", custom
/// buttons, styles). Every edit is pure and never throws: it builds the new model, writes it with
/// <see cref="ToolbarModel.ToJson"/> and reads it back with <see cref="ToolbarModel.Resolve"/>, so a result is valid by
/// construction (<c>Resolve(result.ToJson()) == result</c>). An edit that is invalid or changes nothing returns the input
/// instance. Ids match case-insensitively and keep their own spelling.
/// <para>The pickers (effort, model, provider) are items like any other: each moves, drags, shifts, hides and shows on its
/// own. Any item may be hidden, down to an empty bar.</para>
/// </summary>
public static class ToolbarEditor
{
    private static readonly Quadrant[] Order = Enum.GetValues<Quadrant>();

    /// <summary>Moves an item <paramref name="delta"/> places within its quadrant, stopping at the ends.</summary>
    public static ToolbarModel Move(ToolbarModel m, string id, int delta)
    {
        if (m is null) return m!;
        if (Placed(m, id) is not { } at) return InMenu(m, id) is { } entry ? MoveInMenu(m, entry, delta) : m;
        var items = m.Items(at.Quadrant).ToList();
        var from = items.IndexOf(at.Id);
        var to = (int)Math.Clamp((long)from + delta, 0, items.Count - 1);
        if (to == from) return m;
        items.RemoveAt(from);
        items.Insert(to, at.Id);
        var lists = Lists(m);
        lists[at.Quadrant] = items;
        return Strict(m, With(m, lists));
    }

    /// <summary>Drag and drop: puts an item (shown or not) at a gap of <paramref name="q"/>, <paramref name="index"/> counted
    /// in its current list (0 = before the first, Count = after the last; clamped).</summary>
    public static ToolbarModel MoveTo(ToolbarModel m, string id, Quadrant q, int index)
    {
        if (m is null || !Order.Contains(q) || Known(m, id) is not { } c) return m!;
        var lists = Lists(m);
        var target = lists[q];
        var at = Math.Clamp(index, 0, target.Count);
        if (target.IndexOf(c) is var own and >= 0 && own < at) at--; // it leaves the list before it is put back
        foreach (var list in lists.Values) list.Remove(c);
        target.Insert(at, c);
        // Back on the bar from the menu (moving the more button itself keeps its menu).
        return Strict(m, With(m, lists) with { Menu = [.. m.Menu.Where(x => !x.Equals(c, StringComparison.OrdinalIgnoreCase))] });
    }

    /// <summary>Moves a menu entry <paramref name="delta"/> places within the "more" menu, stopping at the ends.</summary>
    private static ToolbarModel MoveInMenu(ToolbarModel m, string id, int delta)
    {
        var menu = m.Menu.ToList();
        var from = menu.IndexOf(id);
        var to = (int)Math.Clamp((long)from + delta, 0, menu.Count - 1);
        if (to == from) return m;
        menu.RemoveAt(from);
        menu.Insert(to, id);
        return Strict(m, m with { Menu = menu });
    }

    /// <summary>Puts a button (on the bar, in "Not shown" or already in the menu) at gap <paramref name="index"/> of the "more" button's
    /// menu (0 = first, Count = last; clamped). With no more button on the bar one is added at the end of the bottom-right group, as the
    /// menu is ignored without it. Pickers and the more button itself can't go in; the input comes back for those, for an unknown id and
    /// when nothing changes. Its style override goes, as in <see cref="Hide"/>.</summary>
    public static ToolbarModel MoveToMenu(ToolbarModel m, string id, int index)
    {
        if (m is null || Known(m, id) is not { } c || ToolbarPlan.Stays(c)) return m!;
        var lists = Lists(m);
        if (!PlaceMore(m, lists)) return m;
        foreach (var list in lists.Values) list.Remove(c);
        var menu = m.Menu.ToList();
        var at = Math.Clamp(index, 0, menu.Count);
        if (menu.FindIndex(x => x.Equals(c, StringComparison.OrdinalIgnoreCase)) is var own and >= 0)
        {
            if (own < at) at--;
            menu.RemoveAt(own);
        }
        menu.Insert(at, c);
        var overrides = Overrides(m);
        overrides.Remove(c);
        return Strict(m, With(m, lists) with { Menu = menu, Overrides = overrides });
    }

    /// <summary>A drag-and-drop onto the "more" menu card: <see cref="MoveToMenu"/>.</summary>
    public static ToolbarModel DropToMenu(ToolbarModel m, string id, int index) => MoveToMenu(m, id, index);

    /// <summary>The stored spelling of a button in the "more" menu, or null.</summary>
    public static string? InMenu(ToolbarModel m, string? id) =>
        m is null || id is null ? null : m.Menu.FirstOrDefault(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Makes sure the lists hold the more button, adding it at the end of bottom right; false when that group is full.</summary>
    private static bool PlaceMore(ToolbarModel m, Dictionary<Quadrant, List<string>> lists)
    {
        if (Placed(m, ToolbarModel.MoreId) is not null) return true;
        if (lists[Quadrant.BottomRight].Count >= ToolbarModel.MaxItems) return false;
        lists[Quadrant.BottomRight].Add(ToolbarModel.MoreId);
        return true;
    }

    /// <summary>The button rows' column spacing (PopupWindow.xaml), which the planner needs.</summary>
    private const double RowSpacing = 2;

    /// <summary>
    /// "Move what doesn't fit into the menu": runs the renderer's planner for both rows at the given panel widths (DIP) and moves the
    /// buttons it puts into the automatic overflow into the "more" menu, after the entries already there (the bottom row's first, each in
    /// bar order). With no more button placed one is added first (end of bottom right), as it takes room itself. When nothing would
    /// overflow the input comes back (and no more button is added).
    /// </summary>
    public static ToolbarModel FillMenuFromOverflow(ToolbarModel m, double topRoom, double bottomRoom)
    {
        if (m is null) return m!;
        var lists = Lists(m);
        if (!PlaceMore(m, lists)) return m;
        var planned = With(m, lists);
        var overflow = new[] { (Top: false, Room: bottomRoom), (Top: true, Room: topRoom) }
            .SelectMany(r => { var f = ToolbarPlan.PlanRow(planned, r.Top, r.Room, RowSpacing); return f.Dropped.Concat(f.RightDropped); })
            .Distinct().ToList();
        if (overflow.Count == 0) return m;
        foreach (var list in lists.Values) list.RemoveAll(x => overflow.Contains(x));
        var overrides = Overrides(m);
        foreach (var id in overflow) overrides.Remove(id);
        var menu = m.Menu.Concat(overflow.Where(id => !m.Menu.Contains(id))).ToList();
        return Strict(m, With(m, lists) with { Menu = menu, Overrides = overrides });
    }

    /// <summary>A drag-and-drop in Settings: the item (shown or not) goes to gap <paramref name="index"/> of
    /// <paramref name="q"/>. Never throws; a drop that changes nothing returns the input.</summary>
    public static ToolbarModel Drop(ToolbarModel m, string id, Quadrant q, int index) => MoveTo(m, id, q, index);

    /// <summary>The arrows: to the end of the neighbouring quadrant; nothing at the edge (e.g. Up from the top row).</summary>
    public static ToolbarModel ShiftTo(ToolbarModel m, string id, Shift direction)
    {
        if (m is null || Placed(m, id) is not { } at) return m!;
        var (column, top) = (Column(at.Quadrant), at.Quadrant is Quadrant.TopLeft or Quadrant.TopCenter or Quadrant.TopRight);
        Quadrant? target = direction switch
        {
            Shift.Left when column > 0 => At(column - 1, top),
            Shift.Right when column < 2 => At(column + 1, top),
            Shift.Up when !top => At(column, top: true),
            Shift.Down when top => At(column, top: false),
            _ => null,
        };
        return target is { } q ? MoveTo(m, at.Id, q, int.MaxValue) : m;
    }

    /// <summary>The left (0), centre (1) or right (2) column of a quadrant.</summary>
    internal static int Column(Quadrant q) =>
        q is Quadrant.BottomLeft or Quadrant.TopLeft ? 0 : q is Quadrant.BottomCenter or Quadrant.TopCenter ? 1 : 2;

    internal static Quadrant At(int column, bool top) => (top, column) switch
    {
        (false, 0) => Quadrant.BottomLeft, (false, 1) => Quadrant.BottomCenter, (false, _) => Quadrant.BottomRight,
        (true, 0) => Quadrant.TopLeft, (true, 1) => Quadrant.TopCenter, _ => Quadrant.TopRight,
    };

    /// <summary>Takes an item off the bar (to "Not shown"); its style override goes with it, as <see cref="ToolbarModel.Resolve"/>
    /// keeps overrides only for shown items (a picker's span stays: see <see cref="SetPickerSpan"/>).</summary>
    public static ToolbarModel Hide(ToolbarModel m, string id)
    {
        if (m is null) return m!;
        if (Placed(m, id) is not { } at)
            return InMenu(m, id) is { } entry ? Strict(m, m with { Menu = [.. m.Menu.Where(x => x != entry)] }) : m;
        var lists = Lists(m);
        lists[at.Quadrant].Remove(at.Id);
        var overrides = Overrides(m);
        overrides.Remove(at.Id);
        // The menu is ignored without a more button, so its entries go back to "Not shown" with it.
        var menu = at.Id == ToolbarModel.MoreId ? [] : m.Menu;
        return Strict(m, With(m, lists) with { Overrides = overrides, Menu = menu });
    }

    /// <summary>Puts a hidden item at the end of <paramref name="q"/>. An item already shown stays where it is (moving is
    /// <see cref="MoveTo"/>).</summary>
    public static ToolbarModel Show(ToolbarModel m, string id, Quadrant q) =>
        m is null || Known(m, id) is not { } c || Placed(m, c) is not null ? m! : MoveTo(m, c, q, int.MaxValue); // a menu entry comes onto the bar here

    /// <summary>The items not on the bar: built-ins in <see cref="ToolbarModel.BuiltIn"/> order, then custom buttons in theirs.</summary>
    public static IReadOnlyList<string> Hidden(ToolbarModel m)
    {
        if (m is null) return [];
        var placed = Order.SelectMany(m.Items).Concat(m.Menu).ToHashSet(StringComparer.OrdinalIgnoreCase);
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
        return Strict(m, With(m, lists) with
        {
            Overrides = overrides, Buttons = [.. m.Buttons.Where(b => b != button)], Menu = [.. m.Menu.Where(x => x != button.Id)],
        });
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

    /// <summary>The whole bar's size choice: every button gets <paramref name="size"/>, so the items' own sizes are cleared
    /// (their other override keys stay; an override left empty goes). Unchanged: the input.</summary>
    public static ToolbarModel SetBarSize(ToolbarModel m, ButtonSize size)
    {
        if (m is null || !Enum.IsDefined(size)) return m!;
        var overrides = Overrides(m);
        foreach (var (id, style) in overrides.ToList())
            if (style.Size is not null)
            {
                if (style with { Size = null } is var rest && rest == new ButtonStyle()) overrides.Remove(id);
                else overrides[id] = rest;
            }
        return Lenient(m, m with { Style = m.Style with { Size = size }, Overrides = overrides }, null);
    }

    /// <summary>Sets a picker's width in button widths (clamped to 1-8; shown or not: a hidden picker keeps it). A span equal to
    /// the picker's default is stored as no entry. Anything that is not a picker, or no change: the input.</summary>
    public static ToolbarModel SetPickerSpan(ToolbarModel m, string? id, int span)
    {
        if (m is null || id is null || ToolbarModel.Pickers.FirstOrDefault(p => p.Equals(id, StringComparison.OrdinalIgnoreCase)) is not { } picker) return m!;
        span = Math.Clamp(span, ToolbarModel.MinSpan, ToolbarModel.MaxSpan);
        if (span == m.EffectiveSpan(picker)) return m;
        var spans = new Dictionary<string, int>(m.PickerSpans) { [picker] = span };
        // Back at the default: Resolve drops the entry, so the candidate is read back as it would be written.
        return Lenient(m, m with { PickerSpans = spans }, null);
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
        && r.Menu.SequenceEqual(candidate.Menu)
        && r.Buttons.Select(b => b.Id).SequenceEqual(candidate.Buttons.Select(b => b.Id))
        && (button is null || r.Buttons.Any(b => b.Id == button))
        && !r.Equals(m)
            ? r with { Warnings = [] }
            : m;
}
