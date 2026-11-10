using QRCoder;
using TOTP.Core.Services.Interfaces;
using TOTP.Core.Validation;

namespace TOTP.Infrastructure.Services;

public class QrCodeService : IQrCodeService
{
    public string BuildOtpAuthUri(
        string issuer,
        string secret,
        string? account = "",
        int periodSeconds = TotpPeriodPolicy.DefaultSeconds,
        string algorithm = TotpConfigurationPolicy.DefaultAlgorithm,
        int digits = TotpConfigurationPolicy.DefaultDigits)
    {
        if (!TotpPeriodPolicy.IsSupported(periodSeconds))
            throw new ArgumentOutOfRangeException(nameof(periodSeconds));
        if (!TotpConfigurationPolicy.TryNormalizeAlgorithm(algorithm, out var normalizedAlgorithm))
            throw new ArgumentOutOfRangeException(nameof(algorithm));
        if (digits is not (6 or 8))
            throw new ArgumentOutOfRangeException(nameof(digits));

        string label = !string.IsNullOrWhiteSpace(account)
            ? $"{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}"
            : Uri.EscapeDataString(issuer);

        string query = $"secret={Uri.EscapeDataString(secret)}" +
                       $"&issuer={Uri.EscapeDataString(issuer)}" +
                       $"&algorithm={normalizedAlgorithm}&digits={digits}&period={periodSeconds}";

        return $"otpauth://totp/{label}?{query}";
    }


    public byte[] GenerateQr(string uri)
    {
        using QRCodeGenerator qrGenerator = new();
        var qrCodeData = qrGenerator.CreateQrCode(uri, QRCodeGenerator.ECCLevel.Q);

        using PngByteQRCode qrCode = new(qrCodeData);
        byte[] qrCodeImage = qrCode.GetGraphic(20);
        return qrCodeImage;
    }
}
