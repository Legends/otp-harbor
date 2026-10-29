namespace TOTP.Core.Models;

public sealed record AccountTotpGenerationBatch(
    IReadOnlyDictionary<Guid, TotpGenerationResult> Codes,
    IReadOnlySet<Guid> FailedAccountIds);
