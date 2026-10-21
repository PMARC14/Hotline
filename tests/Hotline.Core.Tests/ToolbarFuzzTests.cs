using System.Globalization;
using System.Text;
using System.Text.Json;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class ToolbarFuzzTests
{
    private static readonly string[] Keys =
        ["version", "top", "items", "left", "right", "style", "overrides", "buttons", "size", "outline", "filled", "width", "height",
         "label", "icon", "iconFile", "quickAction", "text", "send", "pin", "b1", "b-2", "x", "", "Bad Id!"];

    private static readonly string[] Strings =
        [.. ToolbarModel.BuiltIn, "PIN", "newchat", "spacer", "b1", "b-2", "c3", "bogus", "compact", "normal", "wide", "huge",
         "E8A5", "e8bd", "b1.png", "b-2.png", "../x.png", "B1.png", "ZZ", "F8FF", "0041", "10FFFD", "", " ", "\u0000\u0007\n", "\uD800", "\uDC00x", "😀"];

    /// <summary>Random JSON text: mostly the toolbar's keys and ids, with wrong types, deep nesting and huge strings.</summary>
    private static void Value(Random r, StringBuilder sb, int depth)
    {
        switch (r.Next(depth > 5 ? 6 : 9))
        {
            case 0: sb.Append(r.Next(3) switch { 0 => "true", 1 => "false", _ => "null" }); break;
            case 1: sb.Append(r.Next(4) == 0 ? (r.NextDouble() * 1e6 - 5e5).ToString("R", CultureInfo.InvariantCulture) : r.Next(-100, 1000).ToString(CultureInfo.InvariantCulture)); break;
            case 2: case 3: case 4:
                sb.Append(JsonSerializer.Serialize(r.Next(40) == 0 ? new string((char)r.Next(32, 0xFFFF), r.Next(1000, 5000)) : Strings[r.Next(Strings.Length)]));
                break;
            case 5: sb.Append(r.Next(3) == 0 ? "1e400" : "-0"); break;
            case 6: case 7:
                sb.Append('{');
                for (int i = 0, n = r.Next(8); i < n; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(JsonSerializer.Serialize(r.Next(5) == 0 ? $"b{r.Next(30)}" : Keys[r.Next(Keys.Length)])).Append(':');
                    Value(r, sb, depth + 1);
                }
                sb.Append('}');
                break;
            default:
                sb.Append('[');
                for (int i = 0, n = r.Next(r.Next(10) == 0 ? 60 : 8); i < n; i++)
                {
                    if (i > 0) sb.Append(',');
                    Value(r, sb, depth + 1);
                }
                sb.Append(']');
                break;
        }
    }

    /// <summary>Valid-looking values for style and button fields.</summary>
    private static readonly string[] Tokens =
        ["true", "false", "\"compact\"", "\"normal\"", "\"wide\"", "30", "5", "999", "44.5", "\"E8A5\"", "\"e8bd\"", "\"Hi\"", "\"q\""];

    /// <summary>The right shape for <paramref name="key"/>, with random (often wrong) contents.</summary>
    private static void Shaped(Random r, StringBuilder sb, string key)
    {
        void Pick(Random r, StringBuilder sb)
        {
            if (r.Next(8) == 0) Value(r, sb, 3);
            else if (r.Next(3) == 0) sb.Append(Tokens[r.Next(Tokens.Length)]);
            else sb.Append(JsonSerializer.Serialize(r.Next(3) == 0 ? $"b{r.Next(30)}" : Strings[r.Next(Strings.Length)]));
        }
        void Object(int count, Action<int> entry)
        {
            sb.Append('{');
            for (var i = 0; i < count; i++) { if (i > 0) sb.Append(','); entry(i); }
            sb.Append('}');
        }
        void Field(string name, Action value) { sb.Append(JsonSerializer.Serialize(name)).Append(':'); value(); }
        void Button(string[] fields) => Object(r.Next(fields.Length + 1), j => Field(r.Next(10) == 0 ? "label" : fields[j], () => Pick(r, sb)));
        void Style() => Object(r.Next(6), i => Field(new[] { "size", "outline", "filled", "width", "height", "x" }[r.Next(6)], () => Pick(r, sb)));
        switch (key)
        {
            case "top" or "items":
                Object(r.Next(4) == 0 ? r.Next(3) : 2, i => Field(i == 0 ? "left" : "right", () =>
                {
                    sb.Append('[');
                    for (int j = 0, n = r.Next(4) == 0 ? 0 : r.Next(r.Next(10) == 0 ? 50 : 10); j < n; j++) { if (j > 0) sb.Append(','); Pick(r, sb); }
                    sb.Append(']');
                }));
                break;
            case "style": Style(); break;
            case "overrides": Object(r.Next(6), i => Field(r.Next(3) == 0 ? $"b{r.Next(30)}" : Strings[r.Next(Strings.Length)], Style)); break;
            default:
                Object(r.Next(r.Next(5) == 0 ? 30 : 6), i => Field(r.Next(8) == 0 ? Strings[r.Next(Strings.Length)] : $"b{i}", () =>
                    Button(r.Next(4) switch { 0 => ["label", "icon", "quickAction"], 1 => ["label", "text", "send"], 2 => ["icon", "iconFile", "text"], _ => ["quickAction", "text", "label", "send"] })));
                break;
        }
    }

    private static string Document(Random r)
    {
        var sb = new StringBuilder();
        if (r.Next(10) == 0) sb.Append('\uFEFF');
        if (r.Next(20) == 0) { Value(r, sb, 0); return sb.ToString(); } // any root type
        sb.Append("{ \"version\": ").Append(r.Next(5) == 0 ? "\"2\"" : r.Next(4).ToString());
        foreach (var key in new[] { "top", "items", "style", "overrides", "buttons" })
        {
            if (r.Next(4) == 0) continue;
            sb.Append(", ").Append(JsonSerializer.Serialize(key)).Append(": ");
            if (r.Next(3) == 0) Value(r, sb, 1); // anything
            else Shaped(r, sb, key);
        }
        sb.Append(r.Next(10) == 0 ? ", }" : " }");
        var text = sb.ToString();
        return r.Next(15) == 0 ? text[..r.Next(text.Length)] : text; // half-written file
    }

    [Fact]
    public void Resolve_never_throws_and_is_idempotent()
    {
        var r = new Random(20261017);
        var varied = 0;
        for (var i = 0; i < 5000; i++)
        {
            var doc = Document(r);
            var m = ToolbarModel.Resolve(doc);
            var custom = m.Buttons.Select(b => b.Id).ToHashSet();
            var seen = new HashSet<string>();
            var ok = m.Buttons.Count <= 24
                && Enum.GetValues<Quadrant>().All(q => m.Items(q).Count <= 40
                    && m.Items(q).All(id => (ToolbarModel.BuiltIn.Contains(id) || custom.Contains(id)) && seen.Add(id)))
                && m.Overrides.Keys.All(seen.Contains);
            if (!ok) Assert.Fail($"invalid model from {doc}\n{m.ToJson()}");
            var again = ToolbarModel.Resolve(m.ToJson());
            if (!m.Equals(again)) Assert.Fail($"not idempotent: {doc}\n{m.ToJson()}\n{again.ToJson()}");
            if (!m.Equals(ToolbarModel.Defaults)) varied++;
        }
        Assert.True(varied > 1000, $"only {varied} documents resolved to something other than the defaults");
    }
}
