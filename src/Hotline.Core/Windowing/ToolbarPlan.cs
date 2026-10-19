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

/// <summary>How a row fits: left buttons shown, whether the overflow button is needed, and whether the pickers kept their
/// minimum room (false: they shrink and hide in what is left, see <see cref="ToolbarLayout.Compute"/>).</summary>
public sealed record RowFit(int LeftShown, bool Overflow, bool PickersKept);

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
    /// A row's groups in order. The pickers are one block, drawn once: where the first picker id is (quadrant order, as
    /// the model resolves it); any later picker id is left out of the rows (it is in <see cref="PickerOrder"/>).
    /// </summary>
    public static RowPlan Row(ToolbarModel model, bool top)
    {
        var first = FirstPicker(model);
        List<string> Group(Quadrant q) =>
            [.. model.Items(q).Where((id, i) => !IsPicker(id) || (q, i) == first)];
        return top
            ? new RowPlan(Group(Quadrant.TopLeft), Group(Quadrant.TopRight))
            : new RowPlan(Group(Quadrant.BottomLeft), Group(Quadrant.BottomRight));
    }

    /// <summary>The picker boxes in the order listed (bottom left, bottom right, top left, top right).</summary>
    public static IReadOnlyList<string> PickerOrder(ToolbarModel model) =>
        [.. Enum.GetValues<Quadrant>().SelectMany(model.Items).Where(IsPicker)];

    private static (Quadrant, int)? FirstPicker(ToolbarModel model)
    {
        foreach (var q in Enum.GetValues<Quadrant>())
            if (model.Items(q).ToList().FindIndex(IsPicker) is var i and >= 0) return (q, i);
        return null;
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
    /// <see cref="ToolbarLayout.Fit"/> for one row (widths of its buttons, pickers excluded), with Fit's overshoot contract
    /// handled: when the overflow button would stick out while the pickers keep <paramref name="pickersMin"/>, the row is
    /// fitted again without that room and the pickers shrink and hide in what is left. Only an overshoot that remains then
    /// (the right group alone doesn't fit) is accepted. The Grid's star column is the caller's (take its gap off the room).
    /// </summary>
    public static RowFit FitRow(double room, double spacing, IReadOnlyList<double> leftWidths, IReadOnlyList<double> rightWidths,
        double pickersMin, double overflowWidth)
    {
        if (pickersMin > 0)
        {
            var kept = ToolbarLayout.Fit(room, spacing, leftWidths, rightWidths, pickersMin, overflowWidth);
            if (!(kept is { LeftShown: 0, Overflow: true }) || Needed(spacing, rightWidths, pickersMin, overflowWidth) <= room)
                return new RowFit(kept.LeftShown, kept.Overflow, true);
        }
        var fit = ToolbarLayout.Fit(room, spacing, leftWidths, rightWidths, 0, overflowWidth);
        return new RowFit(fit.LeftShown, fit.Overflow, false);
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
