using System.Text;
using Hotline.Core.Backends.Agy;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends;

/// <summary>Add / duplicate / remove / set-default operations on the configured connections.</summary>
public static class ConnectionEditor
{
    public static ConnectionProfile Add(ChatSettings chat, BackendType type)
    {
        var info = ConnectionTypes.Of(type);
        var profile = new ConnectionProfile
        {
            Id = UniqueId(chat, Slug(info.DefaultName)),
            Type = type,
            Name = UniqueName(chat, info.DefaultName),
            Endpoint = info.DefaultEndpoint,
            Agent = type == BackendType.Antigravity ? AgyProtocol.DefaultAgent : null,
        };
        chat.Connections.Add(profile);
        return profile;
    }

    public static ConnectionProfile Duplicate(ChatSettings chat, string id)
    {
        var source = Find(chat, id);
        var copy = new ConnectionProfile
        {
            Id = UniqueId(chat, source.Id), Type = source.Type, Name = UniqueName(chat, source.Name + " (copy)"),
            Model = source.Model, Effort = source.Effort, Endpoint = source.Endpoint, CliPath = source.CliPath,
            Agent = source.Agent, ExtraArgs = source.ExtraArgs is null ? null : [.. source.ExtraArgs], Args = source.Args is null ? null : [.. source.Args], KeepCliSessions = source.KeepCliSessions, KeepInHotline = source.KeepInHotline, MaxTokens = source.MaxTokens, Temperature = source.Temperature, Images = source.Images, RefusalFallback = source.RefusalFallback, Tools = source.Tools, // ApproveAllTools is never copied: it needs its own confirmation
            WorkingDirectory = source.WorkingDirectory, Prompt = source.Prompt,
        };
        chat.Connections.Insert(chat.Connections.IndexOf(source) + 1, copy);
        return copy;
    }

    /// <summary>
    /// Removes a connection. The default moves to the first remaining one; removing the last leaves none chosen (the
    /// panel then asks for one, as on first run).
    /// </summary>
    public static bool Remove(ChatSettings chat, string id)
    {
        if (chat.Connections.RemoveAll(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        if (string.Equals(chat.DefaultBackend, id, StringComparison.OrdinalIgnoreCase)) chat.DefaultBackend = chat.Connections.FirstOrDefault()?.Id ?? "";
        return true;
    }

    public static void SetDefault(ChatSettings chat, string id) => chat.DefaultBackend = Find(chat, id).Id;

    /// <summary>Moves a connection up (-1) or down (+1) in the Provider dropdown; saved as chat.order. False at either end.</summary>
    public static bool Move(ChatSettings chat, string id, int delta)
    {
        var index = chat.Connections.FindIndex(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var target = index + delta;
        if (index < 0 || target < 0 || target >= chat.Connections.Count) return false;
        (chat.Connections[index], chat.Connections[target]) = (chat.Connections[target], chat.Connections[index]);
        chat.Order = chat.Connections.Select(b => b.Id).ToList();
        return true;
    }

    private static ConnectionProfile Find(ChatSettings chat, string id) =>
        chat.Connections.FirstOrDefault(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? throw new KeyNotFoundException($"No connection '{id}'.");

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
        for (var i = 2; chat.Connections.Any(b => b.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); i++) id = $"{baseId}-{i}";
        return id;
    }

    private static string UniqueName(ChatSettings chat, string baseName)
    {
        var name = baseName;
        for (var i = 2; chat.Connections.Any(b => b.Name == name); i++) name = $"{baseName} {i}";
        return name;
    }
}
