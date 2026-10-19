using Hotline.Core.Text.Localization;

namespace Hotline.Core.Windowing;

/// <summary>The size of an icon button in DIPs, and its glyph size.</summary>
public sealed record ButtonMetrics(double Width, double Height, double FontSize);

/// <summary>One button row: its left and right groups, left to right. The pickers are one entry (the first picker id).</summary>
public sealed record RowPlan(IReadOnlyList<string> Left, IReadOnlyList<string> Right)
{
    public bool Equals(RowPlan? other) => other is not null && Left.SequenceEqual(other.Left) && Right.SequenceEqual(other.Right);
    public override int GetHashCode() => HashCode.Combine(Left.Count, Right.Count);
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

    /// <summary>The size presets (compact 30×28, normal 36×32, wide 46×32); an item's width/height wins over its preset.</summary>
    public static ButtonMetrics Metrics(ButtonStyle style)
    {
        var (width, height, font) = style.Size switch
        {
            ButtonSize.Compact => (30, 28, 13),
            ButtonSize.Wide => (46, 32, 15),
            _ => (36, 32, 15),
        };
        return new ButtonMetrics(style.Width ?? width, style.Height ?? height, font);
    }

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
            ? new RowPlan(placed[Quadrant.TopLeft], placed[Quadrant.TopRight])
            : new RowPlan(placed[Quadrant.BottomLeft], placed[Quadrant.BottomRight]);
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
    /// <see cref="ToolbarLayout.Fit"/> for one Grid row <c>[left…] [⋯?] [*] [right…]</c> of <paramref name="room"/> DIPs, with
    /// the pickers' column in it when <paramref name="hasPickers"/> (widths are the buttons', pickers excluded). Fit counts
    /// neither the flexible column's gap nor, without a reserve, the pickers' column's gap: both are taken off the room
    /// here. Fit's overshoot contract: when the overflow button would stick out while the pickers keep
    /// <paramref name="pickersMin"/>, the row is fitted again without that room and the pickers shrink and hide in what
    /// is left. Only an overshoot that remains then (the right group alone doesn't fit) is accepted.
    /// </summary>
    public static RowFit FitRow(double room, double spacing, IReadOnlyList<double> leftWidths, IReadOnlyList<double> rightWidths,
        bool hasPickers, double pickersMin, double overflowWidth)
    {
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
            var columns = buttons.Count + (f.Overflow ? 1 : 0) + (hasPickers ? 1 : 0) + 1; // + the flexible column
            return new RowFit(f.LeftShown, f.Overflow, kept, buttons.Sum() + (f.Overflow ? overflowWidth : 0) + spacing * (columns - 1));
        }
    }

    /// <summary>Fit's width for (0 left shown, overflow button): the right group, the button, the pickers and their gaps.</summary>
    private static double Needed(double spacing, IReadOnlyList<double> right, double reserved, double overflowWidth) =>
        right.Sum() + overflowWidth + reserved + spacing * (right.Count + 1);

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
