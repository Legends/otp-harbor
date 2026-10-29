namespace TOTP.Avalonia.Mobile.Platform;

public enum MobileQrScanStatus
{
    Success,
    Cancelled,
    Unavailable,
    Failed
}

public sealed record MobileQrScanResult(MobileQrScanStatus Status, string Payload)
{
    public static MobileQrScanResult Successful(string payload) =>
        new(MobileQrScanStatus.Success, payload);

    public static MobileQrScanResult Cancelled { get; } =
        new(MobileQrScanStatus.Cancelled, string.Empty);

    public static MobileQrScanResult Unavailable { get; } =
        new(MobileQrScanStatus.Unavailable, string.Empty);

    public static MobileQrScanResult Failed { get; } =
        new(MobileQrScanStatus.Failed, string.Empty);
}

public interface IMobileQrScanner
{
    Task<MobileQrScanResult> ScanAsync(CancellationToken cancellationToken = default);
}
