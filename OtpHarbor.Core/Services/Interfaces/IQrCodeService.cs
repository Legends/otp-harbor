namespace TOTP.Core.Services.Interfaces;

using TOTP.Core.Validation;

public interface IQrCodeService
{
    string BuildOtpAuthUri(
        string issuer,
        string secret,
        string? account = "",
        int periodSeconds = TotpPeriodPolicy.DefaultSeconds,
        string algorithm = TotpConfigurationPolicy.DefaultAlgorithm,
        int digits = TotpConfigurationPolicy.DefaultDigits);
    byte[] GenerateQr(string uri);
}
