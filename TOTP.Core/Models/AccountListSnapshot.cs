namespace TOTP.Core.Models;

public sealed record AccountListSnapshotItem(
    Guid Id,
    string Issuer,
    string AccountName,
    int PeriodSeconds,
    bool IsFavorite,
    IReadOnlyList<AccountGroup> Groups,
    TotpGenerationResult? Totp);

public sealed record AccountListSnapshot(
    IReadOnlyList<AccountListSnapshotItem> Accounts,
    IReadOnlySet<Guid> FailedAccountIds);
