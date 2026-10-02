using Hotline.Core.Backends.Agy;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public sealed class AgyWorkspaceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-tests", Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _clock = new();
    public void Dispose() { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }

    [Fact]
    public void Agent_file_keeps_default_components()
    {
        new AgyWorkspace(_dir, _clock).Ensure("Be a pirate.");
        var md = File.ReadAllText(Path.Combine(_dir, ".agents", "agents", "hotline.md"));
        Assert.StartsWith("---", md);
        Assert.Contains("name: hotline", md);
        Assert.Contains("Be a pirate.", md);
        Assert.DoesNotContain("excludeDefaultComponents", md);
    }

    [Fact]
    public void Image_names_are_sanitized_and_deduplicated()
    {
        var ws = new AgyWorkspace(_dir, _clock);
        var a = new Attachment("1", @"..\..\evil:name.png", AttachmentKind.Image, "image/png", [1]);
        var b = new Attachment("2", "evil_name.png", AttachmentKind.Image, "image/png", [2]);
        var paths = ws.SaveImages("m1", [a, b]);
        Assert.Equal(2, paths.Distinct().Count());
        Assert.All(paths, p => Assert.StartsWith("attachments/m1/", p));
        Assert.All(paths, p => Assert.DoesNotContain("..", p));
        Assert.All(paths, p => Assert.True(File.Exists(Path.Combine(_dir, p))));
    }

    [Fact]
    public void Prune_removes_old_attachment_folders()
    {
        var ws = new AgyWorkspace(_dir, _clock);
        ws.SaveImages("old", [new Attachment("1", "a.png", AttachmentKind.Image, "image/png", [1])]);
        ws.SaveImages("new", [new Attachment("2", "b.png", AttachmentKind.Image, "image/png", [1])]);
        Directory.SetLastWriteTimeUtc(Path.Combine(_dir, "attachments", "old"), _clock.GetUtcNow().UtcDateTime.AddDays(-3));
        Assert.Equal(1, ws.PruneAttachments(TimeSpan.FromDays(1)));
        Assert.False(Directory.Exists(Path.Combine(_dir, "attachments", "old")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "attachments", "new")));
    }
}
