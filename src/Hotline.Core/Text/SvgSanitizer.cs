using System.Xml;
using System.Xml.Linq;

namespace Hotline.Core.Text;

/// <summary>
/// Makes a model-written SVG safe to save and open: parses it as XML (no DTDs/entities) and keeps only an allowlist
/// of drawing elements and presentation attributes. Scripts, event handlers, links, foreign HTML, external images
/// and non-local references are dropped. Returns null when the input isn't a well-formed SVG or is too big/deep.
/// </summary>
public static class SvgSanitizer
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

    // Bounds on what a drawing may cost: the clean-up and the renderer both recurse, and a stack overflow can't be caught.
    private const int MaxChars = 1_000_000;
    private const int MaxElements = 5_000;
    private const int MaxDepth = 64;
    private const int MaxUses = 100;

    private static readonly HashSet<string> Elements = new(StringComparer.Ordinal)
    {
        "svg", "g", "defs", "title", "desc", "symbol", "use", "path", "rect", "circle", "ellipse", "line", "polyline", "polygon",
        "text", "tspan", "textPath", "marker", "linearGradient", "radialGradient", "stop", "pattern", "clipPath", "mask",
    };

    private static readonly HashSet<string> Attributes = new(StringComparer.Ordinal)
    {
        "id", "class", "viewBox", "width", "height", "x", "y", "x1", "y1", "x2", "y2", "cx", "cy", "r", "rx", "ry", "d", "points",
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin",
        "stroke-dasharray", "stroke-dashoffset", "opacity", "transform", "font-family", "font-size", "font-weight", "font-style",
        "text-anchor", "dominant-baseline", "alignment-baseline", "letter-spacing", "dx", "dy", "rotate", "offset", "stop-color",
        "stop-opacity", "gradientUnits", "gradientTransform", "fx", "fy", "spreadMethod", "markerWidth", "markerHeight", "refX",
        "refY", "orient", "markerUnits", "marker-start", "marker-mid", "marker-end", "clip-path", "mask", "patternUnits",
        "patternContentUnits", "preserveAspectRatio", "visibility", "display", "style", "href", "startOffset", "textLength",
    };

    // The only properties a style="..." may set: the presentation attributes (not geometry, ids or links).
    private static readonly HashSet<string> StyleProperties = new(StringComparer.Ordinal)
    {
        "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin",
        "stroke-dasharray", "stroke-dashoffset", "opacity", "font-family", "font-size", "font-weight", "font-style",
        "text-anchor", "dominant-baseline", "alignment-baseline", "letter-spacing", "stop-color", "stop-opacity",
        "marker-start", "marker-mid", "marker-end", "clip-path", "mask", "visibility", "display",
    };

    // Anything that can pull in a resource, run code or hide those behind CSS escapes/comments.
    private static readonly string[] Forbidden = ["image-set", "image(", "cross-fade", "@import", "expression", "javascript", "behavior", "binding"];

    public static string? Sanitize(string markup)
    {
        XElement root;
        try
        {
            if (markup.Length > MaxChars || !WithinLimits(markup)) return null;
            using var reader = XmlReader.Create(new StringReader(markup), ReaderSettings());
            root = XElement.Load(reader);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException) { return null; }
        if (root.Name.LocalName != "svg") return null;
        var clean = Clean(root);
        if (clean is null) return null;
        PruneUses(clean);
        return clean.ToString(SaveOptions.DisableFormatting);
    }

    private static XmlReaderSettings ReaderSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true,
        MaxCharactersInDocument = MaxChars,
    };

    /// <summary>Counts elements and depth with the (iterative) reader first, so the recursive clean-up never sees a hostile tree.</summary>
    private static bool WithinLimits(string markup)
    {
        using var reader = XmlReader.Create(new StringReader(markup), ReaderSettings());
        var count = 0;
        while (reader.Read())
            if (reader.NodeType == XmlNodeType.Element && (++count > MaxElements || reader.Depth >= MaxDepth)) return false;
        return true;
    }

    private static XElement? Clean(XElement e)
    {
        var name = e.Name.LocalName;
        if (!Elements.Contains(name)) return null;
        var copy = new XElement(Svg + name);
        foreach (var a in e.Attributes())
        {
            if (a.IsNamespaceDeclaration) continue;
            var attr = a.Name.LocalName;
            if (!Attributes.Contains(attr)) continue;
            var value = a.Value;
            if (attr == "href" && !value.StartsWith('#')) continue;                    // only same-document references
            if (attr == "style") value = CleanStyle(value);
            else if (!SafeValue(value)) continue;
            if (value.Length == 0 && attr == "style") continue;
            copy.SetAttributeValue(attr, value);
        }
        foreach (var node in e.Nodes())
        {
            if (node is XText text) copy.Add(new XText(text.Value));
            else if (node is XElement { Name.LocalName: "a" or "switch" } wrapper)
            {
                foreach (var inner in wrapper.Elements()) if (Clean(inner) is { } cleanInner) copy.Add(cleanInner); // keep the drawing, drop the link
            }
            else if (node is XElement child && Clean(child) is { } cleanChild) copy.Add(cleanChild);
        }
        return copy;
    }

    /// <summary>
    /// No CSS escapes or comments (they hide url( from a text check), none of the <see cref="Forbidden"/> words, and every
    /// url(...) points inside the document.
    /// </summary>
    private static bool SafeValue(string value)
    {
        if (value.Contains('\\') || value.Contains("/*", StringComparison.Ordinal)) return false;
        foreach (var word in Forbidden) if (value.Contains(word, StringComparison.OrdinalIgnoreCase)) return false;
        return LocalUrls(value);
    }

    /// <summary>Keeps only "property: value" pairs for known presentation properties with plain values.</summary>
    private static string CleanStyle(string style)
    {
        var kept = new List<string>();
        foreach (var declaration in style.Split(';'))
        {
            var colon = declaration.IndexOf(':');
            if (colon <= 0) continue;
            var property = declaration[..colon].Trim().ToLowerInvariant();
            var value = declaration[(colon + 1)..].Trim();
            if (!StyleProperties.Contains(property) || value.Length == 0 || !SafeValue(value) || !value.All(PlainStyleChar)) continue;
            kept.Add($"{property}:{value}");
        }
        return string.Join(';', kept);
    }

    // Letters, digits and the punctuation of colours, lengths, font names and url(#id): no ':', '/', '@', '!' or '\'.
    private static bool PlainStyleChar(char c) => char.IsAsciiLetterOrDigit(c) || c is ' ' or '#' or '%' or '.' or ',' or '-' or '+' or '(' or ')' or '\'' or '"' or '_';

    /// <summary>
    /// A &lt;use&gt; may repeat a drawing, so it can multiply work (and cycle). Keep at most <see cref="MaxUses"/>, each pointing at a
    /// present element that itself contains no &lt;use&gt;: one level of reuse, no cycles, no exponential blow-up.
    /// </summary>
    private static void PruneUses(XElement root)
    {
        var kept = 0;
        foreach (var use in root.Descendants(Svg + "use").ToList())
        {
            var id = use.Attribute("href")?.Value is { Length: > 1 } href ? href[1..] : null;
            var targets = id is null ? [] : root.DescendantsAndSelf().Where(x => (string?)x.Attribute("id") == id).ToList();
            if (targets.Count == 0 || targets.Any(t => t.DescendantsAndSelf(Svg + "use").Any()) || ++kept > MaxUses) use.Remove();
        }
    }

    /// <summary>True when every url(...) in the value points inside the document (url(#id)).</summary>
    private static bool LocalUrls(string value)
    {
        var i = 0;
        while ((i = value.IndexOf("url(", i, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            var start = i + 4;
            while (start < value.Length && value[start] is ' ' or '\'' or '"') start++;
            if (start >= value.Length || value[start] != '#') return false;
            i = start;
        }
        return true;
    }
}
