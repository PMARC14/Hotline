# Hotline Plan 3a — Native Chat UI, Relative Sizing, Toolbar, Config in ~/.hotline

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the WebView2 chat view with a fully native WinUI panel (real acrylic) that:
- sizes relative to the monitor, with space reserved above the composer;
- has a bigger + button and a bottom toolbar (pin, capture, new chat, settings);
- supports the native file dialog, paste, and drag-drop while pinned;
- shows problems inline;
- keeps its config in `%USERPROFILE%\.hotline`.

**Architecture:**
- **Core:**
  - Turns Markdown into a small block model (Markdig).
  - Owns sizing math, settings v4 and the data-folder migration.
  - Expresses theme tokens as hex colors that the App turns into brushes.
- **App:**
  - Renders the block model to native WinUI text. `ChatPresenter` replaces `ChatHost`: it wires the controller to native controls (message list, composer, chips, InfoBars, toolbar) and handles attachments (picker, paste, drop, capture).
  - The Web/ folder, WebView2 and the node tests are deleted.

**Tech Stack:** .NET 10, WinUI 3 (Windows App SDK 2.5.1), Markdig 1.4.0 (MIT, API verified 2026-10-01), xUnit v3.

**Spec:** `docs/superpowers/specs/2026-09-30-hotline-design.md` → "Plan 3 (UX overhaul…)", the 3a bullet, and the Stack decision (fully native, no WebView2).

## Global Constraints

- **No WebView2 anywhere.** Delete `src/Hotline.App/Web/`, `tests/web/`, the CI node step, and the `Web\**` content item.
- **Data folder:** `%USERPROFILE%\.hotline` holds `settings.json`, `logs\` and `history\`.
  - The agy workspace stays in the package LocalState (`<LocalState>\agy-workspace`).
  - On first run, copy `settings.json` and `history\` from LocalState if `~/.hotline\settings.json` doesn't exist.
- **Sizing is in DIPs and relative to the monitor work area:**
  - width = `WidthPercent`% of the work area (default 40), clamped to [`MinWidth`, `MaxWidth`] (defaults 600 / 1000 DIP);
  - baseline panel `Height` = 320 DIP (empty space above the composer is reserved for later features);
  - it grows upward to at most `MaxHeightPercent`% (default 70) of the work-area height.
- **Settings schema 4:** `WindowSettings.Width` and `ChatSettings.MaxHeight` are removed (unknown JSON is ignored). A v<4 file with `height` 120 or 520 (old defaults) migrates to 320.
- **Theme tokens are hex colors `#AARRGGBB` or `#RRGGBB`.** The font token is a XAML family list (`"Segoe UI Variable Text, Segoe UI"`). User font names are only allowed if they match `^[\w ,.\-]+$`.
- **Markdown links:** only `http`, `https` and `mailto` become clickable. Everything else renders as plain text. No fuzzy autolinks.
- **Focus while busy:**
  - Pinned popups don't hide on blur.
  - The file dialog hides the popup while it's open, then restores it.
  - Capture uses `WithHiddenAsync`.
- **Inline problems:** rejections and errors appear as InfoBars inside the panel, never as transient toasts.
- **Commit trailer:** commits are authored by pmarc14 and end with the Co-Authored-By and Claude-Session lines.
- **Tests:** `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj` must stay green.
- **Smoke test:** `powershell -File tests\smoke\smoke.ps1 -Install -WithAgy` must pass at the end of Tasks 4 and 5.

## Review Focus

1. **Streaming a long answer with code blocks and tables** must not freeze the UI or flicker badly. Rendering is throttled to about 20 per second, and the list stays scrolled to the bottom only if the user was already at the bottom. Manual check: Task 4, step 7.
2. **Markdown that tries to inject links** such as `javascript:` or `file://`, or a huge table, should render safely as text and keep the panel usable. Pinned by `MarkdownModelTests.Unsafe_link_schemes_render_as_text` and `Large_table_is_preserved` (Task 1).
3. **Pasting a 30 MB photo, dropping a folder, or picking an 11th file** each produce a visible inline message, and the panel keeps working. Manual check: Task 5, step 6.
4. **A user with an old `settings.json`** (v1 to v3) gets sensible new sizes and keeps custom ones. Pinned by `SizingSettingsTests` (Task 3).
5. **A monitor at 100% vs 200% scale and with a small or large work area** gives a panel width that is the same fraction of the screen, never below `MinWidth` or above `MaxWidth`. Pinned by `RelativeWidthTests` (Task 3).

---

### Task 1: Markdown block model (Core)

**Files:**
- Modify: `src/Hotline.Core/Hotline.Core.csproj` (add Markdig 1.4.0)
- Create: `src/Hotline.Core/Text/MarkdownModel.cs`
- Test: `tests/Hotline.Core.Tests/MarkdownModelTests.cs`

**Interfaces:**
- Produces (namespace `Hotline.Core.Text`):
  - blocks: `abstract record MdBlock` with `MdParagraph(IReadOnlyList<MdInline> Inlines)`, `MdHeading(int Level, IReadOnlyList<MdInline> Inlines)`, `MdCode(string? Language, string Code)`, `MdList(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<MdBlock>> Items)`, `MdQuote(IReadOnlyList<MdBlock> Blocks)`, `MdRule()`, `MdTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows, bool HasHeader)`
  - inlines: `abstract record MdInline` with `MdText(string Text, MdStyle Style)`, `MdLink(string Url, IReadOnlyList<MdInline> Inlines)`, `MdBreak()`
  - `[Flags] enum MdStyle { None = 0, Bold = 1, Italic = 2, Code = 4, Strike = 8 }`
  - `static IReadOnlyList<MdBlock> MarkdownModel.Parse(string markdown)`

- [ ] **Step 1: Add the package**

In `src/Hotline.Core/Hotline.Core.csproj` add:
```xml
  <ItemGroup>
    <PackageReference Include="Markdig" Version="1.4.0" />
  </ItemGroup>
```

- [ ] **Step 2: Write the failing tests**

`tests/Hotline.Core.Tests/MarkdownModelTests.cs`:
```csharp
using Hotline.Core.Text;

namespace Hotline.Core.Tests;

public class MarkdownModelTests
{
    private static MdText T(string text, MdStyle style = MdStyle.None) => new(text, style);

    [Fact]
    public void Paragraph_with_styles()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("a **b** *c* ~~d~~ `e`")));
        Assert.Equal(
            [T("a "), T("b", MdStyle.Bold), T(" "), T("c", MdStyle.Italic), T(" "), T("d", MdStyle.Strike), T(" "), T("e", MdStyle.Code)],
            p.Inlines);
    }

    [Fact]
    public void Nested_emphasis_combines_styles()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("***both***")));
        Assert.Equal([T("both", MdStyle.Bold | MdStyle.Italic)], p.Inlines);
    }

    [Fact]
    public void Heading_levels()
    {
        var h = Assert.IsType<MdHeading>(Assert.Single(MarkdownModel.Parse("## Title")));
        Assert.Equal(2, h.Level);
        Assert.Equal([T("Title")], h.Inlines);
    }

    [Fact]
    public void Fenced_code_keeps_language_and_text()
    {
        var c = Assert.IsType<MdCode>(Assert.Single(MarkdownModel.Parse("```cs\nvar x = 1;\nvar y = 2;\n```")));
        Assert.Equal("cs", c.Language);
        Assert.Equal("var x = 1;\nvar y = 2;", c.Code);
    }

    [Fact]
    public void Lists_ordered_and_unordered()
    {
        var blocks = MarkdownModel.Parse("- a\n- b\n\n3. x\n4. y");
        var ul = Assert.IsType<MdList>(blocks[0]);
        Assert.False(ul.Ordered);
        Assert.Equal(2, ul.Items.Count);
        var ol = Assert.IsType<MdList>(blocks[1]);
        Assert.True(ol.Ordered);
        Assert.Equal(3, ol.Start);
        Assert.Equal([T("x")], Assert.IsType<MdParagraph>(ol.Items[0][0]).Inlines);
    }

    [Fact]
    public void Quote_rule_and_table()
    {
        var blocks = MarkdownModel.Parse("> q\n\n---\n\n| a | b |\n|---|---|\n| 1 | 2 |\n");
        Assert.IsType<MdQuote>(blocks[0]);
        Assert.IsType<MdRule>(blocks[1]);
        var t = Assert.IsType<MdTable>(blocks[2]);
        Assert.True(t.HasHeader);
        Assert.Equal(2, t.Rows.Count);
        Assert.Equal([T("2")], t.Rows[1][1]);
    }

    [Fact]
    public void Safe_links_are_links()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("see [docs](https://x.y/z) or <mailto:a@b.c>")));
        var link = Assert.IsType<MdLink>(p.Inlines[1]);
        Assert.Equal("https://x.y/z", link.Url);
        Assert.Equal([T("docs")], link.Inlines);
        Assert.Contains(p.Inlines, i => i is MdLink { Url: "mailto:a@b.c" });
    }

    [Theory]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](file:///C:/Windows/win.ini)")]
    [InlineData("[x](ms-settings:privacy)")]
    public void Unsafe_link_schemes_render_as_text(string md)
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse(md)));
        Assert.DoesNotContain(p.Inlines, i => i is MdLink);
        Assert.Contains(p.Inlines, i => i is MdText { Text: "x" });
    }

    [Fact]
    public void Bare_filenames_are_not_links()
        => Assert.DoesNotContain(Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("open main.py"))).Inlines, i => i is MdLink);

    [Fact]
    public void Line_breaks_and_html_as_text()
    {
        var p = Assert.IsType<MdParagraph>(Assert.Single(MarkdownModel.Parse("one  \ntwo <b>x</b>")));
        Assert.Contains(p.Inlines, i => i is MdBreak);
        Assert.Contains(p.Inlines, i => i is MdText { Text: "<b>" });
    }

    [Fact]
    public void Large_table_is_preserved()
    {
        var md = "| a | b |\n|---|---|\n" + string.Concat(Enumerable.Range(0, 300).Select(i => $"| {i} | x |\n"));
        Assert.Equal(301, Assert.IsType<MdTable>(Assert.Single(MarkdownModel.Parse(md))).Rows.Count);
    }

    [Fact]
    public void Unclosed_code_fence_while_streaming_is_still_code()
        => Assert.Equal("partial", Assert.IsType<MdCode>(Assert.Single(MarkdownModel.Parse("```py\npartial"))).Code);

    [Fact]
    public void Empty_input_gives_no_blocks()
        => Assert.Empty(MarkdownModel.Parse(""));
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with `CS0234: The type or namespace name 'Text' does not exist in the namespace 'Hotline.Core'`.

- [ ] **Step 4: Write minimal implementation**

`src/Hotline.Core/Text/MarkdownModel.cs`:
```csharp
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Hotline.Core.Text;

public abstract record MdBlock;
public sealed record MdParagraph(IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdHeading(int Level, IReadOnlyList<MdInline> Inlines) : MdBlock;
public sealed record MdCode(string? Language, string Code) : MdBlock;
public sealed record MdList(bool Ordered, int Start, IReadOnlyList<IReadOnlyList<MdBlock>> Items) : MdBlock;
public sealed record MdQuote(IReadOnlyList<MdBlock> Blocks) : MdBlock;
public sealed record MdRule : MdBlock;
public sealed record MdTable(IReadOnlyList<IReadOnlyList<IReadOnlyList<MdInline>>> Rows, bool HasHeader) : MdBlock;

public abstract record MdInline;
public sealed record MdText(string Text, MdStyle Style) : MdInline;
public sealed record MdLink(string Url, IReadOnlyList<MdInline> Inlines) : MdInline;
public sealed record MdBreak : MdInline;

[Flags]
public enum MdStyle { None = 0, Bold = 1, Italic = 2, Code = 4, Strike = 8 }

/// <summary>
/// Markdown → a small, UI-agnostic block model the App renders natively. Records compare by value
/// in tests; lists are materialized as arrays wrapped in <see cref="Seq{T}"/> for structural equality.
/// </summary>
public static class MarkdownModel
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseEmphasisExtras().Build();

    public static IReadOnlyList<MdBlock> Parse(string markdown) => Blocks(Markdown.Parse(markdown ?? "", Pipeline));

    private static Seq<MdBlock> Blocks(ContainerBlock container) => new(container.Select(Block).OfType<MdBlock>());

    private static MdBlock? Block(Block block) => block switch
    {
        HeadingBlock h => new MdHeading(h.Level, Inlines(h.Inline)),
        FencedCodeBlock f => new MdCode(string.IsNullOrWhiteSpace(f.Info) ? null : f.Info, f.Lines.ToString()),
        CodeBlock c => new MdCode(null, c.Lines.ToString()),
        ParagraphBlock p => new MdParagraph(Inlines(p.Inline)),
        ListBlock l => new MdList(l.IsOrdered, int.TryParse(l.OrderedStart, out var s) ? s : 1,
            new Seq<IReadOnlyList<MdBlock>>(l.OfType<ListItemBlock>().Select(i => (IReadOnlyList<MdBlock>)Blocks(i)))),
        QuoteBlock q => new MdQuote(Blocks(q)),
        ThematicBreakBlock => new MdRule(),
        Table t => new MdTable(
            new Seq<IReadOnlyList<IReadOnlyList<MdInline>>>(t.OfType<TableRow>().Select(r =>
                (IReadOnlyList<IReadOnlyList<MdInline>>)new Seq<IReadOnlyList<MdInline>>(r.OfType<TableCell>().Select(CellInlines)))),
            t.OfType<TableRow>().FirstOrDefault()?.IsHeader ?? false),
        _ => null,
    };

    private static IReadOnlyList<MdInline> CellInlines(TableCell cell) =>
        new Seq<MdInline>(cell.OfType<ParagraphBlock>().SelectMany(p => Inlines(p.Inline)));

    private static Seq<MdInline> Inlines(ContainerInline? container)
    {
        var result = new List<MdInline>();
        Walk(container, MdStyle.None, result);
        return new Seq<MdInline>(Merge(result));
    }

    private static void Walk(ContainerInline? container, MdStyle style, List<MdInline> output)
    {
        for (var inline = container?.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    output.Add(new MdText(lit.Content.ToString(), style));
                    break;
                case CodeInline code:
                    output.Add(new MdText(code.Content, style | MdStyle.Code));
                    break;
                case EmphasisInline em:
                    var added = em.DelimiterChar == '~' ? MdStyle.Strike : em.DelimiterCount >= 2 ? MdStyle.Bold : MdStyle.Italic;
                    Walk(em, style | added, output);
                    break;
                case LinkInline { IsImage: false } link when IsSafe(link.Url):
                    var children = new List<MdInline>();
                    Walk(link, style, children);
                    output.Add(new MdLink(link.Url!, new Seq<MdInline>(Merge(children))));
                    break;
                case LinkInline link:
                    Walk(link, style, output); // unsafe scheme or image: keep the visible text only
                    break;
                case AutolinkInline auto when IsSafe(auto.Url):
                    output.Add(new MdLink(auto.Url, new Seq<MdInline>([new MdText(auto.Url, style)])));
                    break;
                case AutolinkInline auto:
                    output.Add(new MdText(auto.Url, style));
                    break;
                case LineBreakInline { IsHard: true }:
                    output.Add(new MdBreak());
                    break;
                case LineBreakInline:
                    output.Add(new MdText(" ", style));
                    break;
                case HtmlInline html:
                    output.Add(new MdText(html.Tag, style));
                    break;
                case HtmlEntityInline entity:
                    output.Add(new MdText(entity.Transcoded.ToString(), style));
                    break;
                case ContainerInline nested:
                    Walk(nested, style, output);
                    break;
            }
        }
    }

    /// <summary>Joins adjacent text runs with the same style ("a" + " " → "a ").</summary>
    private static IEnumerable<MdInline> Merge(List<MdInline> inlines)
    {
        MdText? pending = null;
        foreach (var i in inlines)
        {
            if (i is MdText t && pending is not null && pending.Style == t.Style) { pending = pending with { Text = pending.Text + t.Text }; continue; }
            if (pending is not null) yield return pending;
            pending = i as MdText;
            if (pending is null) yield return i;
        }
        if (pending is not null) yield return pending;
    }

    private static bool IsSafe(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http" || u.Scheme == "mailto");
}

/// <summary>Read-only list with value equality (so records containing lists compare structurally).</summary>
public sealed class Seq<T>(IEnumerable<T> items) : IReadOnlyList<T>, IEquatable<IReadOnlyList<T>>
{
    private readonly T[] _items = items.ToArray();
    public T this[int index] => _items[index];
    public int Count => _items.Length;
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => _items.GetEnumerator();
    public bool Equals(IReadOnlyList<T>? other) => other is not null && this.SequenceEqual(other);
    public override bool Equals(object? obj) => obj is IReadOnlyList<T> other && Equals(other);
    public override int GetHashCode() => _items.Aggregate(0, (h, x) => HashCode.Combine(h, x));
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`. `Assert.Equal` on lists compares element-wise; the `Seq<T>` equality makes nested record comparisons structural.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m "feat(core): markdown block model via Markdig with safe links" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_017Gb9pmgys17JP4Z4m7ejtu"
```

---

### Task 2: Native theme tokens (Core)

**Files:**
- Modify: `src/Hotline.Core/Theming/ThemeTokens.cs` (rewrite)
- Test: `tests/Hotline.Core.Tests/ThemeTokensTests.cs` (rewrite), `tests/Hotline.Core.Tests/AppearanceSettingsTests.cs` (three tests)

**Interfaces:**
- Produces:
  - `sealed record ThemeTokens(string Text, string Muted, string Accent, string Surface, string SurfaceStrong, string Border, string UserBubble, string CodeBackground, string Font, int FontSizePx, int RadiusPx)`
  - `static Dark`, `static Light`, `static ThemeTokens For(bool dark, WindowSettings window)`
  - `static (byte A, byte R, byte G, byte B) ThemeColor.Parse(string hex)`
  - The CSS output (`ToCss`) and the scrollbar tokens are removed.

- [ ] **Step 1: Write the failing tests**

Replace `tests/Hotline.Core.Tests/ThemeTokensTests.cs` with:
```csharp
using Hotline.Core.Settings;
using Hotline.Core.Theming;

namespace Hotline.Core.Tests;

public class ThemeTokensTests
{
    [Theory]
    [InlineData("#FF8B7CFF", 255, 0x8B, 0x7C, 0xFF)]
    [InlineData("#0FFFFFFF", 0x0F, 255, 255, 255)]
    [InlineData("#F3F3F3", 255, 0xF3, 0xF3, 0xF3)]
    public void Parses_hex_colors(string hex, int a, int r, int g, int b)
        => Assert.Equal(((byte)a, (byte)r, (byte)g, (byte)b), ThemeColor.Parse(hex));

    [Theory]
    [InlineData("red")]
    [InlineData("rgba(1,2,3,0.5)")]
    [InlineData("#12345")]
    [InlineData("#GG000000")]
    public void Rejects_non_hex(string value)
        => Assert.Throws<FormatException>(() => ThemeColor.Parse(value));

    [Fact]
    public void Builtin_tokens_are_valid_and_differ()
    {
        foreach (var t in new[] { ThemeTokens.Dark, ThemeTokens.Light })
            foreach (var c in new[] { t.Text, t.Muted, t.Accent, t.Surface, t.SurfaceStrong, t.Border, t.UserBubble, t.CodeBackground })
                ThemeColor.Parse(c);
        Assert.NotEqual(ThemeTokens.Dark.Text, ThemeTokens.Light.Text);
    }

    [Fact]
    public void Font_is_a_xaml_family_list()
        => Assert.Equal("Segoe UI Variable Text, Segoe UI", ThemeTokens.Dark.Font);
}
```

In `tests/Hotline.Core.Tests/AppearanceSettingsTests.cs`, replace the three tests `Theme_tokens_follow_font_settings`, `Unsafe_font_family_falls_back_to_default` and `Theme_tokens_include_scrollbar_colors` with:
```csharp
    [Fact]
    public void Theme_tokens_follow_font_settings()
    {
        var t = ThemeTokens.For(dark: true, new WindowSettings { FontSize = 17, FontFamily = "Cascadia Code" });
        Assert.Equal(17, t.FontSizePx);
        Assert.Equal("Cascadia Code, Segoe UI Variable Text, Segoe UI", t.Font);
    }

    [Theory]
    [InlineData("x;}body{evil")]
    [InlineData("<script>")]
    [InlineData("Font\"Name")]
    public void Unsafe_font_family_falls_back_to_default(string family)
        => Assert.Equal(ThemeTokens.Dark.Font, ThemeTokens.For(dark: true, new WindowSettings { FontFamily = family }).Font);
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with `CS0103: The name 'ThemeColor' does not exist`.

- [ ] **Step 3: Implement**

Replace `src/Hotline.Core/Theming/ThemeTokens.cs` with:
```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Hotline.Core.Settings;

namespace Hotline.Core.Theming;

/// <summary>
/// Design tokens for the native chat UI (spec: Theming). Colors are "#AARRGGBB" / "#RRGGBB"; the App turns them
/// into brushes. Font is a XAML family list. User theme files (later) supply the same shape.
/// </summary>
public sealed partial record ThemeTokens(
    string Text, string Muted, string Accent, string Surface, string SurfaceStrong, string Border,
    string UserBubble, string CodeBackground, string Font, int FontSizePx, int RadiusPx)
{
    private const string DefaultFont = "Segoe UI Variable Text, Segoe UI";

    public static ThemeTokens Dark { get; } = new(
        "#FFF3F3F3", "#FFA8A8A8", "#FF8B7CFF", "#0FFFFFFF", "#1AFFFFFF", "#1FFFFFFF", "#388B7CFF", "#59000000", DefaultFont, 14, 8);

    public static ThemeTokens Light { get; } = new(
        "#FF1A1A1A", "#FF5C5C5C", "#FF5B4BF5", "#0A000000", "#12000000", "#1A000000", "#245B4BF5", "#0D000000", DefaultFont, 14, 8);

    /// <summary>Built-in tokens with the user's font settings applied (unsafe font names are ignored).</summary>
    public static ThemeTokens For(bool dark, WindowSettings window)
    {
        var baseTokens = dark ? Dark : Light;
        var family = window.FontFamily?.Trim();
        var font = string.IsNullOrEmpty(family) || !SafeFont().IsMatch(family) ? baseTokens.Font : $"{family}, {DefaultFont}";
        return baseTokens with { Font = font, FontSizePx = window.FontSize };
    }

    [GeneratedRegex(@"^[\w ,.\-]+$")]
    private static partial Regex SafeFont();
}

public static class ThemeColor
{
    public static (byte A, byte R, byte G, byte B) Parse(string hex)
    {
        if (hex.Length is not (7 or 9) || hex[0] != '#' || !uint.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Not a #RRGGBB/#AARRGGBB color: {hex}");
        if (hex.Length == 7) v |= 0xFF000000;
        return ((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
```

- [ ] **Step 4: Run tests, commit**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`. The App build is broken until Task 4 (ChatHost still calls `ToCss`). Only Core and its tests are built in this task.

```powershell
git add -A
git commit -m "feat(core): native theme tokens (hex colors, XAML font list)" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_017Gb9pmgys17JP4Z4m7ejtu"
```

---

### Task 3: Relative sizing, settings v4, data folder (Core)

**Files:**
- Modify: `src/Hotline.Core/Settings/HotlineSettings.cs`, `src/Hotline.Core/Settings/SettingsStore.cs`, `src/Hotline.Core/Windowing/PopupGeometry.cs`
- Create: `src/Hotline.Core/Settings/HotlinePaths.cs`
- Test: `tests/Hotline.Core.Tests/SizingSettingsTests.cs`, `tests/Hotline.Core.Tests/RelativeWidthTests.cs`, `tests/Hotline.Core.Tests/HotlinePathsTests.cs`; update `SettingsStoreTests.cs`, `ChatSettingsTests.cs`

**Interfaces:**
- Produces:
  - `WindowSettings`: `double WidthPercent = 40`, `int MinWidth = 600`, `int MaxWidth = 1000`, `int Height = 320`, `double MaxHeightPercent = 70` (Width removed)
  - `ChatSettings.MaxHeight` removed; `HotlineSettings.CurrentSchemaVersion = 4`
  - `static int PopupGeometry.RelativeWidthDip(int workAreaWidthPx, double scale, double percent, int minDip, int maxDip)`
  - `static string HotlinePaths.DataDirectory(string userProfile)` → `<profile>\.hotline`
  - `static bool HotlinePaths.MigrateFromLegacy(string legacyDir, string dataDir)` copies settings.json and history once; returns true if it migrated

- [ ] **Step 1: Write the failing tests**

`tests/Hotline.Core.Tests/RelativeWidthTests.cs`:
```csharp
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public class RelativeWidthTests
{
    [Theory]
    [InlineData(2880, 2.0, 40, 600, 1000, 600)]   // 1440 DIP wide screen → 576 → clamped up to 600
    [InlineData(3840, 1.0, 40, 600, 1000, 1000)]  // 3840 DIP → 1536 → clamped down to 1000
    [InlineData(2560, 1.0, 30, 600, 1000, 768)]   // 30% of 2560
    [InlineData(1920, 1.5, 50, 600, 1000, 640)]   // 1280 DIP * 50%
    public void Width_is_percent_of_work_area_in_dip_clamped(int workPx, double scale, double pct, int min, int max, int expected)
        => Assert.Equal(expected, PopupGeometry.RelativeWidthDip(workPx, scale, pct, min, max));

    [Fact]
    public void Same_fraction_across_scales()
        => Assert.Equal(PopupGeometry.RelativeWidthDip(3840, 2.0, 40, 100, 5000), PopupGeometry.RelativeWidthDip(1920, 1.0, 40, 100, 5000));

    [Fact]
    public void Max_below_min_uses_min()
        => Assert.Equal(700, PopupGeometry.RelativeWidthDip(1000, 1.0, 10, 700, 500));
}
```

`tests/Hotline.Core.Tests/SizingSettingsTests.cs`:
```csharp
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class SizingSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    private HotlineSettings LoadJson(string json)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, SettingsStore.FileName), json);
        return new SettingsStore(_dir).Load();
    }

    [Fact]
    public void Defaults()
    {
        var w = new SettingsStore(_dir).Load().Window;
        Assert.Equal((40.0, 600, 1000, 320, 70.0), (w.WidthPercent, w.MinWidth, w.MaxWidth, w.Height, w.MaxHeightPercent));
    }

    [Theory]
    [InlineData(1, 120)]
    [InlineData(2, 120)]
    [InlineData(3, 120)]
    [InlineData(1, 520)]
    public void Old_default_heights_migrate_to_320(int schema, int height)
        => Assert.Equal(320, LoadJson($$"""{ "schemaVersion": {{schema}}, "window": { "height": {{height}} } }""").Window.Height);

    [Fact]
    public void Custom_height_is_kept()
        => Assert.Equal(400, LoadJson("""{ "schemaVersion": 3, "window": { "height": 400 } }""").Window.Height);

    [Fact]
    public void Percentages_and_widths_are_clamped()
    {
        var w = LoadJson("""{ "window": { "widthPercent": 500, "maxHeightPercent": 5, "minWidth": 50, "maxWidth": 10 } }""").Window;
        Assert.Equal(90, w.WidthPercent);
        Assert.Equal(30, w.MaxHeightPercent);
        Assert.Equal(320, w.MinWidth);
        Assert.Equal(320, w.MaxWidth); // never below MinWidth
    }
}
```

`tests/Hotline.Core.Tests/HotlinePathsTests.cs`:
```csharp
using Hotline.Core.Settings;

namespace Hotline.Core.Tests;

public sealed class HotlinePathsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }

    [Fact]
    public void Data_directory_is_dot_hotline_in_profile()
        => Assert.Equal(Path.Combine(@"C:\Users\me", ".hotline"), HotlinePaths.DataDirectory(@"C:\Users\me"));

    [Fact]
    public void Migrates_settings_and_history_once()
    {
        var legacy = Path.Combine(_root, "legacy");
        var data = Path.Combine(_root, ".hotline");
        Directory.CreateDirectory(Path.Combine(legacy, "history"));
        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"x\":1}");
        File.WriteAllText(Path.Combine(legacy, "history", "c1.jsonl"), "line");

        Assert.True(HotlinePaths.MigrateFromLegacy(legacy, data));
        Assert.Equal("{\"x\":1}", File.ReadAllText(Path.Combine(data, "settings.json")));
        Assert.Equal("line", File.ReadAllText(Path.Combine(data, "history", "c1.jsonl")));

        File.WriteAllText(Path.Combine(legacy, "settings.json"), "{\"x\":2}");
        Assert.False(HotlinePaths.MigrateFromLegacy(legacy, data)); // new location wins from now on
        Assert.Equal("{\"x\":1}", File.ReadAllText(Path.Combine(data, "settings.json")));
    }

    [Fact]
    public void Nothing_to_migrate_is_fine()
        => Assert.False(HotlinePaths.MigrateFromLegacy(Path.Combine(_root, "none"), Path.Combine(_root, ".hotline")));
}
```

Update existing tests (exact replacements):
```python
# run from repo root: python - <<'EOF' ... EOF
def rw(p, pairs):
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        assert a in s, (p, a[:60]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8', newline='\n').write(s)

rw('tests/Hotline.Core.Tests/SettingsStoreTests.cs', [
    ("        Assert.Equal(560, s.Window.Width);\n        Assert.Equal(120, s.Window.Height);\n",
     "        Assert.Equal(40, s.Window.WidthPercent);\n        Assert.Equal(320, s.Window.Height);\n"),
    ('"""{ "window": { "width": 800 } }"""', '"""{ "window": { "minWidth": 800 } }"""'),
    ("        Assert.Equal(800, s.Window.Width);\n        Assert.Equal(120, s.Window.Height);\n",
     "        Assert.Equal(800, s.Window.MinWidth);\n        Assert.Equal(320, s.Window.Height);\n"),
    ('"""{ "window": { "width": 900 } }"""', '"""{ "window": { "minWidth": 900 } }"""'),
    ("        Assert.Equal(560, s.Window.Width);\n    }", "        Assert.Equal(600, s.Window.MinWidth);\n    }"),
    ('$$"""{ "window": { "width": {{width}} } }"""', '$$"""{ "window": { "minWidth": {{width}} } }"""'),
    ("Assert.Equal(expected, New().Load().Window.Width);", "Assert.Equal(expected, New().Load().Window.MinWidth);"),
    ("        Assert.Equal((560, 120), (s.Window.Width, s.Window.Height));", "        Assert.Equal(320, s.Window.Height);"),
    ("        Assert.Equal((700, 400), (s.Window.Width, s.Window.Height));", "        Assert.Equal(400, s.Window.Height);"),
    ('Assert.Contains("\\"height\\": 120", File.ReadAllText(path));', 'Assert.Contains("\\"height\\": 320", File.ReadAllText(path));'),
])
rw('tests/Hotline.Core.Tests/ChatSettingsTests.cs', [
    ("        Assert.Equal(560, s.Chat.MaxHeight);\n", ""),
])
s = open('tests/Hotline.Core.Tests/ChatSettingsTests.cs', encoding='utf-8').read()
i = s.index("    [Theory]\n    [InlineData(10, 160)]"); j = s.index("    [Fact]", i)
open('tests/Hotline.Core.Tests/ChatSettingsTests.cs', 'w', encoding='utf-8', newline='\n').write(s[:i] + s[j:])
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: build FAILS with CS1061/CS0117 for `WidthPercent`, `RelativeWidthDip`, `HotlinePaths`.

- [ ] **Step 3: Implement**

In `src/Hotline.Core/Settings/HotlineSettings.cs`:
- `CurrentSchemaVersion = 4`.
- In `WindowSettings`, replace the `Width`/`Height` properties (and their doc comments) with:
```csharp
    /// <summary>Panel width as a percentage of the monitor's work area (20–90), clamped to [MinWidth, MaxWidth] DIPs.</summary>
    public double WidthPercent { get; set; } = 40;
    public int MinWidth { get; set; } = 600;
    public int MaxWidth { get; set; } = 1000;
    /// <summary>Baseline panel height in DIPs, including space reserved above the composer.</summary>
    public int Height { get; set; } = 320;
    /// <summary>The panel grows upward with the conversation to at most this share of the screen height (30–95).</summary>
    public double MaxHeightPercent { get; set; } = 70;
```
- In `ChatSettings`, delete the `MaxHeight` property and its comment. Change the `GrowMode` doc comment to say "jump to the maximum height".

In `src/Hotline.Core/Settings/SettingsStore.cs`:
- In `Migrate`, replace the v1→v2 block (the 640/520 → 560/120 lines) with:
```csharp
        // → v4: the popup became a relative-width panel with a 320-DIP baseline. Move untouched old default heights.
        if (s.SchemaVersion < 4 && s.Window.Height is 120 or 520)
            s.Window.Height = 320;
```
- In `Normalize`, replace the `Width`/`Height` clamps and the `Chat.MaxHeight` clamp with:
```csharp
        s.Window.WidthPercent = Math.Clamp(s.Window.WidthPercent, 20, 90);
        s.Window.MinWidth = Math.Clamp(s.Window.MinWidth, 320, 4000);
        s.Window.MaxWidth = Math.Clamp(s.Window.MaxWidth, s.Window.MinWidth, 4000);
        s.Window.Height = Math.Clamp(s.Window.Height, 120, 4000);
        s.Window.MaxHeightPercent = Math.Clamp(s.Window.MaxHeightPercent, 30, 95);
```

Append to `PopupGeometry`:
```csharp
    /// <summary>Panel width in DIPs: percent of the work area (converted from physical pixels), clamped to [min, max].</summary>
    public static int RelativeWidthDip(int workAreaWidthPx, double scale, double percent, int minDip, int maxDip)
        => Math.Clamp((int)Math.Round(workAreaWidthPx / scale * percent / 100.0), minDip, Math.Max(minDip, maxDip));
```

`src/Hotline.Core/Settings/HotlinePaths.cs`:
```csharp
namespace Hotline.Core.Settings;

/// <summary>User-visible data folder (%USERPROFILE%\.hotline) for settings, logs and history.</summary>
public static class HotlinePaths
{
    public static string DataDirectory(string userProfile) => Path.Combine(userProfile, ".hotline");

    /// <summary>
    /// One-time move from the old package-private folder: copies settings.json and history\ when the new folder
    /// has no settings yet. The old files are left in place (harmless; removed with the package).
    /// </summary>
    public static bool MigrateFromLegacy(string legacyDir, string dataDir)
    {
        var legacySettings = Path.Combine(legacyDir, SettingsStore.FileName);
        var newSettings = Path.Combine(dataDir, SettingsStore.FileName);
        if (File.Exists(newSettings) || !File.Exists(legacySettings)) return false;

        Directory.CreateDirectory(dataDir);
        File.Copy(legacySettings, newSettings);
        var legacyHistory = Path.Combine(legacyDir, "history");
        if (Directory.Exists(legacyHistory))
        {
            var newHistory = Path.Combine(dataDir, "history");
            Directory.CreateDirectory(newHistory);
            foreach (var file in Directory.EnumerateFiles(legacyHistory))
                File.Copy(file, Path.Combine(newHistory, Path.GetFileName(file)), overwrite: false);
        }
        return true;
    }
}
```

- [ ] **Step 4: Run tests, commit**

Run: `dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj`
Expected: `failed: 0`.

```powershell
git add -A
git commit -m "feat(core): relative panel sizing, settings v4, ~/.hotline data folder with migration" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_017Gb9pmgys17JP4Z4m7ejtu"
```

---

### Task 4: Native chat panel (App) — replaces WebView2

**Files:**
- Delete: `src/Hotline.App/Web/` (whole folder), `tests/web/`, `src/Hotline.App/Chat/ChatHost.cs`, `src/Hotline.App/Chat/ChatHost.Attachments.cs`
- Modify: `src/Hotline.App/Hotline.App.csproj` (remove the `Web\**` line), `.github/workflows/ci.yml` (remove the setup-node and "Web tests" steps)
- Create: `src/Hotline.App/Chat/MarkdownRenderer.cs`, `src/Hotline.App/Chat/ChatPresenter.cs`, `src/Hotline.App/Chat/ChatPresenter.Attachments.cs` (stubs; filled in by Task 5)
- Replace: `src/Hotline.App/PopupWindow.xaml`, `src/Hotline.App/PopupWindow.xaml.cs`
- Modify: `src/Hotline.App/App.xaml.cs` (data folder, presenter), `tests/smoke/smoke.ps1` (`$state` → `~/.hotline`)

**Interfaces:**
- Consumes: `MarkdownModel`, `ThemeTokens`/`ThemeColor`, `PopupGeometry.RelativeWidthDip`, `HotlinePaths`, `ChatController`, `AttachmentTray`, `BackendFactory.IsAvailable`.
- Produces:
  - `PopupWindow(WindowSettings, GrowMode, PopupToggleGuard, FileLog, bool showDebugStatus)` with:
    - `Pinned`, `Modal()`, `HideForDialog()`, `WithHiddenAsync<T>`, `SetContentHeight(double dip)`
    - events `Shown`, `NewChatRequested`, `CaptureRequested`
    - internal named elements `MessagesScroll`, `MessagesPanel`, `NoticesPanel`, `Composer`, `ChipsPanel`, `PlusButton`, `AttachFilesItem`, `CaptureWindowItem`, `CaptureScreenItem`, `Input`, `SendButton`, `Toolbar`, `PinButton`, `CaptureWindowButton`, `CaptureScreenButton`, `BackendLabel`, `NewChatButton`, `SettingsButton`, `Root`
  - `ChatPresenter(PopupWindow, ChatController, AttachmentTray, HotlineSettings, SettingsStore, FileLog, Func<IReadOnlyList<BackendProfile>>, string dataDirectory)` with `void Initialize()` and `void NewChat()`
  - `static UIElement MarkdownRenderer.Render(IReadOnlyList<MdBlock>, RenderStyle)` with `sealed record RenderStyle(double FontSize, FontFamily Font, Brush Muted, Brush CodeBackground, double Radius, Action<string> OpenLink)`

- [ ] **Step 1: Remove the web view**

```powershell
git rm -r -q src/Hotline.App/Web tests/web src/Hotline.App/Chat/ChatHost.cs src/Hotline.App/Chat/ChatHost.Attachments.cs
```
In `src/Hotline.App/Hotline.App.csproj`, delete the line `<Content Include="Web\**" CopyToOutputDirectory="PreserveNewest" />`.
In `.github/workflows/ci.yml`, delete the `actions/setup-node@v4` step and the `Web tests` step (the two steps between "Unit tests" and "Build app (x64)").

- [ ] **Step 2: Popup layout (XAML)**

Replace `src/Hotline.App/PopupWindow.xaml` with:
```xml
<Window
    x:Class="Hotline.App.PopupWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="Hotline">
    <Grid x:Name="Root" x:FieldModifier="internal" Padding="12,10,12,10" RowSpacing="8"
          AllowDrop="True" KeyDown="Root_KeyDown">
        <Grid.Resources>
            <!-- Keep the composer TextBox see-through in every state so acrylic shows. -->
            <SolidColorBrush x:Key="TextControlBackground" Color="Transparent" />
            <SolidColorBrush x:Key="TextControlBackgroundPointerOver" Color="Transparent" />
            <SolidColorBrush x:Key="TextControlBackgroundFocused" Color="Transparent" />
            <SolidColorBrush x:Key="TextControlBorderBrush" Color="Transparent" />
            <SolidColorBrush x:Key="TextControlBorderBrushPointerOver" Color="Transparent" />
            <SolidColorBrush x:Key="TextControlBorderBrushFocused" Color="Transparent" />
            <Style x:Key="ToolbarButton" TargetType="Button" BasedOn="{StaticResource DefaultButtonStyle}">
                <Setter Property="Width" Value="36" /><Setter Property="Height" Value="32" />
                <Setter Property="Padding" Value="0" /><Setter Property="Background" Value="Transparent" />
                <Setter Property="BorderThickness" Value="0" /><Setter Property="FontFamily" Value="Segoe Fluent Icons" />
                <Setter Property="FontSize" Value="15" />
            </Style>
        </Grid.Resources>
        <Grid.RowDefinitions>
            <RowDefinition Height="*" />      <!-- messages; empty space reserved above the composer -->
            <RowDefinition Height="Auto" />   <!-- inline notices -->
            <RowDefinition Height="Auto" />   <!-- composer -->
            <RowDefinition Height="Auto" />   <!-- toolbar -->
        </Grid.RowDefinitions>

        <ScrollViewer x:Name="MessagesScroll" x:FieldModifier="internal" VerticalScrollBarVisibility="Auto"
                      HorizontalScrollMode="Disabled" HorizontalScrollBarVisibility="Disabled">
            <StackPanel x:Name="MessagesPanel" x:FieldModifier="internal" Spacing="12" Padding="2,4,10,4" VerticalAlignment="Bottom" />
        </ScrollViewer>
        <TextBlock x:Name="StatusText" Margin="2,0" VerticalAlignment="Top" Opacity="0.6" IsHitTestVisible="False"
                   Style="{StaticResource CaptionTextBlockStyle}" Visibility="Collapsed" />

        <StackPanel x:Name="NoticesPanel" x:FieldModifier="internal" Grid.Row="1" Spacing="6" />

        <Border x:Name="Composer" x:FieldModifier="internal" Grid.Row="2" CornerRadius="12" Padding="6" BorderThickness="1"
                Background="{ThemeResource ControlFillColorDefaultBrush}" BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}">
            <StackPanel Spacing="6">
                <StackPanel x:Name="ChipsPanel" x:FieldModifier="internal" Orientation="Horizontal" Spacing="6" Visibility="Collapsed" />
                <Grid ColumnSpacing="6">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="Auto" /><ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" />
                    </Grid.ColumnDefinitions>
                    <Button x:Name="PlusButton" x:FieldModifier="internal" Width="44" Height="44" Padding="0" CornerRadius="10"
                            FontFamily="Segoe Fluent Icons" FontSize="20" Content="&#xE710;" ToolTipService.ToolTip="Add files or capture"
                            VerticalAlignment="Bottom">
                        <Button.Flyout>
                            <MenuFlyout Placement="TopEdgeAlignedLeft">
                                <MenuFlyoutItem x:Name="AttachFilesItem" x:FieldModifier="internal" Text="Attach files…">
                                    <MenuFlyoutItem.Icon><FontIcon Glyph="&#xE723;" /></MenuFlyoutItem.Icon>
                                </MenuFlyoutItem>
                                <MenuFlyoutItem x:Name="CaptureWindowItem" x:FieldModifier="internal" Text="Capture window">
                                    <MenuFlyoutItem.Icon><FontIcon Glyph="&#xE7C4;" /></MenuFlyoutItem.Icon>
                                </MenuFlyoutItem>
                                <MenuFlyoutItem x:Name="CaptureScreenItem" x:FieldModifier="internal" Text="Capture screen">
                                    <MenuFlyoutItem.Icon><FontIcon Glyph="&#xE7F4;" /></MenuFlyoutItem.Icon>
                                </MenuFlyoutItem>
                            </MenuFlyout>
                        </Button.Flyout>
                    </Button>
                    <TextBox x:Name="Input" x:FieldModifier="internal" Grid.Column="1" PlaceholderText="📞 Hotline"
                             AcceptsReturn="True" TextWrapping="Wrap" MaxHeight="180" VerticalAlignment="Center"
                             ScrollViewer.VerticalScrollBarVisibility="Auto" />
                    <Button x:Name="SendButton" x:FieldModifier="internal" Grid.Column="2" Width="44" Height="44" Padding="0"
                            CornerRadius="10" Style="{StaticResource AccentButtonStyle}" FontFamily="Segoe Fluent Icons"
                            FontSize="16" Content="&#xE724;" ToolTipService.ToolTip="Send (Enter)" VerticalAlignment="Bottom" />
                </Grid>
            </StackPanel>
        </Border>

        <!-- Bottom toolbar: room for more features (model/effort picker arrives in Plan 3b). -->
        <Grid x:Name="Toolbar" x:FieldModifier="internal" Grid.Row="3" ColumnSpacing="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
                <ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" /><ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>
            <ToggleButton x:Name="PinButton" x:FieldModifier="internal" Width="36" Height="32" Padding="0" BorderThickness="0"
                          Background="Transparent" FontFamily="Segoe Fluent Icons" FontSize="15" Content="&#xE718;"
                          ToolTipService.ToolTip="Pin open (stays open when you click elsewhere, so you can drag files in)" />
            <Button x:Name="CaptureWindowButton" x:FieldModifier="internal" Grid.Column="1" Style="{StaticResource ToolbarButton}"
                    Content="&#xE7C4;" ToolTipService.ToolTip="Capture the window you were in" />
            <Button x:Name="CaptureScreenButton" x:FieldModifier="internal" Grid.Column="2" Style="{StaticResource ToolbarButton}"
                    Content="&#xE7F4;" ToolTipService.ToolTip="Capture the whole screen" />
            <TextBlock x:Name="BackendLabel" x:FieldModifier="internal" Grid.Column="4" VerticalAlignment="Center" Margin="6,0"
                       Style="{StaticResource CaptionTextBlockStyle}" Opacity="0.7" />
            <Button x:Name="NewChatButton" x:FieldModifier="internal" Grid.Column="5" Style="{StaticResource ToolbarButton}"
                    Content="&#xE8BD;" ToolTipService.ToolTip="New chat (Ctrl+N)" />
            <Button x:Name="SettingsButton" x:FieldModifier="internal" Grid.Column="6" Style="{StaticResource ToolbarButton}"
                    Content="&#xE713;" ToolTipService.ToolTip="Settings" />
        </Grid>
    </Grid>
</Window>
```

- [ ] **Step 3: Popup code-behind**

Replace `src/Hotline.App/PopupWindow.xaml.cs` with:
```csharp
using Hotline.App.Interop;
using Hotline.Core.Activation;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Windowing;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace Hotline.App;

/// <summary>Native acrylic chat panel. Created hidden at startup so showing it is instant.</summary>
public sealed partial class PopupWindow : Window
{
    private readonly WindowSettings _settings;
    private readonly GrowMode _growMode;
    private readonly PopupToggleGuard _guard;
    private readonly FileLog _log;
    private int _modal;
    private RectI _bar;
    private RectI _work;
    private double _scale = 1;
    private int _contentPx;

    public nint Hwnd { get; }
    /// <summary>The window the user was in before the popup appeared (target for monitor choice and capture).</summary>
    public nint PreviousForeground { get; private set; }
    /// <summary>Pinned: stays open when it loses focus (so files can be dragged in from Explorer).</summary>
    public bool Pinned { get; set; }

    public event Action? Shown;
    public event Action? NewChatRequested;
    public event Action<bool>? CaptureRequested;

    public PopupWindow(WindowSettings settings, GrowMode growMode, PopupToggleGuard guard, FileLog log, bool showDebugStatus)
    {
        _settings = settings;
        _growMode = growMode;
        _guard = guard;
        _log = log;
        InitializeComponent();
        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        SystemBackdrop = Backdrops.Create(settings);
        ExtendsContentIntoTitleBar = true;

        var presenter = OverlappedPresenter.Create();
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        presenter.IsAlwaysOnTop = settings.AlwaysOnTop;
        presenter.SetBorderAndTitleBar(hasBorder: true, hasTitleBar: false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        AppWindow.SetIcon("Assets\\Hotline.ico");
        AppWindow.Closing += (_, e) => { e.Cancel = true; HidePopup(); };

        Root.RequestedTheme = settings.Theme switch
        {
            ThemeChoice.Light => ElementTheme.Light,
            ThemeChoice.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (settings.Backdrop == BackdropKind.Solid)
            Root.Background = (Brush)Application.Current.Resources["SolidBackgroundFillColorBaseBrush"];
        if (showDebugStatus)
            StatusText.Visibility = Visibility.Visible;
        MessagesScroll.VerticalScrollBarVisibility = settings.Scrollbar switch
        {
            ScrollbarStyle.Visible => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Visible,
            ScrollbarStyle.Hidden => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Hidden,
            _ => Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
        };

        Activated += OnActivated;
    }

    public bool IsShown => AppWindow.IsVisible;

    public void ShowPopup()
    {
        var fg = Native.GetForegroundWindow();
        if (fg != Hwnd && fg != 0 && !ShellSurfaces.IsShell(Native.ClassNameOf(fg)))
            PreviousForeground = fg; // keep the last real app window; taskbar/desktop/flyouts don't count

        PlaceOnActiveMonitor();
        Activate();
        Native.SetForegroundWindow(Hwnd);
        Shown?.Invoke();
    }

    public void HidePopup()
    {
        if (!AppWindow.IsVisible) return;
        AppWindow.Hide();
        _guard.NoteHidden();
    }

    /// <summary>Hides for a system dialog (file picker) without counting as a user dismissal.</summary>
    public void HideForDialog() => AppWindow.Hide();

    public void Toggle()
    {
        if (_guard.ShouldShowOnToggle(AppWindow.IsVisible)) ShowPopup();
        else HidePopup();
    }

    public void SetStatus(string text) => StatusText.Text = text;

    public void RequestNewChat() => NewChatRequested?.Invoke();
    public void RequestCapture(bool window) => CaptureRequested?.Invoke(window);

    /// <summary>Content height (DIPs) of the conversation; the panel grows upward from its baseline.</summary>
    public void SetContentHeight(double dip)
    {
        _contentPx = (int)Math.Ceiling(dip * _scale);
        if (AppWindow.IsVisible) ApplyHeight();
    }

    /// <summary>While held, focus loss (dialogs, captures) does not hide the popup.</summary>
    public IDisposable Modal()
    {
        _modal++;
        return new Releaser(() => _modal--);
    }

    /// <summary>Hides the popup briefly (e.g. to capture what is underneath), then brings it back.</summary>
    public async Task<T> WithHiddenAsync<T>(Func<Task<T>> work)
    {
        using var _ = Modal();
        AppWindow.Hide();
        await Task.Delay(220); // let DWM repaint without the popup
        try { return await work(); }
        finally
        {
            Activate();
            Native.SetForegroundWindow(Hwnd);
            Shown?.Invoke();
        }
    }

    private void OnActivated(object sender, WindowActivatedEventArgs e)
    {
        if (e.WindowActivationState == WindowActivationState.Deactivated && _settings.HideOnBlur && _modal == 0 && !Pinned)
            HidePopup();
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            HidePopup();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.N && InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down)
                 && !InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
        {
            RequestNewChat();
            e.Handled = true;
        }
    }

    private void ApplyHeight()
    {
        if (_bar.Width == 0) return;
        var maxPx = (int)Math.Round(_work.Height * _settings.MaxHeightPercent / 100.0);
        var r = PopupGeometry.GrowUp(_bar, _contentPx, maxPx, _work, _growMode);
        AppWindow.MoveAndResize(new RectInt32(r.X, r.Y, r.Width, r.Height));
    }

    private void PlaceOnActiveMonitor()
    {
        // A remembered window may have closed since (stale handle): its monitor/DPI lookups then fail and the
        // popup came out at 100% scale ("occasionally small"). Only trust live windows; otherwise use the cursor.
        if (PreviousForeground != 0 && !Native.IsWindow(PreviousForeground))
            PreviousForeground = 0;
        var anchor = PreviousForeground;
        DisplayArea? area = null;
        nint monitor = 0;
        if (anchor != 0)
        {
            area = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(anchor), DisplayAreaFallback.Nearest);
            monitor = Native.MonitorFromWindow(anchor, Native.MONITOR_DEFAULTTONEAREST);
        }
        if (area is null || monitor == 0)
        {
            if (anchor != 0) _log.Error($"no display area for previous foreground window 0x{anchor:X} ({Native.ClassNameOf(anchor)}); using cursor monitor");
            Native.GetCursorPos(out var pt);
            area = DisplayArea.GetFromPoint(new PointInt32(pt.X, pt.Y), DisplayAreaFallback.Nearest) ?? DisplayArea.Primary;
            monitor = Native.MonitorFromPoint(pt, Native.MONITOR_DEFAULTTONEAREST);
        }
        var wa = area.WorkArea;
        var scale = Native.GetDpiForMonitor(monitor, Native.MDT_EFFECTIVE_DPI, out var dpi, out _) == 0 ? dpi / 96.0 : 1.0;

        var widthDip = PopupGeometry.RelativeWidthDip(wa.Width, scale, _settings.WidthPercent, _settings.MinWidth, _settings.MaxWidth);
        var r = PopupGeometry.Place(new RectI(wa.X, wa.Y, wa.Width, wa.Height), widthDip, _settings.Height, scale, _settings.VerticalPosition);
        _log.Debug($"place: anchor=0x{anchor:X} ({Native.ClassNameOf(anchor)}) workArea={wa.X},{wa.Y} {wa.Width}x{wa.Height} scale={scale} -> {r}");
        (_bar, _work, _scale) = (r, new RectI(wa.X, wa.Y, wa.Width, wa.Height), scale);
        ApplyHeight();
    }

    private sealed class Releaser(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}
```

- [ ] **Step 4: Markdown renderer**

`src/Hotline.App/Chat/MarkdownRenderer.cs`:
```csharp
using Hotline.Core.Text;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace Hotline.App.Chat;

internal sealed record RenderStyle(double FontSize, FontFamily Font, Brush Muted, Brush CodeBackground, double Radius, Action<string> OpenLink);

/// <summary>Renders the Core markdown block model as native, selectable WinUI text.</summary>
internal static class MarkdownRenderer
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static UIElement Render(IReadOnlyList<MdBlock> blocks, RenderStyle style)
    {
        var panel = new StackPanel { Spacing = 8 };
        foreach (var block in blocks) panel.Children.Add(RenderBlock(block, style));
        return panel;
    }

    private static UIElement RenderBlock(MdBlock block, RenderStyle s) => block switch
    {
        MdParagraph p => Rich(p.Inlines, s, s.FontSize, bold: false),
        MdHeading h => Rich(h.Inlines, s, s.FontSize + h.Level switch { 1 => 6, 2 => 4, _ => 2 }, bold: true),
        MdCode c => Code(c, s),
        MdList l => List(l, s),
        MdQuote q => new Border
        {
            BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(10, 0, 0, 0),
            BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"], Child = Render(q.Blocks, s),
        },
        MdRule => new Border { Height = 1, Margin = new Thickness(0, 4, 0, 4), Background = s.Muted, Opacity = 0.4 },
        MdTable t => Table(t, s),
        _ => new TextBlock(),
    };

    private static RichTextBlock Rich(IReadOnlyList<MdInline> inlines, RenderStyle s, double size, bool bold)
    {
        var rtb = new RichTextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = size, FontFamily = s.Font };
        var paragraph = new Paragraph();
        foreach (var inline in inlines) paragraph.Inlines.Add(ToInline(inline, s, bold));
        rtb.Blocks.Add(paragraph);
        return rtb;
    }

    private static Inline ToInline(MdInline inline, RenderStyle s, bool bold) => inline switch
    {
        MdText t => ToRun(t, bold),
        MdBreak => new LineBreak(),
        MdLink l => ToLink(l, s, bold),
        _ => new Run(),
    };

    private static Run ToRun(MdText t, bool bold)
    {
        var run = new Run { Text = t.Text };
        if (bold || t.Style.HasFlag(MdStyle.Bold)) run.FontWeight = FontWeights.SemiBold;
        if (t.Style.HasFlag(MdStyle.Italic)) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
        if (t.Style.HasFlag(MdStyle.Strike)) run.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
        if (t.Style.HasFlag(MdStyle.Code)) run.FontFamily = Mono;
        return run;
    }

    private static Hyperlink ToLink(MdLink link, RenderStyle s, bool bold)
    {
        var h = new Hyperlink();
        foreach (var child in link.Inlines.OfType<MdText>()) h.Inlines.Add(ToRun(child, bold));
        h.Click += (_, _) => s.OpenLink(link.Url);
        ToolTipService.SetToolTip(h, link.Url);
        return h;
    }

    private static UIElement Code(MdCode code, RenderStyle s)
    {
        var text = new TextBlock { Text = code.Code, FontFamily = Mono, FontSize = s.FontSize - 1, IsTextSelectionEnabled = true };
        var scroll = new ScrollViewer
        {
            Content = text, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(10, 8, 40, 8),
        };
        var copy = new Button
        {
            Content = new FontIcon { Glyph = "\uE8C8", FontSize = 13 }, Padding = new Thickness(6), Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4),
        };
        ToolTipService.SetToolTip(copy, "Copy");
        copy.Click += (_, _) =>
        {
            var package = new DataPackage();
            package.SetText(code.Code);
            Clipboard.SetContent(package);
        };
        var grid = new Grid();
        grid.Children.Add(scroll);
        grid.Children.Add(copy);
        var label = code.Language is null ? null : new TextBlock { Text = code.Language, FontSize = 11, Foreground = s.Muted, Margin = new Thickness(10, 6, 0, 0) };
        var stack = new StackPanel();
        if (label is not null) stack.Children.Add(label);
        stack.Children.Add(grid);
        return new Border { Background = s.CodeBackground, CornerRadius = new CornerRadius(s.Radius), Child = stack };
    }

    private static UIElement List(MdList list, RenderStyle s)
    {
        var panel = new StackPanel { Spacing = 4 };
        for (var i = 0; i < list.Items.Count; i++)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var marker = new TextBlock { Text = list.Ordered ? $"{list.Start + i}." : "•", FontSize = s.FontSize, FontFamily = s.Font, Foreground = s.Muted };
            var content = Render(list.Items[i], s);
            Grid.SetColumn((FrameworkElement)content, 1);
            row.Children.Add(marker);
            row.Children.Add(content);
            panel.Children.Add(row);
        }
        return panel;
    }

    private static UIElement Table(MdTable table, RenderStyle s)
    {
        var columns = table.Rows.Count == 0 ? 0 : table.Rows.Max(r => r.Count);
        var grid = new Grid { ColumnSpacing = 16, RowSpacing = 4 };
        for (var c = 0; c < columns; c++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var r = 0; r < table.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var c = 0; c < table.Rows[r].Count; c++)
            {
                var cell = Rich(table.Rows[r][c], s, s.FontSize, bold: table.HasHeader && r == 0);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }
        return new ScrollViewer
        {
            Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollMode = ScrollMode.Disabled,
        };
    }
}
```

- [ ] **Step 5: Chat presenter**

`src/Hotline.App/Chat/ChatPresenter.cs`:
```csharp
using System.Diagnostics;
using Hotline.Core.Backends;
using Hotline.Core.Chat;
using Hotline.Core.Diagnostics;
using Hotline.Core.Settings;
using Hotline.Core.Text;
using Hotline.Core.Theming;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;
using Windows.UI.Core;

namespace Hotline.App.Chat;

/// <summary>Connects the native chat panel to the ChatController. All members run on the UI thread.</summary>
internal sealed partial class ChatPresenter(
    PopupWindow popup, ChatController chat, AttachmentTray tray, HotlineSettings settings, SettingsStore store, FileLog log,
    Func<IReadOnlyList<BackendProfile>> profiles, string dataDirectory)
{
    private sealed class AssistantView
    {
        public required Border Body { get; init; }
        public required StackPanel Container { get; init; }
        public string Text { get; set; } = "";
        public bool Streaming { get; set; } = true;
        public bool Dirty { get; set; }
    }

    private readonly Dictionary<string, AssistantView> _assistants = [];
    private DispatcherQueueTimerWrapper? _renderTimer;
    private RenderStyle _style = null!;
    private ThemeTokens _tokens = ThemeTokens.Dark;

    public void Initialize()
    {
        var dark = settings.Window.Theme switch
        {
            ThemeChoice.Dark => true,
            ThemeChoice.Light => false,
            _ => Application.Current.RequestedTheme == ApplicationTheme.Dark,
        };
        _tokens = ThemeTokens.For(dark, settings.Window);
        _style = new RenderStyle(_tokens.FontSizePx, new FontFamily(_tokens.Font), Brush(_tokens.Muted), Brush(_tokens.CodeBackground), _tokens.RadiusPx, OpenLink);
        popup.Input.FontSize = _tokens.FontSizePx;
        popup.Input.FontFamily = _style.Font;

        chat.Event += e => Guard("chat event", () => OnChatEvent(e));
        popup.Shown += () => popup.Input.Focus(FocusState.Programmatic);
        popup.NewChatRequested += NewChat;
        popup.CaptureRequested += window => Run("capture", () => CaptureAsync(window));

        popup.Input.PreviewKeyDown += Input_PreviewKeyDown;
        popup.Input.Paste += Input_Paste;
        popup.SendButton.Click += (_, _) => Run("send", SendAsync);
        popup.AttachFilesItem.Click += (_, _) => Run("pick files", PickFilesAsync);
        popup.CaptureWindowItem.Click += (_, _) => Run("capture window", () => CaptureAsync(window: true));
        popup.CaptureScreenItem.Click += (_, _) => Run("capture screen", () => CaptureAsync(window: false));
        popup.CaptureWindowButton.Click += (_, _) => Run("capture window", () => CaptureAsync(window: true));
        popup.CaptureScreenButton.Click += (_, _) => Run("capture screen", () => CaptureAsync(window: false));
        popup.PinButton.Checked += (_, _) => { popup.Pinned = true; popup.PinButton.Content = "\uE840"; };
        popup.PinButton.Unchecked += (_, _) => { popup.Pinned = false; popup.PinButton.Content = "\uE718"; };
        popup.NewChatButton.Click += (_, _) => NewChat();
        popup.SettingsButton.Click += (_, _) => OpenSettings();
        popup.Root.DragOver += Root_DragOver;
        popup.Root.Drop += Root_Drop;

        popup.MessagesPanel.SizeChanged += (_, _) => ReportHeight();
        popup.NoticesPanel.SizeChanged += (_, _) => ReportHeight();
        popup.Composer.SizeChanged += (_, _) => ReportHeight();

        _renderTimer = new DispatcherQueueTimerWrapper(popup.DispatcherQueue, TimeSpan.FromMilliseconds(50), RenderDirty);
        UpdateBackendLabel();
        log.Info("chat view ready");
    }

    public void NewChat()
    {
        chat.NewChat();
        tray.TakeAll();
        RefreshChips();
    }

    // ---- composer ---------------------------------------------------------------------------

    private void Input_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;
        var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);
        if (shift) return; // newline
        e.Handled = true;
        Run("send", SendAsync);
    }

    private async Task SendAsync()
    {
        if (chat.IsBusy) { chat.Cancel(); return; }
        var text = popup.Input.Text.Trim();
        if (text.Length == 0 && tray.Items.Count == 0) return;
        if (!chat.CanAccept(tray.Items, out var reason))
        {
            Notice(reason!, InfoBarSeverity.Warning); // draft stays in the box
            return;
        }
        var attachments = tray.TakeAll();
        RefreshChips();
        popup.Input.Text = "";
        log.Info($"chat send via {chat.BackendId}: {text.Length} chars, {attachments.Count} attachment(s)");
        await chat.SendAsync(text, attachments);
    }

    private void SetBusy(bool busy)
    {
        popup.SendButton.Content = busy ? "\uE71A" : "\uE724";
        ToolTipService.SetToolTip(popup.SendButton, busy ? "Stop" : "Send (Enter)");
    }

    // ---- conversation -----------------------------------------------------------------------

    private void OnChatEvent(ChatEvent e)
    {
        switch (e)
        {
            case UserMessageAdded u:
                AddUser(u.Message);
                break;
            case AssistantStarted s:
                StartAssistant(s.Id, s.BackendName);
                SetBusy(true);
                break;
            case AssistantDelta d when _assistants.TryGetValue(d.Id, out var view):
                view.Text = d.Replace ? d.Text : view.Text + d.Text;
                view.Dirty = true;
                _renderTimer?.Start();
                break;
            case AssistantCompleted c:
                Finish(c.Id);
                log.Info("chat answer completed");
                break;
            case AssistantCancelled c:
                Finish(c.Id);
                break;
            case AssistantFailed f:
                log.Error($"chat answer failed: {f.Kind}: {f.Message}");
                if (!_assistants.ContainsKey(f.Id)) StartAssistant(f.Id, "");
                Finish(f.Id);
                ShowError(f.Id, f.Message);
                break;
            case ConversationReset:
                _assistants.Clear();
                popup.MessagesPanel.Children.Clear();
                popup.NoticesPanel.Children.Clear();
                SetBusy(false);
                break;
        }
    }

    private void AddUser(ChatMessage message)
    {
        var text = new TextBlock { Text = message.Text, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = _tokens.FontSizePx, FontFamily = _style.Font };
        var stack = new StackPanel { Spacing = 4 };
        if (message.Text.Length > 0) stack.Children.Add(text);
        if (message.Attachments.Count > 0)
            stack.Children.Add(new TextBlock
            {
                Text = string.Join("   ", message.Attachments.Select(a => "📎 " + a.Name)), FontSize = _tokens.FontSizePx - 2, Foreground = _style.Muted, TextWrapping = TextWrapping.Wrap,
            });
        popup.MessagesPanel.Children.Add(new Border
        {
            Child = stack, Background = Brush(_tokens.UserBubble), CornerRadius = new CornerRadius(_tokens.RadiusPx + 2),
            Padding = new Thickness(12, 8, 12, 8), HorizontalAlignment = HorizontalAlignment.Right, MaxWidth = 720,
        });
        ScrollToEnd(force: true);
    }

    private void StartAssistant(string id, string backendName)
    {
        var container = new StackPanel { Spacing = 2 };
        if (backendName.Length > 0)
            container.Children.Add(new TextBlock { Text = backendName, FontSize = 11, Foreground = _style.Muted });
        var body = new Border();
        container.Children.Add(body);
        popup.MessagesPanel.Children.Add(container);
        _assistants[id] = new AssistantView { Body = body, Container = container, Dirty = true };
        _renderTimer?.Start();
    }

    private void Finish(string id)
    {
        if (_assistants.TryGetValue(id, out var view))
        {
            view.Streaming = false;
            view.Dirty = true;
            RenderDirty();
        }
        SetBusy(false);
    }

    private void ShowError(string id, string message)
    {
        if (!_assistants.TryGetValue(id, out var view)) return;
        var bar = new InfoBar { IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Error, Message = message };
        var retry = new Button { Content = "Retry" };
        retry.Click += (_, _) => { bar.IsOpen = false; Run("retry", chat.RetryAsync); };
        bar.ActionButton = retry;
        view.Container.Children.Add(bar);
        ScrollToEnd(force: false);
    }

    private void RenderDirty()
    {
        var any = false;
        foreach (var view in _assistants.Values.Where(v => v.Dirty))
        {
            view.Dirty = false;
            any = true;
            var text = view.Streaming ? view.Text + " ▍" : view.Text;
            view.Body.Child = MarkdownRenderer.Render(MarkdownModel.Parse(text), _style);
        }
        if (!any) _renderTimer?.Stop();
        else ScrollToEnd(force: false);
    }

    // ---- notices, height, scrolling ---------------------------------------------------------

    private void Notice(string message, InfoBarSeverity severity)
    {
        var bar = new InfoBar { IsOpen = true, IsClosable = true, Severity = severity, Message = message };
        bar.Closed += (_, _) => popup.NoticesPanel.Children.Remove(bar);
        popup.NoticesPanel.Children.Add(bar);
        while (popup.NoticesPanel.Children.Count > 3) popup.NoticesPanel.Children.RemoveAt(0);
        var timer = popup.DispatcherQueue.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(10);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => popup.NoticesPanel.Children.Remove(bar);
        timer.Start();
    }

    private void ReportHeight()
    {
        // Messages area (natural height) + notices + composer + toolbar + spacing (3×8) + root padding (20).
        var messages = popup.MessagesPanel.Children.Count == 0 ? 0 : popup.MessagesPanel.ActualHeight;
        var dip = messages + popup.NoticesPanel.ActualHeight + popup.Composer.ActualHeight + popup.Toolbar.ActualHeight + 24 + 20;
        popup.SetContentHeight(dip);
    }

    private void ScrollToEnd(bool force)
    {
        var sv = popup.MessagesScroll;
        var nearBottom = sv.ScrollableHeight - sv.VerticalOffset < 48;
        if (!force && !nearBottom) return;
        popup.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => sv.ChangeView(null, sv.ScrollableHeight, null, disableAnimation: true));
    }

    // ---- toolbar ----------------------------------------------------------------------------

    private void UpdateBackendLabel()
    {
        var profile = profiles().FirstOrDefault(p => p.Id == chat.BackendId);
        popup.BackendLabel.Text = profile is null ? "" :
            profile.Name + (string.IsNullOrWhiteSpace(profile.Model) ? "" : $" · {profile.Model}") + (string.IsNullOrWhiteSpace(profile.Effort) ? "" : $" · {profile.Effort}");
    }

    /// <summary>Until the settings window (Plan 3b) exists: open ~/.hotline so settings.json is one click away.</summary>
    private void OpenSettings()
    {
        Directory.CreateDirectory(dataDirectory);
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{store.FilePath}\"") { UseShellExecute = true });
        popup.HidePopup();
    }

    private void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme is "https" or "http" or "mailto"))
            _ = Launcher.LaunchUriAsync(uri);
    }

    // ---- helpers ----------------------------------------------------------------------------

    private static SolidColorBrush Brush(string hex)
    {
        var (a, r, g, b) = ThemeColor.Parse(hex);
        return new SolidColorBrush(Color.FromArgb(a, r, g, b));
    }

    private void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { log.Error($"{what} failed", ex); }
    }

    private void Run(string what, Func<Task> work) => _ = RunAsync(what, work);

    private async Task RunAsync(string what, Func<Task> work)
    {
        try { await work(); }
        catch (Exception ex)
        {
            log.Error($"{what} failed", ex);
            Notice($"Couldn't {what}: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    /// <summary>Re-arms a DispatcherQueueTimer only when stopped (cheap Start() calls while streaming).</summary>
    private sealed class DispatcherQueueTimerWrapper
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _timer;
        public DispatcherQueueTimerWrapper(Microsoft.UI.Dispatching.DispatcherQueue queue, TimeSpan interval, Action tick)
        {
            _timer = queue.CreateTimer();
            _timer.Interval = interval;
            _timer.IsRepeating = true;
            _timer.Tick += (_, _) => tick();
        }
        public void Start() { if (!_timer.IsRunning) _timer.Start(); }
        public void Stop() => _timer.Stop();
    }

    // Attachments, paste, drag-drop and capture: ChatPresenter.Attachments.cs (Task 5).
    private partial Task PickFilesAsync();
    private partial Task CaptureAsync(bool window);
    private partial void Input_Paste(object sender, TextControlPasteEventArgs e);
    private partial void Root_DragOver(object sender, DragEventArgs e);
    private partial void Root_Drop(object sender, DragEventArgs e);
    private partial void RefreshChips();
}
```

`src/Hotline.App/Chat/ChatPresenter.Attachments.cs` (temporary; Task 5 replaces it):
```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Hotline.App.Chat;

internal sealed partial class ChatPresenter
{
    private partial Task PickFilesAsync() { Notice("Attaching files arrives in the next step.", InfoBarSeverity.Informational); return Task.CompletedTask; }
    private partial Task CaptureAsync(bool window) { Notice("Capture arrives in the next step.", InfoBarSeverity.Informational); return Task.CompletedTask; }
    private partial void Input_Paste(object sender, TextControlPasteEventArgs e) { }
    private partial void Root_DragOver(object sender, DragEventArgs e) { }
    private partial void Root_Drop(object sender, DragEventArgs e) { }
    private partial void RefreshChips() => popup.ChipsPanel.Visibility = tray.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
}
```

- [ ] **Step 6: App wiring**

In `src/Hotline.App/App.xaml.cs`:
1. Replace the `DataDirectory` property with:
```csharp
    /// <summary>Package-private folder (agy workspace); falls back for unpackaged dev runs.</summary>
    internal static string PrivateDirectory
    {
        get
        {
            try { return Windows.Storage.ApplicationData.Current.LocalFolder.Path; }
            catch (Exception) // no package identity: running unpackaged (dev smoke run)
            { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hotline"); }
        }
    }

    /// <summary>User-visible data folder: %USERPROFILE%\.hotline (settings.json, logs, history).</summary>
    internal static string DataDirectory => HotlinePaths.DataDirectory(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
```
2. At the start of `OnLaunched`, replace `var dataDir = DataDirectory;` with:
```csharp
        var dataDir = DataDirectory;
        var privateDir = PrivateDirectory;
        bool migrated;
        try { migrated = HotlinePaths.MigrateFromLegacy(privateDir, dataDir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { migrated = false; Debug.WriteLine(ex); }
```
   After the line that logs `starting ...`, add `if (migrated) _log.Info($"migrated settings and history from {privateDir} to {dataDir}");`.
3. Change the `AgyWorkspace` path to `Path.Combine(privateDir, "agy-workspace")`.
4. Change popup construction to `_popup = new PopupWindow(settings.Window, settings.Chat.GrowMode, new PopupToggleGuard(TimeProvider.System, TimeSpan.FromMilliseconds(300)), _log, showDebugStatus: _log.Verbose);`
5. Replace the `_chatHost` field and its type with `private ChatPresenter? _presenter;`.
6. Replace the `_chatHost = new ChatHost(...)` line and `_ = InitChatAsync(dataDir);` with:
```csharp
        _presenter = new ChatPresenter(_popup, chat, new AttachmentTray(new AttachmentLimits()), settings, store, _log, () => settings.Chat.Backends, dataDir);
        try { _presenter.Initialize(); }
        catch (Exception ex) { _log.Error("chat panel failed to initialize", ex); }
```
7. Delete the `InitChatAsync` method.
8. In the tray `onOpenSettings` lambda, keep opening `store.FilePath` (unchanged).

In `tests/smoke/smoke.ps1`, point the data paths at the new folder: replace
`$state = "$env:LOCALAPPDATA\Packages\$pfn\LocalState"` with `$state = Join-Path $env:USERPROFILE '.hotline'`
(settings.json and logs\hotline.log now live there).

- [ ] **Step 7: Build, install, verify**

Run:
```powershell
dotnet build src/Hotline.App/Hotline.App.csproj -c Debug -p:Platform=x64
dotnet test --project tests/Hotline.Core.Tests/Hotline.Core.Tests.csproj
powershell -File tests\smoke\smoke.ps1 -Install -WithAgy
```
Expected: `Build succeeded.`, `failed: 0`, `Smoke test passed.` (the smoke test's `chat view loads` check matches the presenter's `chat view ready` log line).

Then verify by screenshot, with a colorful window behind the popup:
1. The panel is **translucent**: the colours blur through acrylic.
2. The panel is about 40% of the screen width and 320 DIP tall, with the composer at the bottom and empty space above it.
3. The + button is 44 DIP, and the toolbar shows pin, two capture buttons, the backend label, new chat and settings.
4. Ask "Write a short markdown answer with a heading, a bulleted list, a fenced C# code block and a 2×2 table". It streams smoothly and renders all of those natively; the code block's copy button works.
5. `%USERPROFILE%\.hotline\settings.json` exists (migrated) with `"schemaVersion": 4` and `"height": 320`.
6. Get-Process `msedgewebview2` shows no process belonging to Hotline.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m "feat(app): native WinUI chat panel (acrylic), relative sizing, toolbar; remove WebView2" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_017Gb9pmgys17JP4Z4m7ejtu"
```

---

### Task 5: Native attachments — file dialog, paste, drag-drop, capture, chips (App)

**Files:**
- Replace: `src/Hotline.App/Chat/ChatPresenter.Attachments.cs`
- Modify: `README.md`, `tests/smoke/smoke.ps1` (nothing to add beyond Task 4 — verify only)

**Interfaces:**
- Consumes: `AttachmentFactory`, `AttachmentTray`, `ImageProcessor`, `ScreenCapture`, `PopupWindow.Modal/HideForDialog/WithHiddenAsync/Pinned`.
- Produces: the attachment flows; chips with thumbnail and remove button; inline notices for rejections.

- [ ] **Step 1: Implementation**

Replace `src/Hotline.App/Chat/ChatPresenter.Attachments.cs` with:
```csharp
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Hotline.App.Capture;
using Hotline.Core.Chat;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace Hotline.App.Chat;

internal sealed partial class ChatPresenter
{
    private static readonly AttachmentLimits Limits = new();

    private partial async Task PickFilesAsync()
    {
        var picker = new FileOpenPicker { ViewMode = PickerViewMode.List, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, popup.Hwnd);
        IReadOnlyList<StorageFile> files = [];
        using (popup.Modal())
        {
            popup.HideForDialog(); // the normal Windows dialog, not one boxed in by the small always-on-top panel
            try { files = await picker.PickMultipleFilesAsync(); }
            finally { popup.ShowPopup(); }
            await AddFilesAsync(files);
        }
    }

    private partial async Task CaptureAsync(bool window)
    {
        var target = popup.PreviousForeground;
        var usedWindow = window && ScreenCapture.TryGetWindowRect(target, out _);
        var png = await popup.WithHiddenAsync(async () =>
        {
            var rect = usedWindow && ScreenCapture.TryGetWindowRect(target, out var w) ? w : ScreenCapture.MonitorRect(target);
            var bgra = ScreenCapture.GrabBgra(rect);
            return await ImageProcessor.EncodeBgraPngAsync(bgra, rect.Width, rect.Height, settings.Chat.MaxImagePixels);
        });
        if (window && !usedWindow)
            Notice("That window isn't available (closed or minimized), so the whole screen was captured.", InfoBarSeverity.Informational);
        var name = usedWindow ? $"window-{DateTime.Now:HHmmss}.png" : $"screen-{DateTime.Now:HHmmss}.png";
        await AddAttachmentAsync(AttachmentFactory.FromBytes(name, "image/png", png, Limits));
    }

    private partial void Input_Paste(object sender, TextControlPasteEventArgs e)
    {
        var content = Clipboard.GetContent();
        if (content.Contains(StandardDataFormats.StorageItems))
        {
            e.Handled = true;
            Run("paste files", async () => await AddFilesAsync((await content.GetStorageItemsAsync()).OfType<StorageFile>().ToList()));
        }
        else if (content.Contains(StandardDataFormats.Bitmap))
        {
            e.Handled = true;
            Run("paste image", async () =>
            {
                var reference = await content.GetBitmapAsync();
                using var stream = await reference.OpenReadAsync();
                var bytes = new byte[stream.Size];
                using var reader = new DataReader(stream);
                await reader.LoadAsync((uint)stream.Size);
                reader.ReadBytes(bytes);
                await AddBytesAsync($"pasted-{DateTime.Now:HHmmss}.png", "image/png", bytes);
            });
        }
        // plain text: default paste
    }

    private partial void Root_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems) && !e.DataView.Contains(StandardDataFormats.Bitmap)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Attach to Hotline";
    }

    private partial async void Root_Drop(object sender, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                var folders = items.OfType<StorageFolder>().ToList();
                if (folders.Count > 0) Notice($"Folders can't be attached ({string.Join(", ", folders.Select(f => f.Name))}).", InfoBarSeverity.Warning);
                await AddFilesAsync(items.OfType<StorageFile>().ToList());
            }
        }
        catch (Exception ex) { log.Error("drop failed", ex); Notice($"Couldn't attach dropped items: {ex.Message}", InfoBarSeverity.Error); }
        finally { deferral.Complete(); }
    }

    private async Task AddFilesAsync(IReadOnlyList<StorageFile> files)
    {
        foreach (var file in files)
        {
            try
            {
                var size = (await file.GetBasicPropertiesAsync()).Size;
                if (size > (ulong)Limits.MaxImageBytes)
                {
                    Notice($"{file.Name} is too large ({size / (1024 * 1024)} MB; max {Limits.MaxImageBytes / (1024 * 1024)} MB).", InfoBarSeverity.Warning);
                    continue;
                }
                var buffer = await FileIO.ReadBufferAsync(file);
                await AddBytesAsync(file.Name, file.ContentType, buffer.ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException)
            {
                log.Error($"could not read {file.Name}", ex);
                Notice($"{file.Name} couldn't be read.", InfoBarSeverity.Warning);
            }
        }
    }

    private async Task AddBytesAsync(string name, string? mime, byte[] data)
    {
        Attachment attachment;
        try
        {
            attachment = AttachmentFactory.FromBytes(name, string.IsNullOrEmpty(mime) ? null : mime, data, Limits);
            if (attachment.Kind == AttachmentKind.Image)
            {
                var png = await ImageProcessor.NormalizeAsync(attachment.Data, settings.Chat.MaxImagePixels);
                attachment = attachment with { Data = png, MimeType = "image/png", Name = Path.ChangeExtension(attachment.Name, ".png") };
            }
        }
        catch (AttachmentRejectedException ex) { Notice(ex.Message, InfoBarSeverity.Warning); return; }
        catch (Exception ex) when (ex is ArgumentException or COMException) { Notice($"{name} couldn't be read as an image.", InfoBarSeverity.Warning); return; }
        await AddAttachmentAsync(attachment);
    }

    private async Task AddAttachmentAsync(Attachment attachment)
    {
        try { tray.Add(attachment); }
        catch (AttachmentRejectedException ex) { Notice(ex.Message, InfoBarSeverity.Warning); return; }
        RefreshChips();
        log.Info($"attachment added: {attachment.Kind} {attachment.Data.Length} bytes");
        await Task.CompletedTask;
    }

    private partial void RefreshChips()
    {
        popup.ChipsPanel.Children.Clear();
        foreach (var a in tray.Items)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            if (a.Kind == AttachmentKind.Image)
            {
                var image = new Image { Width = 28, Height = 28, Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill };
                _ = SetThumbnailAsync(image, a.Data);
                chip.Children.Add(image);
            }
            else chip.Children.Add(new FontIcon { Glyph = "\uE8A5", FontSize = 16 });
            chip.Children.Add(new TextBlock { Text = a.Name, MaxWidth = 160, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            var remove = new Button { Content = new FontIcon { Glyph = "\uE711", FontSize = 10 }, Padding = new Thickness(4), BorderThickness = new Thickness(0), Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent) };
            ToolTipService.SetToolTip(remove, "Remove");
            var id = a.Id;
            remove.Click += (_, _) => { tray.Remove(id); RefreshChips(); };
            chip.Children.Add(remove);
            popup.ChipsPanel.Children.Add(new Border
            {
                Child = chip, Padding = new Thickness(6, 3, 2, 3), CornerRadius = new CornerRadius(8),
                Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"],
            });
        }
        popup.ChipsPanel.Visibility = tray.Items.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private async Task SetThumbnailAsync(Image image, byte[] png)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(png.AsBuffer());
            stream.Seek(0);
            var bitmap = new BitmapImage { DecodePixelWidth = 56 };
            await bitmap.SetSourceAsync(stream);
            image.Source = bitmap;
        }
        catch (Exception ex) { log.Error("thumbnail failed", ex); } // chip still shows the name
    }
}
```
Note: `Root_Drop` is declared `private partial void` in ChatPresenter.cs, and its implementation here is `private partial async void`. If the compiler rejects adding `async` to the partial implementation, change both declarations to `private partial void` and wrap the body in `Run("drop", async () => { … })`, keeping `deferral.Complete()` in the `finally`.

- [ ] **Step 2: Build and install**

Run:
```powershell
dotnet build src/Hotline.App/Hotline.App.csproj -c Debug -p:Platform=x64
powershell -File tests\smoke\smoke.ps1 -Install -WithAgy
```
Expected: `Build succeeded.` and `Smoke test passed.`

- [ ] **Step 3: Verify paste and capture (scriptable)**

- Put an image on the clipboard, focus the input, press Ctrl+V: a chip with a thumbnail appears. Ask about it, and agy answers.
- Press the toolbar "capture window" button with a test window behind: a `window-HHmmss.png` chip appears, and agy reads it.

- [ ] **Step 4: Verify the file dialog (human)**

+ → "Attach files…" opens the **normal, full-size Windows file dialog**, and the panel reappears afterwards with the chips.

- [ ] **Step 5: Verify drag-drop (human)**

Click 📌 (pin), switch to Explorer, and drag a file onto the panel. The caption reads "Attach to Hotline" and a chip appears. Unpin, and clicking elsewhere hides the panel again.

- [ ] **Step 6: Verify rejections (human)**

Each of these shows a visible inline InfoBar, and the panel keeps working:
- drop a folder;
- drop a 30 MB file;
- drop a `.zip`;
- attach 11 files.

- [ ] **Step 7: README and commit**

In `README.md`:
- **Chatting section:** replace the paragraph with:
  > Press the Copilot key, type, Enter. **+** or the toolbar attaches files and captures the window you were in or the whole screen. Ctrl+V pastes images or files. To drag files in, **pin** the panel first (📌), because it otherwise hides when you click elsewhere. Ctrl+N or a long press starts a new chat, and Esc hides the panel.
- **Settings table:**
  - replace the `window.width`/`window.height` row with `| \`window.widthPercent\` / \`minWidth\` / \`maxWidth\` | \`40\` / \`600\` / \`1000\` | Panel width: % of the screen, clamped (DIPs) |` and `| \`window.height\` / \`maxHeightPercent\` | \`320\` / \`70\` | Baseline height (DIPs) and how much of the screen it may grow to |`;
  - remove the `chat.maxHeight` row;
  - change the `window.scrollbar` description to `auto, visible, hidden`.
- **Paths:** replace all `LocalState` log and settings paths with `%USERPROFILE%\.hotline\` (`settings.json`, `logs\hotline.log`, `history\`).

```powershell
git add -A
git commit -m "feat(app): native file dialog, paste, drag-drop (pin), capture and attachment chips with inline notices" -m "Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>" -m "Claude-Session: https://claude.ai/code/session_017Gb9pmgys17JP4Z4m7ejtu"
```

---

## Later (Plan 3b, written after 3a lands)

- **Settings window:** NavigationView pages built with CommunityToolkit SettingsControls (General, Keys, Appearance, AI backends, Attachments, History & privacy, Advanced).
- **Live apply:** changes take effect immediately, via a `SettingsChanged` event.
- **Shortcuts:** "Open settings folder" and "Open logs" buttons.
- **Toolbar picker:** model and effort for the active backend, using `agy models` and the low/medium/high/max effort levels.
- **Smoke test path:** `tests/smoke/smoke.ps1` must read settings and logs from `~/.hotline` (the `$state` paths). Do this in 3a Task 4 if the smoke test fails.
