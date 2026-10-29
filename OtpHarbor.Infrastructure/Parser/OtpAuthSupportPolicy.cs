namespace TOTP.Infrastructure.Parser;

using TOTP.Core.Validation;

internal static class OtpAuthSupportPolicy
{
    public static bool TryResolveSupportedIdentity(
        OtpauthParser.TOTPData parsed,
        out string issuer,
        out string accountName)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        issuer = parsed.Issuer?.Trim() ?? string.Empty;
        accountName = parsed.Label.Trim();
        if (parsed.Issuer is null)
        {
            // The established Google Key URI format permits a label without
            // an issuer. Preserve that sole identity in OTP Harbor's required
            // issuer field instead of inventing an issuer or discarding it.
            issuer = accountName;
            accountName = string.Empty;
        }

        return string.Equals(parsed.Algorithm, "SHA1", StringComparison.OrdinalIgnoreCase)
            && parsed.Digits == 6
            && TotpPeriodPolicy.IsSupported(parsed.Period)
            && issuer.Length is > 0 and <= 256
            && accountName.Length <= 256;
    }
}
