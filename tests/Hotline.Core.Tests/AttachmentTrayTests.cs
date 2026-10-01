using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class AttachmentTrayTests
{
    private static Attachment Img(string name) => new(Ids.New(), name, AttachmentKind.Image, "image/png", [1]);

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
