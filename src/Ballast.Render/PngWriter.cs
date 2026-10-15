using System.Buffers.Binary;
using System.IO.Compression;

namespace Ballast.Render;

/// <summary>Minimal RGBA8 PNG encoder, so screenshots need no image library.</summary>
public static class PngWriter
{
    public static void Write(string path, ReadOnlySpan<byte> rgba, int width, int height, int scale = 1)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        Write(file, rgba, width, height, scale);
    }

    /// <param name="scale">Nearest-neighbour upscale factor: keeps low-res renders crisp and pixelated.</param>
    public static void Write(Stream output, ReadOnlySpan<byte> rgba, int width, int height, int scale = 1)
    {
        int w = width * scale, h = height * scale;
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, w);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], h);
        header[8] = 8;  // bit depth
        header[9] = 6;  // RGBA
        Chunk(output, "IHDR"u8, header);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + w * 4];
            for (int y = 0; y < h; y++)
            {
                var src = rgba.Slice(y / scale * width * 4, width * 4);
                for (int x = 0; x < w; x++)
                    src.Slice(x / scale * 4, 4).CopyTo(row.AsSpan(1 + x * 4));
                z.Write(row);
            }
        }
        Chunk(output, "IDAT"u8, raw.GetBuffer().AsSpan(0, (int)raw.Length));
        Chunk(output, "IEND"u8, []);
    }

    /// <summary>
    /// Reads back a PNG this writer wrote (8-bit RGBA, unfiltered rows): a kept photo shown again (Dark Territory's logbook,
    /// note 721). Null for a file it can't read or any other kind of PNG; it's not a general decoder.
    /// </summary>
    public static (int Width, int Height, byte[] Rgba)? Read(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 33 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47)
                return null;
            int at = 8, width = 0, height = 0;
            using var idat = new MemoryStream();
            while (at + 8 <= bytes.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(at));
                var type = System.Text.Encoding.ASCII.GetString(bytes, at + 4, 4);
                if (length < 0 || at + 12 + length > bytes.Length)
                    return null;
                var data = bytes.AsSpan(at + 8, length);
                if (type == "IHDR")
                {
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    if (data[8] != 8 || data[9] != 6 || data[12] != 0)
                        return null;
                }
                else if (type == "IDAT")
                    idat.Write(data);
                else if (type == "IEND")
                    break;
                at += 12 + length;
            }
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
                return null;
            idat.Position = 0;
            using var z = new ZLibStream(idat, CompressionMode.Decompress);
            var rgba = new byte[width * height * 4];
            var row = new byte[1 + width * 4];
            for (int y = 0; y < height; y++)
            {
                z.ReadExactly(row);
                if (row[0] != 0)
                    return null;
                row.AsSpan(1).CopyTo(rgba.AsSpan(y * width * 4));
            }
            return (width, height, rgba);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return null;
        }
    }

    static void Chunk(Stream s, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);
        s.Write(type);
        s.Write(data);
        uint crc = Crc(Crc(0xFFFFFFFF, type), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++)
            c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
            crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}
