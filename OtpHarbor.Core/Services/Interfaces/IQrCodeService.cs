namespace TOTP.Core.Services.Interfaces;

using TOTP.Core.Validation;

public interface IQrCodeService
{
    string BuildOtpAuthUri(
        string issuer,
        string secret,
        string? account = "",
        int periodSeconds = TotpPeriodPolicy.DefaultSeconds);
    byte[] GenerateQr(string uri);
}
