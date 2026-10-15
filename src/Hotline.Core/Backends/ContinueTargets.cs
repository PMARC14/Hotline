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

    /// <summary>Settings › Continue in › Add: entries people commonly use.</summary>
    public static IReadOnlyList<(string Key, ContinueTarget Target)> ReadyMade { get; } =
    [
        ("llamacpp", LlamaCpp("http://127.0.0.1:8080")),
        ("openwebui", new(ContinueKind.OpenWebUi, "Open WebUI", Url: "http://localhost:3000")),
        ("lmstudio", new(ContinueKind.App, "LM Studio", Command: @"%LOCALAPPDATA%\Programs\LM Studio\LM Studio.exe", CopyChat: true)),
        ("chatgpt", new(ContinueKind.Web, "ChatGPT", Url: "https://chatgpt.com/?q={chat}")),
        ("claude", new(ContinueKind.Web, "Claude", Url: "https://claude.ai/new?q={chat}")),
    ];
}
