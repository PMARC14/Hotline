using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hotline.Core.Backends;
using Hotline.Core.Files;
using Hotline.Core.State;
using Hotline.Core.Text.Localization;

namespace Hotline.Core.Tools;

public sealed record McpPlaintextEnvEntry(string Server, string EnvKey, string SuggestedSecretName);

public enum McpMoveStatus { Moved, ChangedNothingMoved, Failed, LockerUpdatedFileFailed }

public sealed record McpMoveResult(bool Succeeded, McpMoveStatus Status, SafeFileResult FileResult, string? Error);

/// <summary>
/// Resolves ${hotline:secret:&lt;name&gt;} env tokens from Windows Credential Locker at server launch,
/// scrubs resolved secrets and long env values before logging/display, and moves plaintext env entries
/// from mcp.json into the Locker via SafeFile + BackupSet. Never puts secrets in errors or logs.
/// </summary>
public static partial class McpSecrets
{
    public const string HotlinePrefix = "${hotline:";
    public const string LockerKeyPrefix = "mcp:secret:";
    private const int ScrubMinPlainEnvLength = 8;
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly ConditionalWeakTable<IReadOnlyDictionary<string, string>, IReadOnlyList<string>> AttachedScrubs = new();

    [GeneratedRegex(@"\A[A-Za-z0-9._-]+\z")]
    private static partial Regex SecretNameRegex();

    [GeneratedRegex(@"\A\$\{hotline:secret:([A-Za-z0-9._-]+)\}\z")]
    private static partial Regex SecretTokenRegex();

    public static bool IsValidSecretName(string? name) => !string.IsNullOrEmpty(name) && SecretNameRegex().IsMatch(name);

    public static string LockerKey(string secretName) => LockerKeyPrefix + secretName;

    public static string TokenFor(string secretName) => $"${{hotline:secret:{secretName}}}";

    public static bool TryParseToken(string value, out string secretName, out bool isHotlinePrefix)
    {
        isHotlinePrefix = value.StartsWith(HotlinePrefix, StringComparison.Ordinal);
        if (!isHotlinePrefix)
        {
            secretName = "";
            return false;
        }
        var match = SecretTokenRegex().Match(value);
        if (!match.Success)
        {
            secretName = "";
            return false;
        }
        secretName = match.Groups[1].Value;
        return true;
    }

    public static bool TryResolveEnv(
        McpServerConfig server, ISecretStore secrets,
        out IReadOnlyDictionary<string, string> resolved, out string? error)
    {
        if (server.Env.Count == 0)
        {
            resolved = server.Env;
            error = null;
            return true;
        }

        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (envKey, envValue) in server.Env)
        {
            if (TryParseToken(envValue, out var secretName, out var isHotlinePrefix))
            {
                string? secretValue;
                try { secretValue = secrets.Get(LockerKey(secretName)); }
                catch (Exception) { secretValue = null; }
                if (secretValue is null)
                {
                    resolved = new Dictionary<string, string>();
                    error = L.F(Strings.Mcp_MissingSecret, server.Name, secretName, envKey);
                    return false;
                }
                copy[envKey] = secretValue;
            }
            else if (isHotlinePrefix)
            {
                resolved = new Dictionary<string, string>();
                error = L.F(Strings.Mcp_BadSecretToken, server.Name, envKey);
                return false;
            }
            else
            {
                copy[envKey] = envValue;
            }
        }

        resolved = copy;
        error = null;
        return true;
    }

    /// <summary>Collects every resolved secret value (any non-empty length) plus other env values of 8+ characters, longest first.</summary>
    public static IReadOnlyList<string> CollectScrubValues(McpServerConfig rawServer, IReadOnlyDictionary<string, string>? resolvedEnv = null)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        var effective = resolvedEnv ?? rawServer.Env;
        foreach (var (key, rawVal) in rawServer.Env)
        {
            if (TryParseToken(rawVal, out _, out _))
            {
                if (effective.TryGetValue(key, out var sec) && sec.Length > 0) values.Add(sec);
            }
            else
            {
                if (rawVal.Length >= ScrubMinPlainEnvLength) values.Add(rawVal);
            }
        }
        foreach (var val in effective.Values)
            if (val.Length >= ScrubMinPlainEnvLength) values.Add(val);
        return values.OrderByDescending(v => v.Length).ToArray();
    }

    internal static void AttachScrubValues(IReadOnlyDictionary<string, string> env, IReadOnlyList<string> values)
    {
        if (values.Count > 0) AttachedScrubs.AddOrUpdate(env, values);
    }

    public static IReadOnlyList<string> GetScrubValues(McpServerConfig server)
    {
        if (AttachedScrubs.TryGetValue(server.Env, out var attached)) return attached;
        return CollectScrubValues(server, server.Env);
    }

    public static string Scrub(string? text, IEnumerable<string> sensitiveValues)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var current = text;
        foreach (var secret in sensitiveValues.Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.Ordinal).OrderByDescending(s => s.Length))
            current = current.Replace(secret, "***", StringComparison.Ordinal);
        return current;
    }

    public static IReadOnlyList<McpPlaintextEnvEntry> FindPlaintextEnv(McpConfig config)
    {
        var list = new List<McpPlaintextEnvEntry>();
        foreach (var server in config.Servers)
            foreach (var (key, value) in server.Env)
            {
                if (value.StartsWith(HotlinePrefix, StringComparison.Ordinal)) continue;
                list.Add(new McpPlaintextEnvEntry(server.Name, key, SuggestSecretName(server.Name, key)));
            }
        return list;
    }

    public static string SuggestSecretName(string server, string envKey)
    {
        var raw = $"{server}_{envKey}";
        var chars = raw.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' ? char.ToLowerInvariant(c) : '_').ToArray();
        var cleaned = new string(chars).Trim('_');
        return string.IsNullOrEmpty(cleaned) ? "secret" : cleaned;
    }

    public static McpMoveResult MoveToLocker(
        string dataDir, string serverName, string envKey, string secretName,
        ISecretStore secrets, StateStore? stateStore = null, IFileSystem? fileSystem = null, Func<DateTime>? clock = null)
    {
        if (!IsValidSecretName(secretName))
            return new McpMoveResult(false, McpMoveStatus.Failed, SafeFileResult.WriteFailed, Strings.Tools_SecretNameInvalid);

        var fs = fileSystem ?? new RealFileSystem();
        var mcpPath = Path.Combine(dataDir, "mcp.json");
        JsonObject? root;
        try
        {
            if (!fs.FileExists(mcpPath))
                return new McpMoveResult(false, McpMoveStatus.ChangedNothingMoved, SafeFileResult.Unchanged, Strings.Mcp_MoveChangedNothingMoved);
            var text = Encoding.UTF8.GetString(fs.ReadAllBytes(mcpPath));
            root = JsonNode.Parse(text, documentOptions: McpConfig.ReadOptions) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException or FormatException or ArgumentException)
        {
            return new McpMoveResult(false, McpMoveStatus.Failed, SafeFileResult.WriteFailed, L.F(Strings.Mcp_Unreadable, ex.Message));
        }

        if (root is null)
            return new McpMoveResult(false, McpMoveStatus.Failed, SafeFileResult.WriteFailed, L.F(Strings.Mcp_MoveNotFound, serverName, envKey));

        if (root["version"] is JsonValue ver && ver.TryGetValue<double>(out var version) && version > McpConfig.CurrentVersion)
            return new McpMoveResult(false, McpMoveStatus.Failed, SafeFileResult.NewerVersion, L.F(Strings.Mcp_MoveNewerVersion, version));

        if (root["mcpServers"] is not JsonObject servers
            || servers[serverName] is not JsonObject serverObj
            || serverObj["env"] is not JsonObject envObj
            || !envObj.TryGetPropertyValue(envKey, out var envNode)
            || envNode is not JsonValue envVal
            || !envVal.TryGetValue<string>(out var plaintext)
            || plaintext.StartsWith(HotlinePrefix, StringComparison.Ordinal))
            return new McpMoveResult(false, McpMoveStatus.ChangedNothingMoved, SafeFileResult.Unchanged, Strings.Mcp_MoveChangedNothingMoved);

        var lockerKey = LockerKey(secretName);
        string? previous = null;
        try { previous = secrets.Get(lockerKey); } catch (Exception) { previous = null; }

        try { secrets.Set(lockerKey, plaintext); }
        catch (Exception) { return new McpMoveResult(false, McpMoveStatus.Failed, SafeFileResult.Unchanged, Strings.Tools_MoveToLockerFailed); }

        envObj[envKey] = TokenFor(secretName);
        var newBytes = Encoding.UTF8.GetBytes(root.ToJsonString(WriteOptions) + Environment.NewLine);
        var backup = new BackupSet(dataDir, BackupReason.Save, fs, clock);
        // Never pass lastWrittenHash: the previous mcp.json holds a plaintext secret that must always be backed up.
        var result = new SafeFile(fs).Replace(mcpPath, newBytes, backup, McpConfig.CurrentVersion, lastWrittenHash: null);
        if (result is SafeFileResult.Written or SafeFileResult.Unchanged)
        {
            stateStore?.SetLastWritten("mcp.json", SafeFile.Hash(newBytes));
            return new McpMoveResult(true, McpMoveStatus.Moved, result, null);
        }

        try { secrets.Set(lockerKey, previous); }
        catch (Exception)
        {
            return new McpMoveResult(false, McpMoveStatus.LockerUpdatedFileFailed, result,
                L.F(Strings.Mcp_MoveLockerUpdatedFileFailed, secretName, lockerKey));
        }

        var err = result switch
        {
            SafeFileResult.NewerVersion => L.F(Strings.Mcp_MoveNewerVersion, McpConfig.CurrentVersion + 1),
            SafeFileResult.BackupFailed => Strings.Mcp_MoveBackupFailed,
            _ => Strings.Mcp_MoveWriteFailed,
        };
        return new McpMoveResult(false, McpMoveStatus.Failed, result, err);
    }
}
