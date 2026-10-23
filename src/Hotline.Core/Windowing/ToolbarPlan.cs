using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>The size of an icon button in DIPs, and its glyph size.</summary>
public sealed record ButtonMetrics(double Width, double Height, double FontSize);

/// <summary>One button row: its left, centre and right groups, left to right. A picker is an entry like a button.</summary>
public sealed record RowPlan(IReadOnlyList<string> Left, IReadOnlyList<string> Center, IReadOnlyList<string> Right)
{
    /// <summary>The row holds the placed "more" button: it is the row's menu button, and its menu takes the row's overflow.</summary>
    public bool HasMore => Left.Concat(Center).Concat(Right).Contains(ToolbarModel.MoreId);

    public bool Equals(RowPlan? other) =>
        other is not null && Left.SequenceEqual(other.Left) && Center.SequenceEqual(other.Center) && Right.SequenceEqual(other.Right);
    public override int GetHashCode() => HashCode.Combine(Left.Count, Center.Count, Right.Count);

    /// <summary>What can move into the "⋯" menu when the row is too narrow: the left group, then the centre (dropped from its end first).
    /// The right group gives up buttons only after these (from its left end, see <see cref="ToolbarLayout.Fit"/>).</summary>
    public IReadOnlyList<string> Droppable => [.. Left, .. Center];
}

/// <summary>How a row fits: left buttons shown, whether the overflow button is needed, whether the pickers kept their
/// minimum room (false: they shrink and hide in what is left, see <see cref="ToolbarLayout.Compute"/>), the width of
/// everything in the row but the pickers themselves (all columns' gaps included, one gap kept for the pickers; for Compute's fixedWidth), and how many
/// right-group buttons moved into the menu (from the right group's left end; 0 unless the right group alone is too wide).</summary>
public sealed record RowFit(int LeftShown, bool Overflow, bool PickersKept, double FixedWidth, int RightDropped = 0);

/// <summary>
/// The renderer's decisions for a resolved <see cref="ToolbarModel"/>, kept pure so they're tested: what each row holds,
/// the button sizes, which left buttons go into the overflow menu, and the This PC line.
/// </summary>
public static class ToolbarPlan
{
    public static bool IsPicker(string id) => ToolbarModel.Pickers.Contains(id);

    /// <summary>The items that never move into the menu: the pickers (they shrink and hide) and the "more" button (the menu itself).</summary>
    public static bool Stays(string id) => IsPicker(id) || id == ToolbarModel.MoreId;

    /// <summary>The buttons the "more" menu lists from the model's own <see cref="ToolbarModel.Menu"/>: known buttons only, once each, none on
    /// the bar, and none at all while no more button is placed (a model built in memory may hold what <see cref="ToolbarModel.Resolve"/> drops).</summary>
    public static IReadOnlyList<string> MenuItems(ToolbarModel model)
    {
        var placed = Placement(model).Values.SelectMany(l => l).ToHashSet();
        if (!placed.Contains(ToolbarModel.MoreId)) return [];
        return [.. model.Menu.Where(id => Known(model, id) && !Stays(id) && !placed.Contains(id)).Distinct()];
    }

    /// <summary>Explicit widths and heights, and every size of a custom button, stay within these (DIP).</summary>
    public const int MinDip = 20, MaxDip = 80;
    /// <summary>A custom button's largest glyph: its icon is one of a few plain ones, drawn no bigger than an 80-DIP button holds.</summary>
    public const int MaxCustomFont = 22;

    /// <summary>
    /// The bar is a grid of cells: an item's size from <see cref="Metrics(ButtonStyle, bool)"/> is its OUTER cell, its padding inside. Cells touch
    /// (no column gap), so doubling or halving a button still lines up with the rest and a row is the sum of its cells. The visible
    /// button is the cell less <see cref="CellInset"/> on each side (<see cref="Inner"/>), so touching items show a 2 DIP gap.
    /// </summary>
    public const double CellGap = 0, CellInset = 1;

    /// <summary>The visible size of an item in a cell of <paramref name="cell"/> DIPs.</summary>
    public static double Inner(double cell) => Math.Max(0, cell - 2 * CellInset);

    /// <summary>The panel's padding on every side (DIP); PopupWindow.xaml's Root Padding is this value.</summary>
    public const double PanelPadding = 10;

    /// <summary>The panel width whose bar room is <paramref name="buttons"/> normal buttons across: the buttons plus the padding on both sides
    /// (11 buttons = 460, about a quarter of a 1920-wide screen at 100%).</summary>
    public static double WidthForButtons(int buttons) => buttons * BaseWidth + 2 * PanelPadding;

    /// <summary>The one base size every preset is a multiple of (DIP; the glyph in points): change these to rescale them all.</summary>
    public const double BaseWidth = 40, BaseHeight = 36, BaseFont = 16;

    /// <summary>
    /// The size presets, from the base (<see cref="BaseWidth"/> × <see cref="BaseHeight"/>, glyph <see cref="BaseFont"/>):
    /// compact is half of it (never under 20×20), normal the base, wide twice it; the glyph is 0.8, 1 and 1.2 times the
    /// base's. An item's width/height wins over its preset but stays within 20-80. A custom button (<paramref name="isCustom"/>)
    /// never goes past 80×80 and a glyph of 22, whatever the bar's or its own size (a safety net should the base grow).
    /// The renderer and every fit measure through here, so what is drawn is what was fitted.
    /// </summary>
    public static ButtonMetrics Metrics(ButtonStyle style, bool isCustom = false) =>
        Metrics(style, isCustom, BaseWidth, BaseHeight, BaseFont);

    /// <summary><see cref="Metrics(ButtonStyle, bool)"/> from another base (tests).</summary>
    public static ButtonMetrics Metrics(ButtonStyle style, bool isCustom, double baseWidth, double baseHeight, double baseFont)
    {
        var (scale, glyph) = style.Size switch
        {
            ButtonSize.Compact => (0.5, 0.8),
            ButtonSize.Wide => (2.0, 1.2),
            _ => (1.0, 1.0),
        };
        var width = Math.Max(MinDip, Math.Round(baseWidth * scale));
        var height = Math.Max(MinDip, Math.Round(baseHeight * scale));
        var font = Math.Round(baseFont * glyph);
        // Resolve already clamps a file's width and height; a model built in memory (Settings, the self-test) may not be.
        if (style.Width is { } w) width = Math.Clamp(w, MinDip, MaxDip);
        if (style.Height is { } h) height = Math.Clamp(h, MinDip, MaxDip);
        if (isCustom) (width, height, font) = (Math.Clamp(width, MinDip, MaxDip), Math.Clamp(height, MinDip, MaxDip), Math.Min(font, MaxCustomFont));
        return new ButtonMetrics(width, height, font);
    }

    /// <summary>The size of a placed item: its effective style, as a custom button when it is one.</summary>
    public static ButtonMetrics Metrics(ToolbarModel model, string id) =>
        Metrics(model.EffectiveStyle(id), model.Buttons.Any(b => b.Id == id));

    /// <summary>
    /// A row's groups in order. Resolve gives only valid models, but Settings and the self-test build them in memory, so
    /// this holds anyway: an id that is neither built-in nor a defined custom button is left out (see <see cref="Unknown"/>),
    /// and an id listed twice is drawn where it is first (quadrant order); a picker is placed like a button.
    /// </summary>
    public static RowPlan Row(ToolbarModel model, bool top)
    {
        var placed = Placement(model);
        return top
            ? new RowPlan(placed[Quadrant.TopLeft], placed[Quadrant.TopCenter], placed[Quadrant.TopRight])
            : new RowPlan(placed[Quadrant.BottomLeft], placed[Quadrant.BottomCenter], placed[Quadrant.BottomRight]);
    }

    /// <summary>Placed ids that are neither built-in nor defined under "buttons" (never drawn).</summary>
    public static IReadOnlyList<string> Unknown(ToolbarModel model) =>
        [.. Enum.GetValues<Quadrant>().SelectMany(model.Items).Where(id => !Known(model, id)).Distinct()];

    /// <summary>The pickers on the bar, in the order listed (bottom row first, then the top row).</summary>
    public static IReadOnlyList<string> PickerOrder(ToolbarModel model) =>
        [.. Enum.GetValues<Quadrant>().SelectMany(model.Items).Where(IsPicker).Distinct()];

    private static bool Known(ToolbarModel model, string id) =>
        ToolbarModel.BuiltIn.Contains(id) || model.Buttons.Any(b => b.Id == id);

    private static Dictionary<Quadrant, List<string>> Placement(ToolbarModel model)
    {
        var placed = Enum.GetValues<Quadrant>().ToDictionary(q => q, _ => new List<string>());
        var seen = new HashSet<string>();
        foreach (var q in Enum.GetValues<Quadrant>())
            foreach (var id in model.Items(q))
            {
                if (!Known(model, id) || !seen.Add(id)) continue;
                placed[q].Add(id);
            }
        return placed;
    }

    /// <summary>A left group with <paramref name="leftShown"/> of its buttons kept (from the start); the pickers always stay
    /// (they shrink and hide instead, see <see cref="ToolbarLayout.Compute"/>).</summary>
    public static (IReadOnlyList<string> Shown, IReadOnlyList<string> Dropped) Split(IReadOnlyList<string> left, int leftShown)
    {
        var (shown, dropped) = (new List<string>(), new List<string>());
        var buttons = 0;
        foreach (var id in left)
            (Stays(id) || buttons++ < leftShown ? shown : dropped).Add(id);
        return (shown, dropped);
    }

    /// <summary>A right group with its first <paramref name="rightDropped"/> buttons moved into the menu (the pickers always stay).</summary>
    public static (IReadOnlyList<string> Shown, IReadOnlyList<string> Dropped) SplitRight(IReadOnlyList<string> right, int rightDropped)
    {
        var (shown, dropped) = (new List<string>(), new List<string>());
        var buttons = 0;
        foreach (var id in right)
            (Stays(id) || buttons++ >= rightDropped ? shown : dropped).Add(id);
        return (shown, dropped);
    }

    /// <summary>
    /// <see cref="ToolbarLayout.Fit"/> for one Grid row <c>[left…] [⋯?] [*] [centre… [*]]? [right…]</c> of <paramref name="room"/> DIPs, with
    /// the row's pickers (wherever they sit in it) when <paramref name="hasPickers"/>; the widths are the buttons', pickers
    /// excluded. The pickers count as one extra column here (one gap kept for them; <see cref="PlanRow"/> adds the others when
    /// it knows how many stay). Fit counts neither the flexible column's gap nor, without a reserve, that column's gap: both
    /// are taken off the room here. Fit's overshoot contract: when the overflow button would stick out while the pickers keep
    /// <paramref name="pickersMin"/>, the row is fitted again without that room and the pickers shrink and hide in what
    /// is left. Only an overshoot that remains then (not even one right button and the overflow button fit, a panel narrower
    /// than about two buttons) is accepted. The pickers keep their minimum room before the right group gives up buttons.
    /// </summary>
    public static RowFit FitRow(double room, double spacing, IReadOnlyList<double> leftWidths, IReadOnlyList<double> rightWidths,
        bool hasPickers, double pickersMin, double overflowWidth, bool hasCenter = false, bool alwaysOverflow = false)
    {
        room -= hasCenter ? spacing : 0; // a centre group has a second flexible column (counted whether or not it keeps a button)
        if (hasPickers && pickersMin > 0)
        {
            var keptRoom = room - spacing; // the flexible column (Fit already counts the gap before the reserve)
            var (kept, fits) = ToolbarLayout.FitChecked(keptRoom, spacing, leftWidths, rightWidths, pickersMin, overflowWidth, alwaysOverflow);
            if (fits) return Result(kept, true);
        }
        var fit = ToolbarLayout.Fit(room - spacing * (hasPickers ? 2 : 1), spacing, leftWidths, rightWidths, 0, overflowWidth, alwaysOverflow);
        return Result(fit, false);

        RowFit Result(FitResult f, bool kept)
        {
            var buttons = leftWidths.Take(f.LeftShown).Concat(rightWidths.Skip(f.RightDropped)).ToList();
            var columns = buttons.Count + (f.Overflow ? 1 : 0) + (hasPickers ? 1 : 0) + (hasCenter ? 2 : 1); // + the flexible column(s)
            return new RowFit(f.LeftShown, f.Overflow, kept, buttons.Sum() + (f.Overflow ? overflowWidth : 0) + spacing * (columns - 1), f.RightDropped);
        }
    }

    // ---- the pickers' widths ----

    /// <summary>The pickers in the order they give way when the row is short: effort first, model last.</summary>
    public static readonly IReadOnlyList<string> PickerHideOrder = ["effort", "provider", "model"];

    /// <summary>The pickers' unit: one standard button width, whatever the bar's size (a compact bar keeps readable drop-downs,
    /// a wide bar doesn't make them huge).</summary>
    public const double PickerUnit = BaseWidth;

    /// <summary>The narrowest each picker gets before it hides (DIP): room for its arrow and a readable start of its text.
    /// <see cref="PickerSpecs"/> caps it at the picker's own width, so a span of 1 is both its preferred and its minimum.</summary>
    public static double PickerFloor(string id) => id switch { "effort" => 56, "model" => 84, "provider" => 72, _ => PickerUnit };

    /// <summary>
    /// The three pickers as <see cref="ToolbarLayout.Compute"/> takes them. A picker is its span (button widths) times the
    /// <see cref="PickerUnit"/> wide; short of room it shrinks to its <see cref="PickerFloor"/> (never past its own width) and
    /// then hides, and spare room never grows it past its span. Only the ids in <paramref name="available"/> take part (the
    /// others have nothing to show or are not on the bar).
    /// </summary>
    public static IReadOnlyList<PickerSpec> PickerSpecs(ToolbarModel model, IReadOnlyCollection<string> available) =>
        [.. ToolbarModel.Pickers.Select(id =>
        {
            var width = model.EffectiveSpan(id) * PickerUnit;
            return new PickerSpec(id, width, Math.Min(width, PickerFloor(id)), available.Contains(id), Max: width);
        })];

    /// <summary>How one row is laid out at a width: see <see cref="PlanRow"/>.</summary>
    /// <param name="Plan">The row's groups.</param>
    /// <param name="Shown">The left and centre ids that stay in the row, pickers included, in order.</param>
    /// <param name="Dropped">The left and centre buttons in the "⋯" menu, in order (never a picker).</param>
    /// <param name="PickerWidths">The width of each picker in this row, 0 = hidden; empty when the row has none.</param>
    /// <param name="ButtonWidths">The width of each button kept in the row (the right group and the shown left ones).</param>
    /// <param name="Fit">How the buttons fit (shown count, "⋯", whether the pickers kept their minimum, the fixed width).</param>
    /// <param name="Used">The width of the whole row: the fixed part, the pickers and the gaps between them (each shown picker is a column).</param>
    /// <param name="RightShown">The right group's ids that stay in the row (pickers included), in order.</param>
    /// <param name="RightDropped">The right group's buttons in the "⋯" menu, in order; they follow <paramref name="Dropped"/> there.</param>
    /// <param name="UserMenu">The buttons the user put in the "more" button's menu (<see cref="MenuItems"/>) when this row holds it; they come first in the menu.</param>
    public sealed record RowLayout(
        RowPlan Plan, IReadOnlyList<string> Shown, IReadOnlyList<string> Dropped, IReadOnlyDictionary<string, double> PickerWidths,
        IReadOnlyDictionary<string, double> ButtonWidths, RowFit Fit, double Used,
        IReadOnlyList<string> RightShown, IReadOnlyList<string> RightDropped, IReadOnlyList<string> UserMenu)
    {
        /// <summary>Everything in the row's menu: the user's menu entries (a row with the "more" button), then the dropped buttons in bar
        /// order: left, centre, then the right group.</summary>
        public IReadOnlyList<string> Menu => [.. UserMenu, .. Dropped, .. RightDropped];
    }

    /// <summary>
    /// Everything the renderer decides for one row in <paramref name="room"/> DIPs, in one pure call (the renderer, ProviderBar and
    /// the tests all go through here): which left/centre buttons stay or move into "⋯" (<see cref="FitRow"/>, with the pickers'
    /// minimum kept back while they can have it; the right group gives up buttons only when it alone is too wide) and the pickers' widths from what is left (<see cref="ToolbarLayout.Compute"/>).
    /// <paramref name="spacing"/> is the row's column spacing (between any two items, pickers too);
    /// <paramref name="availablePickers"/> are the pickers with something to show (null = all of them). Each picker of the row
    /// is its own elastic column in the group it is listed in; they all shrink together and hide in <see cref="PickerHideOrder"/>.
    /// </summary>
    public static RowLayout PlanRow(ToolbarModel model, bool top, double room, double spacing,
        IReadOnlyCollection<string>? availablePickers = null)
    {
        var plan = Row(model, top);
        var rowPickers = plan.Left.Concat(plan.Center).Concat(plan.Right).Where(IsPicker).ToList();
        var hasPickers = rowPickers.Count > 0;
        var leftIds = plan.Droppable.Where(id => !Stays(id)).ToList();
        var rightIds = plan.Right.Where(id => !Stays(id)).ToList();
        // A row with the placed "more" button: that button is the row's menu button (always drawn, sized by its own style).
        var hasMore = plan.HasMore;
        var overflowWidth = hasMore ? Metrics(model, ToolbarModel.MoreId).Width : Metrics(model.Style).Width;

        // Only this row's pickers: the other row's are planned by its own call.
        var specs = PickerSpecs(model, [.. (availablePickers ?? ToolbarModel.Pickers).Where(rowPickers.Contains)]).Where(p => rowPickers.Contains(p.Name)).ToList();
        var shownSpecs = specs.Where(p => p.Available).ToList();
        // The pickers at their minimum (as Compute counts their gaps): the buttons move into "⋯" before that room is taken.
        var pickersMin = shownSpecs.Count == 0 ? 0 : shownSpecs.Sum(p => p.Min) + spacing * shownSpecs.Count;
        var fit = FitRow(room, spacing, [.. leftIds.Select(id => Metrics(model, id).Width)], [.. rightIds.Select(id => Metrics(model, id).Width)],
            hasPickers, pickersMin, overflowWidth, hasCenter: plan.Center.Count > 0, alwaysOverflow: hasMore);
        var (shown, dropped) = Split(plan.Droppable, fit.LeftShown);
        var (rightShown, rightDropped) = SplitRight(plan.Right, fit.RightDropped);

        var widths = hasPickers ? ToolbarLayout.Compute(room, fit.FixedWidth, spacing, specs, PickerHideOrder) : new Dictionary<string, double>();
        var visible = widths.Values.Count(w => w > 0);
        var buttons = new Dictionary<string, double>();
        foreach (var id in leftIds.Take(fit.LeftShown).Concat(rightIds.Skip(fit.RightDropped))) buttons[id] = Metrics(model, id).Width;
        if (hasMore) buttons[ToolbarModel.MoreId] = overflowWidth;
        var used = fit.FixedWidth + widths.Values.Sum() + spacing * Math.Max(0, visible - 1);
        return new RowLayout(plan, shown, dropped, widths, buttons, fit, used, rightShown, rightDropped, hasMore ? MenuItems(model) : []);
    }

    private const int ReportedWarnings = 3;

    /// <summary>This PC's "Button rows" line: fine, or the read error, or the first warnings; plus a note when no Settings
    /// button is placed (Settings then opens only from the tray icon).</summary>
    public static (bool Ok, string Detail) Report(ToolbarModel model, string? error)
    {
        if (error is not null) return (false, error);
        var ok = model.Warnings.Count == 0;
        var detail = ok ? Strings.Pc_ToolbarOk
            : L.F(Strings.Pc_ToolbarWarnings, model.Warnings.Count, string.Join("; ", model.Warnings.Take(ReportedWarnings)));
        if (!Enum.GetValues<Quadrant>().Any(q => model.Items(q).Contains("settings"))) detail += " " + Strings.Pc_ToolbarNoSettings;
        return (ok, detail);
    }
}
