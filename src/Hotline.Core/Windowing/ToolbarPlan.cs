using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>The size of an icon button in DIPs, and its glyph size.</summary>
public sealed record ButtonMetrics(double Width, double Height, double FontSize);

/// <summary>One button row: its left, centre and right groups, left to right. The pickers are one entry (the first picker id).</summary>
public sealed record RowPlan(IReadOnlyList<string> Left, IReadOnlyList<string> Center, IReadOnlyList<string> Right)
{
    public bool Equals(RowPlan? other) =>
        other is not null && Left.SequenceEqual(other.Left) && Center.SequenceEqual(other.Center) && Right.SequenceEqual(other.Right);
    public override int GetHashCode() => HashCode.Combine(Left.Count, Center.Count, Right.Count);

    /// <summary>What can move into the "⋯" menu when the row is too narrow: the left group, then the centre (dropped from its end first).</summary>
    public IReadOnlyList<string> Droppable => [.. Left, .. Center];
}

/// <summary>How a row fits: left buttons shown, whether the overflow button is needed, whether the pickers kept their
/// minimum room (false: they shrink and hide in what is left, see <see cref="ToolbarLayout.Compute"/>), and the width of
/// everything in the row but the pickers themselves (all columns' gaps included; for Compute's fixedWidth).</summary>
public sealed record RowFit(int LeftShown, bool Overflow, bool PickersKept, double FixedWidth);

/// <summary>
/// The renderer's decisions for a resolved <see cref="ToolbarModel"/>, kept pure so they're tested: what each row holds,
/// the button sizes, which left buttons go into the overflow menu, and the This PC line.
/// </summary>
public static class ToolbarPlan
{
    public static bool IsPicker(string id) => ToolbarModel.Pickers.Contains(id);

    /// <summary>Explicit widths and heights, and every size of a custom button, stay within these (DIP).</summary>
    public const int MinDip = 20, MaxDip = 80;
    /// <summary>A custom button's largest glyph: its icon is one of a few plain ones, drawn no bigger than an 80-DIP button holds.</summary>
    public const int MaxCustomFont = 22;

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
    /// an id listed twice is drawn where it is first (quadrant order), and the pickers are one block, drawn where the
    /// first picker id is (any later picker id is in <see cref="PickerOrder"/> only).
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

    /// <summary>The picker boxes in the order listed (bottom left, bottom right, top left, top right).</summary>
    public static IReadOnlyList<string> PickerOrder(ToolbarModel model) =>
        [.. Enum.GetValues<Quadrant>().SelectMany(model.Items).Where(IsPicker).Distinct()];

    private static bool Known(ToolbarModel model, string id) =>
        ToolbarModel.BuiltIn.Contains(id) || model.Buttons.Any(b => b.Id == id);

    private static Dictionary<Quadrant, List<string>> Placement(ToolbarModel model)
    {
        var placed = Enum.GetValues<Quadrant>().ToDictionary(q => q, _ => new List<string>());
        var seen = new HashSet<string>();
        var pickersPlaced = false;
        foreach (var q in Enum.GetValues<Quadrant>())
            foreach (var id in model.Items(q))
            {
                if (!Known(model, id) || !seen.Add(id)) continue;
                if (IsPicker(id)) { if (pickersPlaced) continue; pickersPlaced = true; }
                placed[q].Add(id);
            }
        return placed;
    }

    /// <summary>A left group with <paramref name="leftShown"/> of its buttons kept (from the start); the pickers always stay.</summary>
    public static (IReadOnlyList<string> Shown, IReadOnlyList<string> Dropped) Split(IReadOnlyList<string> left, int leftShown)
    {
        var (shown, dropped) = (new List<string>(), new List<string>());
        var buttons = 0;
        foreach (var id in left)
            (IsPicker(id) || buttons++ < leftShown ? shown : dropped).Add(id);
        return (shown, dropped);
    }

    /// <summary>
    /// <see cref="ToolbarLayout.Fit"/> for one Grid row <c>[left…] [⋯?] [*] [centre… [*]]? [right…]</c> of <paramref name="room"/> DIPs, with
    /// the pickers' column in it when <paramref name="hasPickers"/> (widths are the buttons', pickers excluded). Fit counts
    /// neither the flexible column's gap nor, without a reserve, the pickers' column's gap: both are taken off the room
    /// here. Fit's overshoot contract: when the overflow button would stick out while the pickers keep
    /// <paramref name="pickersMin"/>, the row is fitted again without that room and the pickers shrink and hide in what
    /// is left. Only an overshoot that remains then (the right group alone doesn't fit) is accepted.
    /// </summary>
    public static RowFit FitRow(double room, double spacing, IReadOnlyList<double> leftWidths, IReadOnlyList<double> rightWidths,
        bool hasPickers, double pickersMin, double overflowWidth, bool hasCenter = false)
    {
        room -= hasCenter ? spacing : 0; // a centre group has a second flexible column (counted whether or not it keeps a button)
        if (hasPickers && pickersMin > 0)
        {
            var keptRoom = room - spacing; // the flexible column (Fit already counts the gap before the reserve)
            var kept = ToolbarLayout.Fit(keptRoom, spacing, leftWidths, rightWidths, pickersMin, overflowWidth);
            if (!(kept is { LeftShown: 0, Overflow: true }) || Needed(spacing, rightWidths, pickersMin, overflowWidth) <= keptRoom)
                return Result(kept, true);
        }
        var fit = ToolbarLayout.Fit(room - spacing * (hasPickers ? 2 : 1), spacing, leftWidths, rightWidths, 0, overflowWidth);
        return Result(fit, false);

        RowFit Result(FitResult f, bool kept)
        {
            var buttons = leftWidths.Take(f.LeftShown).Concat(rightWidths).ToList();
            var columns = buttons.Count + (f.Overflow ? 1 : 0) + (hasPickers ? 1 : 0) + (hasCenter ? 2 : 1); // + the flexible column(s)
            return new RowFit(f.LeftShown, f.Overflow, kept, buttons.Sum() + (f.Overflow ? overflowWidth : 0) + spacing * (columns - 1));
        }
    }

    /// <summary>Fit's width for (0 left shown, overflow button): the right group, the button, the pickers and their gaps.</summary>
    private static double Needed(double spacing, IReadOnlyList<double> right, double reserved, double overflowWidth) =>
        right.Sum() + overflowWidth + reserved + spacing * (right.Count + 1);

    // ---- the pickers' widths ----

    /// <summary>No picker is narrower than this (DIP): a drop-down needs room for its arrow and a letter or two, whatever the bar's size.</summary>
    public const double MinPickerWidth = 28;

    /// <summary>The pickers in the order they give way when the row is short: effort first, model last.</summary>
    public static readonly IReadOnlyList<string> PickerHideOrder = ["effort", "provider", "model"];

    /// <summary>The pickers' unit: the bar's normal button width, so they grow and shrink with the bar's size like the buttons.</summary>
    public static double PickerUnit(ToolbarModel model) => Metrics(model.Style).Width;

    /// <summary>
    /// The three pickers as <see cref="ToolbarLayout.Compute"/> takes them. A picker is its span (button widths) times the
    /// <see cref="PickerUnit"/> wide, never under <see cref="MinPickerWidth"/>; short of room it shrinks to one unit (also
    /// never under that floor) and then hides, and spare room never grows it past its span. Only the ids in
    /// <paramref name="available"/> take part (the others have nothing to show or are not on the bar).
    /// </summary>
    public static IReadOnlyList<PickerSpec> PickerSpecs(ToolbarModel model, IReadOnlyCollection<string> available)
    {
        var unit = PickerUnit(model);
        var min = Math.Max(unit, MinPickerWidth);
        return [.. ToolbarModel.Pickers.Select(id =>
        {
            var width = Math.Max(model.EffectiveSpan(id) * unit, MinPickerWidth);
            return new PickerSpec(id, width, min, available.Contains(id), Max: width);
        })];
    }

    /// <summary>How one row is laid out at a width: see <see cref="PlanRow"/>.</summary>
    /// <param name="Plan">The row's groups.</param>
    /// <param name="Shown">The left and centre ids that stay in the row, pickers included, in order.</param>
    /// <param name="Dropped">The left and centre buttons in the "⋯" menu, in order (never a picker).</param>
    /// <param name="PickerWidths">Each picker's width, 0 = hidden; empty when the row has no pickers.</param>
    /// <param name="ButtonWidths">The width of each button kept in the row (the right group and the shown left ones).</param>
    /// <param name="Fit">How the buttons fit (shown count, "⋯", whether the pickers kept their minimum, the fixed width).</param>
    /// <param name="Used">The width of the whole row: the fixed part, the pickers and the gaps between them.</param>
    public sealed record RowLayout(
        RowPlan Plan, IReadOnlyList<string> Shown, IReadOnlyList<string> Dropped, IReadOnlyDictionary<string, double> PickerWidths,
        IReadOnlyDictionary<string, double> ButtonWidths, RowFit Fit, double Used);

    /// <summary>
    /// Everything the renderer decides for one row in <paramref name="room"/> DIPs, in one pure call (the renderer, ProviderBar and
    /// the tests all go through here): which left/centre buttons stay or move into "⋯" (<see cref="FitRow"/>, with the pickers'
    /// minimum kept back while they can have it) and the pickers' widths from what is left (<see cref="ToolbarLayout.Compute"/>).
    /// <paramref name="spacing"/> is the row's column spacing, <paramref name="pickerSpacing"/> the one inside the pickers' block;
    /// <paramref name="availablePickers"/> are the pickers with something to show (null = all of them).
    /// </summary>
    public static RowLayout PlanRow(ToolbarModel model, bool top, double room, double spacing, double pickerSpacing,
        IReadOnlyCollection<string>? availablePickers = null)
    {
        var plan = Row(model, top);
        var hasPickers = plan.Droppable.Concat(plan.Right).Any(IsPicker);
        var leftIds = plan.Droppable.Where(id => !IsPicker(id)).ToList();
        var rightIds = plan.Right.Where(id => !IsPicker(id)).ToList();
        var overflowWidth = Metrics(model.Style).Width;

        var onBar = PickerOrder(model);
        var specs = hasPickers ? PickerSpecs(model, [.. (availablePickers ?? ToolbarModel.Pickers).Where(onBar.Contains)]) : [];
        var shownSpecs = specs.Where(p => p.Available).ToList();
        // The pickers at their minimum (as Compute counts their gaps): the buttons move into "⋯" before that room is taken.
        var pickersMin = shownSpecs.Count == 0 ? 0 : shownSpecs.Sum(p => p.Min) + pickerSpacing * shownSpecs.Count;
        var fit = FitRow(room, spacing, [.. leftIds.Select(id => Metrics(model, id).Width)], [.. rightIds.Select(id => Metrics(model, id).Width)],
            hasPickers, pickersMin, overflowWidth, hasCenter: plan.Center.Count > 0);
        var (shown, dropped) = Split(plan.Droppable, fit.LeftShown);

        var widths = hasPickers ? ToolbarLayout.Compute(room, fit.FixedWidth, pickerSpacing, specs, PickerHideOrder) : new Dictionary<string, double>();
        var visible = widths.Values.Count(w => w > 0);
        var buttons = new Dictionary<string, double>();
        foreach (var id in leftIds.Take(fit.LeftShown).Concat(rightIds)) buttons[id] = Metrics(model, id).Width;
        var used = fit.FixedWidth + widths.Values.Sum() + pickerSpacing * Math.Max(0, visible - 1);
        return new RowLayout(plan, shown, dropped, widths, buttons, fit, used);
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
