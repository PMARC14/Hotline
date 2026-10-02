using Hotline.Core.Backends.Agy;

namespace Hotline.Core.Tests;

public class AgyPermissionsTests
{
    [Fact]
    public void Adds_rules_and_keeps_other_settings()
    {
        var json = AgyPermissions.AddRules("""{ "colorScheme": "tokyo night", "trustedWorkspaces": ["C:\\Users\\x"] }""", ["command(ls)"], ["command(rm)"]);
        Assert.Contains("tokyo night", json);
        Assert.Contains("trustedWorkspaces", json);
        Assert.Equal(["command(ls)"], AgyPermissions.Rules(json, "allow"));
        Assert.Equal(["command(rm)"], AgyPermissions.Rules(json, "deny"));
    }

    [Fact]
    public void Merging_twice_adds_no_duplicates_and_keeps_user_rules()
    {
        var once = AgyPermissions.AddRules("""{ "permissions": { "allow": ["command(npm test)"], "ask": ["command(*)"] } }""", ["command(ls)"], []);
        var twice = AgyPermissions.AddRules(once, ["command(ls)", "command(dir)"], []);
        Assert.Equal(["command(npm test)", "command(ls)", "command(dir)"], AgyPermissions.Rules(twice, "allow"));
        Assert.Equal(["command(*)"], AgyPermissions.Rules(twice, "ask"));
    }

    [Fact]
    public void Missing_or_empty_file_starts_fresh_and_comments_are_tolerated()
    {
        Assert.Equal(["command(ls)"], AgyPermissions.Rules(AgyPermissions.AddRules(null, ["command(ls)"], []), "allow"));
        Assert.Equal(["command(ls)"], AgyPermissions.Rules(AgyPermissions.AddRules("// note\n{ }", ["command(ls)"], []), "allow"));
    }

    [Fact]
    public void Read_only_preset_has_no_destructive_commands()
    {
        Assert.DoesNotContain(AgyPermissions.ReadOnlyCommandRules, r => r.Contains("Remove") || r.Contains("rm)") || r.Contains("push"));
        Assert.Contains("command(Remove-Item)", AgyPermissions.DenyRules);
    }
}
