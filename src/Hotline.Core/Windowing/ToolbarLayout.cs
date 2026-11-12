namespace Hotline.Core.Windowing;

/// <summary>One bottom-bar picker: preferred and minimum width in DIPs, and whether it has anything to show.</summary>
/// <param name="Max">How wide it may grow into spare room (null = never past Preferred).</param>
public sealed record PickerSpec(string Name, double Preferred, double Min, bool Available = true, double? Max = null);

/// <summary>How many left-group buttons stay on the bar, whether the "more" button is needed for the rest, and how many
/// buttons the right group gave up (from its left end; 0 unless the right group alone is wider than the room).</summary>
public sealed record FitResult(int LeftShown, bool Overflow, int RightDropped = 0, bool ShrunkLastRight = false);

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
    /// Decides what fits in <paramref name="room"/>: left-group buttons are dropped from the group's end (behind an overflow
    /// button) until everything fits; only when the right group alone is too wide does it give up buttons from its LEFT end (at
    /// least one stays). Needed width = shown buttons + overflow button + spacing * gaps (between adjacent shown items, one more
    /// before <paramref name="reservedWidth"/>) + reserved; pure.
    /// Overshoot contract: if even one right button + the overflow button + reserved cannot fit, that one button and the overflow
    /// button are returned anyway (hidden items stay reachable) and the total exceeds <paramref name="room"/>; the caller then
    /// drops the pickers (reservedWidth = 0) and accepts only an overshoot that remains.
    /// </summary>
    public static FitResult Fit(
        double room, double spacing, IReadOnlyList<double> leftWidths, IReadOnlyList<double> rightWidths,
        double reservedWidth, double overflowWidth = 32, bool alwaysOverflow = false) =>
        FitChecked(room, spacing, leftWidths, rightWidths, reservedWidth, overflowWidth, alwaysOverflow).Result;

    /// <summary><see cref="Fit"/>, and whether the result is within the room (false = the documented overshoot).
    /// <paramref name="alwaysOverflow"/>: the menu button is a placed "more" button, drawn (and counted) whether or not anything is dropped.</summary>
    public static (FitResult Result, bool Fits) FitChecked(
        double room, double spacing, IReadOnlyList<double> leftWidths, IReadOnlyList<double> rightWidths,
        double reservedWidth, double overflowWidth = 32, bool alwaysOverflow = false)
    {
        static double Clean(double v) => double.IsNaN(v) || v < 0 ? 0 : v;
        room = Clean(room); spacing = double.IsFinite(spacing) ? Clean(spacing) : 0; reservedWidth = Clean(reservedWidth); overflowWidth = Clean(overflowWidth);
        var left = (leftWidths ?? []).Select(Clean).ToArray();
        var right = (rightWidths ?? []).Select(Clean).ToArray();

        double Needed(int shown, int rightDropped)
        {
            var overflow = alwaysOverflow || shown < left.Length || rightDropped > 0;
            var items = shown + right.Length - rightDropped + (overflow ? 1 : 0);
            var gaps = Math.Max(0, items - 1) + (reservedWidth > 0 && items > 0 ? 1 : 0);
            var sum = right.Skip(rightDropped).Sum() + left.Take(shown).Sum() + (overflow ? overflowWidth : 0);
            return sum + spacing * gaps + reservedWidth;
        }

        if (Needed(left.Length, 0) <= room) return (new FitResult(left.Length, alwaysOverflow), true);
        for (var shown = left.Length - 1; shown > 0; shown--)
            if (Needed(shown, 0) <= room) return (new FitResult(shown, true), true);
        // No left button fits: the right group gives up buttons from its left end, keeping at least one.
        var lastRight = Math.Max(0, right.Length - 1);
        for (var dropped = 0; dropped <= lastRight; dropped++)
            if (Needed(0, dropped) <= room) return (new FitResult(0, alwaysOverflow || left.Length > 0 || dropped > 0, dropped), true);
        if (right.Length > 0 && right[lastRight] > overflowWidth * 1.5)
        {
            right[lastRight] = overflowWidth;
            if (Needed(0, lastRight) <= room) return (new FitResult(0, alwaysOverflow || left.Length > 0 || lastRight > 0, lastRight, ShrunkLastRight: true), true);
            return (new FitResult(0, alwaysOverflow || left.Length > 0 || lastRight > 0, lastRight, ShrunkLastRight: true), false);
        }
        return (new FitResult(0, alwaysOverflow || left.Length > 0 || lastRight > 0, lastRight), false);
    }
}
