namespace TOTP.Core.Services.Interfaces;

public enum QrImageDecodeStatus
{
    Success = 0,
    NoQrCode,
    InvalidImage,
    TooLarge,
    Failed
}

public sealed record QrImageDecodeResult(QrImageDecodeStatus Status, string? Payload)
{
    public bool IsDecoded => Status == QrImageDecodeStatus.Success
        && !string.IsNullOrWhiteSpace(Payload);

    public static QrImageDecodeResult Decoded(string payload) =>
        new(QrImageDecodeStatus.Success,
            payload ?? throw new ArgumentNullException(nameof(payload)));

    public static QrImageDecodeResult Rejected(QrImageDecodeStatus status)
    {
        if (status == QrImageDecodeStatus.Success)
            throw new ArgumentOutOfRangeException(nameof(status));

        return new(status, null);
    }
}

public interface IQrImageDecoder
{
    Task<QrImageDecodeResult> DecodeAsync(
        Stream image,
        CancellationToken cancellationToken = default);
}
