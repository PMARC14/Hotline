namespace Hotline.Core.Windowing;

/// <summary>One bottom-bar picker: preferred and minimum width in DIPs, and whether it has anything to show.</summary>
/// <param name="Max">How wide it may grow into spare room (null = never past Preferred).</param>
public sealed record PickerSpec(string Name, double Preferred, double Min, bool Available = true, double? Max = null);

/// <summary>How many left-group buttons stay on the bar, and whether the "more" button is needed for the rest.</summary>
public sealed record FitResult(int LeftShown, bool Overflow);

/// <summary>
/// Lays out the bottom bar's pickers in the space left after the fixed buttons. The fixed buttons always fit
/// (pickers never push them off the edge). Pickers shrink evenly toward their minimum; if even that doesn't fit,
/// pickers are hidden one at a time in <paramref name="hideOrder"/> order.
/// Widths are explicit, so the bar never reflows when a picker's text changes.
/// </summary>
public static class ToolbarLayout
{
    /// <returns>Width per picker name; 0 = hidden.</returns>
    public static IReadOnlyDictionary<string, double> Compute(
        double barWidth, double fixedWidth, double spacing, IReadOnlyList<PickerSpec> pickers, IReadOnlyList<string> hideOrder)
    {
        var result = pickers.ToDictionary(p => p.Name, _ => 0.0);
        var visible = pickers.Where(p => p.Available).ToList();
        var space = Math.Max(0, barWidth - fixedWidth);

        foreach (var drop in hideOrder.Append(null))
        {
            var gaps = spacing * visible.Count;
            var room = space - gaps;
            var preferred = visible.Sum(p => p.Preferred);
            var minimum = visible.Sum(p => p.Min);
            if (visible.Count > 0 && room >= minimum)
            {
                if (room >= preferred)
                {
                    // Spare room goes to the pickers that may grow, in proportion to how much they can (never past Max).
                    var growth = visible.Sum(p => Math.Max(0, (p.Max ?? p.Preferred) - p.Preferred));
                    var share = growth <= 0 ? 0 : Math.Min(1, (room - preferred) / growth);
                    foreach (var p in visible) result[p.Name] = Math.Floor(p.Preferred + Math.Max(0, (p.Max ?? p.Preferred) - p.Preferred) * share);
                }
                else
                {
                    // Shrink each picker by the same share of its (preferred - min) slack.
                    var t = (room - minimum) / Math.Max(1e-9, preferred - minimum);
                    foreach (var p in visible) result[p.Name] = Math.Floor(p.Min + (p.Preferred - p.Min) * t);
                }
                return result;
            }
            if (drop is null) break;
            visible.RemoveAll(p => p.Name == drop);
        }
        return result; // nothing fits: all hidden
    }

    /// <summary>
    /// Decides how many left-group buttons fit in <paramref name="room"/>. The right group is always kept whole; left
    /// buttons are dropped from the end (behind an overflow button) until everything fits. Pure and idempotent.
    /// Needed width = sum(right) + sum(shown left) + [overflowWidth if any left button is dropped]
    /// + <paramref name="spacing"/> * gaps + <paramref name="reservedWidth"/>.
    /// Gaps: one between each pair of adjacent shown items in the flat row [shown left..., overflow button?, right...]
    /// (items - 1), plus one more before <paramref name="reservedWidth"/> when it is above 0 and any item is shown.
    /// The Grid's star spacer column is not counted. Takes the largest LeftShown that fits (so it never decreases as
    /// <paramref name="room"/> grows). If the right group (+ reserved) alone exceeds the room, LeftShown = 0 and
    /// Overflow = (any left buttons). Negative or NaN inputs count as 0 (a non-finite spacing too).
    /// Overshoot contract: when the right group + reserved fits but adding the overflow button (and its gap) does not,
    /// the result is (0, true) and the total can exceed <paramref name="room"/> by at most overflowWidth + one spacing
    /// (the button stays so hidden left items remain reachable). The caller must then drop the pickers (call again with
    /// reservedWidth = 0) and only accept an overshoot that remains.
    /// The Grid's star spacer column is not counted.
    /// </summary>
    public static FitResult Fit(
        double room, double spacing, IReadOnlyList<double> leftWidths, IReadOnlyList<double> rightWidths,
        double reservedWidth, double overflowWidth = 32)
    {
        static double Clean(double v) => double.IsNaN(v) || v < 0 ? 0 : v;
        room = Clean(room); spacing = double.IsFinite(spacing) ? Clean(spacing) : 0; reservedWidth = Clean(reservedWidth); overflowWidth = Clean(overflowWidth);
        var left = (leftWidths ?? []).Select(Clean).ToArray();
        var right = (rightWidths ?? []).Select(Clean).ToArray();

        double Needed(int shown, bool overflow)
        {
            var items = shown + right.Length + (overflow ? 1 : 0);
            var gaps = Math.Max(0, items - 1) + (reservedWidth > 0 && items > 0 ? 1 : 0);
            var sum = right.Sum() + left.Take(shown).Sum() + (overflow ? overflowWidth : 0);
            return sum + spacing * gaps + reservedWidth;
        }

        if (Needed(left.Length, false) <= room) return new FitResult(left.Length, false);
        for (var shown = left.Length - 1; shown > 0; shown--)
            if (Needed(shown, true) <= room) return new FitResult(shown, true);
        return new FitResult(0, left.Length > 0);
    }
}
