using System.Globalization;
using TOTP.Core.Services.Models;

namespace TOTP.Avalonia.Shared.Import;

public static class ImportReviewFormatter
{
    public static string Format(Func<string, string> localize, AccountImportPreview preview, AccountImportReadSummary? summary = null)
    {
        var lines = new List<string>
        {
            string.Format(CultureInfo.CurrentCulture, localize(MigrationStringKeys.ReviewSummary),
                preview.TotalCount, preview.ConflictCount, summary?.UnsupportedCount ?? 0,
                summary?.InvalidCount ?? 0, summary?.NonOtpCount ?? 0, preview.NewCount),
            string.Format(CultureInfo.CurrentCulture, localize(MigrationStringKeys.InputDuplicates), preview.InputDuplicateCount),
            localize(MigrationStringKeys.CurrentVault)
        };
        lines.AddRange(preview.Entries.Select(entry => string.Format(CultureInfo.CurrentCulture,
            localize(MigrationStringKeys.ReviewEntry), Safe(entry.Issuer), Safe(entry.AccountName),
            entry.Algorithm, entry.Digits, entry.PeriodSeconds)));
        return string.Join(Environment.NewLine, lines);
    }

    public static string Blocked(Func<string, string> localize, AccountImportReadSummary summary) =>
        string.Format(CultureInfo.CurrentCulture, localize(MigrationStringKeys.ReviewBlocked),
            summary.SupportedCount, summary.InvalidCount, summary.UnsupportedCount, summary.NonOtpCount);

    private static string Safe(string? text) => string.Concat((text ?? string.Empty).Select(character => char.IsControl(character) ? ' ' : character));
}
