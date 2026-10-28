using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace TOTP.Tests.Services;

internal static class OtpHarborRuntimePngTestData
{
    internal static byte[] Create(
        int width = 128,
        int height = 128,
        bool includeAncillaryChunk = false,
        byte red = 255,
        byte green = 255,
        byte blue = 255,
        int transparentInset = 0)
    {
        if (transparentInset < 0 || transparentInset * 2 >= width || transparentInset * 2 >= height)
            throw new ArgumentOutOfRangeException(nameof(transparentInset));
        var rowBytes = 1 + width * 4;
        var pixels = new byte[rowBytes * height];
        for (var row = 0; row < height; row++)
        {
            var start = row * rowBytes;
            pixels[start] = 0;
            for (var pixel = start + 1; pixel < start + rowBytes; pixel += 4)
            {
                pixels[pixel] = red;
                pixels[pixel + 1] = green;
                pixels[pixel + 2] = blue;
                var column = (pixel - start - 1) / 4;
                pixels[pixel + 3] = row >= transparentInset
                    && row < height - transparentInset
                    && column >= transparentInset
                    && column < width - transparentInset
                        ? (byte)255
                        : (byte)0;
            }
        }

        byte[] compressed;
        using (var compressedStream = new MemoryStream())
        {
            using (var zlib = new ZLibStream(
                       compressedStream,
                       CompressionLevel.NoCompression,
                       leaveOpen: true))
                zlib.Write(pixels);
            compressed = compressedStream.ToArray();
        }

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header[..4], (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.Slice(4, 4), (uint)height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(png, "IHDR", header);
        if (includeAncillaryChunk) WriteChunk(png, "tEXt", "forbidden"u8);
        WriteChunk(png, "IDAT", compressed);
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    internal static string Sha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, ComputeCrc32(typeBytes, data));
        output.Write(crc);
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type) crc = UpdateCrc32(crc, value);
        foreach (var value in data) crc = UpdateCrc32(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte value)
    {
        crc ^= value;
        for (var bit = 0; bit < 8; bit++)
            crc = (crc & 1) == 0 ? crc >> 1 : 0xedb88320U ^ crc >> 1;
        return crc;
    }
}
