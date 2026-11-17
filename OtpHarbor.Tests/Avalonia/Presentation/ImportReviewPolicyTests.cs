using System.Globalization;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Shared.Import;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class ImportReviewPolicyTests
{
    private static AccountImportEntryPreview Entry => new("Demo", "alice", "SHA512", 8, 60);
    private static AccountImportPreview Clean => new(1, 0, ImportConflictStrategy.SkipExisting) { NewCount = 1, Entries = [Entry] };

    [Theory]
    [InlineData(null)]
    [InlineData("new")]
    [InlineData("empty")]
    [InlineData("metadata")]
    [InlineData("count")]
    [InlineData("conflict-count")]
    [InlineData("existing")]
    [InlineData("conflict")]
    [InlineData("invalid")]
    [InlineData("unsupported")]
    [InlineData("non-totp")]
    public void OnlyCompleteExactDuplicates_AllowAcknowledgementWithoutReview(string? issue)
    {
        var duplicates = new AccountImportPreview(1, 1, ImportConflictStrategy.SkipExisting)
            { UnchangedCount = 1, Entries = [Entry with { Existing = Entry }] };
        var preview = issue switch
        {
            "new" => duplicates with { NewCount = 1 },
            "empty" => duplicates with { TotalCount = 0 },
            "metadata" => duplicates with { Entries = [] },
            "count" => duplicates with { UnchangedCount = 0 },
            "conflict-count" => duplicates with { ConflictCount = 0 },
            "existing" => duplicates with { Entries = [Entry] },
            "conflict" => duplicates with { ChangedConflicts = [new(0, "Demo", "alice", "Demo", "alice", true, false, false, false)] },
            _ => duplicates
        };
        var summary = new AccountImportReadSummary(1, invalidCount: issue == "invalid" ? 1 : 0,
            unsupportedCount: issue == "unsupported" ? 1 : 0, nonOtpCount: issue == "non-totp" ? 1 : 0);
        Assert.Equal(issue is null, ImportReviewPolicy.IsOnlyExactDuplicates(preview, summary));
        Assert.True(ImportReviewPolicy.IsOnlyExactDuplicates(duplicates, null));
        Assert.False(ImportReviewPolicy.IsOnlyExactDuplicates(Clean, new(1)));
    }

    [Theory]
    [InlineData("en", "Import contains only duplicates. Nothing to import!")]
    [InlineData("de", "Der Import enthält nur Duplikate. Es gibt nichts zu importieren!")]
    [InlineData("fr", "L’import ne contient que des doublons. Rien à importer !")]
    [InlineData("es", "La importación solo contiene duplicados. ¡No hay nada que importar!")]
    public void DuplicateAcknowledgement_UsesCompleteActiveLocale(string locale, string expected)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        Assert.Equal(expected, new AvaloniaStringCatalog().Get(ReviewStringKeys.OnlyDuplicates, culture));
        Assert.Equal(expected, new MobileStringCatalog(culture).Get(ReviewStringKeys.OnlyDuplicates));
    }

    [Fact]
    public void CleanAccounts_AllowOneConfirmation()
    {
        Assert.True(ImportReviewPolicy.IsClean(Clean, new(1)));
        Assert.True(ImportReviewPolicy.IsClean(Clean, null));
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("metadata")]
    [InlineData("count")]
    [InlineData("existing")]
    [InlineData("duplicate")]
    [InlineData("input-duplicate")]
    [InlineData("conflict")]
    [InlineData("invalid")]
    [InlineData("unsupported")]
    [InlineData("non-totp")]
    public void AnythingNeedingAttention_RequiresReview(string issue)
    {
        var preview = issue switch
        {
            "empty" => new AccountImportPreview(0, 0, ImportConflictStrategy.SkipExisting),
            "metadata" => Clean with { Entries = [] },
            "count" => Clean with { NewCount = 0 },
            "existing" => Clean with { Entries = [Entry with { Existing = Entry }] },
            "duplicate" => Clean with { UnchangedCount = 1 },
            "input-duplicate" => Clean with { InputDuplicateCount = 1 },
            "conflict" => Clean with { ConflictCount = 1 },
            _ => Clean
        };
        var summary = new AccountImportReadSummary(1, invalidCount: issue == "invalid" ? 1 : 0,
            unsupportedCount: issue == "unsupported" ? 1 : 0, nonOtpCount: issue == "non-totp" ? 1 : 0);
        Assert.False(ImportReviewPolicy.IsClean(preview, summary));
    }

    [Theory]
    [InlineData("en", "Import this new account now?", "Import 2 new accounts now?", "QR code")]
    [InlineData("de", "Dieses neue Konto jetzt importieren?", "Jetzt 2 neue Konten importieren?", "QR-Code")]
    [InlineData("fr", "Importer ce nouveau compte maintenant ?", "Importer ces 2 nouveaux comptes maintenant ?", "Code QR")]
    [InlineData("es", "¿Importar esta cuenta nueva ahora?", "¿Importar 2 cuentas nuevas ahora?", "Código QR")]
    public void CleanConfirmationAndQrSource_UseTheCompleteActiveLocale(string locale, string single, string plural, string qr)
    {
        var desktop = new AvaloniaStringCatalog();
        var mobile = new MobileStringCatalog(CultureInfo.GetCultureInfo(locale));
        foreach (var localize in new Func<string, string>[] { key => desktop.Get(key, CultureInfo.GetCultureInfo(locale)), mobile.Get })
        {
            Assert.Equal(single, ImportReviewPolicy.ConfirmText(localize, 1));
            Assert.Equal(plural, ImportReviewPolicy.ConfirmText(localize, 2));
            Assert.Equal(qr, localize(FileImportStringKeys.Format(AccountImportFormat.QrCode)));
        }
    }

    [Fact]
    public void QrAdapter_PreservesExistingParametersEarlierProvenanceAndAllDecisions()
    {
        var existing = new AccountImportEntryPreview("Demo", "alice", "SHA1", 6, 30);
        var source = new QrAccountImportPreview(2, 0, 1, [new(0, "Demo", "alice", true, true, true, true)], 1, 3)
        {
            Entries = [Entry with { Existing = existing, ExistingIsFromExport = true }, existing with { Existing = existing }]
        };
        var preview = QrImportReviewAdapter.ToAccountPreview(source);
        Assert.Equal(1, preview.UnchangedCount);
        Assert.Equal(1, preview.InputDuplicateCount);
        Assert.Equal("SHA1", preview.Entries[0].Existing!.Algorithm);
        Assert.True(Assert.Single(preview.ChangedConflicts).DigitsChanged);
        foreach (var action in Enum.GetValues<AccountImportConflictAction>())
        {
            var mapped = QrImportReviewAdapter.ToQrResolution(new([new(0, action)]));
            Assert.Equal(action switch
            {
                AccountImportConflictAction.Replace => QrAccountConflictDecision.UpdateExisting,
                AccountImportConflictAction.KeepBoth => QrAccountConflictDecision.KeepBoth,
                _ => QrAccountConflictDecision.Skip
            }, Assert.Single(mapped!.Conflicts).Decision);
        }
        Assert.Null(QrImportReviewAdapter.ToQrResolution(null));
        Assert.Throws<ArgumentException>(() => QrImportReviewAdapter.ToAccountPreview(source with { Entries = [] }));
        Assert.Throws<ArgumentException>(() => QrImportReviewAdapter.ToAccountPreview(source with { Entries = [Entry, existing] }));
    }
}
