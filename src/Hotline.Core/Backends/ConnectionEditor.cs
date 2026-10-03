using System.Text;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

/// <summary>Add / duplicate / remove / set-default operations on the configured connections.</summary>
public static class ConnectionEditor
{
    public static BackendProfile Add(ChatSettings chat, BackendType type)
    {
        var info = ConnectionTypes.Of(type);
        var profile = new BackendProfile
        {
            Id = UniqueId(chat, Slug(info.DefaultName)),
            Type = type,
            Name = UniqueName(chat, info.DefaultName),
            Endpoint = info.DefaultEndpoint,
            Agent = type == BackendType.Antigravity ? AgyProtocol.DefaultAgent : null,
        };
        chat.Backends.Add(profile);
        return profile;
    }

    public static BackendProfile Duplicate(ChatSettings chat, string id)
    {
        var source = Find(chat, id);
        var copy = new BackendProfile
        {
            Id = UniqueId(chat, source.Id), Type = source.Type, Name = UniqueName(chat, source.Name + " (copy)"),
            Model = source.Model, Effort = source.Effort, Endpoint = source.Endpoint, CliPath = source.CliPath,
            Agent = source.Agent, ExtraArgs = source.ExtraArgs, Args = source.Args is null ? null : [.. source.Args], KeepCliSessions = source.KeepCliSessions, RefusalFallback = source.RefusalFallback, Tools = source.Tools, // ApproveAllTools is never copied: it needs its own confirmation
            WorkingDirectory = source.WorkingDirectory, Prompt = source.Prompt,
        };
        chat.Backends.Insert(chat.Backends.IndexOf(source) + 1, copy);
        return copy;
    }

    /// <summary>Removes a connection; the last one can't be removed. The default moves to the first remaining one.</summary>
    public static bool Remove(ChatSettings chat, string id)
    {
        if (chat.Backends.Count <= 1) return false;
        if (chat.Backends.RemoveAll(b => b.Id == id) == 0) return false;
        if (chat.DefaultBackend == id) chat.DefaultBackend = chat.Backends[0].Id;
        return true;
    }

    public static void SetDefault(ChatSettings chat, string id) => chat.DefaultBackend = Find(chat, id).Id;

    private static BackendProfile Find(ChatSettings chat, string id) =>
        chat.Backends.FirstOrDefault(b => b.Id == id) ?? throw new KeyNotFoundException($"No connection '{id}'.");

    private static string Slug(string name)
    {
        var sb = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
            sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        var slug = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length == 0 ? "connection" : slug;
    }

    private static string UniqueId(ChatSettings chat, string baseId)
    {
        var id = baseId;
        for (var i = 2; chat.Backends.Any(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); i++) id = $"{baseId}-{i}";
        return id;
    }

    private static string UniqueName(ChatSettings chat, string baseName)
    {
        var name = baseName;
        for (var i = 2; chat.Backends.Any(b => b.Name == name); i++) name = $"{baseName} {i}";
        return name;
    }
}
