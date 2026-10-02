namespace TOTP.Infrastructure.Parser;

using TOTP.Core.Validation;

internal static class OtpAuthSupportPolicy
{
    public static bool IsSupported(OtpauthParser.TOTPData parsed) =>
        string.Equals(parsed.Algorithm, "SHA1", StringComparison.OrdinalIgnoreCase)
        && parsed.Digits == 6
        && TotpPeriodPolicy.IsSupported(parsed.Period)
        && !string.IsNullOrWhiteSpace(parsed.Issuer)
        && parsed.Issuer.Trim().Length <= 256
        && parsed.Label.Trim().Length <= 256;
}
