using System.Globalization;
using TOTP.Avalonia.Mobile.Localization;
using TOTP.Avalonia.Shared.Import;
using TOTP.Core.Models;
using TOTP.Core.Services.Models;

namespace TOTP.Tests.Avalonia.Presentation;

public sealed class ImportReviewViewModelTests
{
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
