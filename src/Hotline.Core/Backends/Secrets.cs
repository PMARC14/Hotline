namespace Hotline.Core.Backends;

/// <summary>Where API keys live (the App uses Windows Credential Locker; never settings.json).</summary>
public interface ISecretStore
{
    string? Get(string key);
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
    public static string ApiKey(string connectionId) => $"connection:{connectionId}:apiKey";
}
