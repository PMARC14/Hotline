namespace Hotline.Core.Windowing;

/// <summary>One bottom-bar picker: preferred and minimum width in DIPs, and whether it has anything to show.</summary>
/// <param name="Max">How wide it may grow into spare room (null = never past Preferred).</param>
public sealed record PickerSpec(string Name, double Preferred, double Min, bool Available = true, double? Max = null);

/// <summary>How many left-group buttons stay on the bar, whether the "more" button is needed for the rest, and how many
/// buttons the right group gave up (from its left end; 0 unless the right group alone is wider than the room).</summary>
public sealed record FitResult(int LeftShown, bool Overflow, int RightDropped = 0);

/// <summary>
/// Lays out the bottom bar's pickers in the space left after the fixed buttons. The buttons are fitted first
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
    /// Decides what fits in <paramref name="room"/>: how many left-group buttons stay, then (only when not even none of them
    /// fit) how many buttons the right group gives up. Left buttons are dropped from the end (behind an overflow button)
    /// until everything fits; the right group is kept whole as long as it can be. Only when the right group alone (+ the
    /// overflow button when anything is dropped, + reserved) is wider than the room does it drop buttons too, from its LEFT end,
    /// so the rightmost ones (typically Settings) stay; at least one right button is always kept. Pure and idempotent.
    /// Needed width = sum(shown right) + sum(shown left) + [overflowWidth if any button is dropped]
    /// + <paramref name="spacing"/> * gaps + <paramref name="reservedWidth"/>.
    /// Gaps: one between each pair of adjacent shown items in the flat row [shown left..., overflow button?, shown right...]
    /// (items - 1), plus one more before <paramref name="reservedWidth"/> when it is above 0 and any item is shown.
    /// The Grid's star spacer column is not counted. Takes the largest LeftShown that fits (so it never decreases as
    /// <paramref name="room"/> grows), and then the fewest right buttons dropped. Negative or NaN inputs count as 0 (a
    /// non-finite spacing too).
    /// Overshoot contract: only when even ONE right button plus the overflow button (and its gap) and the reserved width
    /// cannot fit (a panel narrower than about two buttons) the result is that one right button with the overflow button
    /// anyway (so the hidden items stay reachable) and the total exceeds <paramref name="room"/>. The caller then drops the
    /// pickers (calls again with reservedWidth = 0) and only accepts an overshoot that remains.
    /// </summary>
    public static FitResult Fit(
        double room, double spacing, IReadOnlyList<double> leftWidths, IReadOnlyList<double> rightWidths,
        double reservedWidth, double overflowWidth = 32) =>
        FitChecked(room, spacing, leftWidths, rightWidths, reservedWidth, overflowWidth).Result;

    /// <summary><see cref="Fit"/>, and whether the result is within the room (false = the documented overshoot).</summary>
    public static (FitResult Result, bool Fits) FitChecked(
        double room, double spacing, IReadOnlyList<double> leftWidths, IReadOnlyList<double> rightWidths,
        double reservedWidth, double overflowWidth = 32)
    {
        static double Clean(double v) => double.IsNaN(v) || v < 0 ? 0 : v;
        room = Clean(room); spacing = double.IsFinite(spacing) ? Clean(spacing) : 0; reservedWidth = Clean(reservedWidth); overflowWidth = Clean(overflowWidth);
        var left = (leftWidths ?? []).Select(Clean).ToArray();
        var right = (rightWidths ?? []).Select(Clean).ToArray();

        double Needed(int shown, int rightDropped)
        {
            var overflow = shown < left.Length || rightDropped > 0;
            var items = shown + right.Length - rightDropped + (overflow ? 1 : 0);
            var gaps = Math.Max(0, items - 1) + (reservedWidth > 0 && items > 0 ? 1 : 0);
            var sum = right.Skip(rightDropped).Sum() + left.Take(shown).Sum() + (overflow ? overflowWidth : 0);
            return sum + spacing * gaps + reservedWidth;
        }

        if (Needed(left.Length, 0) <= room) return (new FitResult(left.Length, false), true);
        for (var shown = left.Length - 1; shown > 0; shown--)
            if (Needed(shown, 0) <= room) return (new FitResult(shown, true), true);
        // No left button fits: the right group gives up buttons from its left end, keeping at least one.
        var lastRight = Math.Max(0, right.Length - 1);
        for (var dropped = 0; dropped <= lastRight; dropped++)
            if (Needed(0, dropped) <= room) return (new FitResult(0, left.Length > 0 || dropped > 0, dropped), true);
        return (new FitResult(0, left.Length > 0 || lastRight > 0, lastRight), false);
    }
}
