using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

/// <summary>Web: open a URL template in the browser. OpenWebUi: create a real chat through its API. App: start a program.</summary>
public enum ContinueKind { Web, OpenWebUi, App }

/// <summary>
/// One "Continue this chat in ▸" target (spec: docs/superpowers/specs/2026-10-15-continue-in-targets-design.md).
/// <paramref name="Url"/>: Web — the template; OpenWebUi — the Open WebUI address. <paramref name="Command"/>: App — the
/// program and arguments, split like a command line. <paramref name="Model"/>: OpenWebUi — the new chat's model (null =
/// the chat's own). <paramref name="CopyChat"/>: also copy the whole chat to the clipboard first.
/// </summary>
public sealed record ContinueTarget(ContinueKind Kind, string Name, string? Url = null, string? Command = null, string? Model = null,
    bool CopyChat = false);

/// <summary>The values a target's placeholders take: {chat}, {last}, {file}, {model}.</summary>
public sealed record ContinueValues(string Chat, string Last, string? File, string Model);

/// <summary>A filled web target; <paramref name="Shortened"/>: the chat was too long, so only the last message went in.</summary>
public sealed record FilledUrl(Uri Uri, bool Shortened);

/// <summary>Which targets a chat is offered: its connection's own list, else the shared one, else the default.</summary>
public static partial class ContinueTargets
{
    /// <summary>The targets for a chat on <paramref name="profile"/> (null profile = the shared list).</summary>
    public static IReadOnlyList<ContinueTarget> For(BackendProfile? profile, HotlineSettings settings) =>
        profile?.ContinueIn ?? Shared(settings);

    /// <summary>
    /// <c>chat.continueIn</c>, or by default the llama.cpp web UI at the first Local connection's address (its origin:
    /// the endpoint is usually the /v1 API path), or nothing when there is no Local connection.
    /// </summary>
    public static IReadOnlyList<ContinueTarget> Shared(HotlineSettings settings)
    {
        if (settings.Chat.ContinueIn is { } list) return list;
        var local = settings.Chat.Backends.FirstOrDefault(b => b.Type == BackendType.Local && Uri.TryCreate(b.Endpoint, UriKind.Absolute, out _));
        return local is null ? [] : [LlamaCpp(new Uri(local.Endpoint!).GetLeftPart(UriPartial.Authority))];
    }

    private static ContinueTarget LlamaCpp(string origin) => new(ContinueKind.Web, "llama.cpp", Url: origin + "/?q={chat}&model={model}");

    /// <summary>Browsers and servers start refusing longer URLs; past this, {chat} becomes the last message (and the chat is copied).</summary>
    public const int MaxUrlLength = 8000;

    private const string ContinuePrompt = "Here is a conversation from another app. Continue it: reply to the last message.\n\n";

    /// <summary>The placeholder values for a chat: {chat}, {last}, {file} (null until written) and {model}.</summary>
    public static ContinueValues Values(string title, IReadOnlyList<ChatMessage> messages, string? file, string? model) => new(
        ContinuePrompt + CliHandoff.Markdown(title, messages),
        messages.LastOrDefault(m => m.Role == ChatRole.User)?.Text ?? "",
        file, model ?? "");

    /// <summary>
    /// Fills a web template (values URL-encoded). Null unless the result is an absolute http/https URL — encoding keeps
    /// chat text from turning into a scheme. Too long: refilled with the last message in place of the chat.
    /// </summary>
    public static FilledUrl? FillUrl(string template, ContinueValues values)
    {
        var shortened = false;
        var text = Fill(template, values, Uri.EscapeDataString);
        if (text.Length > MaxUrlLength && template.Contains("{chat}", StringComparison.Ordinal))
        {
            text = Fill(template, values with { Chat = values.Last }, Uri.EscapeDataString);
            shortened = true;
        }
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? new FilledUrl(uri, shortened) : null;
    }

    /// <summary>
    /// An app target's program and arguments: %VARIABLES% expanded, split like a command line (double quotes group), then
    /// each placeholder filled inside its own argument — chat text never splits into more arguments or reaches a shell.
    /// </summary>
    public static (string Program, IReadOnlyList<string> Args) FillCommand(string command, ContinueValues values)
    {
        var parts = Split(Environment.ExpandEnvironmentVariables(command)).Select(p => Fill(p, values, v => v)).ToList();
        return parts.Count == 0 ? ("", []) : (parts[0], parts.Skip(1).ToList());
    }

    /// <summary>One pass over the template: text put in for one placeholder is never scanned for another.</summary>
    private static string Fill(string template, ContinueValues v, Func<string, string> encode) =>
        Placeholder().Replace(template, m => encode(m.Groups[1].Value switch
        {
            "chat" => v.Chat,
            "last" => v.Last,
            "file" => v.File ?? "",
            _ => v.Model,
        }));

    [System.Text.RegularExpressions.GeneratedRegex(@"\{(chat|last|file|model)\}")]
    private static partial System.Text.RegularExpressions.Regex Placeholder();

    private static List<string> Split(string commandLine)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false, any = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any) { parts.Add(current.ToString()); current.Clear(); any = false; }
                continue;
            }
            current.Append(c);
            any = true;
        }
        if (any) parts.Add(current.ToString());
        return parts;
    }

    /// <summary>Settings › Continue in › Add: entries people commonly use.</summary>
    public static IReadOnlyList<(string Key, ContinueTarget Target)> ReadyMade { get; } =
    [
        ("llamacpp", LlamaCpp("http://127.0.0.1:8080")),
        ("openwebui", new(ContinueKind.OpenWebUi, "Open WebUI", Url: "http://localhost:3000")),
        ("lmstudio", new(ContinueKind.App, "LM Studio", Command: @"""%LOCALAPPDATA%\Programs\LM Studio\LM Studio.exe""", CopyChat: true)),
        ("chatgpt", new(ContinueKind.Web, "ChatGPT", Url: "https://chatgpt.com/?q={chat}")),
        ("claude", new(ContinueKind.Web, "Claude", Url: "https://claude.ai/new?q={chat}")),
    ];
}
