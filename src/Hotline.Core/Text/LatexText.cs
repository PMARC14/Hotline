using System.Text;

namespace Hotline.Core.Text;

/// <summary>
/// Turns the LaTeX that chat models put in $...$ / $$...$$ into readable Unicode (π, x², a₁, √2, ½-style fractions,
/// ≤, →, ℝ…). Not a typesetter: it covers the common notation in chat answers and degrades to readable text
/// (unknown commands keep their name). Never throws.
/// </summary>
public static class LatexText
{
    private static readonly Dictionary<string, string> Symbols = new()
    {
        // Greek
        ["alpha"] = "α", ["beta"] = "β", ["gamma"] = "γ", ["delta"] = "δ", ["epsilon"] = "ε", ["varepsilon"] = "ε", ["zeta"] = "ζ",
        ["eta"] = "η", ["theta"] = "θ", ["vartheta"] = "ϑ", ["iota"] = "ι", ["kappa"] = "κ", ["lambda"] = "λ", ["mu"] = "μ", ["nu"] = "ν",
        ["xi"] = "ξ", ["pi"] = "π", ["varpi"] = "ϖ", ["rho"] = "ρ", ["sigma"] = "σ", ["tau"] = "τ", ["upsilon"] = "υ", ["phi"] = "φ",
        ["varphi"] = "φ", ["chi"] = "χ", ["psi"] = "ψ", ["omega"] = "ω",
        ["Gamma"] = "Γ", ["Delta"] = "Δ", ["Theta"] = "Θ", ["Lambda"] = "Λ", ["Xi"] = "Ξ", ["Pi"] = "Π", ["Sigma"] = "Σ",
        ["Upsilon"] = "Υ", ["Phi"] = "Φ", ["Psi"] = "Ψ", ["Omega"] = "Ω",
        // operators and relations
        ["times"] = "×", ["cdot"] = "⋅", ["div"] = "÷", ["pm"] = "±", ["mp"] = "∓", ["ast"] = "∗", ["star"] = "⋆", ["circ"] = "∘",
        ["le"] = "≤", ["leq"] = "≤", ["ge"] = "≥", ["geq"] = "≥", ["neq"] = "≠", ["ne"] = "≠", ["approx"] = "≈", ["sim"] = "∼",
        ["simeq"] = "≃", ["cong"] = "≅", ["equiv"] = "≡", ["propto"] = "∝", ["ll"] = "≪", ["gg"] = "≫",
        ["infty"] = "∞", ["partial"] = "∂", ["nabla"] = "∇", ["sum"] = "∑", ["prod"] = "∏", ["int"] = "∫", ["iint"] = "∬", ["oint"] = "∮",
        ["in"] = "∈", ["notin"] = "∉", ["ni"] = "∋", ["subset"] = "⊂", ["subseteq"] = "⊆", ["supset"] = "⊃", ["supseteq"] = "⊇",
        ["cup"] = "∪", ["cap"] = "∩", ["emptyset"] = "∅", ["varnothing"] = "∅", ["forall"] = "∀", ["exists"] = "∃", ["neg"] = "¬",
        ["land"] = "∧", ["wedge"] = "∧", ["lor"] = "∨", ["vee"] = "∨", ["oplus"] = "⊕", ["otimes"] = "⊗",
        ["to"] = "→", ["rightarrow"] = "→", ["leftarrow"] = "←", ["gets"] = "←", ["leftrightarrow"] = "↔", ["Rightarrow"] = "⇒",
        ["Leftarrow"] = "⇐", ["Leftrightarrow"] = "⇔", ["implies"] = "⇒", ["iff"] = "⇔", ["mapsto"] = "↦", ["uparrow"] = "↑", ["downarrow"] = "↓",
        ["ldots"] = "…", ["cdots"] = "⋯", ["dots"] = "…", ["vdots"] = "⋮", ["ddots"] = "⋱", ["prime"] = "′", ["degree"] = "°",
        ["angle"] = "∠", ["perp"] = "⊥", ["parallel"] = "∥", ["hbar"] = "ℏ", ["ell"] = "ℓ", ["Re"] = "ℜ", ["Im"] = "ℑ", ["aleph"] = "ℵ",
        ["langle"] = "⟨", ["rangle"] = "⟩", ["lfloor"] = "⌊", ["rfloor"] = "⌋", ["lceil"] = "⌈", ["rceil"] = "⌉", ["mid"] = "∣",
        ["%"] = "%", ["$"] = "$", ["&"] = "&", ["#"] = "#", ["_"] = "_", ["{"] = "{", ["}"] = "}",
        ["quad"] = "  ", ["qquad"] = "    ", [","] = " ", [";"] = " ", [":"] = " ", ["!"] = "", [" "] = " ", ["\\"] = " ",
        ["left"] = "", ["right"] = "", ["big"] = "", ["Big"] = "", ["bigg"] = "", ["Bigg"] = "", ["displaystyle"] = "", ["limits"] = "",
        ["sin"] = "sin", ["cos"] = "cos", ["tan"] = "tan", ["log"] = "log", ["ln"] = "ln", ["exp"] = "exp", ["lim"] = "lim",
        ["max"] = "max", ["min"] = "min", ["det"] = "det", ["mod"] = "mod",
    };

    /// <summary>Commands whose single argument is shown as plain text.</summary>
    private static readonly HashSet<string> TextCommands =
        ["text", "textbf", "textit", "mathrm", "mathbf", "mathit", "mathsf", "mathtt", "operatorname", "boldsymbol", "mathcal", "vec", "hat", "bar", "overline", "underline", "tilde", "dot"];

    private static readonly Dictionary<char, char> Blackboard = new()
    {
        ['R'] = 'ℝ', ['N'] = 'ℕ', ['Z'] = 'ℤ', ['Q'] = 'ℚ', ['C'] = 'ℂ', ['P'] = 'ℙ', ['H'] = 'ℍ',
    };

    private const string SupFrom = "0123456789+-=()abcdefghijklmnoprstuvwxyzABDEGHIJKLMNOPRTUVW";
    private const string SupTo   = "⁰¹²³⁴⁵⁶⁷⁸⁹⁺⁻⁼⁽⁾ᵃᵇᶜᵈᵉᶠᵍʰⁱʲᵏˡᵐⁿᵒᵖʳˢᵗᵘᵛʷˣʸᶻᴬᴮᴰᴱᴳᴴᴵᴶᴷᴸᴹᴺᴼᴾᴿᵀᵁⱽᵂ";
    private const string SubFrom = "0123456789+-=()aehijklmnoprstuvx";
    private const string SubTo   = "₀₁₂₃₄₅₆₇₈₉₊₋₌₍₎ₐₑₕᵢⱼₖₗₘₙₒₚᵣₛₜᵤᵥₓ";

    public static string ToUnicode(string latex)
    {
        try
        {
            var pos = 0;
            return Convert(latex ?? "", ref pos, stopAtBrace: false).Trim('\n');
        }
        catch (Exception) { return latex ?? ""; }
    }

    private static string Convert(string s, ref int pos, bool stopAtBrace)
    {
        var sb = new StringBuilder();
        while (pos < s.Length)
        {
            var c = s[pos];
            if (c == '}' && stopAtBrace) { pos++; break; }
            if (c == '{') { pos++; sb.Append(Convert(s, ref pos, stopAtBrace: true)); continue; }
            if (c is '^' or '_')
            {
                pos++;
                if (pos >= s.Length) { sb.Append(c); break; }
                if (c == '^' && s.AsSpan(pos).StartsWith(@"\circ")) { pos += 5; sb.Append('°'); continue; }
                sb.Append(Script(Argument(s, ref pos), superscript: c == '^'));
                continue;
            }
            if (c == '\\') { sb.Append(Command(s, ref pos)); continue; }
            if (c == '&') { pos++; sb.Append(' '); continue; } // alignment
            if (c == '~') { pos++; sb.Append(' '); continue; }
            sb.Append(c);
            pos++;
        }
        return sb.ToString();
    }

    /// <summary>A group {..}, a command, or a single character — converted.</summary>
    private static string Argument(string s, ref int pos)
    {
        while (pos < s.Length && s[pos] == ' ') pos++;
        if (pos >= s.Length) return "";
        if (s[pos] == '{') { pos++; return Convert(s, ref pos, stopAtBrace: true); }
        if (s[pos] == '\\') return Command(s, ref pos);
        return s[pos++].ToString();
    }

    private static string Command(string s, ref int pos)
    {
        pos++; // backslash
        if (pos >= s.Length) return "";
        string name;
        if (char.IsLetter(s[pos]))
        {
            var start = pos;
            while (pos < s.Length && char.IsLetter(s[pos])) pos++;
            name = s[start..pos];
        }
        else name = s[pos++].ToString();

        switch (name)
        {
            case "frac" or "dfrac" or "tfrac":
            {
                var a = Argument(s, ref pos);
                var b = Argument(s, ref pos);
                if (b.Length == 0) return a + "⁄";
                return Simple(a) && Simple(b) ? $"{a}⁄{b}" : $"{Group(a)}/{Group(b)}";
            }
            case "sqrt":
            {
                var arg = Argument(s, ref pos);
                return Simple(arg) ? $"√{arg}" : $"√({arg})";
            }
            case "mathbb":
            {
                var arg = Argument(s, ref pos);
                return string.Concat(arg.Select(ch => Blackboard.TryGetValue(ch, out var bb) ? bb : ch));
            }
            case "begin" or "end":
                Argument(s, ref pos); // environment name (matrix, aligned…): show the content only
                return "";
        }
        if (TextCommands.Contains(name)) return Argument(s, ref pos);
        return Symbols.TryGetValue(name, out var symbol) ? symbol : name;
    }

    private static string Script(string text, bool superscript)
    {
        var (from, to) = superscript ? (SupFrom, SupTo) : (SubFrom, SubTo);
        if (text.Length > 0 && text.All(ch => from.Contains(ch)))
            return string.Concat(text.Select(ch => to[from.IndexOf(ch)]));
        var mark = superscript ? "^" : "_";
        return text.Length <= 1 ? mark + text : $"{mark}({text})";
    }

    private static bool Simple(string text) => text.Length > 0 && text.All(char.IsLetterOrDigit);

    private static string Group(string text) => Simple(text) ? text : $"({text})";
}
