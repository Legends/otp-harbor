using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenCvSharp;

namespace TOTP.Camera.OpenCv;

internal sealed class OpenCvCameraSession(
    VideoCapture capture,
    QRCodeDetector detector) : ICameraSession
{
    private const int MaximumEncodedFrameBytes = 4 * 1024 * 1024;
    private readonly VideoCapture _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    private readonly QRCodeDetector _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    private bool _disposed;

    public bool TryRead(bool decodeQr, out CameraFrame frame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        using var mat = new Mat();
        if (!_capture.Read(mat) || mat.Empty())
        {
            frame = default;
            return false;
        }

        var encoded = mat.ImEncode(".png");
        if (encoded.Length == 0 || encoded.Length > MaximumEncodedFrameBytes)
        {
            CryptographicOperations.ZeroMemory(encoded);
            frame = default;
            return false;
        }

        var fingerprint = ComputeFingerprint(encoded);
        var decoded = decodeQr ? OpenCvQrDecoder.Decode(mat, _detector) : null;
        frame = new CameraFrame(encoded, fingerprint, decoded);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _capture.Release();
        }
        finally
        {
            _capture.Dispose();
            _detector.Dispose();
        }
    }

    private static ulong ComputeFingerprint(ReadOnlySpan<byte> encoded)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(encoded, digest);
        return BinaryPrimitives.ReadUInt64LittleEndian(digest);
    }

}
