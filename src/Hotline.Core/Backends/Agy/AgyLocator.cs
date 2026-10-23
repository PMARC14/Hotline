namespace Hotline.Core.Backends.Agy;

public static class AgyLocator
{
    public static string? Find(string? configuredPath, Func<string, bool> exists, string? localAppData, string? pathEnv)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var expanded = Environment.ExpandEnvironmentVariables(configuredPath);
            return exists(expanded) ? expanded : null;
        }
        if (!string.IsNullOrEmpty(localAppData))
        {
            var installed = Path.Combine(localAppData, "agy", "bin", "agy.exe");
            if (exists(installed)) return installed;
        }
        foreach (var dir in (pathEnv ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim(), "agy.exe");
            if (exists(candidate)) return candidate;
        }
        return null;
    }
}
