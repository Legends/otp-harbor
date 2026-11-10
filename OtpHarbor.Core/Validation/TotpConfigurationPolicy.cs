namespace TOTP.Core.Validation;

public static class TotpConfigurationPolicy
{
    public const string DefaultAlgorithm = "SHA1";
    public const int DefaultDigits = 6;

    public static bool IsSupported(string? algorithm, int digits) =>
        TryNormalizeAlgorithm(algorithm, out _) && digits is 6 or 8;

    public static bool TryNormalizeAlgorithm(string? algorithm, out string normalized)
    {
        normalized = algorithm?.ToUpperInvariant() ?? string.Empty;
        return normalized is "SHA1" or "SHA256" or "SHA512";
    }
}
