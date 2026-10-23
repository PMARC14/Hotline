using System.Buffers.Binary;

namespace Hotline.Core.Windowing;

/// <summary>Why a picture can't be a custom button's icon (Settings shows a message for each).</summary>
public enum CustomIconProblem { None, NotPng, TooLarge, TooSmall, TooBig, Corrupt, Unreadable }

/// <summary>
/// Pictures as custom button icons: PNG only, at most 256 KB, each side 16-256 px. A chosen file is copied to
/// icons\&lt;button id&gt;.png in the data folder, so toolbar.json only ever names a file Hotline made itself (the user's
/// original is never touched, and no name from outside becomes a path). <see cref="Validate"/> reads the header only:
/// it never decodes pixels and never allocates from a size read out of the file.
/// </summary>
public static class CustomIcons
{
    public const int MaxBytes = 256 * 1024, MinSide = 16, MaxSide = 256;

    private static ReadOnlySpan<byte> Signature => [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static string Folder(string dataDirectory) => Path.Combine(dataDirectory, "icons");

    /// <summary>The one name a button's picture can have: its id + ".png", exactly (ids are lower case, so is the extension).</summary>
    public static bool IsFileName(string? name, string? buttonId) =>
        name is not null && ToolbarModel.IsCustomId(buttonId) && string.Equals(name, buttonId + ".png", StringComparison.Ordinal);

    /// <summary>The path of a stored picture, or null when <paramref name="fileName"/> isn't a name Hotline makes.</summary>
    public static string? IconPath(string iconsFolder, string? fileName) =>
        fileName is { Length: > 4 } && fileName.EndsWith(".png", StringComparison.Ordinal) && IsFileName(fileName, fileName[..^4])
            ? Path.Combine(iconsFolder, fileName)
            : null;

    /// <summary>Checks a PNG's signature, its IHDR chunk (length, CRC, both sides) and that an IEND chunk follows.</summary>
    public static CustomIconProblem Validate(ReadOnlySpan<byte> data)
    {
        if (data.Length > MaxBytes) return CustomIconProblem.TooLarge;
        if (data.Length < Signature.Length || !data[..Signature.Length].SequenceEqual(Signature)) return CustomIconProblem.NotPng;
        // IHDR comes first: length 13, "IHDR", width, height, 5 more bytes, CRC over type and data.
        const int ihdrAt = 8, ihdrLength = 13;
        if (data.Length < ihdrAt + 12 + ihdrLength
            || BinaryPrimitives.ReadUInt32BigEndian(data[ihdrAt..]) != ihdrLength
            || !data.Slice(ihdrAt + 4, 4).SequenceEqual("IHDR"u8)
            || BinaryPrimitives.ReadUInt32BigEndian(data[(ihdrAt + 8 + ihdrLength)..]) != Crc(data.Slice(ihdrAt + 4, 4 + ihdrLength)))
            return CustomIconProblem.Corrupt;
        var width = BinaryPrimitives.ReadUInt32BigEndian(data[(ihdrAt + 8)..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data[(ihdrAt + 12)..]);

        // Walk the chunks to IEND; a chunk running past the end is a cut or damaged file.
        long at = ihdrAt + 12 + ihdrLength;
        var ended = false;
        while (at + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(data[(int)at..]);
            if (length > int.MaxValue || at + 12 + length > data.Length) return CustomIconProblem.Corrupt;
            if (data.Slice((int)at + 4, 4).SequenceEqual("IEND"u8)) { ended = true; break; }
            at += 12 + length;
        }
        if (!ended) return CustomIconProblem.Corrupt;

        if (width < MinSide || height < MinSide) return CustomIconProblem.TooSmall;
        if (width > MaxSide || height > MaxSide) return CustomIconProblem.TooBig;
        return CustomIconProblem.None;
    }

    /// <summary>
    /// Reads and checks a picture: the bytes when it is a valid icon, else the problem. Anything but a drive path is
    /// refused before anything touches it (a network path would send the user's credentials to a server); the size is checked before reading, and at most <see cref="MaxBytes"/> + 1 bytes are read.
    /// Never throws.
    /// </summary>
    public static (byte[]? Bytes, CustomIconProblem Problem) Read(string path)
    {
        if (!IsLocalPath(path)) return (null, CustomIconProblem.Unreadable);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return (null, CustomIconProblem.Unreadable);
            if (info.Length > MaxBytes) return (null, CustomIconProblem.TooLarge);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[MaxBytes + 1];
            var read = 0;
            while (read < buffer.Length && stream.Read(buffer, read, buffer.Length - read) is var n and > 0) read += n;
            var bytes = buffer[..read]; // the file may have grown since the size check: Validate sees the extra byte
            var problem = Validate(bytes);
            return problem == CustomIconProblem.None ? (bytes, problem) : (null, problem);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException)
        {
            return (null, CustomIconProblem.Unreadable);
        }
    }

    /// <summary>
    /// Copies a valid picture to <paramref name="iconsFolder"/>\&lt;buttonId&gt;.png (made on demand) through a temporary
    /// file, so an existing icon is replaced whole or not at all. Returns the stored name, or null and the problem.
    /// Never throws.
    /// </summary>
    public static (string? FileName, CustomIconProblem Problem) Import(string sourcePath, string iconsFolder, string buttonId)
    {
        if (!ToolbarModel.IsCustomId(buttonId)) return (null, CustomIconProblem.Unreadable);
        var (bytes, problem) = Read(sourcePath);
        if (bytes is null) return (null, problem);
        var name = buttonId + ".png";
        var temp = Path.Combine(iconsFolder, $".{buttonId}.{Guid.NewGuid():N}.tmp");
        try
        {
            Directory.CreateDirectory(iconsFolder);
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, Path.Combine(iconsFolder, name), overwrite: true);
            return (name, CustomIconProblem.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (null, CustomIconProblem.Unreadable);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Deletes a button's own picture (only &lt;buttonId&gt;.png inside <paramref name="iconsFolder"/>). Best effort:
    /// false when there was none or it can't be deleted.</summary>
    public static bool Delete(string iconsFolder, string buttonId)
    {
        if (!ToolbarModel.IsCustomId(buttonId)) return false;
        try
        {
            var path = Path.Combine(iconsFolder, buttonId + ".png");
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>A drive path (C:\… or C:/…) only: every network, device or rooted form (\\server, //server, \\?\, \\.\,
    /// \??\UNC\…, \x) and every relative one is refused.</summary>
    private static bool IsLocalPath(string? path) =>
        path is { Length: >= 3 } && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/';

    private static readonly uint[] CrcTable = MakeCrcTable();

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>The PNG chunk CRC (CRC-32, ISO 3309).</summary>
    private static uint Crc(ReadOnlySpan<byte> data)
    {
        var c = 0xFFFFFFFFu;
        foreach (var b in data) c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return ~c;
    }
}
