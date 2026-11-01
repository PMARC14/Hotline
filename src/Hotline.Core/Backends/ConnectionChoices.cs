using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

/// <summary>
/// The CLI connections the first-run card offers: all of them, always, in one fixed order, each marked installed or not
/// (an installed one can be picked; a missing one is shown dimmed so new users see it exists).
/// </summary>
public static class ConnectionChoices
{
    public static readonly IReadOnlyList<BackendType> Clis = [BackendType.ClaudeCode, BackendType.Codex, BackendType.Antigravity];

    public static IReadOnlyList<(BackendType Type, bool Installed)> Plan(IReadOnlyCollection<BackendType> installed) =>
        [.. Clis.Select(t => (t, installed.Contains(t)))];
}
