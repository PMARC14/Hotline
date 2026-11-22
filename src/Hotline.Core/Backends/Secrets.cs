namespace Hotline.Core.Backends;

/// <summary>Where API keys live (the App uses Windows Credential Locker; never settings.json).</summary>
public interface ISecretStore
{
    string? Get(string key);
    /// <summary>Like <see cref="Get"/>, but false when the store could not be read (a missing key is true with null).</summary>
    bool TryGet(string key, out string? value)
    {
        value = Get(key);
        return true;
    }
    /// <summary>Stores the value; null or empty deletes it.</summary>
    void Set(string key, string? value);
}

public sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _values = [];
    public string? Get(string key) => _values.GetValueOrDefault(key);
    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(value)) _values.Remove(key);
        else _values[key] = value;
    }
}

public static class SecretKeys
{
    public static string ApiKey(string connectionId) => $"connection:{connectionId.ToLowerInvariant()}:apiKey";
}
