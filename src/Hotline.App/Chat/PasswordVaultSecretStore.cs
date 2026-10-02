using Hotline.Core.Backends;
using Hotline.Core.Diagnostics;
using Windows.Security.Credentials;

namespace Hotline.App.Chat;

/// <summary>API keys in Windows Credential Locker (per user, encrypted), never in settings.json.</summary>
internal sealed class PasswordVaultSecretStore(FileLog log) : ISecretStore
{
    private const string Resource = "Hotline";
    private readonly PasswordVault _vault = new();

    public string? Get(string key)
    {
        try
        {
            var credential = _vault.Retrieve(Resource, key);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception) { return null; } // not found
    }

    public void Set(string key, string? value)
    {
        try { _vault.Remove(_vault.Retrieve(Resource, key)); } catch (Exception) { /* nothing stored */ }
        if (string.IsNullOrEmpty(value)) return;
        try { _vault.Add(new PasswordCredential(Resource, key, value)); }
        catch (Exception ex) { log.Error("saving API key failed", ex); throw; }
    }
}
