using System.Globalization;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Import;

public static class ImportReviewPolicy
{
    public static bool IsOnlyExactDuplicates(AccountImportPreview preview, AccountImportReadSummary? summary) =>
        preview.TotalCount > 0 && preview.Entries.Count == preview.TotalCount
        && preview.NewCount == 0 && preview.UnchangedCount == preview.TotalCount && preview.ConflictCount == preview.TotalCount
        && preview.ChangedConflicts.Count == 0 && preview.Entries.All(entry => entry.Existing is not null)
        && (summary is null || summary.SupportedCount == preview.TotalCount
            && summary.InvalidCount == 0 && summary.UnsupportedCount == 0 && summary.NonOtpCount == 0 && summary.TrashedCount == 0);

    public static bool IsClean(AccountImportPreview preview, AccountImportReadSummary? summary) =>
        preview.TotalCount > 0 && preview.Entries.Count == preview.TotalCount
        && preview.NewCount == preview.TotalCount && preview.ConflictCount == 0
        && preview.UnchangedCount == 0 && preview.InputDuplicateCount == 0
        && preview.ChangedConflicts.Count == 0 && preview.Entries.All(entry => entry.Existing is null)
        && (summary is null || summary.SupportedCount == preview.TotalCount
            && summary.InvalidCount == 0 && summary.UnsupportedCount == 0 && summary.NonOtpCount == 0 && summary.TrashedCount == 0);

    public static string ConfirmText(Func<string, string> localize, int count) =>
        string.Format(CultureInfo.CurrentCulture,
            localize(count == 1 ? ReviewStringKeys.ImportNewOne : ReviewStringKeys.ImportNewMany), count);
}
