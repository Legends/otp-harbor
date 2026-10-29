using System.Security.Cryptography;
using Avalonia.Media.Imaging;

namespace TOTP.Avalonia.Mobile.Platform;

public sealed class MobileQrImageFactory : IMobileQrImageFactory
{
    public MobileQrImageHandle Create(ReadOnlyMemory<byte> pngBytes)
    {
        if (pngBytes.IsEmpty) throw new ArgumentException("PNG data is required.", nameof(pngBytes));

        var decodingCopy = pngBytes.ToArray();
        try
        {
            using var stream = new MemoryStream(decodingCopy, writable: false);
            var bitmap = new Bitmap(stream);
            return new MobileQrImageHandle(bitmap, bitmap);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decodingCopy);
        }
    }
}
