using System.Globalization;
using System.Windows.Input;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Shared.Import;
using TOTP.Core.Services.Models;
using TOTP.Core.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class ProviderFormatParityPresentationTests
{
    [Theory]
    [InlineData("en", "2 entries marked as deleted are excluded.", "The export contains no active TOTP accounts. Deleted and non-TOTP entries are excluded.")]
    [InlineData("de", "2 als gelöscht markierte Einträge werden ausgeschlossen.", "Der Export enthält keine aktiven TOTP-Konten. Gelöschte Einträge und Einträge ohne TOTP werden ausgeschlossen.")]
    [InlineData("fr", "2 entrées marquées comme supprimées sont exclues.", "L’export ne contient aucun compte TOTP actif. Les entrées supprimées ou sans TOTP sont exclues.")]
    [InlineData("es", "Se excluyen 2 entradas marcadas como eliminadas.", "La exportación no contiene cuentas TOTP activas. Se excluyen las entradas eliminadas y las que no contienen TOTP.")]
    public async Task DeletedEntryDiagnostics_UseTheActiveLocaleAndRequireReviewWithoutBlockingActiveAccounts(string language, string excluded, string noActive)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        var desktop = new AvaloniaStringCatalog();
        var mobile = new MobileStringCatalog(culture);
        foreach (var localize in new Func<string, string>[] { key => desktop.Get(key, culture), mobile.Get })
        {
            var entry = new AccountImportEntryPreview("Example", "alice@example.test", "SHA512", 8, 60);
            var preview = new AccountImportPreview(1, 0, ImportConflictStrategy.SkipExisting) { NewCount = 1, Entries = [entry] };
            var summary = new AccountImportReadSummary(1, trashedCount: 2) { Format = AccountImportFormat.TotpUriList };
            var review = new ImportReviewViewModel(preview, summary, "URI", "synthetic.txt", localize);
            Assert.Equal(3, review.TotalCount);
            Assert.Equal(2, review.UnsupportedCount);
            Assert.True(review.HasExcluded);
            Assert.True(review.CanImport);
            Assert.Equal(excluded, review.ExcludedText);
            Assert.EndsWith(excluded, ImportReviewFormatter.Format(localize, preview, summary));
            Assert.EndsWith(excluded, ImportReviewFormatter.Blocked(localize, summary));
            Assert.Equal(noActive, localize(FileImportStringKeys.NoActiveAccounts));
            Assert.False(ImportReviewPolicy.IsClean(preview, summary));
            var duplicate = preview with { NewCount = 0, ConflictCount = 1, UnchangedCount = 1, Entries = [entry with { Existing = entry }] };
            Assert.False(ImportReviewPolicy.IsOnlyExactDuplicates(duplicate, summary));

            var selection = new AuthenticatorImportViewModel(localize, new Command(), new Command());
            var waiting = selection.WaitForFileReviewAsync(summary, "synthetic.txt", TestContext.Current.CancellationToken);
            Assert.Equal(3, selection.TotalEntries);
            Assert.Equal(1, selection.ValidEntries);
            Assert.Equal(2, selection.AttentionEntries);
            selection.ClearFileCommand.Execute(null);
            Assert.False(await waiting);
            Assert.False(selection.HasSelectedFile);
        }
    }

    private sealed class Command : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { }
    }
}
