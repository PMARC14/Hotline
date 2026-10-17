namespace Hotline.Core.Windowing;

/// <summary>One bottom-bar picker: preferred and minimum width in DIPs, and whether it has anything to show.</summary>
/// <param name="Max">How wide it may grow into spare room (null = never past Preferred).</param>
public sealed record PickerSpec(string Name, double Preferred, double Min, bool Available = true, double? Max = null);

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
}
