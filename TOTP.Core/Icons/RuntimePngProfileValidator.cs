using System.Buffers.Binary;
using System.Security.Cryptography;

namespace TOTP.Core.Icons;

public static class RuntimePngProfileValidator
{
    public const int Width = 128;
    public const int Height = 128;
    public const int MaximumEncodedBytes = 1024 * 1024;
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static void ValidateEncoded(ReadOnlySpan<byte> png, string expectedSha256)
    {
        if (png.Length is <= 0 or > MaximumEncodedBytes
            || png.Length < Signature.Length
            || !png[..Signature.Length].SequenceEqual(Signature))
            throw new InvalidDataException("The runtime icon is not a supported PNG.");
        if (!IsLowercaseSha256(expectedSha256))
            throw new InvalidDataException("The runtime icon hash is invalid.");

        var actualHash = SHA256.HashData(png);
        var expectedHash = Convert.FromHexString(expectedSha256);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            throw new InvalidDataException("The runtime icon hash does not match its manifest.");

        var offset = Signature.Length;
        var sawHeader = false;
        var sawImageData = false;
        var imageDataEnded = false;
        var sawEnd = false;
        long imageDataBytes = 0;
        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
                throw new InvalidDataException("The runtime PNG chunk structure is invalid.");
            var unsignedLength = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            offset += 4;
            if (unsignedLength > MaximumEncodedBytes)
                throw new InvalidDataException("The runtime PNG chunk length is invalid.");
            var length = (int)unsignedLength;
            if (png.Length - offset < length + 8)
                throw new InvalidDataException("The runtime PNG chunk length is invalid.");

            var type = png.Slice(offset, 4);
            offset += 4;
            var data = png.Slice(offset, length);
            offset += length;
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            offset += 4;
            if (ComputeCrc32(type, data) != expectedCrc)
                throw new InvalidDataException("The runtime PNG chunk checksum is invalid.");

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || sawImageData || length != 13
                    || BinaryPrimitives.ReadUInt32BigEndian(data[..4]) != Width
                    || BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)) != Height
                    || data[8] != 8
                    || data[9] != 6
                    || data[10] != 0
                    || data[11] != 0
                    || data[12] != 0)
                    throw new InvalidDataException("The runtime PNG header is outside the supported RGBA profile.");
                sawHeader = true;
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!sawHeader || imageDataEnded || sawEnd || length == 0)
                    throw new InvalidDataException("The runtime PNG image-data chunks are invalid.");
                sawImageData = true;
                imageDataBytes = checked(imageDataBytes + length);
                if (imageDataBytes > MaximumEncodedBytes)
                    throw new InvalidDataException("The runtime PNG image data is too large.");
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (!sawHeader || !sawImageData || sawEnd || length != 0 || offset != png.Length)
                    throw new InvalidDataException("The runtime PNG end chunk is invalid.");
                sawEnd = true;
            }
            else
            {
                throw new InvalidDataException("The runtime PNG contains a forbidden chunk.");
            }

            if (sawImageData && !type.SequenceEqual("IDAT"u8)) imageDataEnded = true;
        }

        if (!sawEnd)
            throw new InvalidDataException("The runtime PNG is incomplete.");
    }

    private static bool IsLowercaseSha256(string value) =>
        value.Length == 64
        && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static uint ComputeCrc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = uint.MaxValue;
        foreach (var value in type) crc = UpdateCrc32(crc, value);
        foreach (var value in data) crc = UpdateCrc32(crc, value);
        return ~crc;
    }

    private static uint UpdateCrc32(uint crc, byte value)
        => CrcTable[(crc ^ value) & 0xff] ^ crc >> 8;

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
                value = (value & 1) == 0 ? value >> 1 : 0xedb88320U ^ value >> 1;
            table[index] = value;
        }
        return table;
    }
}
