namespace Hotline.Core.Windowing;

/// <summary>A named toolbar.json document: one reference layout.</summary>
public sealed record ToolbarProfile(string Name, string Json);

/// <summary>
/// Reference layouts for the button rows: the layouts people are likely to ask for, the extremes, and a damaged file. The
/// automatic tests run every one at every panel width and bar size through the same decisions the renderer makes
/// (<see cref="ToolbarPlan.PlanRow"/>), the panel's self-test draws the interesting ones, and docs/configuration.md lists
/// them. Each Json is a complete toolbar.json (a leading comment says what the profile shows).
/// </summary>
public static class ToolbarProfiles
{
    public static IReadOnlyList<ToolbarProfile> All { get; } = Build();

    /// <summary>The profile of that name; throws for an unknown name (a typo in a test).</summary>
    public static ToolbarProfile Named(string name) => All.First(p => p.Name == name);

    private const string Pickers = "\"effort\", \"model\", \"provider\"";
    private const string Buttons = "\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\", \"prompt\", \"recent\", \"continueIn\", \"newChat\", \"settings\"";

    private static string Group(string left = "", string center = "", string right = "") =>
        $"{{ \"left\": [{left}], \"center\": [{center}], \"right\": [{right}] }}";

    private static string Doc(string description, string bottom, string top = "", string style = "{}", string overrides = "{}", string buttons = "{}", string menu = "") =>
        $"// {description}\n{{ \"version\": 2,\n  \"top\": {(top.Length > 0 ? top : Group())},\n  \"items\": {bottom},\n  \"style\": {style},\n"
        + $"  \"menu\": [{menu}],\n  \"overrides\": {overrides},\n  \"buttons\": {buttons} }}";

    /// <summary>The ids c1..cN, as list text and as button definitions.</summary>
    private static string CustomIds(int count) => string.Join(", ", Enumerable.Range(1, count).Select(i => $"\"c{i}\""));

    private static string CustomButtons(int count) =>
        "{ " + string.Join(", ", Enumerable.Range(1, count).Select(i => $"\"c{i}\": {{ \"label\": \"Custom {i}\", \"text\": \"text {i}\" }}")) + " }";

    private static IReadOnlyList<ToolbarProfile> Build() =>
    [
        new("default", Doc("Today's layout: the pickers in the top row, the nine buttons bottom left.",
            Group(Buttons), top: Group(Pickers))),

        new("pickers-bottom-left-before", Doc("The pickers at the start of the bottom-left group, before the buttons.",
            Group($"{Pickers}, {Buttons}"))),

        new("pickers-bottom-left-after", Doc("The pickers at the end of the bottom-left group, after the buttons.",
            Group($"{Buttons}, {Pickers}"))),

        new("pickers-bottom-left-between", Doc("The pickers in the middle of the bottom-left group, with buttons on both sides.",
            Group($"\"pin\", \"captureWindow\", {Pickers}, \"prompt\", \"recent\", \"settings\"", right: "\"newChat\""))),

        new("pickers-bottom-centre", Doc("The pickers in the bottom-centre group, buttons left and right of them.",
            Group("\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\"", Pickers, "\"prompt\", \"recent\", \"settings\""))),

        new("pickers-bottom-centre-crowded", Doc("The pickers in the bottom-centre group with eight buttons left of them and the rest right.",
            Group("\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\", \"prompt\", \"recent\", \"continueIn\", \"newChat\"", Pickers, "\"settings\""))),

        new("pickers-bottom-right", Doc("The pickers in the bottom-right group, which is the last to move into the menu.",
            Group(Buttons, right: Pickers))),

        new("pickers-bottom-right-after-buttons", Doc("The bottom-right group holds some buttons, then the pickers.",
            Group("\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\"", right: $"\"settings\", {Pickers}"))),

        new("pickers-top-right", Doc("The pickers in the top row's right group, the buttons bottom left.",
            Group(Buttons), top: Group(right: Pickers))),

        new("pickers-top-centre-both-rows", Doc("The pickers in the top-centre group, with buttons in both rows.",
            Group("\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\"", right: "\"settings\""),
            top: Group("\"prompt\", \"recent\"", Pickers, "\"continueIn\", \"newChat\""))),

        // The pickers are separate items: the profiles above list all three side by side, these place them one by one.
        new("pickers-apart-corners", Doc("Separated pickers: effort top left, model top right, provider bottom centre, buttons bottom left and right.",
            Group("\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\"", "\"provider\"", "\"prompt\", \"recent\", \"settings\""),
            top: Group("\"effort\"", right: "\"model\""))),

        new("picker-between-buttons", Doc("One picker (model) between buttons in the bottom row, the other two pickers not shown.",
            Group("\"pin\", \"captureWindow\", \"model\", \"captureScreen\", \"captureRegion\", \"prompt\"", right: "\"recent\", \"newChat\", \"settings\""))),

        new("pickers-one-per-row-right", Doc("A picker in each row's right group: effort after a button in the top row, model before the last buttons in the bottom row.",
            Group("\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\"", right: "\"model\", \"newChat\", \"settings\""),
            top: Group("\"prompt\"", right: "\"recent\", \"effort\""))),

        new("spans-all-1", Doc("Every picker one button wide, bottom left after the buttons.",
            Group($"{Buttons}, {Pickers}"),
            overrides: "{ \"effort\": { \"span\": 1 }, \"model\": { \"span\": 1 }, \"provider\": { \"span\": 1 } }")),

        new("spans-all-8", Doc("Every picker eight button widths wide, in the top row.",
            Group(Buttons), top: Group(Pickers),
            overrides: "{ \"effort\": { \"span\": 8 }, \"model\": { \"span\": 8 }, \"provider\": { \"span\": 8 } }")),

        new("spans-mixed", Doc("Spans 1, 4 and 8 (effort, model, provider), bottom left before the buttons.",
            Group($"{Pickers}, {Buttons}"),
            overrides: "{ \"effort\": { \"span\": 1 }, \"model\": { \"span\": 4 }, \"provider\": { \"span\": 8 } }")),

        new("all-compact", Doc("Every button and the pickers' unit at the compact size, pickers bottom left after the buttons.",
            Group($"{Buttons}, {Pickers}"), style: "{ \"size\": \"compact\" }")),

        new("all-wide", Doc("Every button and the pickers' unit at the wide size, pickers in the top row.",
            Group(Buttons), top: Group(Pickers), style: "{ \"size\": \"wide\" }")),

        new("mixed-button-sizes", Doc("A different size, width or height on several buttons, pickers bottom left after them.",
            Group($"{Buttons}, {Pickers}"),
            overrides: "{ \"pin\": { \"size\": \"compact\" }, \"captureWindow\": { \"size\": \"wide\" }, \"captureScreen\": { \"width\": 80, \"height\": 20 }, "
                       + "\"prompt\": { \"size\": \"wide\", \"outline\": true }, \"settings\": { \"width\": 20 } }")),

        new("custom-24-pickers-bottom", Doc("Twenty-four custom buttons and the pickers in the bottom row: pickers in the middle of the left group.",
            Group($"\"pin\", {CustomIds(12)}, {Pickers}, {string.Join(", ", Enumerable.Range(13, 12).Select(i => $"\"c{i}\""))}", right: "\"settings\""),
            buttons: CustomButtons(24))),

        new("custom-24-wide-pickers-bottom-centre", Doc("Twenty-four wide custom buttons around the pickers in the bottom-centre group.",
            Group(CustomIds(8), Pickers, string.Join(", ", Enumerable.Range(9, 16).Select(i => $"\"c{i}\"")) + ", \"settings\""),
            style: "{ \"size\": \"wide\" }", buttons: CustomButtons(24))),

        new("only-pickers", Doc("Nothing but the three pickers, bottom left.",
            Group(Pickers))),

        new("only-right-group", Doc("Only a right group in the bottom row: it is the last to move into the menu.",
            Group(right: $"\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\", \"prompt\", \"recent\", \"settings\", {Pickers}"))),

        new("all-items-in-one-group", Doc("Every built-in item and all 24 custom buttons in one group (the most distinct items there can be), then unknown ids.",
            Group($"{Buttons}, {Pickers}, \"continueIn\", {CustomIds(24)}, " + string.Join(", ", Enumerable.Range(1, 10).Select(i => $"\"nothing{i}\""))),
            buttons: CustomButtons(24))),

        new("empty-bar", Doc("An explicit empty bar: both rows present and empty, nothing to draw.",
            Group())),

        // The "more" button (toolbar.json "menu"): its box takes the listed buttons, then what the row's overflow drops.
        new("more-menu-bottom-right-with-5-items", Doc("A more button last in the bottom-right group with five buttons in its menu, the pickers in the top row.",
            Group("\"pin\", \"captureWindow\", \"captureScreen\"", right: "\"settings\", \"more\""), top: Group(Pickers),
            menu: "\"captureRegion\", \"prompt\", \"recent\", \"continueIn\", \"newChat\"")),

        new("more-menu-top-centre-with-overflow", Doc("A more button in the top-centre group with two menu buttons; the six buttons beside it overflow into the same menu when the row is short.",
            Group(Pickers),
            top: Group("\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\", \"prompt\", \"recent\"", "\"more\"", "\"settings\""),
            menu: "\"continueIn\", \"newChat\"")),

        // There is one more item, so "both rows" is one row with it (a custom button in its menu) and the other with its own automatic button.
        new("more-in-both-rows", Doc("Both rows crowded: the bottom row has the more button, the top row keeps its own automatic overflow button.",
            Group(CustomIds(11), right: "\"more\""),
            top: Group("\"pin\", \"captureWindow\", \"captureScreen\", \"captureRegion\", \"prompt\", \"recent\", \"continueIn\", \"newChat\"", right: "\"settings\""),
            buttons: CustomButtons(12), menu: "\"c12\"")),

        new("damaged-file",
            """
            // A damaged file that still renders: wrong types, repeated keys, unknown ids, bad sizes and spans.
            { "version": 2,
              "items": { "left": ["pin", 5, null, "bogus", "pin", "model", ["x"]], "left": ["settings", "effort", "NEWCHAT", "provider", "ok"], "right": "settings" },
              "top": [ "pin" ],
              "style": { "size": "huge", "outline": "yes", "width": "wide", "outlineColor": "red" },
              "overrides": { "model": { "span": 99, "outline": true }, "effort": { "span": "2" }, "nobody": 3, "pin": { "width": 5000 }, "provider": { "span": 6 } },
              "buttons": { "Bad Id!": { "label": "x", "text": "t" }, "ok": { "label": "OK", "text": "hi" }, "both": { "quickAction": "a", "text": "b" } },
              "unknown": [1, 2, 3] }
            """),
    ];
}
