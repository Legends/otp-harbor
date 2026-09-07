using System.Buffers;
using System.Security.Cryptography;
using OpenCvSharp;
using TOTP.Core.Services.Interfaces;

namespace TOTP.Camera.OpenCv;

public sealed class OpenCvQrImageDecoder : IQrImageDecoder
{
    public const int MaximumEncodedImageBytes = 32 * 1024 * 1024;
    public const int MaximumImageDimension = 8192;
    public const int MaximumDecodedPixels = 25_000_000;

    public async Task<QrImageDecodeResult> DecodeAsync(
        Stream image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!image.CanRead)
            return QrImageDecodeResult.Rejected(QrImageDecodeStatus.InvalidImage);

        byte[]? encoded = null;
        var rented = ArrayPool<byte>.Shared.Rent(81_920);
        using var buffer = new MemoryStream();
        try
        {
            while (true)
            {
                var read = await image.ReadAsync(rented, cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (buffer.Length + read > MaximumEncodedImageBytes)
                    return QrImageDecodeResult.Rejected(QrImageDecodeStatus.TooLarge);

                await buffer.WriteAsync(rented.AsMemory(0, read), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (buffer.Length == 0)
                return QrImageDecodeResult.Rejected(QrImageDecodeStatus.InvalidImage);

            encoded = buffer.ToArray();
            return await Task.Run(() => Decode(encoded), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return QrImageDecodeResult.Rejected(QrImageDecodeStatus.Failed);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rented);
            ArrayPool<byte>.Shared.Return(rented);
            if (encoded is not null) CryptographicOperations.ZeroMemory(encoded);
            if (buffer.TryGetBuffer(out var segment))
                CryptographicOperations.ZeroMemory(segment.AsSpan());
        }
    }

    private static QrImageDecodeResult Decode(byte[] encoded)
    {
        using var image = Cv2.ImDecode(encoded, ImreadModes.Color);
        if (image.Empty())
            return QrImageDecodeResult.Rejected(QrImageDecodeStatus.InvalidImage);

        if (image.Width > MaximumImageDimension || image.Height > MaximumImageDimension ||
            (long)image.Width * image.Height > MaximumDecodedPixels)
        {
            return QrImageDecodeResult.Rejected(QrImageDecodeStatus.TooLarge);
        }

        using var detector = new QRCodeDetector();
        var payload = OpenCvQrDecoder.Decode(image, detector);
        return string.IsNullOrWhiteSpace(payload)
            ? QrImageDecodeResult.Rejected(QrImageDecodeStatus.NoQrCode)
            : QrImageDecodeResult.Decoded(payload);
    }
}
