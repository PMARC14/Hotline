using System.Xml;
using System.Xml.Linq;

namespace Hotline.Core.Text;

/// <summary>
/// Makes a model-written SVG safe to save and open: parses it as XML (no DTDs/entities) and keeps only an allowlist
/// of drawing elements and presentation attributes. Scripts, event handlers, links, foreign HTML, external images
/// and non-local references are dropped. Returns null when the input isn't a well-formed SVG.
/// </summary>
public static class SvgSanitizer
{
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";

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

    public static string? Sanitize(string markup)
    {
        XElement root;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, IgnoreComments = true, IgnoreProcessingInstructions = true };
            using var reader = XmlReader.Create(new StringReader(markup), settings);
            root = XElement.Load(reader);
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException) { return null; }
        if (root.Name.LocalName != "svg") return null;
        var clean = Clean(root);
        return clean?.ToString(SaveOptions.DisableFormatting);
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
            if (value.Contains("url(", StringComparison.OrdinalIgnoreCase) && !LocalUrls(value)) continue;
            if (attr == "style" && (value.Contains("url(", StringComparison.OrdinalIgnoreCase) && !LocalUrls(value)
                                    || value.Contains("expression", StringComparison.OrdinalIgnoreCase)
                                    || value.Contains("javascript", StringComparison.OrdinalIgnoreCase))) continue;
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
