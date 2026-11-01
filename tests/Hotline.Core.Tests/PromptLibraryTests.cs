using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public sealed class PromptLibraryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"), "prompts");
    public void Dispose() { var root = Path.GetDirectoryName(_dir)!; if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    private PromptLibrary NewLib()
    {
        var lib = new PromptLibrary(_dir);
        lib.EnsureDefault();
        return lib;
    }

    [Fact]
    public void Ensure_default_creates_file_once_without_overwriting_edits()
    {
        var lib = new PromptLibrary(_dir);
        lib.EnsureDefault();
        Assert.Equal(PromptLibrary.DefaultText, lib.Read("default"));
        File.WriteAllText(lib.PathFor("default"), "my edits");
        lib.EnsureDefault();
        Assert.Equal("my edits", lib.Read("default"));
    }

    [Fact]
    public void List_puts_default_first_then_alphabetical()
    {
        var lib = NewLib();
        File.WriteAllText(lib.PathFor("zeta"), "z");
        File.WriteAllText(lib.PathFor("alpha"), "a");
        Assert.Equal(["default", "alpha", "zeta"], lib.List());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("missing")]
    [InlineData("../evil")]
    [InlineData("blank")] // exists, but only whitespace
    public void Missing_or_unsafe_names_fall_back_to_default(string? name)
    {
        var lib = NewLib();
        File.WriteAllText(lib.PathFor("blank"), "   ");
        Assert.Equal(PromptLibrary.DefaultText, lib.Read(name));
    }

    [Fact]
    public void Create_copies_default_with_unique_name_and_delete_protects_default()
    {
        var lib = NewLib();
        var a = lib.Create();
        var b = lib.Create();
        Assert.Equal(("prompt", "prompt-2"), (a, b));
        Assert.Equal(PromptLibrary.DefaultText, lib.Read("prompt"));
        Assert.True(lib.Delete("prompt"));
        Assert.False(lib.Delete("default"));
        Assert.DoesNotContain("prompt", lib.List());
    }

    [Theory]
    [InlineData("work notes", true)]
    [InlineData("code-review_2", true)]
    [InlineData("../x", false)]
    [InlineData("a:b", false)]
    [InlineData("", false)]
    [InlineData("name\n", false)]
    public void Name_validation(string name, bool ok) => Assert.Equal(ok, PromptLibrary.IsValidName(name));
}
