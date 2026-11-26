using System.Globalization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Desktop.Localization;
using TOTP.Avalonia.Shared.Import;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class ImportReviewViewModelTests
{
    [Fact]
    public void NativeBackupReview_OffersOnlyReplaceAndSkip()
    {
        var strings = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));
        var model = new ImportReviewViewModel(Preview(), null, "OTP Harbor", "backup.otphbp", strings.Get, allowKeepBoth: false);
        Assert.DoesNotContain(model.Actions, action => action.Action == AccountImportConflictAction.KeepBoth);
        var row = Assert.Single(model.Rows, entry => entry.IsConflict);
        Assert.False(row.CanKeepBoth);
        row.IsKeepBoth = true;
        Assert.Null(row.SelectedAction);
        Assert.DoesNotContain("Keep both", row.ChoicesHelp);
        row.IsReplace = true;
        Assert.True(model.CanImport);
        Assert.Equal(AccountImportConflictAction.Replace, Assert.Single(model.CreateResolution()!.Conflicts).Action);
    }

    [Theory]
    [InlineData("en", "Different issuer names", "Different account names", "Different issuer and account names", "Account parameters differ", "Different secrets")]
    [InlineData("de", "Unterschiedliche Ausstellernamen", "Unterschiedliche Kontonamen", "Unterschiedliche Aussteller- und Kontonamen", "Kontoparameter unterscheiden sich", "Unterschiedliche Schlüssel")]
    [InlineData("fr", "Noms d’émetteur différents", "Noms de compte différents", "Noms d’émetteur et de compte différents", "Paramètres du compte différents", "Secrets différents")]
    [InlineData("es", "Nombres de emisor diferentes", "Nombres de cuenta diferentes", "Nombres de emisor y de cuenta diferentes", "Parámetros de cuenta diferentes", "Secretos diferentes")]
    public void ConflictHeading_IdentifiesChangedNamesInBothPresentationsAndActiveLocale(
        string locale, string issuerHeading, string accountHeading, string bothHeading, string parametersHeading, string secretHeading)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        var desktop = new AvaloniaStringCatalog();
        var mobile = new MobileStringCatalog(culture);
        foreach (var localize in new Func<string, string>[] { key => desktop.Get(key, culture), mobile.Get })
        foreach (var issuerChanged in new[] { false, true })
        foreach (var accountChanged in new[] { false, true })
        foreach (var secretChanged in new[] { false, true })
        {
            var incomingIssuer = issuerChanged ? "1Google Test" : "Google Test";
            var incomingAccount = accountChanged ? "other@example.test" : "carol@example.test";
            var model = new ImportReviewViewModel(new(1, 1, ImportConflictStrategy.SkipExisting)
            {
                Entries = [new(incomingIssuer, incomingAccount, "SHA1", 6, 60)
                    { Existing = new("Google Test", "carol@example.test", "SHA1", 6, 30) }],
                ChangedConflicts = [new(0, "Google Test", "carol@example.test", incomingIssuer, incomingAccount,
                    issuerChanged, accountChanged, secretChanged, true)]
            }, null, "CSV", "synthetic.csv", localize);
            var row = Assert.Single(model.Rows);

            Assert.Equal(secretChanged ? secretHeading
                : issuerChanged && accountChanged ? bothHeading
                : issuerChanged ? issuerHeading : accountChanged ? accountHeading : parametersHeading, row.IssueText);
            Assert.Contains(localize(ReviewStringKeys.FieldPeriod), row.ChangedFieldsText);
            Assert.False(model.CanImport);
            row.IsKeepBoth = true;
            Assert.True(model.CanImport);
            Assert.Equal(AccountImportConflictAction.KeepBoth, Assert.Single(model.CreateResolution()!.Conflicts).Action);
        }
    }

    [Theory]
    [InlineData("en", "Different issuer names")]
    [InlineData("de", "Unterschiedliche Ausstellernamen")]
    [InlineData("fr", "Noms d’émetteur différents")]
    [InlineData("es", "Nombres de emisor diferentes")]
    public void EarlierAccountComparison_HasOneExplicitHeadingAndLocalizedAccountWording(string culture, string issue)
    {
        var strings = new MobileStringCatalog(CultureInfo.GetCultureInfo(culture));
        var model = new ImportReviewViewModel(new(1, 1, ImportConflictStrategy.SkipExisting)
        {
            Entries = [new("Incoming", "user", "SHA1", 6, 10)
                { ImportIndex = 0, Existing = new("Earlier", "user", "SHA1", 6, 30), ExistingIsFromExport = true }],
            ChangedConflicts = [new(0, "Earlier", "user", "Incoming", "user", true, false, false, true)]
        }, null, "Aegis", "synthetic.json", strings.Get);
        var row = Assert.Single(model.Rows);
        Assert.Equal(issue, row.IssueText);
        Assert.Equal(strings.Get(ReviewStringKeys.EarlierExport), row.ExistingText);
        Assert.DoesNotContain(row.ExistingText, row.ExistingDetails);
        Assert.StartsWith(row.ExistingText + Environment.NewLine, row.ExistingParameters);
        Assert.Contains("Earlier", row.ExistingDetails);
        Assert.Contains("Incoming", row.IncomingParameters);
    }

    [Fact]
    public void ExpandingCard_OnlyNotifiesExpansionAndIgnoresRepeatedValues()
    {
        var model = Create();
        var row = model.Rows.Single(item => item.IsConflict);
        var rowChanges = new List<string?>();
        var reviewChanges = 0;
        row.PropertyChanged += (_, args) => rowChanges.Add(args.PropertyName);
        model.PropertyChanged += (_, _) => reviewChanges++;

        row.IsExpanded = false;
        row.IsExpanded = false;

        Assert.Equal([nameof(row.IsExpanded)], rowChanges);
        Assert.Equal(0, reviewChanges);
        Assert.False(model.CanImport);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fr")]
    [InlineData("es")]
    public void Review_RequiresAnExplicitDecisionAndReturnsEveryConflictInTheActiveLocale(string language)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo(language));
        var model = new ImportReviewViewModel(Preview(), new AccountImportReadSummary(3, nonOtpCount: 2), "2FAS", "export.2fas", catalog.Get);
        Assert.Equal(5, model.TotalCount);
        Assert.Equal(1, model.ReadyCount);
        Assert.Equal(1, model.ConflictCount);
        Assert.Equal(1, model.DuplicateCount);
        Assert.Equal(2, model.UnsupportedCount);
        Assert.False(model.CanImport);
        Assert.Null(model.CreateResolution());
        var conflict = Assert.Single(model.Rows, row => row.IsConflict);
        conflict.IsKeepBoth = true;
        Assert.True(model.CanImport);
        Assert.Equal(2, model.ImportCount);
        Assert.Equal(AccountImportConflictAction.KeepBoth, Assert.Single(model.CreateResolution()!.Conflicts).Action);
        Assert.Equal(string.Format(catalog.Get(ReviewStringKeys.ImportCount), 2), model.ImportText);
        Assert.Equal(catalog.Get(ReviewStringKeys.SecretDiffers), conflict.IssueText);
        Assert.Contains("SHA512", conflict.IncomingParameters);
        Assert.DoesNotContain("JBSWY", System.Text.Json.JsonSerializer.Serialize(Preview()));
    }

    [Fact]
    public void LanguageChange_RefreshesActionNamesWithoutLosingDecisionsOrCollapsedState()
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));
        var model = new ImportReviewViewModel(Preview(), null, "2FAS", "export", catalog.Get);
        var row = Assert.Single(model.Rows, row => row.IsConflict);
        row.IsKeepBoth = true;
        row.IsExpanded = false;
        var changes = 0;
        row.SelectedAction!.PropertyChanged += (_, _) => changes++;
        catalog.ApplyCulture("de");
        model.RefreshLanguage();
        Assert.Equal("Konflikte prüfen", model.Title);
        Assert.Equal("Beide behalten", row.SelectedAction.Name);
        Assert.True(row.IsKeepBoth);
        Assert.False(row.IsExpanded);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void SummaryFilters_DefaultToConflictsAndPreserveSearchCountsAndDecisions()
    {
        var model = Create();
        Assert.Equal(ImportReviewCategory.Conflicts, model.SelectedCategory.Category);
        var conflict = Assert.Single(model.VisibleRows);
        Assert.True(conflict.IsConflict);
        Assert.Equal(2, model.MobileItems.Count);
        var originalCounts = model.Categories.Select(category => category.Count).ToArray();
        conflict.IsReplace = true;
        foreach (var category in model.Categories)
        {
            model.FilterCommand.Execute(category);
            Assert.Same(category, model.SelectedCategory);
            Assert.Single(model.Categories, option => option.IsSelected);
            Assert.True(category.IsSelected);
            Assert.Equal(originalCounts, model.Categories.Select(option => option.Count).ToArray());
            Assert.All(model.VisibleRows, row =>
                Assert.True(category.Category == ImportReviewCategory.All || row.Category == category.Category));
            Assert.Equal(model.VisibleRows.Count + 1, model.MobileItems.Count);
            Assert.True(conflict.IsReplace);
        }
        model.Search = "carol";
        model.FilterCommand.Execute(model.Categories.Single(option => option.Category == ImportReviewCategory.Duplicates));
        Assert.Single(model.VisibleRows);
        model.FilterCommand.Execute(model.Categories.Single(option => option.Category == ImportReviewCategory.Conflicts));
        Assert.Empty(model.VisibleRows);
        Assert.Equal("carol", model.Search);
        Assert.Equal(originalCounts, model.Categories.Select(option => option.Count).ToArray());
        model.Search = string.Empty;
        Assert.Same(conflict, Assert.Single(model.VisibleRows));
        Assert.True(model.CanImport);
    }

    [Fact]
    public void SummaryFilters_WithoutConflictsDefaultToAllEntries()
    {
        var model = new ImportReviewViewModel(new(2, 1, ImportConflictStrategy.SkipExisting)
        {
            NewCount = 1,
            UnchangedCount = 1,
            Entries = [new("New", "alice", "SHA1", 6, 30),
                new("Duplicate", "bob", "SHA1", 6, 30) { Existing = new("Duplicate", "bob", "SHA1", 6, 30) }]
        }, null, "2FAS", "export.2fas", new MobileStringCatalog(CultureInfo.GetCultureInfo("en")).Get);
        Assert.Equal(ImportReviewCategory.All, model.SelectedCategory.Category);
        Assert.Equal(2, model.VisibleRows.Count);
        Assert.Equal(3, model.MobileItems.Count);
    }

    [Fact]
    public void FiltersAndBulkActions_DoNotAlterHiddenUnselectedRows()
    {
        var model = Create();
        model.SelectedCategory = model.Categories.Single(category => category.Category == ImportReviewCategory.Conflicts);
        model.AllVisibleSelected = true;
        Assert.Single(model.VisibleRows);
        model.BulkAction = model.Actions.Single(action => action.Action == AccountImportConflictAction.Skip);
        Assert.True(Assert.Single(model.VisibleRows).IsSkip);
        Assert.True(model.CanImport);
        Assert.Equal(1, model.ImportCount);
        model.Search = "nothing matches";
        Assert.Empty(model.VisibleRows);
        Assert.False(model.AllVisibleSelected);
        Assert.Single(model.CreateResolution()!.Conflicts);
    }

    [Fact]
    public void ExactDuplicates_CannotBeSelectedOrReceiveBulkActions()
    {
        var entries = Enumerable.Range(0, 10).Select(index =>
        {
            var entry = new AccountImportEntryPreview("Demo " + index, "alice", "SHA1", 6, 30);
            return entry with { Existing = entry };
        }).ToArray();
        var model = new ImportReviewViewModel(new(10, 10, ImportConflictStrategy.SkipExisting)
            { UnchangedCount = 10, Entries = entries }, null, "QR", "QR", new MobileStringCatalog(CultureInfo.GetCultureInfo("en")).Get);
        model.SelectedCategory = model.Categories.Single(category => category.Category == ImportReviewCategory.Duplicates);
        Assert.False(model.HasSelectableConflicts);
        model.AllVisibleSelected = true;
        foreach (var row in model.Rows) row.IsSelected = true;
        model.BulkAction = model.Actions.Single(action => action.Action == AccountImportConflictAction.KeepBoth);
        Assert.False(model.AllVisibleSelected);
        Assert.False(model.HasSelection);
        Assert.All(model.Rows, row => { Assert.False(row.IsSelected); Assert.Null(row.SelectedAction); Assert.Equal("Skip", row.ActionSummary); });
        Assert.Equal(0, model.ImportCount);
        Assert.False(model.CanImport);
    }

    [Fact]
    public void SelectionAvailability_FollowsVisibleConflictsAndSearch()
    {
        var model = Create();
        model.SelectedCategory = model.Categories.Single(category => category.Category == ImportReviewCategory.Duplicates);
        Assert.False(model.HasSelectableConflicts);
        model.SelectedCategory = model.Categories.Single(category => category.Category == ImportReviewCategory.Conflicts);
        Assert.True(model.HasSelectableConflicts);
        model.AllVisibleSelected = true;
        Assert.True(model.HasSelection);
        model.Search = "nothing matches";
        Assert.False(model.HasSelectableConflicts);
        model.Search = string.Empty;
        Assert.True(model.HasSelectableConflicts);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BackAndCancelNeverReturnAnImportDecisionAndCompletionIsSingleUse(bool goBack)
    {
        var model = Create();
        var events = new List<AccountImportResolution?>();
        model.CloseRequested += events.Add;
        (goBack ? model.BackCommand : model.CancelCommand).Execute(null);
        Assert.Equal(goBack, model.BackRequested);
        (goBack ? model.CancelCommand : model.BackCommand).Execute(null);
        model.Rows.Single(row => row.IsConflict).IsReplace = true;
        model.ImportCommand.Execute(null);
        Assert.Null(Assert.Single(events));
        Assert.Equal(goBack, model.BackRequested);
        Assert.False(model.CanImport);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    public void InvalidOrUnsupportedTotp_CannotBePartiallyImported(int invalid, int unsupported)
    {
        var catalog = new MobileStringCatalog(CultureInfo.GetCultureInfo("en"));
        var model = new ImportReviewViewModel(Preview(), new AccountImportReadSummary(3, invalid, unsupported), "Other", "export", catalog.Get);
        model.Rows.Single(row => row.IsConflict).IsSkip = true;
        Assert.False(model.CanImport);
        Assert.Null(model.CreateResolution());
    }

    private static ImportReviewViewModel Create() => new(Preview(), null, "Aegis", "accounts.json", new MobileStringCatalog(CultureInfo.GetCultureInfo("en")).Get);
    internal static AccountImportPreview Preview() => new(3, 2, ImportConflictStrategy.SkipExisting)
    {
        NewCount = 1,
        UnchangedCount = 1,
        Entries = [new("New account", "alice", "SHA1", 6, 30) { ImportIndex = 0 },
            new("Conflict", "bob", "SHA512", 8, 60) { ImportIndex = 1, Existing = new("Conflict", "bob", "SHA1", 6, 30) },
            new("Duplicate", "carol", "SHA1", 6, 30) { ImportIndex = 2, Existing = new("Duplicate", "carol", "SHA1", 6, 30) }],
        ChangedConflicts = [new(1, "Conflict", "bob", "Conflict", "bob", false, false, true, true, AlgorithmChanged: true, DigitsChanged: true)]
    };
}
