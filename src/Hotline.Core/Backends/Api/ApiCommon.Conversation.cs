using System.Text;
using Hotline.Core.Chat;

namespace Hotline.Core.Backends.Api;

/// <summary>Shaping a conversation for the chat APIs (see <see cref="ApiCommon"/>).</summary>
public static partial class ApiCommon
{
    /// <summary>
    /// Merges consecutive messages of the same role (e.g. a question whose answer was cancelled, then the next
    /// question): Anthropic and Gemini require alternating turns.
    /// </summary>
    public static IReadOnlyList<ChatMessage> Alternating(IReadOnlyList<ChatMessage> conversation)
    {
        var result = new List<ChatMessage>();
        foreach (var original in conversation)
        {
            var m = original.Role == ChatRole.Assistant ? original with { Text = ToolLoop.StripNotes(original.Text) } : original;
            if (result.Count > 0 && result[^1].Role == m.Role)
            {
                var last = result[^1];
                var text = string.Join("\n\n", new[] { last.Text, m.Text }.Where(t => t.Length > 0));
                result[^1] = last with { Text = text, Attachments = [.. last.Attachments, .. m.Attachments] };
            }
            else result.Add(m);
        }
        return result;
    }

    public static string WithTextFiles(ChatMessage m)
    {
        var files = m.Attachments.Where(a => a.Kind == AttachmentKind.Text).ToList();
        if (files.Count == 0) return m.Text;
        var sb = new StringBuilder();
        foreach (var f in files) sb.Append("Attached file ").Append(f.Name).Append(":\n```\n").Append(f.AsText()).Append("\n```\n\n");
        sb.Append(m.Text.Length > 0 ? m.Text : "Please look at the attachment(s).");
        return sb.ToString();
    }
}
