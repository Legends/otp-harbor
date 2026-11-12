using TOTP.Core.Models;

namespace TOTP.Core.Services.Models;

public enum AccountImportStatus
{
    Completed,
    Cancelled,
    InvalidTargets,
    ExistingAccountsUnavailable,
    RecoveryBackupFailed
}

public enum AccountImportConflictAction
{
    Skip,
    Replace,
    KeepBoth
}

public sealed record AccountImportConflict(
    int ImportIndex,
    string CurrentIssuer,
    string CurrentAccountName,
    string BackupIssuer,
    string BackupAccountName,
    bool IssuerChanged,
    bool AccountNameChanged,
    bool SecretChanged,
    bool PeriodChanged,
    bool FavoriteChanged = false,
    bool GroupMembershipChanged = false,
    bool AlgorithmChanged = false,
    bool DigitsChanged = false);

public sealed record AccountImportConflictResolution(
    int ImportIndex,
    AccountImportConflictAction Action);

public sealed record AccountImportResolution(
    IReadOnlyList<AccountImportConflictResolution> Conflicts);

public sealed record AccountImportPreview(
    int TotalCount,
    int ConflictCount,
    ImportConflictStrategy ConflictStrategy)
{
    public IReadOnlyList<AccountImportEntryPreview> Entries { get; init; } = [];
    public int NewCount { get; init; }
    public int InputDuplicateCount { get; init; }

    public int UnchangedCount { get; init; }

    public IReadOnlyList<AccountImportConflict> ChangedConflicts { get; init; } = [];

    public int ChangedConflictCount => ChangedConflicts.Count;
}

public sealed record AccountImportEntryPreview(string Issuer, string? AccountName, string Algorithm, int Digits, int PeriodSeconds)
{
    public int ImportIndex { get; init; } = -1;
    public AccountImportEntryPreview? Existing { get; init; }
    public bool ExistingIsFromExport { get; init; }
}

public sealed record AccountImportOutcome(
    AccountImportStatus Status,
    int Added = 0,
    int Replaced = 0,
    int Skipped = 0,
    int Failed = 0);
