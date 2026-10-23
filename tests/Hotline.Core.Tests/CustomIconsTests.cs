using System.Buffers.Binary;
using System.Text;
using Hotline.Core.Windowing;

namespace Hotline.Core.Tests;

public sealed class CustomIconsTests : IDisposable
{
    /// <summary>A 16×16 PNG saved by System.Drawing (a real encoder, so the checker isn't only tested against our own writer).</summary>
    private static readonly byte[] Real16 = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAABAAAAAQCAYAAAAf8/9hAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAVSURBVDhPY2AYXuA/GI2CUTAKyAEAeu0B/zEZFcQAAAAASUVORK5CYII=");

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hotline-icons-" + Guid.NewGuid().ToString("N"));
    private string Icons => Path.Combine(_dir, "icons");

    public CustomIconsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ---- a minimal PNG writer (bitwise CRC, independent of the checker's table) ----

    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            c ^= b;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }

    private static void Chunk(MemoryStream s, string type, byte[] data, bool badCrc = false)
    {
        var head = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(head, (uint)data.Length);
        s.Write(head);
        var typed = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        s.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(head, Crc(typed) ^ (badCrc ? 1u : 0u));
        s.Write(head);
    }

    /// <summary>A PNG header and chunks only (the IDAT is filler: the checker never decodes pixels).</summary>
    private static byte[] Png(uint width, uint height, bool badIhdrCrc = false, bool iend = true, int idat = 10)
    {
        var s = new MemoryStream();
        s.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; ihdr[9] = 6; // 8-bit RGBA
        Chunk(s, "IHDR", ihdr, badIhdrCrc);
        Chunk(s, "IDAT", new byte[idat]);
        if (iend) Chunk(s, "IEND", []);
        return s.ToArray();
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ---- Validate ----

    [Fact]
    public void Validate_accepts_a_png_from_a_real_encoder() =>
        Assert.Equal(CustomIconProblem.None, CustomIcons.Validate(Real16));

    [Fact]
    public void Validate_rejects_what_isnt_a_png()
    {
        Assert.Equal(CustomIconProblem.NotPng, CustomIcons.Validate([]));
        Assert.Equal(CustomIconProblem.NotPng, CustomIcons.Validate(Encoding.ASCII.GetBytes("GIF89a....................................")));
        var wrong = Png(16, 16);
        wrong[1] = (byte)'J';
        Assert.Equal(CustomIconProblem.NotPng, CustomIcons.Validate(wrong));
        Assert.Equal(CustomIconProblem.NotPng, CustomIcons.Validate(Real16.AsSpan(0, 5)));
    }

    [Theory]
    [InlineData(16u, 16u, CustomIconProblem.None)]
    [InlineData(256u, 256u, CustomIconProblem.None)]
    [InlineData(16u, 256u, CustomIconProblem.None)] // any aspect within the range
    [InlineData(100000u, 100000u, CustomIconProblem.TooBig)]
    [InlineData(257u, 16u, CustomIconProblem.TooBig)]
    [InlineData(uint.MaxValue, uint.MaxValue, CustomIconProblem.TooBig)]
    [InlineData(0u, 0u, CustomIconProblem.TooSmall)]
    [InlineData(15u, 15u, CustomIconProblem.TooSmall)]
    [InlineData(16u, 15u, CustomIconProblem.TooSmall)]
    public void Validate_checks_the_sides(uint width, uint height, CustomIconProblem expected) =>
        Assert.Equal(expected, CustomIcons.Validate(Png(width, height)));

    [Fact]
    public void Validate_rejects_a_file_over_256_KB()
    {
        Assert.Equal(CustomIconProblem.TooLarge, CustomIcons.Validate(Png(16, 16, idat: 256 * 1024)));
        var exact = Png(16, 16, idat: 256 * 1024 - Png(16, 16, idat: 0).Length);
        Assert.Equal(256 * 1024, exact.Length);
        Assert.Equal(CustomIconProblem.None, CustomIcons.Validate(exact));
    }

    [Fact]
    public void Validate_rejects_a_damaged_file()
    {
        Assert.Equal(CustomIconProblem.Corrupt, CustomIcons.Validate(Png(16, 16, badIhdrCrc: true)));
        Assert.Equal(CustomIconProblem.Corrupt, CustomIcons.Validate(Png(16, 16, iend: false)));
        var full = Png(16, 16);
        foreach (var cut in new[] { 8, 12, 20, 32, 40, full.Length - 1 })
            Assert.Equal(CustomIconProblem.Corrupt, CustomIcons.Validate(full.AsSpan(0, cut)));
        var longChunk = Png(16, 16);
        BinaryPrimitives.WriteUInt32BigEndian(longChunk.AsSpan(33), 0xFFFFFFF0u); // the IDAT claims 4 GB
        Assert.Equal(CustomIconProblem.Corrupt, CustomIcons.Validate(longChunk));
        var ihdrLength = Png(16, 16);
        ihdrLength[11] = 14; // IHDR length isn't 13
        Assert.Equal(CustomIconProblem.Corrupt, CustomIcons.Validate(ihdrLength));
    }

    [Fact]
    public void Validate_never_throws_on_random_bytes()
    {
        var random = new Random(20261021);
        var signature = Png(16, 16)[..8];
        for (var i = 0; i < 20000; i++)
        {
            var bytes = new byte[random.Next(random.Next(10) == 0 ? 2000 : 80)];
            random.NextBytes(bytes);
            if (random.Next(2) == 0 && bytes.Length >= 8) signature.CopyTo(bytes, 0);
            if (random.Next(3) == 0) // a mutated valid file
            {
                bytes = Png((uint)random.Next(0, 400), (uint)random.Next(0, 400), iend: random.Next(4) > 0);
                for (var k = random.Next(4); k > 0; k--) bytes[random.Next(bytes.Length)] = (byte)random.Next(256);
                if (random.Next(3) == 0) bytes = bytes[..random.Next(bytes.Length)];
            }
            _ = CustomIcons.Validate(bytes);
        }
    }

    // ---- names ----

    [Theory]
    [InlineData("b1.png", "b1", true)]
    [InlineData("my-button.png", "my-button", true)]
    [InlineData("b1.PNG", "b1", false)]
    [InlineData("B1.png", "b1", false)]
    [InlineData("b2.png", "b1", false)]
    [InlineData("../b1.png", "b1", false)]
    [InlineData("..\\b1.png", "b1", false)]
    [InlineData("b1.png ", "b1", false)]
    [InlineData("b1", "b1", false)]
    [InlineData("", "", false)]
    [InlineData(null, "b1", false)]
    [InlineData("pin.png", "Pin", false)]
    public void IsFileName_is_the_button_id_dot_png_only(string? name, string id, bool expected) =>
        Assert.Equal(expected, CustomIcons.IsFileName(name, id));

    [Fact]
    public void IconPath_builds_only_from_a_valid_name()
    {
        Assert.Equal(Path.Combine(Icons, "b1.png"), CustomIcons.IconPath(Icons, "b1.png"));
        Assert.Null(CustomIcons.IconPath(Icons, "../b1.png"));
        Assert.Null(CustomIcons.IconPath(Icons, "C:\\x.png"));
        Assert.Null(CustomIcons.IconPath(Icons, "b1.PNG"));
        Assert.Null(CustomIcons.IconPath(Icons, null));
        Assert.Equal(Path.Combine("data", "icons"), CustomIcons.Folder("data"));
    }

    // ---- Import ----

    [Fact]
    public void Import_copies_a_valid_png_under_the_buttons_id()
    {
        var source = Write("My Picture (final).png", Real16);
        var (name, problem) = CustomIcons.Import(source, Icons, "b1");
        Assert.Equal(CustomIconProblem.None, problem);
        Assert.Equal("b1.png", name);
        Assert.Equal(Real16, File.ReadAllBytes(Path.Combine(Icons, "b1.png")));
        Assert.Equal(Real16, File.ReadAllBytes(source)); // the original stays
        Assert.Equal(["b1.png"], Directory.GetFiles(Icons).Select(Path.GetFileName)); // no temp file left
    }

    [Fact]
    public void Import_replaces_the_buttons_own_icon_only()
    {
        Assert.Equal("b1.png", CustomIcons.Import(Write("a.png", Png(16, 16)), Icons, "b1").FileName);
        Assert.Equal("b2.png", CustomIcons.Import(Write("b.png", Png(32, 32)), Icons, "b2").FileName);
        var newer = Png(64, 64);
        Assert.Equal("b1.png", CustomIcons.Import(Write("c.png", newer), Icons, "b1").FileName);
        Assert.Equal(newer, File.ReadAllBytes(Path.Combine(Icons, "b1.png")));
        Assert.Equal(Png(32, 32), File.ReadAllBytes(Path.Combine(Icons, "b2.png")));
        Assert.Equal(["b1.png", "b2.png"], Directory.GetFiles(Icons).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void Import_keeps_the_old_icon_when_the_new_one_is_refused()
    {
        var good = Png(16, 16);
        CustomIcons.Import(Write("a.png", good), Icons, "b1");
        Assert.Equal((null, CustomIconProblem.TooBig), CustomIcons.Import(Write("big.png", Png(512, 512)), Icons, "b1"));
        Assert.Equal((null, CustomIconProblem.TooLarge), CustomIcons.Import(Write("huge.png", Png(16, 16, idat: 300 * 1024)), Icons, "b1"));
        Assert.Equal((null, CustomIconProblem.NotPng), CustomIcons.Import(Write("x.png", Encoding.ASCII.GetBytes("<svg/>")), Icons, "b1"));
        Assert.Equal(good, File.ReadAllBytes(Path.Combine(Icons, "b1.png")));
        Assert.Single(Directory.GetFiles(Icons));
    }

    [Theory]
    [InlineData(@"\\server\share\a.png")]
    [InlineData("//server/share/a.png")]
    [InlineData(@"\/server\share\a.png")]
    [InlineData(@"\\?\C:\a.png")]
    [InlineData(@"\\.\pipe\x")]
    [InlineData(@"\??\UNC\server\share\a.png")]
    [InlineData(@"\??\C:\a.png")]
    [InlineData("/??/C:/a.png")]
    [InlineData(@"\a.png")]         // rooted, no drive
    [InlineData("a.png")]          // relative
    [InlineData(@"C:a.png")]        // drive-relative
    [InlineData("")]
    public void Import_refuses_network_device_and_relative_paths_before_touching_them(string source)
    {
        Assert.Equal((null, CustomIconProblem.Unreadable), CustomIcons.Import(source, Icons, "b1"));
        Assert.False(Directory.Exists(Icons));
    }

    [Fact]
    public void Import_of_a_missing_file_or_with_a_bad_id_does_nothing()
    {
        Assert.Equal((null, CustomIconProblem.Unreadable), CustomIcons.Import(Path.Combine(_dir, "missing.png"), Icons, "b1"));
        var source = Write("a.png", Real16);
        Assert.Equal((null, CustomIconProblem.Unreadable), CustomIcons.Import(source, Icons, "../evil"));
        Assert.Equal((null, CustomIconProblem.Unreadable), CustomIcons.Import(source, Icons, "B1"));
        Assert.Equal((null, CustomIconProblem.Unreadable), CustomIcons.Import(source, Icons, null!));
        Assert.False(Directory.Exists(Icons));
    }

    [Fact]
    public void Read_returns_the_bytes_of_a_valid_icon_only()
    {
        var (bytes, problem) = CustomIcons.Read(Write("a.png", Real16));
        Assert.Equal(CustomIconProblem.None, problem);
        Assert.Equal(Real16, bytes);
        Assert.Equal((null, CustomIconProblem.Corrupt), CustomIcons.Read(Write("b.png", Png(16, 16, iend: false))));
        Assert.Equal((null, CustomIconProblem.Unreadable), CustomIcons.Read(Path.Combine(_dir, "none.png")));
    }

    [Fact]
    public void Delete_removes_only_the_buttons_own_file()
    {
        CustomIcons.Import(Write("a.png", Real16), Icons, "b1");
        CustomIcons.Import(Write("b.png", Real16), Icons, "b2");
        File.WriteAllBytes(Path.Combine(_dir, "outside.png"), Real16);
        Assert.True(CustomIcons.Delete(Icons, "b1"));
        Assert.False(CustomIcons.Delete(Icons, "b1")); // already gone
        Assert.False(CustomIcons.Delete(Icons, "../outside"));
        Assert.False(CustomIcons.Delete(Path.Combine(_dir, "nowhere"), "b2"));
        Assert.Equal(["b2.png"], Directory.GetFiles(Icons).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(_dir, "outside.png")));
    }
}
