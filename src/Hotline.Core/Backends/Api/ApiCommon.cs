using Hotline.Core.Text.Localization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Hotline.Core.Chat;
using Hotline.Core.Settings;

namespace Hotline.Core.Backends.Api;

/// <summary>Shared plumbing for the HTTP API backends: endpoint, key, transport safety, SSE and error mapping.</summary>
public static partial class ApiCommon
{
    private const string RateLimited = "rate limited";

    public static string Endpoint(BackendProfile p) =>
        (string.IsNullOrWhiteSpace(p.Endpoint) ? ConnectionTypes.Of(p.Type).DefaultEndpoint! : p.Endpoint.Trim()).TrimEnd('/');

    /// <summary>The API key, or a NotConfigured error when the connection needs one and has none.</summary>
    public static string? Key(BackendProfile p, ISecretStore secrets)
    {
        var key = secrets.Get(SecretKeys.ApiKey(p.Id));
        if (string.IsNullOrEmpty(key) && !ConnectionTypes.Of(p.Type).ApiKeyOptional)
            throw new BackendException(BackendErrorKind.NotConfigured, L.F(Strings.Api_NeedKey, Name(p)));
        return string.IsNullOrEmpty(key) ? null : key;
    }

    /// <summary>A key only travels over https, or plain http to this PC (local servers).</summary>
    public static void RequireSafeTransport(BackendProfile p, string endpoint, string? key)
    {
        // Checked even without a key: a hand-edited endpoint must fail as a normal error, not as a UriFormatException later.
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            throw new BackendException(BackendErrorKind.NotConfigured, L.F(Strings.Api_BadEndpoint, Name(p), endpoint));
        if (key is null) return;
        if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
            throw new BackendException(BackendErrorKind.NotConfigured,
                $"Not sending the API key for {Name(p)} over plain http to {uri.Host}; use an https:// endpoint.");
    }

    public static string Name(BackendProfile p) => string.IsNullOrWhiteSpace(p.Name) ? p.Id : p.Name;
}
