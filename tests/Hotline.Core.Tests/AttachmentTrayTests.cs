using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class AttachmentTrayTests
{
    private static Attachment Img(string name) => new(Ids.New(), name, AttachmentKind.Image, "image/png", [1]);

    [Fact]
    public void Consume_snapshot_preserves_new_attachments_even_after_reset()
    {
        var tray = new AttachmentTray(new AttachmentLimits());
        var old = Img("old.png");
        tray.Add(old);
        var snapshot = tray.Items.ToArray();
        tray.TakeAll();
        var added = Img("new.png");
        tray.Add(added);
        tray.Consume(snapshot);
        Assert.Equal([added], tray.Items);
    }

    [Fact]
    public async Task Image_intake_limits_normalized_png_and_bounds_raw_input()
    {
        var limits = new AttachmentLimits(MaxImageBytes: 2);
        var normalized = await AttachmentFactory.FromImageAsync("large.bmp", new byte[3], limits, _ => Task.FromResult(new byte[2]));
        Assert.Equal("large.png", normalized.Name);
        Assert.Equal("image/png", normalized.MimeType);
        await Assert.ThrowsAsync<AttachmentRejectedException>(() => AttachmentFactory.FromImageAsync("large.bmp", [1], limits, _ => Task.FromResult(new byte[3])));
        Assert.Throws<AttachmentRejectedException>(() => AttachmentFactory.RequireBitmapSize(256UL * 1024 * 1024 + 1));
        AttachmentFactory.RequireBitmapSize(256UL * 1024 * 1024);
    }

    [Fact]
    public void Add_remove_and_take_all()
    {
        var tray = new AttachmentTray(new AttachmentLimits());
        var a = Img("a.png"); var b = Img("b.png");
        tray.Add(a); tray.Add(b);
        Assert.True(tray.Remove(a.Id));
        Assert.False(tray.Remove("missing"));
        var taken = tray.TakeAll();
        Assert.Equal([b], taken);
        Assert.Empty(tray.Items);
    }

    [Fact]
    public void Eleventh_attachment_is_rejected_and_tray_unchanged()
    {
        var tray = new AttachmentTray(new AttachmentLimits(MaxCount: 10));
        for (var i = 0; i < 10; i++) tray.Add(Img($"{i}.png"));
        var ex = Assert.Throws<AttachmentRejectedException>(() => tray.Add(Img("11.png")));
        Assert.Contains("10", ex.Message);
        Assert.Equal(10, tray.Items.Count);
    }
}
