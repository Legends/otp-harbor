namespace TOTP.Core.Validation;

public static class TotpPeriodPolicy
{
    public const int DefaultSeconds = 30;
    public const int MinimumSeconds = 5;
    public const int MaximumSeconds = 3600;

    public static bool IsSupported(int periodSeconds) =>
        periodSeconds is >= MinimumSeconds and <= MaximumSeconds;
}
