using System.Security.Cryptography;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace TOTP.Avalonia.Desktop.Platform;

public sealed class AvaloniaQrImageFactory : IAvaloniaQrImageFactory
{
    public AvaloniaQrImageHandle Create(ReadOnlyMemory<byte> pngBytes)
    {
        if (pngBytes.IsEmpty) throw new ArgumentException("PNG data is required.", nameof(pngBytes));

        var decodingCopy = pngBytes.ToArray();
        try
        {
            using var stream = new MemoryStream(decodingCopy, writable: false);
            var bitmap = new Bitmap(stream);
            return new AvaloniaQrImageHandle(bitmap, bitmap);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decodingCopy);
        }
    }
}
