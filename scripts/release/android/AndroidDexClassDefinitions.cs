using System;
using System.IO;
using System.Text;

namespace OtpHarbor.Android
{
    public static class DexClassDefinitions
    {
        public static int Count(byte[] dex, string descriptor)
        {
            if (dex.Length < 112 || dex[0] != 'd' || dex[1] != 'e' || dex[2] != 'x' || dex[3] != '\n'
                || dex[7] != 0 || Read(dex, 32) != dex.Length || Read(dex, 36) != 112
                || Read(dex, 40) != 0x12345678)
                throw new InvalidDataException("Invalid DEX header.");
            var strings = Read(dex, 56);
            var stringOffset = Read(dex, 60);
            var types = Read(dex, 64);
            var typeOffset = Read(dex, 68);
            var classes = Read(dex, 96);
            var classOffset = Read(dex, 100);
            Range(dex, stringOffset, (long)strings * 4);
            Range(dex, typeOffset, (long)types * 4);
            Range(dex, classOffset, (long)classes * 32);
            var expected = Encoding.ASCII.GetBytes(descriptor);
            var found = 0;
            for (long index = 0; index < classes; index++)
            {
                var type = Read(dex, (long)classOffset + index * 32);
                if (type >= types) throw new InvalidDataException("Invalid DEX class type.");
                var name = Read(dex, (long)typeOffset + (long)type * 4);
                if (name >= strings) throw new InvalidDataException("Invalid DEX type descriptor.");
                long position = Read(dex, (long)stringOffset + (long)name * 4);
                uint characters = 0;
                var shift = 0;
                while (true)
                {
                    Range(dex, position, 1);
                    var value = dex[(int)position++];
                    if (shift == 28 && value > 15) throw new InvalidDataException("Invalid DEX string length.");
                    characters |= (uint)(value & 127) << shift;
                    if (value < 128) break;
                    shift += 7;
                    if (shift > 28) throw new InvalidDataException("Invalid DEX string length.");
                }
                if (characters != descriptor.Length) continue;
                Range(dex, position, expected.Length + 1);
                if (dex[(int)position + expected.Length] != 0) continue;
                var matches = true;
                for (var character = 0; character < expected.Length; character++)
                    if (dex[(int)position + character] != expected[character]) { matches = false; break; }
                if (matches) found++;
            }
            return found;
        }

        private static uint Read(byte[] data, long offset)
        {
            Range(data, offset, 4);
            var index = (int)offset;
            return (uint)data[index] | (uint)data[index + 1] << 8 | (uint)data[index + 2] << 16 | (uint)data[index + 3] << 24;
        }

        private static void Range(byte[] data, long offset, long length)
        {
            if (offset < 0 || length < 0 || offset > data.Length || length > data.Length - offset)
                throw new InvalidDataException("DEX table points outside its file.");
        }
    }
}
