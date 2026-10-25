namespace TOTP.Core.Services.Models;

public enum QrAccountConflictDecision
{
    Cancel,
    Skip,
    UpdateExisting,
    KeepBoth
}

public enum QrAccountImportStatus
{
    Added,
    Updated,
    KeptBoth,
    Skipped,
    DuplicateUnchanged,
    Cancelled,
    BulkImported
}

public sealed record QrAccountConflict(
    int ImportIndex,
    string Issuer,
    string AccountName,
    bool SecretChanged,
    bool PeriodChanged)
{
    public QrAccountConflict(string issuer, string accountName)
        : this(0, issuer, accountName, SecretChanged: true, PeriodChanged: false)
    {
    }
}

public sealed record QrAccountImportConflictResolution(
    int ImportIndex,
    QrAccountConflictDecision Decision);

public sealed record QrAccountImportResolution(
    IReadOnlyList<QrAccountImportConflictResolution> Conflicts);

public sealed record QrAccountImportPreview(
    int TotalCount,
    int NewCount,
    int UnchangedCount,
    IReadOnlyList<QrAccountConflict> ChangedConflicts,
    int BatchIndex,
    int BatchSize);

public sealed record QrAccountImportOutcome(
    QrAccountImportStatus Status,
    Guid AccountId,
    string Issuer,
    string AccountName,
    int TotalCount = 1,
    int AddedCount = 0,
    int UpdatedCount = 0,
    int KeptBothCount = 0,
    int DuplicateCount = 0,
    int SkippedCount = 0,
    int FailedCount = 0,
    int BatchIndex = 0,
    int BatchSize = 1)
{
    public int ImportedCount => AddedCount + UpdatedCount + KeptBothCount;

    public int UnchangedOrSkippedCount => DuplicateCount + SkippedCount;

    public bool HasMoreBatches => BatchIndex + 1 < BatchSize;
}
