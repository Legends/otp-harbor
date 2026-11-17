using TOTP.Core.Models;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Import;

public static class QrImportReviewAdapter
{
    public static AccountImportPreview ToAccountPreview(QrAccountImportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (preview.Entries.Count != preview.TotalCount)
            throw new ArgumentException("QR review requires complete account metadata.", nameof(preview));
        var conflicts = preview.ChangedConflicts.Select(conflict =>
        {
            if (conflict.ImportIndex < 0 || conflict.ImportIndex >= preview.Entries.Count)
                throw new ArgumentException("QR conflict index is invalid.", nameof(preview));
            var incoming = preview.Entries[conflict.ImportIndex];
            var existing = incoming.Existing
                ?? throw new ArgumentException("QR conflict requires existing account metadata.", nameof(preview));
            return new AccountImportConflict(conflict.ImportIndex, existing.Issuer, existing.AccountName ?? string.Empty,
                incoming.Issuer, incoming.AccountName ?? string.Empty,
                IssuerChanged: false, AccountNameChanged: false, conflict.SecretChanged, conflict.PeriodChanged,
                AlgorithmChanged: conflict.AlgorithmChanged, DigitsChanged: conflict.DigitsChanged);
        }).ToArray();
        return new AccountImportPreview(preview.TotalCount, preview.UnchangedCount + conflicts.Length,
            ImportConflictStrategy.SkipExisting)
        {
            Entries = preview.Entries, NewCount = preview.NewCount, UnchangedCount = preview.UnchangedCount,
            ChangedConflicts = conflicts, InputDuplicateCount = preview.Entries.Count(entry => entry.ExistingIsFromExport)
        };
    }

    public static QrAccountImportResolution? ToQrResolution(AccountImportResolution? resolution) =>
        resolution is null ? null : new(resolution.Conflicts.Select(conflict => new QrAccountImportConflictResolution(
            conflict.ImportIndex, conflict.Action switch
            {
                AccountImportConflictAction.Skip => QrAccountConflictDecision.Skip,
                AccountImportConflictAction.Replace => QrAccountConflictDecision.UpdateExisting,
                AccountImportConflictAction.KeepBoth => QrAccountConflictDecision.KeepBoth,
                _ => throw new ArgumentOutOfRangeException(nameof(resolution))
            })).ToArray());
}
