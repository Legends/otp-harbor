namespace TOTP.Core.Models;

public sealed record TotpGenerationResult(
    string Code,
    int RemainingSeconds,
    int PeriodSeconds,
    string NextCode = "");
