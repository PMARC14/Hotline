using System.Text;
using Hotline.Core.Chat;

namespace Hotline.Core.Tests;

public class AttachmentFactoryTests
{
    private static readonly AttachmentLimits Limits = new();

    [Theory]
    [InlineData("shot.png", null, "image/png")]
    [InlineData("photo.JPG", null, "image/jpeg")]
    [InlineData("pasted", "image/webp", "image/webp")]
    public void Images_are_recognised_by_extension_or_mime(string name, string? mime, string expectedMime)
    {
        var a = AttachmentFactory.FromBytes(name, mime, [1, 2, 3], Limits);
        Assert.Equal(AttachmentKind.Image, a.Kind);
        Assert.Equal(expectedMime, a.MimeType);
        Assert.Equal(name, a.Name);
        Assert.False(string.IsNullOrEmpty(a.Id));
    }

    [Theory]
    [InlineData("notes.md", null)]
    [InlineData("Program.cs", null)]
    [InlineData("data", "text/csv")]
    public void Utf8_text_files_are_accepted(string name, string? mime)
    {
        var a = AttachmentFactory.FromBytes(name, mime, Encoding.UTF8.GetBytes("héllo"), Limits);
        Assert.Equal(AttachmentKind.Text, a.Kind);
        Assert.Equal("héllo", a.AsText());
    }

    [Fact]
    public void Oversized_image_is_rejected_with_reason()
    {
        var ex = Assert.Throws<AttachmentRejectedException>(() =>
            AttachmentFactory.FromBytes("big.png", null, new byte[21 * 1024 * 1024], Limits));
        Assert.Contains("big.png", ex.Message);
        Assert.Contains("20 MB", ex.Message);
    }

    [Fact]
    public void Oversized_text_is_rejected()
        => Assert.Throws<AttachmentRejectedException>(() =>
            AttachmentFactory.FromBytes("huge.log", null, new byte[201 * 1024], Limits));

    [Fact]
    public void Non_utf8_text_is_rejected()
        => Assert.Throws<AttachmentRejectedException>(() =>
            AttachmentFactory.FromBytes("bad.txt", null, [0xC3, 0x28, 0xFF], Limits));

    [Theory]
    [InlineData("setup.exe", null)]
    [InlineData("archive.zip", "application/zip")]
    [InlineData("noext", null)]
    public void Unsupported_types_are_rejected(string name, string? mime)
    {
        var ex = Assert.Throws<AttachmentRejectedException>(() => AttachmentFactory.FromBytes(name, mime, [1], Limits));
        Assert.Contains("unsupported", ex.Message);
    }
}
