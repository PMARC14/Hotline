using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class ToolbarLayoutTests
{
    private static readonly PickerSpec[] Pickers =
    [
        new("effort", 76, 60), new("model", 160, 90), new("provider", 140, 80),
    ];
    private static readonly string[] HideOrder = ["effort", "provider", "model"];
    private const double Fixed = 5 * 36; // pin, 2 captures, prompt, new chat, settings... (any fixed total)
    private const double Gap = 4;

    private static double Used(IReadOnlyDictionary<string, double> w) => w.Values.Sum() + Gap * w.Values.Count(v => v > 0);

    [Fact]
    public void Wide_bar_gets_preferred_widths()
    {
        var w = ToolbarLayout.Compute(1000, Fixed, Gap, Pickers, HideOrder);
        Assert.Equal((76.0, 160.0, 140.0), (w["effort"], w["model"], w["provider"]));
    }

    [Theory]
    [InlineData(300)]
    [InlineData(420)]
    [InlineData(480)]
    [InlineData(520)]
    [InlineData(600)]
    [InlineData(560)]
    [InlineData(100)]
    public void Pickers_never_take_the_fixed_buttons_space(double bar)
    {
        var w = ToolbarLayout.Compute(bar, Fixed, Gap, Pickers, HideOrder);
        Assert.True(Used(w) <= Math.Max(0, bar - Fixed) + 0.001, $"used {Used(w)} of {bar - Fixed}");
        foreach (var p in Pickers) Assert.True(w[p.Name] == 0 || w[p.Name] >= p.Min, p.Name);
    }

    [Fact]
    public void Narrow_bar_hides_effort_first_then_provider_and_keeps_model()
    {
        var mid = ToolbarLayout.Compute(Fixed + 200, Fixed, Gap, Pickers, HideOrder);
        Assert.Equal(0, mid["effort"]);
        Assert.True(mid["model"] > 0 && mid["provider"] > 0);
        var narrow = ToolbarLayout.Compute(Fixed + 120, Fixed, Gap, Pickers, HideOrder);
        Assert.Equal((0.0, 0.0), (narrow["effort"], narrow["provider"]));
        Assert.True(narrow["model"] >= 90);
    }

    [Fact]
    public void Unavailable_picker_takes_no_space()
    {
        var specs = new[] { Pickers[0] with { Available = false }, Pickers[1], Pickers[2] };
        var w = ToolbarLayout.Compute(1000, Fixed, Gap, specs, HideOrder);
        Assert.Equal(0, w["effort"]);
        Assert.Equal(160, w["model"]);
    }

    [Fact]
    public void Widths_are_stable_for_the_same_bar_width()
        => Assert.Equal(ToolbarLayout.Compute(530, Fixed, Gap, Pickers, HideOrder), ToolbarLayout.Compute(530, Fixed, Gap, Pickers, HideOrder));

    [Fact]
    public void Shrinking_is_monotonic()
    {
        double? last = null;
        for (var bar = 1000.0; bar >= Fixed; bar -= 7)
        {
            var used = Used(ToolbarLayout.Compute(bar, Fixed, Gap, Pickers, HideOrder));
            if (last is { } l) Assert.True(used <= l + 0.001, $"grew at {bar}");
            last = used;
        }
    }
}
