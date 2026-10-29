namespace TOTP.Core.Services.Interfaces;

public enum QrScannerFailureKind
{
    None = 0,
    PermissionDenied,
    NoCamera,
    DeviceLost,
    Stalled,
    NativeRuntimeUnavailable,
    Unexpected
}

public sealed record QrScannerRunResult(string? DecodedText, QrScannerFailureKind Failure)
{
    public bool IsDecoded => Failure == QrScannerFailureKind.None
        && !string.IsNullOrWhiteSpace(DecodedText);

    public static QrScannerRunResult Decoded(string decodedText) =>
        new(decodedText ?? throw new ArgumentNullException(nameof(decodedText)), QrScannerFailureKind.None);

    public static QrScannerRunResult Failed(QrScannerFailureKind failure)
    {
        if (failure == QrScannerFailureKind.None)
            throw new ArgumentOutOfRangeException(nameof(failure));

        return new(null, failure);
    }
}

public interface IQrScannerRunner
{
    Task<QrScannerRunResult> RunAsync(
        CancellationToken token,
        Action<byte[]> onPreview,
        Action onFirstFrame);

    Task<QrScannerRunResult> RunAsync(
        CancellationToken token,
        Action<byte[]> onPreview,
        Action onCameraOpened,
        Action onFirstFrame) =>
        RunAsync(token, onPreview, onFirstFrame);
}
