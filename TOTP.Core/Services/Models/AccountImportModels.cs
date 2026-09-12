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
    Replace
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
    bool PeriodChanged);

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
    public int NewCount { get; init; }

    public int UnchangedCount { get; init; }

    public IReadOnlyList<AccountImportConflict> ChangedConflicts { get; init; } = [];

    public int ChangedConflictCount => ChangedConflicts.Count;
}

public sealed record AccountImportOutcome(
    AccountImportStatus Status,
    int Added = 0,
    int Replaced = 0,
    int Skipped = 0,
    int Failed = 0);
