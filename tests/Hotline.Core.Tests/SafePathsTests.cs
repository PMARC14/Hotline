using Hotline.Core.Activation;
using Hotline.Core.Chat;
using Hotline.Core.Platform;

namespace Hotline.Core.Tests;

/// <summary>Paths and ids that come from links or semi-trusted files must never reach a network share or escape a folder.</summary>
public sealed class SafePathsTests
{
    [Theory]
    [InlineData(@"\\server\share\a.png")]
    [InlineData("//attacker.com/share/a.png")]
    [InlineData(@"\/attacker.com/share/a.png")]
    [InlineData(@"/\attacker.com\share\a.png")]
    [InlineData(@"\\?\UNC\attacker.com\share\a.png")]
    [InlineData(@"\\?\C:\Users\me\a.png")]
    [InlineData(@"\\.\pipe\x")]
    [InlineData("//?/UNC/attacker.com/share/a.png")]
    [InlineData("a.png")]
    [InlineData(@"..\a.png")]
    [InlineData(@"\Users\me\a.png")]
    [InlineData("C:a.png")]
    [InlineData("")]
    [InlineData("   ")]
    public void Unsafe_paths_are_not_local(string path)
    {
        Assert.False(SafeDirectory.IsLocal(path));
        Assert.False(AppActionRequest.IsSafeLocalImagePath(path));
    }

    [Theory]
    [InlineData(@"C:\Users\me\Pictures")]
    [InlineData("C:/Users/me/Pictures")]
    [InlineData(@"D:\")]
    public void Drive_paths_are_local(string path) => Assert.True(SafeDirectory.IsLocal(path));

    [Fact]
    public void Null_and_nul_characters_are_not_local()
    {
        Assert.False(SafeDirectory.IsLocal(null));
        Assert.False(SafeDirectory.IsLocal("C:\\x\0y"));
    }

    [Theory]
    [InlineData("//attacker.com/share/a.png")]
    [InlineData(@"\\?\UNC\attacker.com\share\a.png")]
    [InlineData(@"C:\x.exe")]
    [InlineData("relative.png")]
    public void Images_from_unsafe_places_or_types_are_refused(string path) => Assert.False(AppActionRequest.IsSafeLocalImagePath(path));

    [Theory]
    [InlineData(@"C:\Users\me\shot.png")]
    [InlineData("C:/Users/me/shot.JPG")]
    [InlineData(@"C:\a\b.jpeg")]
    public void Local_drive_images_are_accepted(string path) => Assert.True(AppActionRequest.IsSafeLocalImagePath(path));

    [Theory]
    [InlineData("0123456789ab")]
    [InlineData("c1")]
    [InlineData("a-b_C9")]
    public void Plain_history_ids_are_safe(string id) => Assert.True(HistoryIds.IsSafe(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"..\x")]
    [InlineData("../x")]
    [InlineData(@"C:\x")]
    [InlineData("a/b")]
    [InlineData("a.b")]
    [InlineData("a b")]
    [InlineData("x\0y")]
    public void Odd_history_ids_are_refused(string? id) => Assert.False(HistoryIds.IsSafe(id));

    [Fact]
    public void Very_long_history_ids_are_refused()
    {
        Assert.True(HistoryIds.IsSafe(new string('a', 64)));
        Assert.False(HistoryIds.IsSafe(new string('a', 65)));
    }
}
